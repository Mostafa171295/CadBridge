// Revit STEP 3: isolated footings under the grid intersections. Sizes in mm.
// Run 01_Grids and 02_Levels first. The boxes with a list are filled FROM REVIT after you press Connect.
//   FamilyFile : a footing family (.rfa) from a folder on this PC. Press "Folder..." to choose YOUR family folder.
//                It is loaded into the project when it is not there yet, and its first type is used instead of FootingType.
//                Leave "(none)" to use FootingType (a type that is already in the project).
//   LevelName  : the level the footings sit on (normally the Foundation level made by 02_Levels).
//   Scope      : Type     -> a type "CB 1500x1500x500" is created (or reused) and the size is written in the TYPE.
//                Instance -> the size is written in each footing (only if the family has INSTANCE parameters).
//                The size goes to the family parameters Length / Width / Foundation Thickness (or Thickness).
//   Place      : All = every grid intersection, Perimeter = only the outer ones.
//   Skip       : intersections to leave out, as horizontal-vertical grid names, for example  B-2, C-3
//   Offset     : elevation from the level (mm), negative = lower.
//   MaterialName: clear the box if you want to keep the material of the family.
// Running it twice does not duplicate: a footing at the same point and level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string FamilyFile = @"{{FamilyFile=@footingFiles}}";     // list of .rfa files ("(none)" = do not load anything)
string FootingType = "{{FootingType=@footingTypes}}";    // list from Revit:  Family : Type
string MaterialName = "{{MaterialName=@materials}}";     // list from Revit
string LevelName = "{{LevelName=@levels}}";              // list from Revit
string Scope = "{{Scope=Type|Instance}}";
string Place = "{{Place=All|Perimeter}}";
string Skip = "{{Skip=}}";
double Length = {{Length=1500}};
double Width = {{Width=1500}};
double Thickness = {{Thickness=500}};
double Offset = {{Offset=0}};

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

// ---- which intersections to use
var skip = new HashSet<string>(Skip.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(x => x.Trim().ToUpperInvariant()));
bool perimeterOnly = Place.Trim().StartsWith("P", StringComparison.OrdinalIgnoreCase);
bool Wanted(int i, int j)
{
    if (perimeterOnly && i != 0 && j != 0 && i != nh - 1 && j != nv - 1) return false;
    string na = hGrids[i].Name.Trim().ToUpperInvariant(), nb = vGrids[j].Name.Trim().ToUpperInvariant();
    return !skip.Contains(na + "-" + nb) && !skip.Contains(nb + "-" + na);
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

Parameter[] SizeParams(ParameterSet ps)
{
    return new Parameter[]
    {
        FindParam(ps, "Length", "L", "Foundation Length"),
        FindParam(ps, "Width", "B", "Foundation Width"),
        FindParam(ps, "Foundation Thickness", "Thickness", "Depth", "h")
    };
}
bool HasSize(ParameterSet ps)
{
    foreach (Parameter sp in SizeParams(ps))
        if (sp != null && !sp.IsReadOnly && sp.StorageType == StorageType.Double) return true;
    return false;
}
int SetSize(ParameterSet ps)
{
    double[] vals = { Ft(Length), Ft(Width), Ft(Thickness) };
    Parameter[] sps = SizeParams(ps);
    int n = 0;
    for (int k = 0; k < 3; k++)
        if (sps[k] != null && !sps[k].IsReadOnly && sps[k].StorageType == StorageType.Double) { sps[k].Set(vals[k]); n++; }
    return n;
}

// ---- read the choices
string sc = Scope.Trim().ToLower();
bool typeMode = sc.StartsWith("t");
bool instMode = sc.StartsWith("i");
if (!typeMode && !instMode) throw new Exception("Scope must be Type or Instance.");

Level level = levels[LevelIndex(LevelName, "LevelName")];
FamilySymbol baseSym = ResolveSymbol(BuiltInCategory.OST_StructuralFoundation, FootingType, "footing");

// footings that are already in the model (same point + same level = skipped)
var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
bool AlreadyThere(XYZ pt)
{
    foreach (FamilyInstance e in existing)
    {
        LocationPoint lp = e.Location as LocationPoint;
        if (lp == null || !e.LevelId.Equals(level.Id)) continue;
        if (Math.Abs(lp.Point.X - pt.X) < 0.01 && Math.Abs(lp.Point.Y - pt.Y) < 0.01) return true;
    }
    return false;
}

int placed = 0, skippedCount = 0, sizeOnInstance = 0, materialSet = 0, offsetProblems = 0;
string usedType = baseSym.Name;

using (Transaction t = new Transaction(doc, "CAD Bridge: footings"))
{
    t.Start();

    FamilySymbol sym = baseSym;

    // Type mode: make/reuse a type with this size and write the size in the TYPE
    if (typeMode && HasSize(baseSym.Parameters))
    {
        string newName = "CB " + num(Length) + "x" + num(Width) + "x" + num(Thickness);
        sym = FindSymbol(BuiltInCategory.OST_StructuralFoundation, baseSym.FamilyName, newName);
        if (sym == null) sym = (FamilySymbol)baseSym.Duplicate(newName);
        SetSize(sym.Parameters);
        usedType = newName;
    }

    if (!sym.IsActive) sym.Activate();
    doc.Regenerate();

    for (int i = 0; i < nh; i++)
    {
        for (int j = 0; j < nv; j++)
        {
            if (!Wanted(i, j)) continue;
            XYZ pt = new XYZ(node[i, j].X, node[i, j].Y, level.Elevation);
            if (AlreadyThere(pt)) { skippedCount++; continue; }

            FamilyInstance fi = doc.Create.NewFamilyInstance(pt, sym, level, Autodesk.Revit.DB.Structure.StructuralType.Footing);
            placed++;

            if (Offset != 0)
            {
                Parameter po = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                if (po == null || po.IsReadOnly) po = fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                if (po != null && !po.IsReadOnly) po.Set(Ft(Offset)); else offsetProblems++;
            }

            if (instMode && HasSize(fi.Parameters)) { SetSize(fi.Parameters); sizeOnInstance++; }
            if (ApplyMaterial(fi, sym)) materialSet++;
        }
    }

    t.Commit();
}

Log("Placed " + placed + " footings on level " + level.Name + " | family: " + baseSym.FamilyName + " | type: " + usedType);
if (skippedCount > 0) Log("Skipped " + skippedCount + " places that already have a footing.");
if (typeMode && !HasSize(baseSym.Parameters)) Log("Note: this family has no editable Length / Width / Thickness parameters, so it was used as is.");
if (instMode)
{
    if (sizeOnInstance == placed) Log("Size " + num(Length) + " x " + num(Width) + " x " + num(Thickness) + " was written in each footing (INSTANCE).");
    else Log("Note: this family has no editable INSTANCE size parameters, so the size was NOT changed. Use Scope = Type instead.");
}
if (MaterialName.Trim().Length > 0 && materialSet != placed)
    Log("Note: the family does not allow setting the structural material from here (applied to " + materialSet + " of " + placed + ").");
if (offsetProblems > 0) Log("Note: could not set the offset on " + offsetProblems + " footings. Check Offset in Properties.");
