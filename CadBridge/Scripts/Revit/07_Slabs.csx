// Revit STEP 7: floor slabs on one level or on a range of levels. Sizes in mm.
// Run 01_Grids and 02_Levels first. The boxes with a list are filled FROM REVIT after you press Connect.
//   SlabType   : a floor type of the project (a floor is a system family, so there is no .rfa file to choose).
//   FromLevel / ToLevel : a slab is made on every level from FromLevel up to ToLevel (ToLevel empty = only FromLevel).
//   Layout     : OneSlab = one slab over the whole grid area (+ Overhang on every side),
//                PerBay  = one slab for every bay between four grid intersections (Overhang is not used).
//   Thickness  : the thickness of the structural layer (mm). A type "CB Slab 200" is made from SlabType (or reused).
//                0 = use SlabType as it is. If the type has finish layers, the total is a little more - see the log.
//   Offset     : height of the slab top above the level (mm, negative = lower).
//   Structural : Yes = the slab is a structural floor.
// Running it twice does not duplicate: a slab with the same outline on the same level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string SlabType = "{{SlabType=@floorTypes}}";            // list from Revit
string FromLevel = "{{FromLevel=@levels}}";              // list from Revit
string ToLevel = "{{ToLevel=@levels}}";                  // list from Revit
string Layout = "{{Layout=OneSlab|PerBay}}";
double Thickness = {{Thickness=200}};
double Overhang = {{Overhang=0}};
double Offset = {{Offset=0}};
string Structural = "{{Structural=Yes|No}}";

double Ft(double mm) { return mm / 304.8; }      // Revit works in feet internally
string num(double v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }

Parameter FindParam(ParameterSet ps, params string[] names)
{
    foreach (string n in names)
        foreach (Parameter p in ps)
            if (p.Definition != null && p.Definition.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return p;
    return null;
}

// ---- levels of the project (lowest first)
var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
if (levels.Count == 0) throw new Exception("No level found in this project. Run 02_Levels first.");
int LevelIndex(string levelText, string what)
{
    int k = levels.FindIndex(l => l.Name == (levelText ?? "").Trim());
    if (k < 0)
        throw new Exception(what + " not found: '" + levelText + "'. Press Connect to refresh the lists. Available: " +
                            string.Join(", ", levels.Select(l => l.Name)));
    return k;
}

// ---- the grid lines of the project (made by 01_Grids). Grids must be parallel to X / Y.
var vGrids = new List<Grid>();      // lines that run along Y  (numbered 1, 2, 3 ...)
var hGrids = new List<Grid>();      // lines that run along X  (lettered A, B, C ...)
foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>())
{
    Line gl = g.Curve as Line;
    if (gl == null) continue;                        // curved / multi-segment grids are ignored
    if (Math.Abs(gl.Direction.Y) >= Math.Abs(gl.Direction.X)) vGrids.Add(g); else hGrids.Add(g);
}
if (vGrids.Count < 2 || hGrids.Count < 2)
    throw new Exception("Need at least 2 vertical and 2 horizontal grids. Run 01_Grids first.");
vGrids = vGrids.OrderBy(g => ((Line)g.Curve).GetEndPoint(0).X).ToList();
hGrids = hGrids.OrderBy(g => ((Line)g.Curve).GetEndPoint(0).Y).ToList();
int nv = vGrids.Count, nh = hGrids.Count;

// intersection of two (infinite) lines, in plan
XYZ Cross(Line la, Line lb)
{
    XYZ p = la.GetEndPoint(0), r = la.Direction, q = lb.GetEndPoint(0), s = lb.Direction;
    double den = r.X * s.Y - r.Y * s.X;
    if (Math.Abs(den) < 1e-9) return null;
    double tt = ((q.X - p.X) * s.Y - (q.Y - p.Y) * s.X) / den;
    return new XYZ(p.X + tt * r.X, p.Y + tt * r.Y, 0);
}

// node[i, j] = crossing of horizontal grid i (A, B, ...) with vertical grid j (1, 2, ...)
XYZ[,] node = new XYZ[nh, nv];
for (int i = 0; i < nh; i++)
    for (int j = 0; j < nv; j++)
    {
        node[i, j] = Cross((Line)hGrids[i].Curve, (Line)vGrids[j].Curve);
        if (node[i, j] == null) throw new Exception("Grids " + hGrids[i].Name + " and " + vGrids[j].Name + " do not cross.");
    }

// ---- read the choices
int i0 = LevelIndex(FromLevel, "FromLevel");
int i1 = ToLevel.Trim().Length == 0 ? i0 : LevelIndex(ToLevel, "ToLevel");
if (i1 < i0) throw new Exception("ToLevel must be the same level as FromLevel or a higher one.");
bool oneSlab = Layout.Trim().StartsWith("O", StringComparison.OrdinalIgnoreCase);
bool structural = Structural.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase);

FloorType baseType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
    .FirstOrDefault(x => x.Name == SlabType.Trim());
if (baseType == null) throw new Exception("Floor type not found: '" + SlabType + "'. Press Connect to refresh the lists.");

// ---- the outlines (4 corners each, counter-clockwise), in plan
var outlines = new List<XYZ[]>();
if (oneSlab)
{
    XYZ c00 = node[0, 0], c01 = node[0, nv - 1], c11 = node[nh - 1, nv - 1], c10 = node[nh - 1, 0];
    XYZ ux = (c01 - c00).Normalize(), uy = (c10 - c00).Normalize();
    double o = Ft(Overhang);
    outlines.Add(new XYZ[] { c00 - ux * o - uy * o, c01 + ux * o - uy * o, c11 + ux * o + uy * o, c10 - ux * o + uy * o });
}
else
{
    for (int i = 0; i < nh - 1; i++)
        for (int j = 0; j < nv - 1; j++)
            outlines.Add(new XYZ[] { node[i, j], node[i, j + 1], node[i + 1, j + 1], node[i + 1, j] });
}

// slabs that are already in the model (same outline + same level = skipped)
var existing = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList();
bool AlreadyThere(XYZ[] pts, Level lv)
{
    double minX = pts.Min(q => q.X), maxX = pts.Max(q => q.X), minY = pts.Min(q => q.Y), maxY = pts.Max(q => q.Y);
    foreach (Floor e in existing)
    {
        if (!e.LevelId.Equals(lv.Id)) continue;
        BoundingBoxXYZ bb = e.get_BoundingBox(null);
        if (bb == null) continue;
        if (Math.Abs(bb.Min.X - minX) < 0.02 && Math.Abs(bb.Max.X - maxX) < 0.02 &&
            Math.Abs(bb.Min.Y - minY) < 0.02 && Math.Abs(bb.Max.Y - maxY) < 0.02) return true;
    }
    return false;
}

// one floor from 4 corners (the API is different before Revit 2022)
Floor MakeSlab(XYZ[] pts, Level lv, FloorType ft)
{
    XYZ[] z = pts.Select(q => new XYZ(q.X, q.Y, lv.Elevation)).ToArray();
#if REVIT2022_OR_NEWER
    var edges = new List<Curve>();
    for (int k = 0; k < 4; k++) edges.Add(Line.CreateBound(z[k], z[(k + 1) % 4]));
    CurveLoop loop = CurveLoop.Create(edges);
    return Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, lv.Id, structural, null, 0);
#else
    CurveArray ca = new CurveArray();
    for (int k = 0; k < 4; k++) ca.Append(Line.CreateBound(z[k], z[(k + 1) % 4]));
    return doc.Create.NewFloor(ca, ft, lv, structural);
#endif
}

int placed = 0, skippedCount = 0, offsetProblems = 0;
string usedType = baseType.Name;
string thicknessNote = "";

using (Transaction t = new Transaction(doc, "CAD Bridge: slabs"))
{
    t.Start();

    FloorType ft = baseType;

    // a type with the wanted thickness (made from the chosen type, or reused)
    if (Thickness > 0)
    {
        string newName = "CB Slab " + num(Thickness);
        FloorType made = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(x => x.Name == newName);
        if (made == null) made = (FloorType)baseType.Duplicate(newName);

        CompoundStructure cs = made.GetCompoundStructure();
        if (cs != null && cs.LayerCount > 0)
        {
            int layer = cs.StructuralMaterialIndex;
            if (layer < 0 || layer >= cs.LayerCount) layer = 0;
            cs.SetLayerWidth(layer, Ft(Thickness));
            made.SetCompoundStructure(cs);
            thicknessNote = "Total thickness of the type '" + newName + "' = " + num(cs.GetWidth() * 304.8) + " mm.";
            ft = made;
            usedType = newName;
        }
        else thicknessNote = "Note: the type '" + SlabType + "' has no layers that can be changed, so it was used as is.";
    }

    for (int lvIndex = i0; lvIndex <= i1; lvIndex++)
    {
        Level lv = levels[lvIndex];
        foreach (XYZ[] pts in outlines)
        {
            if (AlreadyThere(pts, lv)) { skippedCount++; continue; }
            Floor fl = MakeSlab(pts, lv, ft);
            placed++;

            if (Offset != 0)
            {
                Parameter po = fl.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                if (po != null && !po.IsReadOnly) po.Set(Ft(Offset)); else offsetProblems++;
            }
        }
    }

    t.Commit();
}

Log("Placed " + placed + " slabs on " + (i1 - i0 + 1) + " level(s) from " + levels[i0].Name + " to " + levels[i1].Name + " | type: " + usedType);
if (thicknessNote.Length > 0) Log(thicknessNote);
if (skippedCount > 0) Log("Skipped " + skippedCount + " slabs that already exist.");
if (!oneSlab && Overhang != 0) Log("Note: Overhang is only used with Layout = OneSlab.");
if (offsetProblems > 0) Log("Note: could not set the offset on " + offsetProblems + " slabs. Check Height Offset From Level in Properties.");
