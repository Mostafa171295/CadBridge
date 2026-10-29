// Revit STEP 8: walls / shear walls along the grid lines, storey by storey. Sizes in mm.
// Run 01_Grids and 02_Levels first. The boxes with a list are filled FROM REVIT after you press Connect.
//   WallTypeName : a wall type of the project (a wall is a system family, so there is no .rfa file to choose).
//   FromLevel / ToLevel : the walls go from FromLevel up to ToLevel, one wall per storey (ToLevel must be higher).
//   Place      : Perimeter = the four outer walls, AllGrids = a wall on every grid line,
//                OnlyX = on the lettered grids only (A, B, C), OnlyY = on the numbered grids only (1, 2, 3).
//   Structural : Yes = structural walls (bearing / shear walls).
//   BaseOffset / TopOffset : offsets from the base / top level (mm).
// The walls are drawn on the grid centre line and are attached to the top level, so they follow it when the level moves.
// Running it twice does not duplicate: a wall with the same line and base level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string WallTypeName = "{{WallTypeName=@wallTypes}}";     // list from Revit
string FromLevel = "{{FromLevel=@levels}}";              // list from Revit
string ToLevel = "{{ToLevel=@levels}}";                  // list from Revit
string Place = "{{Place=Perimeter|AllGrids|OnlyX|OnlyY}}";
string Structural = "{{Structural=Yes|No}}";
double BaseOffset = {{BaseOffset=0}};
double TopOffset = {{TopOffset=0}};

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
int i1 = LevelIndex(ToLevel, "ToLevel");
if (i1 <= i0) throw new Exception("ToLevel must be higher than FromLevel.");
bool structural = Structural.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase);

WallType wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
    .FirstOrDefault(x => x.Name == WallTypeName.Trim());
if (wallType == null) throw new Exception("Wall type not found: '" + WallTypeName + "'. Press Connect to refresh the lists.");

// ---- the wall lines (from the first to the last intersection of a grid line)
string pl = Place.Trim().ToLower();
bool perimeter = pl.StartsWith("p");
bool doX = perimeter || pl.StartsWith("a") || pl.StartsWith("onlyx");
bool doY = perimeter || pl.StartsWith("a") || pl.StartsWith("onlyy");

var lines = new List<Tuple<XYZ, XYZ>>();
if (doX)
    for (int i = 0; i < nh; i++)
    {
        if (perimeter && i != 0 && i != nh - 1) continue;
        lines.Add(Tuple.Create(node[i, 0], node[i, nv - 1]));
    }
if (doY)
    for (int j = 0; j < nv; j++)
    {
        if (perimeter && j != 0 && j != nv - 1) continue;
        lines.Add(Tuple.Create(node[0, j], node[nh - 1, j]));
    }
if (lines.Count == 0) throw new Exception("No wall lines selected. Check Place.");

// walls that are already in the model (same line + same base level = skipped)
var existing = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
bool Near(XYZ u, XYZ v) { return Math.Abs(u.X - v.X) < 0.01 && Math.Abs(u.Y - v.Y) < 0.01; }
bool AlreadyThere(XYZ a, XYZ b, Level lv)
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

int placed = 0, skippedCount = 0, attachProblems = 0;

using (Transaction t = new Transaction(doc, "CAD Bridge: walls"))
{
    t.Start();

    for (int lvIndex = i0; lvIndex < i1; lvIndex++)
    {
        Level baseLv = levels[lvIndex];
        Level topLv = levels[lvIndex + 1];
        double height = topLv.Elevation - baseLv.Elevation;

        foreach (Tuple<XYZ, XYZ> ln in lines)
        {
            XYZ a = new XYZ(ln.Item1.X, ln.Item1.Y, baseLv.Elevation);
            XYZ b = new XYZ(ln.Item2.X, ln.Item2.Y, baseLv.Elevation);
            if (AlreadyThere(a, b, baseLv)) { skippedCount++; continue; }

            Wall w = Wall.Create(doc, Line.CreateBound(a, b), wallType.Id, baseLv.Id, height, Ft(BaseOffset), false, structural);
            placed++;

            // attach the top to the next level
            try
            {
                Parameter ptop = w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                Parameter poff = w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                if (ptop != null && !ptop.IsReadOnly) ptop.Set(topLv.Id);
                if (poff != null && !poff.IsReadOnly) poff.Set(Ft(TopOffset));
            }
            catch { attachProblems++; }
        }
    }

    t.Commit();
}

Log("Placed " + placed + " walls from " + levels[i0].Name + " to " + levels[i1].Name + " | type: " + wallType.Name + " | structural: " + (structural ? "yes" : "no"));
if (skippedCount > 0) Log("Skipped " + skippedCount + " walls that already exist.");
if (attachProblems > 0) Log("Note: could not attach the top of " + attachProblems + " walls to the next level. Check Top Constraint in Properties.");
