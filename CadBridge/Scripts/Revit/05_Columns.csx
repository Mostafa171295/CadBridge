// Revit STEP 5: structural columns on the grid intersections, storey by storey. Sizes in mm.
// Run 01_Grids and 02_Levels first. The boxes with a list are filled FROM REVIT after you press Connect.
//   FamilyFile : a column family (.rfa) from a folder on this PC. Press "Folder..." to choose YOUR family folder.
//                It is loaded into the project when it is not there yet, and its first type is used instead of ColumnType.
//                Leave "(none)" to use ColumnType (a type that is already in the project).
//   BaseLevel / TopLevel : the columns go from BaseLevel up to TopLevel (TopLevel must be higher).
//   Mode       : PerStorey = one column per storey (a column from each level to the next one), Single = one tall column.
//   Scope      : Type     -> a type "CB 300x600" is created (or reused) and b / h are written in the TYPE.
//                Instance -> b / h are written in each column (only if the family has INSTANCE parameters b / h).
//                Steel families: the section comes from the chosen type (the size boxes are ignored).
//   Place      : All = every grid intersection, Perimeter = only the outer ones.
//   Skip       : intersections to leave out, as horizontal-vertical grid names, for example  B-2, C-3
//   BaseOffset / TopOffset : offsets from the base / top level (mm).
//   MaterialName: clear the box if you want to keep the material of the family.
// Running it twice does not duplicate: a column at the same point and base level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string FamilyFile = @"{{FamilyFile=@columnFiles}}";      // list of .rfa files ("(none)" = do not load anything)
string ColumnType = "{{ColumnType=@columnTypes}}";       // list from Revit:  Family : Type
string MaterialName = "{{MaterialName=@materials}}";     // list from Revit
string BaseLevel = "{{BaseLevel=@levels}}";              // list from Revit
string TopLevel = "{{TopLevel=@levels}}";                // list from Revit
string Mode = "{{Mode=PerStorey|Single}}";
string Scope = "{{Scope=Type|Instance}}";
string Place = "{{Place=All|Perimeter}}";
string Skip = "{{Skip=}}";
double B = {{B=300}};
double H = {{H=600}};
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

// ---- read the choices
string sc = Scope.Trim().ToLower();
bool typeMode = sc.StartsWith("t");
bool instMode = sc.StartsWith("i");
if (!typeMode && !instMode) throw new Exception("Scope must be Type or Instance.");
bool perStorey = Mode.Trim().StartsWith("P", StringComparison.OrdinalIgnoreCase);

int i0 = LevelIndex(BaseLevel, "BaseLevel");
int i1 = LevelIndex(TopLevel, "TopLevel");
if (i1 <= i0) throw new Exception("TopLevel must be higher than BaseLevel.");

FamilySymbol baseSym = ResolveSymbol(BuiltInCategory.OST_StructuralColumns, ColumnType, "column");

// columns that are already in the model (same point + same base level = skipped)
var existing = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
    .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
bool AlreadyThere(XYZ pt, Level baseLv)
{
    foreach (FamilyInstance e in existing)
    {
        LocationPoint lp = e.Location as LocationPoint;
        if (lp == null) continue;
        Parameter bl = e.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
        if (bl == null || !bl.AsElementId().Equals(baseLv.Id)) continue;
        if (Math.Abs(lp.Point.X - pt.X) < 0.01 && Math.Abs(lp.Point.Y - pt.Y) < 0.01) return true;
    }
    return false;
}

Parameter tb = FindParam(baseSym.Parameters, "b");
Parameter th = FindParam(baseSym.Parameters, "h");
bool typeHasSize = tb != null && th != null && !tb.IsReadOnly && !th.IsReadOnly;

int placed = 0, skippedCount = 0, sizeOnInstance = 0, materialSet = 0, heightProblems = 0;
string usedType = baseSym.Name;

using (Transaction t = new Transaction(doc, "CAD Bridge: columns"))
{
    t.Start();

    FamilySymbol sym = baseSym;

    // Type mode: make/reuse a type with this size and write b / h in the TYPE
    if (typeMode && typeHasSize)
    {
        string newName = "CB " + num(B) + "x" + num(H);
        sym = FindSymbol(BuiltInCategory.OST_StructuralColumns, baseSym.FamilyName, newName);
        if (sym == null) sym = (FamilySymbol)baseSym.Duplicate(newName);
        FindParam(sym.Parameters, "b").Set(Ft(B));
        FindParam(sym.Parameters, "h").Set(Ft(H));
        usedType = newName;
    }

    if (!sym.IsActive) sym.Activate();
    doc.Regenerate();

    int lastBase = perStorey ? i1 - 1 : i0;
    for (int lvIndex = i0; lvIndex <= lastBase; lvIndex++)
    {
        Level baseLv = levels[lvIndex];
        Level topLv = perStorey ? levels[lvIndex + 1] : levels[i1];

        for (int i = 0; i < nh; i++)
        {
            for (int j = 0; j < nv; j++)
            {
                if (!Wanted(i, j)) continue;
                XYZ pt = new XYZ(node[i, j].X, node[i, j].Y, baseLv.Elevation);
                if (AlreadyThere(pt, baseLv)) { skippedCount++; continue; }

                FamilyInstance fi = doc.Create.NewFamilyInstance(pt, sym, baseLv, Autodesk.Revit.DB.Structure.StructuralType.Column);
                placed++;

                // top level + offsets
                try
                {
                    Parameter topLvl = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                    Parameter topOff = fi.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                    Parameter baseOff = fi.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                    if (topLvl != null && !topLvl.IsReadOnly) topLvl.Set(topLv.Id);
                    if (topOff != null && !topOff.IsReadOnly) topOff.Set(Ft(TopOffset));
                    if (baseOff != null && !baseOff.IsReadOnly) baseOff.Set(Ft(BaseOffset));
                }
                catch { heightProblems++; }

                if (instMode)
                {
                    Parameter ib = FindParam(fi.Parameters, "b");
                    Parameter ih = FindParam(fi.Parameters, "h");
                    if (ib != null && ih != null && !ib.IsReadOnly && !ih.IsReadOnly) { ib.Set(Ft(B)); ih.Set(Ft(H)); sizeOnInstance++; }
                }
                if (ApplyMaterial(fi, sym)) materialSet++;
            }
        }
    }

    t.Commit();
}

Log("Placed " + placed + " columns from " + levels[i0].Name + " to " + levels[i1].Name + " | family: " + baseSym.FamilyName + " | type: " + usedType);
if (skippedCount > 0) Log("Skipped " + skippedCount + " places that already have a column.");
if (typeMode && !typeHasSize) Log("Note: this type has no editable b / h parameters (steel section?), so it was used as is. Pick another type to change the size.");
if (instMode)
{
    if (sizeOnInstance == placed) Log("Size b=" + num(B) + " h=" + num(H) + " was written in each column (INSTANCE).");
    else Log("Note: this family has no editable INSTANCE parameters b / h, so the size was NOT changed. Use Scope = Type instead.");
}
if (MaterialName.Trim().Length > 0 && materialSet != placed)
    Log("Note: the family does not allow setting the structural material from here (applied to " + materialSet + " of " + placed + ").");
if (heightProblems > 0) Log("Note: could not set the top level on " + heightProblems + " columns. Check Top Level / Top Offset in Properties.");
