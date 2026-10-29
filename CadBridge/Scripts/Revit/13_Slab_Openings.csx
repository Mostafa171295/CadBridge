// Revit STEP 13: OPENINGS in the slabs (stairwell, lift shaft, duct / pipe shaft). Sizes in mm.
// It makes a Revit "shaft opening": a vertical hole that cuts every floor and roof between two levels.
// Run 01_Grids, 02_Levels and 07_Slabs first. The boxes with a list are filled FROM REVIT after you press Connect.
//   LowerLevel / UpperLevel : the shaft goes from LowerLevel up to UpperLevel. It cuts the slabs of the levels ABOVE LowerLevel
//                 up to UpperLevel (the slab of LowerLevel itself is not cut).  Example: Level 1 -> Level 2 cuts the Level 2 slab;
//                 Level 1 -> Roof cuts all the slabs of the building above Level 1.
//   Shape       : Rectangle (Width x Length) or Circle (Width = the diameter).
//   Width / Length : size in X and in Y (mm).
//   Where       : AtBay  = the centre of a bay, as  A-B/1-2  (grids A and B, grids 1 and 2),
//                 AtGrid = a grid crossing, for example  B-2,
//                 both empty = absolute OffsetX / OffsetY (mm).  OffsetX / OffsetY (mm) always move the centre from that place.
// Running it twice does not duplicate: a shaft with the same place and levels is skipped.
// (12_Stairs can make its own opening, so this script is for lift shafts, ducts, voids and extra stair openings.)
// Available variables: uiapp, doc, OutDir, Log(...)

string LowerLevel = "{{LowerLevel=@levels}}";            // list from Revit
string UpperLevel = "{{UpperLevel=@levels}}";            // list from Revit
string Shape = "{{Shape=Rectangle|Circle}}";
double Width = {{Width=2500}};
double Length = {{Length=5000}};
string AtBay = "{{AtBay=}}";
string AtGrid = "{{AtGrid=}}";
double OffsetX = {{OffsetX=0}};
double OffsetY = {{OffsetY=0}};

List<Level> levels = Cb.Levels(doc);
if (levels.Count < 2) throw new Exception("At least two levels are needed. Run 02_Levels first.");
int LevelIdx(string name, string what)
{
    int found = levels.FindIndex(q => q.Name == (name ?? "").Trim());
    if (found < 0)
        throw new Exception(what + " not found: '" + name + "'. Press Connect to refresh the lists. Available: " + string.Join(", ", levels.Select(q => q.Name)));
    return found;
}
int i0 = LevelIdx(LowerLevel, "LowerLevel");
int i1 = LevelIdx(UpperLevel, "UpperLevel");
if (i1 <= i0) throw new Exception("UpperLevel must be higher than LowerLevel.");
Level lowLv = levels[i0], upLv = levels[i1];

// ---- the centre
XYZ centre = new XYZ(0, 0, 0);
if (AtBay.Trim().Length > 0 || AtGrid.Trim().Length > 0)
{
    CbGrid grid = CbGrid.Read(doc);
    XYZ found = AtBay.Trim().Length > 0 ? grid.BayCentre(AtBay) : grid.At(AtGrid);
    if (found == null)
        throw new Exception(AtBay.Trim().Length > 0 ? "AtBay '" + AtBay + "' not found. Use the form A-B/1-2." : "AtGrid '" + AtGrid + "' not found. Use the form B-2.");
    centre = found;
}
double cx = centre.X + Cb.Ft(OffsetX), cy = centre.Y + Cb.Ft(OffsetY);
double z = lowLv.Elevation;
bool circle = Shape.Trim().StartsWith("C", StringComparison.OrdinalIgnoreCase);
double halfX = Cb.Ft(Width) / 2.0, halfY = Cb.Ft(Length) / 2.0;
if (halfX <= 0 || (!circle && halfY <= 0)) throw new Exception("Width and Length must be bigger than zero.");

// ---- is there already a shaft here?
bool AlreadyThere()
{
    foreach (Element old in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_ShaftOpening).WhereElementIsNotElementType().ToElements())
    {
        BoundingBoxXYZ ob = old.get_BoundingBox(null);
        if (ob == null) continue;
        double ocx = (ob.Min.X + ob.Max.X) / 2.0, ocy = (ob.Min.Y + ob.Max.Y) / 2.0;
        if (Math.Abs(ocx - cx) < 0.3 && Math.Abs(ocy - cy) < 0.3 && Math.Abs(ob.Min.Z - lowLv.Elevation) < 1.0) return true;
    }
    return false;
}
if (AlreadyThere()) { Log("A shaft already exists at this place - skipped."); return; }

// ---- the outline
CurveArray profile = new CurveArray();
if (circle)
{
    XYZ west = new XYZ(cx - halfX, cy, z), east = new XYZ(cx + halfX, cy, z);
    profile.Append(Arc.Create(west, east, new XYZ(cx, cy + halfX, z)));
    profile.Append(Arc.Create(east, west, new XYZ(cx, cy - halfX, z)));
}
else
{
    XYZ a = new XYZ(cx - halfX, cy - halfY, z), b = new XYZ(cx + halfX, cy - halfY, z);
    XYZ c = new XYZ(cx + halfX, cy + halfY, z), e = new XYZ(cx - halfX, cy + halfY, z);
    profile.Append(Line.CreateBound(a, b));
    profile.Append(Line.CreateBound(b, c));
    profile.Append(Line.CreateBound(c, e));
    profile.Append(Line.CreateBound(e, a));
}

int slabsAbove = 0;
foreach (Floor fl in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>())
{
    Level fLv = Cb.LevelOf(doc, fl);
    if (fLv != null && fLv.Elevation > lowLv.Elevation + 0.001 && fLv.Elevation <= upLv.Elevation + 0.001) slabsAbove++;
}

using (Transaction t = new Transaction(doc, "CAD Bridge: slab opening"))
{
    t.Start();
    doc.Create.NewOpening(lowLv, upLv, profile);
    t.Commit();
}

Log("Shaft opening made: " + (circle ? "circle, diameter " + Cb.N(Width) + " mm" : Cb.N(Width) + " x " + Cb.N(Length) + " mm") +
    " | centre X " + Cb.N(Math.Round(Cb.Mm(cx))) + ", Y " + Cb.N(Math.Round(Cb.Mm(cy))) + " | from " + lowLv.Name + " to " + upLv.Name +
    " | slabs it can cut: " + slabsAbove + ".");
if (slabsAbove == 0) Log("Note: no floor was found on the levels above " + lowLv.Name + " up to " + upLv.Name + ", so the shaft cuts nothing yet (run 07_Slabs).");
