// Revit STEP 4: tie beams (grade beams) between the footings, on the foundation level. Sizes in mm.
// Run 01_Grids and 02_Levels first (and 05_Columns if you want the beams to end on columns).
// The boxes with a list are filled FROM REVIT after you press Connect.
//   FamilyFile : a beam family (.rfa) from a folder on this PC. Press "Folder..." to choose YOUR family folder.
//                It is loaded into the project when it is not there yet, and its first type is used instead of BeamType.
//                Leave "(none)" to use BeamType (a type that is already in the project).
//   LevelName  : the level of the tie beams (normally the Foundation level made by 02_Levels).
//   Scope      : Type     -> a type "CB 300x600" is created (or reused) and b / h are written in the TYPE.
//                Instance -> b / h are written in each beam (only if the family has INSTANCE parameters b / h).
//                Steel families: the section comes from the chosen type (the size boxes are ignored).
//   BeamDirection: Both, X only (beams along the lettered grids A, B, C) or Y only (beams along 1, 2, 3).
//   Place      : All = every span, Perimeter = only the outer frame.
//   SkipLines  : grid names whose beams are NOT wanted, for example  C, 3
//   Offset / Justify: vertical offset of the beam (mm, negative = lower) and which face stays on the level (Top, Center, Bottom, Origin).
//   MaterialName: clear the box if you want to keep the material of the family.
// Beams run from intersection to intersection. Running it twice does not duplicate (same span + same level = skipped).
// Available variables: uiapp, doc, OutDir, Log(...)

string FamilyFile = @"{{FamilyFile=@beamFiles}}";        // list of .rfa files ("(none)" = do not load anything)
string BeamType = "{{BeamType=@beamTypes}}";             // list from Revit:  Family : Type
string MaterialName = "{{MaterialName=@materials}}";     // list from Revit
string LevelName = "{{LevelName=@levels}}";              // list from Revit
string Scope = "{{Scope=Type|Instance}}";
string BeamDirection = "{{BeamDirection=Both|X only|Y only}}";
string Place = "{{Place=All|Perimeter}}";
string SkipLines = "{{SkipLines=}}";
double B = {{B=300}};
double H = {{H=600}};
double Offset = {{Offset=0}};
string Justify = "{{Justify=Top|Center|Bottom|Origin}}";

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

// ---- family: a type already in the project, or a .rfa file (loaded into the project when it is not there yet)
FamilySymbol FindSymbol(BuiltInCategory cat, string familyName, string typeName)
{
    return new FilteredElementCollector(doc).OfCategory(cat).OfClass(typeof(FamilySymbol))
        .Cast<FamilySymbol>().FirstOrDefault(fs => fs.FamilyName == familyName && fs.Name == typeName);
}

FamilySymbol ResolveSymbol(BuiltInCategory cat, string typeText, string label)
{
    string path = "";
    string ff = FamilyFile.Trim();
    if (ff.Length > 0 && !ff.StartsWith("("))
    {
        int bar = ff.IndexOf("  |  ");
        path = bar >= 0 ? ff.Substring(bar + 5).Trim() : ff;
        if (!File.Exists(path)) throw new Exception("Family file not found: " + path);
    }
    typeText = (typeText ?? "").Trim();
    if (path.Length == 0 && typeText.Length == 0)
        throw new Exception("Choose a " + label + " type or a family file from the lists. Press Connect first so the lists are read; " +
                            "use the Folder... button to point to your family folder.");

    string famName = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) : "";
    if (path.Length > 0)
    {
        bool there = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Any(f => f.Name == famName);
        if (!there)
        {
            using (Transaction tl = new Transaction(doc, "CAD Bridge: load family"))
            {
                tl.Start();
                Family loadedFam;
                bool okLoad = doc.LoadFamily(path, out loadedFam);
                if (!okLoad) { tl.RollBack(); throw new Exception("Revit could not load the family: " + path); }
                tl.Commit();
            }
            Log("Family loaded into the project: " + famName);
        }
        else Log("Family is already in the project: " + famName);
    }

    var all = new FilteredElementCollector(doc).OfCategory(cat).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
    FamilySymbol found;
    if (path.Length > 0)
    {
        found = all.Where(fs => fs.FamilyName == famName).OrderBy(fs => fs.Name).FirstOrDefault();
        if (found == null)
            throw new Exception("The family '" + famName + "' has no " + label + " type. Is it really a " + label + " family?");
    }
    else
    {
        string famPart = "", typePart = typeText;
        int cut = typePart.IndexOf(" : ");
        if (cut >= 0) { famPart = typePart.Substring(0, cut).Trim(); typePart = typePart.Substring(cut + 3).Trim(); }
        found = all.FirstOrDefault(fs => fs.Name == typePart && (famPart.Length == 0 || fs.FamilyName == famPart));
        if (found == null)
            throw new Exception(label + " type not found in this project: " + typeText + ". Press Connect to refresh the lists.");
    }
    return found;
}

// ---- optional material from the project
ElementId matId = ElementId.InvalidElementId;
if (MaterialName.Trim().Length > 0)
{
    Material matFound = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
        .FirstOrDefault(m => m.Name.Equals(MaterialName.Trim(), StringComparison.OrdinalIgnoreCase));
    if (matFound == null) throw new Exception("Material not found: " + MaterialName + ". Press Connect to refresh the lists.");
    matId = matFound.Id;
}
bool ApplyMaterial(FamilyInstance fi, FamilySymbol fsym)
{
    if (matId == ElementId.InvalidElementId) return false;
    Parameter pm = fi.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
    if (pm != null && !pm.IsReadOnly) { pm.Set(matId); return true; }
    Parameter pm2 = fsym.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
    if (pm2 != null && !pm2.IsReadOnly) { pm2.Set(matId); return true; }
    return false;
}

// ---- read the choices
string sc = Scope.Trim().ToLower();
bool typeMode = sc.StartsWith("t");
bool instMode = sc.StartsWith("i");
if (!typeMode && !instMode) throw new Exception("Scope must be Type or Instance.");

int i0 = LevelIndex(LevelName, "LevelName");
int i1 = i0;
FamilySymbol baseSym = ResolveSymbol(BuiltInCategory.OST_StructuralFraming, BeamType, "beam");

// ---- the spans: from intersection to intersection
var skipLines = new HashSet<string>(SkipLines.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(x => x.Trim().ToUpperInvariant()));
bool doX = !BeamDirection.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase);
bool doY = !BeamDirection.Trim().StartsWith("X", StringComparison.OrdinalIgnoreCase);
bool perimeterOnly = Place.Trim().StartsWith("P", StringComparison.OrdinalIgnoreCase);

var spans = new List<Tuple<XYZ, XYZ>>();
if (doX)
    for (int i = 0; i < nh; i++)
    {
        if (skipLines.Contains(hGrids[i].Name.Trim().ToUpperInvariant())) continue;
        if (perimeterOnly && i != 0 && i != nh - 1) continue;
        for (int j = 0; j < nv - 1; j++) spans.Add(Tuple.Create(node[i, j], node[i, j + 1]));
    }
if (doY)
    for (int j = 0; j < nv; j++)
    {
        if (skipLines.Contains(vGrids[j].Name.Trim().ToUpperInvariant())) continue;
        if (perimeterOnly && j != 0 && j != nv - 1) continue;
        for (int i = 0; i < nh - 1; i++) spans.Add(Tuple.Create(node[i, j], node[i + 1, j]));
    }
if (spans.Count == 0) throw new Exception("No beam spans selected. Check BeamDirection, Place and SkipLines.");

// beams that are already in the model (same span + same level = skipped)
var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
bool Near(XYZ u, XYZ v) { return Math.Abs(u.X - v.X) < 0.01 && Math.Abs(u.Y - v.Y) < 0.01; }
bool AlreadyThere(XYZ a, XYZ b, Level lv)
{
    foreach (FamilyInstance e in existing)
    {
        LocationCurve lc = e.Location as LocationCurve;
        if (lc == null || !e.LevelId.Equals(lv.Id)) continue;
        XYZ p = lc.Curve.GetEndPoint(0), q = lc.Curve.GetEndPoint(1);
        if ((Near(p, a) && Near(q, b)) || (Near(p, b) && Near(q, a))) return true;
    }
    return false;
}

// z-justification: Top = 0, Center = 1, Bottom = 2, Origin = 3
int justifyValue = 0;
string jj = Justify.Trim().ToLower();
if (jj.StartsWith("c")) justifyValue = 1; else if (jj.StartsWith("b")) justifyValue = 2; else if (jj.StartsWith("o")) justifyValue = 3;

Parameter tb = FindParam(baseSym.Parameters, "b");
Parameter th = FindParam(baseSym.Parameters, "h");
bool typeHasSize = tb != null && th != null && !tb.IsReadOnly && !th.IsReadOnly;

int placed = 0, skippedCount = 0, sizeOnInstance = 0, materialSet = 0, offsetProblems = 0;
string usedType = baseSym.Name;

using (Transaction t = new Transaction(doc, "CAD Bridge: beams"))
{
    t.Start();

    FamilySymbol sym = baseSym;

    // Type mode: make/reuse a type with this size and write b / h in the TYPE
    if (typeMode && typeHasSize)
    {
        string newName = "CB " + num(B) + "x" + num(H);
        sym = FindSymbol(BuiltInCategory.OST_StructuralFraming, baseSym.FamilyName, newName);
        if (sym == null) sym = (FamilySymbol)baseSym.Duplicate(newName);
        FindParam(sym.Parameters, "b").Set(Ft(B));
        FindParam(sym.Parameters, "h").Set(Ft(H));
        usedType = newName;
    }

    if (!sym.IsActive) sym.Activate();
    doc.Regenerate();

    for (int lvIndex = i0; lvIndex <= i1; lvIndex++)
    {
        Level lv = levels[lvIndex];
        foreach (Tuple<XYZ, XYZ> sp in spans)
        {
            XYZ a = new XYZ(sp.Item1.X, sp.Item1.Y, lv.Elevation);
            XYZ b = new XYZ(sp.Item2.X, sp.Item2.Y, lv.Elevation);
            if (AlreadyThere(a, b, lv)) { skippedCount++; continue; }

            FamilyInstance fi = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), sym, lv, Autodesk.Revit.DB.Structure.StructuralType.Beam);
            placed++;

            Parameter pj = fi.get_Parameter(BuiltInParameter.Z_JUSTIFICATION);
            if (pj != null && !pj.IsReadOnly) pj.Set(justifyValue);

            if (Offset != 0)
            {
                Parameter pz = fi.get_Parameter(BuiltInParameter.Z_OFFSET_VALUE);
                if (pz != null && !pz.IsReadOnly) pz.Set(Ft(Offset)); else offsetProblems++;
            }

            if (instMode)
            {
                Parameter ib = FindParam(fi.Parameters, "b");
                Parameter ih = FindParam(fi.Parameters, "h");
                if (ib != null && ih != null && !ib.IsReadOnly && !ih.IsReadOnly) { ib.Set(Ft(B)); ih.Set(Ft(H)); sizeOnInstance++; }
            }
            if (ApplyMaterial(fi, sym)) materialSet++;
        }
    }

    t.Commit();
}

Log("Placed " + placed + " beams on " + (i1 - i0 + 1) + " level(s) from " + levels[i0].Name + " to " + levels[i1].Name +
    " | family: " + baseSym.FamilyName + " | type: " + usedType);
if (skippedCount > 0) Log("Skipped " + skippedCount + " spans that already have a beam.");
if (typeMode && !typeHasSize) Log("Note: this type has no editable b / h parameters (steel section?), so it was used as is. Pick another type to change the size.");
if (instMode)
{
    if (sizeOnInstance == placed) Log("Size b=" + num(B) + " h=" + num(H) + " was written in each beam (INSTANCE).");
    else Log("Note: this family has no editable INSTANCE parameters b / h, so the size was NOT changed. Use Scope = Type instead.");
}
if (MaterialName.Trim().Length > 0 && materialSet != placed)
    Log("Note: the family does not allow setting the structural material from here (applied to " + materialSet + " of " + placed + ").");
if (offsetProblems > 0) Log("Note: could not set the offset on " + offsetProblems + " beams. Check z Offset Value in Properties.");
