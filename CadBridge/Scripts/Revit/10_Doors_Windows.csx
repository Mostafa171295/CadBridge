// Revit STEP 10: doors OR windows in the walls, in the middle of the bays between the grid lines. Sizes in mm.
// Run 01_Grids, 02_Levels and 08_Walls first. Run this script once for the windows and once for the doors
// (change Kind, the type and the Lines each time). Run 11_Rooms afterwards to create the rooms.
// The boxes with a list are filled FROM REVIT after you press Connect.
//   Kind        : Window or Door.
//   DoorType / WindowType : a type that is already loaded in the project (the one for the chosen Kind is used).
//   DoorFile / WindowFile : OR an external family (.rfa) from a folder on this PC - press "Folder..." to choose YOUR family folder.
//                 It is loaded into the project when it is not there yet and its first type is used instead of DoorType / WindowType.
//                 Leave "(none)" to use the type from the project.
//   FromLevel / ToLevel   : the walls whose base level is in this range get openings (ToLevel = FromLevel -> one level).
//   Lines       : where the openings go.  Perimeter = the four outer grid lines,  All = every grid line,
//                 or grid names separated by commas, for example  A, C, 1, 5.
//   Bays        : All = every bay of those lines, or bay numbers counted along the line from 1, for example  1, 3.
//                 (a bay = the distance between two neighbouring grids)
//   Width / Height : mm. 0 = keep the size of the type. Otherwise a type "CB W1200xH1200" is made (or reused) from the chosen type.
//   Sill        : height of the window sill above the level (mm). Doors always sit on the level.
// The opening is placed at the middle of the bay, in the wall that runs along that grid line.
// Running it twice does not duplicate: an opening within 1 ft (30 cm) of the same point is skipped.
//   Host        : Grid = the old way (middle of the bays).  SelectedWalls = ONLY the walls you selected in Revit
//                 (select one wall or many walls in Revit first, then press Run). FromLevel / ToLevel / Lines / Bays are ignored then:
//                 each wall gets its openings on its own level.
//   Count       : (SelectedWalls) how many openings in each selected wall, spread evenly along the wall.
//   FromStart   : (SelectedWalls, Count = 1) mm from the START of the wall to the centre of the opening. 0 = middle of the wall.
// Available variables: uiapp, doc, OutDir, Log(...)

string Kind = "{{Kind=Window|Door}}";
string DoorType = "{{DoorType=@doorTypes}}";             // list from Revit:  Family : Type
string WindowType = "{{WindowType=@windowTypes}}";       // list from Revit:  Family : Type
string DoorFile = @"{{DoorFile=@doorFiles}}";             // list of .rfa files ("(none)" = do not load anything)
string WindowFile = @"{{WindowFile=@windowFiles}}";       // list of .rfa files ("(none)" = do not load anything)
string FromLevel = "{{FromLevel=@levels}}";              // list from Revit
string ToLevel = "{{ToLevel=@levels}}";                  // list from Revit
string Lines = "{{Lines=Perimeter}}";
string Bays = "{{Bays=All}}";
double Width = {{Width=0}};
double Height = {{Height=0}};
double Sill = {{Sill=900}};
string Host = "{{Host=Grid|SelectedWalls}}";
int Count = {{Count=1}};
double FromStart = {{FromStart=0}};

bool useSel = Host.Trim().StartsWith("S", StringComparison.OrdinalIgnoreCase);
bool isDoor = Kind.Trim().StartsWith("D", StringComparison.OrdinalIgnoreCase);
BuiltInCategory cat = isDoor ? BuiltInCategory.OST_Doors : BuiltInCategory.OST_Windows;
string label = isDoor ? "door" : "window";

List<Level> levels = Cb.Levels(doc);
if (levels.Count == 0) throw new Exception("No level found in this project. Run 02_Levels first.");
int LevelIdx(string name, string what)
{
    int found = levels.FindIndex(q => q.Name == (name ?? "").Trim());
    if (found < 0)
        throw new Exception(what + " not found: '" + name + "'. Press Connect to refresh the lists. Available: " + string.Join(", ", levels.Select(q => q.Name)));
    return found;
}
int i0 = 0, i1 = 0;
if (!useSel)
{
    i0 = LevelIdx(FromLevel, "FromLevel");
    i1 = Math.Max(i0, LevelIdx(ToLevel, "ToLevel"));
}

List<Wall> selWalls = new List<Wall>();
if (useSel)
{
    foreach (ElementId sid in uiapp.ActiveUIDocument.Selection.GetElementIds())
    {
        Wall sw = doc.GetElement(sid) as Wall;
        if (sw != null) selWalls.Add(sw);
    }
    if (selWalls.Count == 0)
        throw new Exception("Host = SelectedWalls but no wall is selected in Revit. Select the wall(s) in Revit first, then press Run again.");
    if (Count < 1) Count = 1;
}

CbGrid grid = useSel ? null : CbGrid.Read(doc);

// ---- the type
string typeText = (isDoor ? DoorType : WindowType).Trim();
string fileText = (isDoor ? DoorFile : WindowFile).Trim();
string rfaPath = "";
if (fileText.Length > 0 && !fileText.StartsWith("("))
{
    int fbar = fileText.IndexOf("  |  ");
    rfaPath = fbar >= 0 ? fileText.Substring(fbar + 5).Trim() : fileText;
    if (!File.Exists(rfaPath)) throw new Exception("Family file not found: " + rfaPath);
}
if (typeText.Length == 0 && rfaPath.Length == 0)
    throw new Exception("Choose a " + label + " type or a family file. Press Connect first so the lists are read; use the Folder... button to point to your family folder.");

string famFileName = rfaPath.Length > 0 ? Path.GetFileNameWithoutExtension(rfaPath) : "";
if (rfaPath.Length > 0)
{
    bool there = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Any(f => f.Name == famFileName);
    if (!there)
    {
        using (Transaction tl = new Transaction(doc, "CAD Bridge: load family"))
        {
            tl.Start();
            Family loadedFam;
            if (!doc.LoadFamily(rfaPath, out loadedFam)) { tl.RollBack(); throw new Exception("Revit could not load the family: " + rfaPath); }
            tl.Commit();
        }
        Log("Family loaded into the project: " + famFileName);
    }
    else Log("Family is already in the project: " + famFileName);
}

List<FamilySymbol> allSyms = new FilteredElementCollector(doc).OfCategory(cat).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
FamilySymbol baseSym;
if (rfaPath.Length > 0)
{
    baseSym = allSyms.Where(q => q.FamilyName == famFileName).OrderBy(q => q.Name).FirstOrDefault();
    if (baseSym == null) throw new Exception("The family '" + famFileName + "' has no " + label + " type. Is it really a " + label + " family?");
}
else
{
    string famPart = "", typePart = typeText;
    int cut = typePart.IndexOf(" : ");
    if (cut >= 0) { famPart = typePart.Substring(0, cut).Trim(); typePart = typePart.Substring(cut + 3).Trim(); }
    baseSym = allSyms.FirstOrDefault(q => q.Name == typePart && (famPart.Length == 0 || q.FamilyName == famPart));
    if (baseSym == null) throw new Exception(label + " type not found in this project: " + typeText + ". Press Connect to refresh the lists.");
}

// ---- which lines and which bays
HashSet<string> wanted = new HashSet<string>(Lines.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(q => q.Trim().ToUpperInvariant()));
bool allLines = wanted.Contains("ALL");
bool perimeterLines = wanted.Count == 0 || wanted.Contains("PERIMETER");
bool LineWanted(string gridName, bool outer)
{
    if (allLines) return true;
    if (wanted.Contains(gridName.Trim().ToUpperInvariant())) return true;
    return perimeterLines && outer;
}

HashSet<int> bayNumbers = new HashSet<int>();
bool allBays = Bays.Trim().Length == 0 || Bays.Trim().Equals("all", StringComparison.OrdinalIgnoreCase);
if (!allBays)
    foreach (string part in Bays.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
    {
        int bn;
        if (!int.TryParse(part.Trim(), out bn) || bn < 1) throw new Exception("Bays: cannot read '" + part.Trim() + "'. Use All or numbers like 1, 3.");
        bayNumbers.Add(bn);
    }
bool BayWanted(int bayNo) { return allBays || bayNumbers.Contains(bayNo); }

// the points: middle of every wanted bay
List<XYZ> points = new List<XYZ>();
if (!useSel) {
for (int i = 0; i < grid.NH; i++)
{
    if (!LineWanted(grid.H[i].Name, i == 0 || i == grid.NH - 1)) continue;
    for (int j = 1; j < grid.NV; j++)
        if (BayWanted(j)) points.Add((grid.Node[i, j - 1] + grid.Node[i, j]) / 2.0);
}
for (int j = 0; j < grid.NV; j++)
{
    if (!LineWanted(grid.V[j].Name, j == 0 || j == grid.NV - 1)) continue;
    for (int i = 1; i < grid.NH; i++)
        if (BayWanted(i)) points.Add((grid.Node[i - 1, j] + grid.Node[i, j]) / 2.0);
}
}
if (!useSel && points.Count == 0) throw new Exception("No bay selected. Check Lines and Bays.");

// openings that are already in the model
List<FamilyInstance> existing = new FilteredElementCollector(doc).OfCategory(cat).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
bool Taken(XYZ pt, Level lv)
{
    foreach (FamilyInstance old in existing)
    {
        LocationPoint lp = old.Location as LocationPoint;
        if (lp == null) continue;
        Level ol = Cb.LevelOf(doc, old);
        if (ol != null && !ol.Id.Equals(lv.Id)) continue;
        if (Math.Abs(lp.Point.X - pt.X) < 1.0 && Math.Abs(lp.Point.Y - pt.Y) < 1.0) return true;
    }
    return false;
}

int placed = 0, skippedCount = 0, noWall = 0, sillProblems = 0;
string usedType = baseSym.Name;

using (Transaction t = new Transaction(doc, "CAD Bridge: " + label + "s"))
{
    t.Start();

    FamilySymbol sym = baseSym;
    if (Width > 0 || Height > 0)
    {
        string newName = "CB W" + Cb.N(Width) + "xH" + Cb.N(Height);
        FamilySymbol sized = allSyms.FirstOrDefault(q => q.FamilyName == baseSym.FamilyName && q.Name == newName);
        if (sized == null) sized = (FamilySymbol)baseSym.Duplicate(newName);
        Parameter pw = Cb.Find(sized, "Width");
        Parameter ph = Cb.Find(sized, "Height");
        if (Width > 0 && pw != null && !pw.IsReadOnly) pw.Set(Cb.Ft(Width));
        if (Height > 0 && ph != null && !ph.IsReadOnly) ph.Set(Cb.Ft(Height));
        if ((Width > 0 && (pw == null || pw.IsReadOnly)) || (Height > 0 && (ph == null || ph.IsReadOnly)))
            Log("Note: this family has no editable Width / Height parameter in the type, so the size was not fully applied.");
        sym = sized;
        usedType = newName;
    }
    if (!sym.IsActive) sym.Activate();
    doc.Regenerate();

    Action<XYZ, Wall, Level> put = (pt, hostWall, lv) =>
    {
        XYZ place = new XYZ(pt.X, pt.Y, lv.Elevation);
        if (Taken(place, lv)) { skippedCount++; return; }
        FamilyInstance fi = doc.Create.NewFamilyInstance(place, sym, hostWall, lv, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        placed++;
        if (!isDoor)
        {
            Parameter ps = Cb.Find(fi, "Sill Height");
            if (ps != null && !ps.IsReadOnly) ps.Set(Cb.Ft(Sill)); else sillProblems++;
        }
    };

    if (useSel)
    {
        foreach (Wall w in selWalls)
        {
            LocationCurve wlc = w.Location as LocationCurve;
            Level wl = Cb.LevelOf(doc, w);
            if (wlc == null || wl == null) { noWall++; continue; }
            Curve c = wlc.Curve;
            List<double> fractions = new List<double>();
            if (Count == 1)
                fractions.Add(FromStart > 0 ? Math.Min(Cb.Ft(FromStart) / c.Length, 0.98) : 0.5);
            else
                for (int k = 0; k < Count; k++) fractions.Add((k + 0.5) / Count);
            foreach (double f in fractions) put(c.Evaluate(f, true), w, wl);
        }
    }
    else
    for (int lvIndex = i0; lvIndex <= i1; lvIndex++)
    {
        Level lv = levels[lvIndex];
        List<Wall> levelWalls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>()
            .Where(q => { Level wl = Cb.LevelOf(doc, q); return wl != null && wl.Id.Equals(lv.Id); }).ToList();

        foreach (XYZ pt in points)
        {
            Wall host = null;
            foreach (Wall cand in levelWalls)
            {
                LocationCurve lc = cand.Location as LocationCurve;
                if (lc == null) continue;
                XYZ onCurve = new XYZ(pt.X, pt.Y, lc.Curve.GetEndPoint(0).Z);
                if (lc.Curve.Distance(onCurve) < 0.05) { host = cand; break; }
            }
            if (host == null) { noWall++; continue; }

            put(pt, host, lv);
        }
    }

    t.Commit();
}

Log("Placed " + placed + " " + label + "(s) " + (useSel ? "in " + selWalls.Count + " selected wall(s)" : "on " + (i1 - i0 + 1) + " level(s) from " + levels[i0].Name + " to " + levels[i1].Name) +
    " | family: " + baseSym.FamilyName + " | type: " + usedType);
if (skippedCount > 0) Log("Skipped " + skippedCount + " places that already have a " + label + ".");
if (noWall > 0) Log("Note: " + noWall + " place(s) had no wall (run 08_Walls for those lines and levels, or change Lines / Bays).");
if (sillProblems > 0) Log("Note: could not set the sill height on " + sillProblems + " window(s). Check 'Sill Height' in Properties.");
