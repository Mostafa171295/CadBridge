// Revit STEP 15: FENCE / BOUNDARY WALL (سور) or ROOF PARAPET around the building. Sizes in mm.
// Four walls in a rectangle around the grid area. Run 01_Grids and 02_Levels first.
// The boxes with a list are filled FROM REVIT after you press Connect.
//   WallTypeName : a wall type of the project.
//   LevelName    : the level the wall stands on:  Ground for a site boundary wall,  the Roof level for a parapet.
//   Offset       : distance from the outer grid lines to the wall centre line (mm). 4000 = a fence 4 m outside the building,
//                  0 = a parapet on the outer grids, negative = inside.
//   Height       : wall height (mm).    BaseOffset : height of the wall base above the level (mm, negative = lower).
//   OmitSides    : sides to leave open (for a gate), letters separated by commas:  N, S, E, W  (North = the +Y side, East = the +X side).
//   Structural   : Yes = a structural wall (for example a retaining wall), No = a normal wall.
// Running it twice does not duplicate: a wall with the same line and level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string WallTypeName = "{{WallTypeName=@wallTypes}}";     // list from Revit
string LevelName = "{{LevelName=@levels}}";              // list from Revit
double Offset = {{Offset=4000}};
double Height = {{Height=2400}};
double BaseOffset = {{BaseOffset=0}};
string OmitSides = "{{OmitSides=}}";
string Structural = "{{Structural=No|Yes}}";

List<Level> levels = Cb.Levels(doc);
Level lv = levels.FirstOrDefault(q => q.Name == (LevelName ?? "").Trim());
if (lv == null) throw new Exception("LevelName not found: '" + LevelName + "'. Press Connect to refresh the lists.");
WallType wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
    .FirstOrDefault(q => q.Name == (WallTypeName ?? "").Trim());
if (wallType == null) throw new Exception("Wall type not found: '" + WallTypeName + "'. Press Connect to refresh the lists.");
if (Height <= 0) throw new Exception("Height must be bigger than zero.");

CbGrid grid = CbGrid.Read(doc);
double off = Cb.Ft(Offset);
double x0 = grid.Node[0, 0].X - off, x1 = grid.Node[0, grid.NV - 1].X + off;
double y0 = grid.Node[0, 0].Y - off, y1 = grid.Node[grid.NH - 1, 0].Y + off;
if (x1 - x0 < 0.1 || y1 - y0 < 0.1) throw new Exception("The Offset is too negative: the rectangle has no size.");
double z = lv.Elevation;

HashSet<string> omit = new HashSet<string>(OmitSides.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(q => q.Trim().ToUpperInvariant()));
bool Omitted(string letter) { return omit.Contains(letter) || omit.Contains(letter == "N" ? "NORTH" : letter == "S" ? "SOUTH" : letter == "E" ? "EAST" : "WEST"); }

// side name, start, end
List<Tuple<string, XYZ, XYZ>> sides = new List<Tuple<string, XYZ, XYZ>>();
if (!Omitted("S")) sides.Add(Tuple.Create("S", new XYZ(x0, y0, z), new XYZ(x1, y0, z)));
if (!Omitted("E")) sides.Add(Tuple.Create("E", new XYZ(x1, y0, z), new XYZ(x1, y1, z)));
if (!Omitted("N")) sides.Add(Tuple.Create("N", new XYZ(x1, y1, z), new XYZ(x0, y1, z)));
if (!Omitted("W")) sides.Add(Tuple.Create("W", new XYZ(x0, y1, z), new XYZ(x0, y0, z)));
if (sides.Count == 0) throw new Exception("All four sides are omitted.");

List<Wall> existing = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
bool Near(XYZ u, XYZ v) { return Math.Abs(u.X - v.X) < 0.01 && Math.Abs(u.Y - v.Y) < 0.01; }
bool AlreadyThere(XYZ a, XYZ b)
{
    foreach (Wall e in existing)
    {
        LocationCurve lc = e.Location as LocationCurve;
        if (lc == null || !e.LevelId.Equals(lv.Id)) continue;
        XYZ p = lc.Curve.GetEndPoint(0), q = lc.Curve.GetEndPoint(1);
        if ((Near(p, a) && Near(q, b)) || (Near(p, b) && Near(q, a))) return true;
    }
    return false;
}

bool structural = Cb.Yes(Structural);
int made = 0, skippedCount = 0;
double totalLen = 0;

using (Transaction t = new Transaction(doc, "CAD Bridge: fence walls"))
{
    t.Start();
    foreach (Tuple<string, XYZ, XYZ> sd in sides)
    {
        if (AlreadyThere(sd.Item2, sd.Item3)) { skippedCount++; continue; }
        Wall w = Wall.Create(doc, Line.CreateBound(sd.Item2, sd.Item3), wallType.Id, lv.Id, Cb.Ft(Height), Cb.Ft(BaseOffset), false, structural);
        made++;
        totalLen += sd.Item2.DistanceTo(sd.Item3);
    }
    t.Commit();
}

Log("Placed " + made + " wall(s) on level " + lv.Name + " | type: " + wallType.Name + " | height " + Cb.N(Height) + " mm | length " +
    Cb.N(Math.Round(Cb.M(totalLen), 2)) + " m" + (skippedCount > 0 ? " | skipped (already there): " + skippedCount : "") + ".");
