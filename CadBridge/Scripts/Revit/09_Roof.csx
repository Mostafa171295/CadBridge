// Revit STEP 9: the roof on the top level. Sizes in mm.
// Run 01_Grids and 02_Levels first. The boxes with a list are filled FROM REVIT after you press Connect.
//   RoofTypeName : a roof type of the project (a roof is a system family, so there is no .rfa file to choose).
//   LevelName  : the level the roof sits on (normally the Roof level made by 02_Levels).
//   Overhang   : how far the roof goes beyond the outer grids on every side.
//   Shape      : Flat   = no slope,
//                Hip    = all four sides slope,
//                Gable  = the two LONG sides slope (the short sides are vertical gable ends),
//                Shed   = one side slopes (the roof rises away from the grid line with the lowest Y).
//   SlopePercent : slope in percent (rise / run x 100). 30 = 30 %, about 16.7 degrees. Not used for a Flat roof.
//   BaseOffset : height of the roof base above the level (mm).
// Available variables: uiapp, doc, OutDir, Log(...)

string RoofTypeName = "{{RoofTypeName=@roofTypes}}";     // list from Revit
string LevelName = "{{LevelName=@levels}}";              // list from Revit
string Shape = "{{Shape=Flat|Hip|Gable|Shed}}";
double Overhang = {{Overhang=500}};
double SlopePercent = {{SlopePercent=30}};
double BaseOffset = {{BaseOffset=0}};

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
Level level = levels[LevelIndex(LevelName, "LevelName")];
RoofType roofType = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>()
    .FirstOrDefault(x => x.Name == RoofTypeName.Trim());
if (roofType == null) throw new Exception("Roof type not found: '" + RoofTypeName + "'. Press Connect to refresh the lists.");

string shp = Shape.Trim().ToLower();
bool flat = shp.StartsWith("f") || SlopePercent <= 0;
double ratio = SlopePercent / 100.0;

// ---- the outline: the grid area + the overhang (counter-clockwise, in plan)
XYZ c00 = node[0, 0], c01 = node[0, nv - 1], c11 = node[nh - 1, nv - 1], c10 = node[nh - 1, 0];
XYZ ux = (c01 - c00).Normalize(), uy = (c10 - c00).Normalize();
double o = Ft(Overhang);
XYZ[] corner =
{
    c00 - ux * o - uy * o,
    c01 + ux * o - uy * o,
    c11 + ux * o + uy * o,
    c10 - ux * o + uy * o
};
for (int k = 0; k < 4; k++) corner[k] = new XYZ(corner[k].X, corner[k].Y, level.Elevation);

double lenX = corner[0].DistanceTo(corner[1]);      // sides 0 and 2 run along X
double lenY = corner[1].DistanceTo(corner[2]);      // sides 1 and 3 run along Y

int slopedSides = 0;
FootPrintRoof roof = null;

using (Transaction t = new Transaction(doc, "CAD Bridge: roof"))
{
    t.Start();

    CurveArray footprint = new CurveArray();
    for (int k = 0; k < 4; k++) footprint.Append(Line.CreateBound(corner[k], corner[(k + 1) % 4]));

    ModelCurveArray mapping;
#pragma warning disable CS0618
    roof = doc.Create.NewFootPrintRoof(footprint, level, roofType, out mapping);
#pragma warning restore CS0618

    // which sides define a slope (side k = corner k -> corner k+1)
    int side = 0;
    foreach (ModelCurve mc in mapping)
    {
        bool slope = false;
        if (!flat)
        {
            if (shp.StartsWith("h")) slope = true;
            else if (shp.StartsWith("g")) slope = (lenX >= lenY) ? (side == 0 || side == 2) : (side == 1 || side == 3);
            else if (shp.StartsWith("s")) slope = (side == 0);
        }
        roof.set_DefinesSlope(mc, slope);
        if (slope) { roof.set_SlopeAngle(mc, ratio); slopedSides++; }
        side++;
    }

    if (BaseOffset != 0)
    {
        Parameter po = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
        if (po != null && !po.IsReadOnly) po.Set(Ft(BaseOffset));
    }

    t.Commit();
}

Log("Roof made on level " + level.Name + " | type: " + roofType.Name + " | shape: " + (flat ? "flat" : Shape.Trim() + ", slope " + num(SlopePercent) + " %") +
    " | sloped sides: " + slopedSides + " | overhang " + num(Overhang) + " mm.");
if (!flat && slopedSides == 0) Log("Note: no side was given a slope. Check Shape.");
