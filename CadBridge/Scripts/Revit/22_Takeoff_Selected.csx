// Revit STEP 22: QUANTITIES (حصر) of the elements you SELECT in Revit  ->  Excel file. The model is NOT changed.
// How to use:  1) In Revit select the elements (click, Ctrl+click, window selection - any category, mixed is fine).
//              2) Press Run code here.  The file is saved in the project's Files folder (button "Open files folder").
//   Scope        : Selected = what is selected now in Revit.  Category = every element of the chosen Category.
//   Category     : used only when Scope = Category.
//   OnLevel      : optional filter - exact level name (empty = any).   TypeContains : optional filter - part of the type name.
//   Formwork     : Yes = also measure the formwork area of concrete elements (faces that touch fresh concrete, from the 3D geometry).
//   SteelKgPerM3 : estimated steel in kg per m3 of concrete (0 = leave steel out). Applied to the concrete elements only.
//   FileName     : name of the Excel file (date and time are added).
// What is measured for each element (Revit's own numbers, in metres):
//   Count | Length (walls, beams, railings...) | Area (walls: side area net of openings, floors / roofs: area) | Volume |
//   Doors and windows: width x height and the opening area.
// Excel sheets:  Summary (grouped by category and type, live formulas)  |  Detail (one line per element, filterable)  |  Notes
//   SaveFolder   : the folder where the Excel file is saved. Press Browse... to choose it (empty = the project's Files folder).
// Available variables: uiapp, doc, OutDir, Log(...)

string Scope = "{{Scope=Selected|Category}}";
string Category = "{{Category=Walls|Doors|Windows|Columns|Beams|Footings|Floors|Roofs|Railings|Stairs}}";
string OnLevel = "{{OnLevel=}}";
string TypeContains = "{{TypeContains=}}";
string Formwork = "{{Formwork=No|Yes}}";
double SteelKgPerM3 = {{SteelKgPerM3=0}};
string FileName = "{{FileName=BOQ_Selected}}";
string SaveFolder = @"{{SaveFolder=}}";                   // where the Excel file is saved (empty = the project's Files folder) - use the Browse button

// ---- 1) collect the elements
List<Element> els = new List<Element>();
if (Scope.Trim().StartsWith("S", StringComparison.OrdinalIgnoreCase))
{
    foreach (ElementId sid in uiapp.ActiveUIDocument.Selection.GetElementIds())
    {
        Element e = doc.GetElement(sid);
        if (e != null && !(e is ElementType) && e.Category != null) els.Add(e);
    }
    if (els.Count == 0)
        throw new Exception("Nothing is selected in Revit. Select the element(s) in Revit first, then press Run code again.");
}
else
{
    BuiltInCategory bic;
    switch (Category.Trim().ToLowerInvariant())
    {
        case "doors": bic = BuiltInCategory.OST_Doors; break;
        case "windows": bic = BuiltInCategory.OST_Windows; break;
        case "columns": bic = BuiltInCategory.OST_StructuralColumns; break;
        case "beams": bic = BuiltInCategory.OST_StructuralFraming; break;
        case "footings": bic = BuiltInCategory.OST_StructuralFoundation; break;
        case "floors": bic = BuiltInCategory.OST_Floors; break;
        case "roofs": bic = BuiltInCategory.OST_Roofs; break;
        case "railings": bic = BuiltInCategory.OST_StairsRailing; break;
        case "stairs": bic = BuiltInCategory.OST_Stairs; break;
        default: bic = BuiltInCategory.OST_Walls; break;
    }
    els = new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType().ToList();
}
string lvName = (OnLevel ?? "").Trim();
if (lvName.Length > 0)
    els = els.Where(e => { Level l = Cb.LevelOf(doc, e); return l != null && l.Name == lvName; }).ToList();
string tc = (TypeContains ?? "").Trim();
if (tc.Length > 0)
    els = els.Where(e => Cb.TypeName(doc, e).IndexOf(tc, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
if (els.Count == 0) throw new Exception("No element matches. Check Scope, Category, OnLevel and TypeContains.");

bool doForm = Cb.Yes(Formwork);
string[] rcWords = { "RC", "Concrete", "Shear", "خرسان" };

// ---- 2) measure
// one detail row = { id, category, type, level, count, length m, area m2, volume m3, formwork m2, steel kg, size text }
List<object[]> rows = new List<object[]>();
int noVolume = 0, noLevel = 0;

foreach (Element e in els)
{
    string cat = e.Category.Name;
    string typ = Cb.TypeName(doc, e);
    Level lv = Cb.LevelOf(doc, e);
    string lvl = lv != null ? lv.Name : "(no level)";
    if (lv == null) noLevel++;

    double len = Cb.M(Cb.Dbl(e, BuiltInParameter.CURVE_ELEM_LENGTH));
    LocationCurve lc = e.Location as LocationCurve;
    if (len <= 0 && lc != null) len = Cb.M(lc.Curve.Length);
    double area = Cb.M2(Cb.Dbl(e, BuiltInParameter.HOST_AREA_COMPUTED));
    double vol = Cb.M3(Cb.Dbl(e, BuiltInParameter.HOST_VOLUME_COMPUTED));
    string size = "";

    FamilyInstance fi = e as FamilyInstance;
    bool isOpening = e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors || e.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows;
    if (isOpening && fi != null)
    {
        double w = Cb.OpenSize(doc, fi, true), h = Cb.OpenSize(doc, fi, false);
        if (w > 0 && h > 0)
        {
            area = Cb.M2(w * h);
            size = Cb.N(Math.Round(Cb.Mm(w))) + " x " + Cb.N(Math.Round(Cb.Mm(h)));
        }
    }
    if (vol <= 0 && !isOpening)
    {
        try
        {
            double v = 0;
            foreach (Solid s in Cb.Solids(e)) v += s.Volume;
            vol = Cb.M3(v);
        }
        catch { }
    }
    if (vol <= 0 && !isOpening) noVolume++;

    // concrete element? (only these get formwork and steel)
    string kind = null;
    int cid = e.Category.Id.IntegerValue;
    if (cid == (int)BuiltInCategory.OST_StructuralColumns && fi != null && Cb.IsConcrete(fi)) kind = "column";
    else if (cid == (int)BuiltInCategory.OST_StructuralFoundation && fi != null && Cb.IsConcrete(fi)) kind = "footing";
    else if (cid == (int)BuiltInCategory.OST_StructuralFraming && fi != null && Cb.IsConcrete(fi)) kind = "beam";
    else if (cid == (int)BuiltInCategory.OST_Floors) kind = "slab";
    else if (cid == (int)BuiltInCategory.OST_Walls && e is Wall && Cb.IsRcWall(doc, (Wall)e, rcWords)) kind = "wall";

    double form = 0, steel = 0;
    if (kind != null)
    {
        if (doForm)
        {
            XYZ axis = null;
            if (lc != null) { Line ln = lc.Curve as Line; if (ln != null) axis = ln.Direction; }
            try { form = Cb.M2(Cb.FormworkFt2(Cb.Solids(e), kind, axis)); } catch { form = 0; }
        }
        if (SteelKgPerM3 > 0) steel = vol * SteelKgPerM3;
    }

    rows.Add(new object[] { e.Id.IntegerValue, cat, typ, lvl, 1, len, area, vol, form, steel, size });
}

// ---- 3) Excel
string subtitle = "Elements: " + rows.Count + "   |   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "   |   units: m, m2, m3, kg";

CbSheet det = CbXlsx.Table("Detail", "Element list   |   قائمة العناصر", subtitle,
    new[] { "#", "Element Id", "Category", "Type", "Level", "Count", "Length (m)", "Area (m2)", "Volume (m3)", "Formwork (m2)", "Steel (kg)", "Size W x H (mm)" },
    new double[] { 5, 11, 22, 42, 16, 8, 12, 12, 12, 14, 12, 18 });
int dr = 5;
foreach (object[] r in rows)
{
    det.Set(dr, 1, dr - 4, CbXlsx.S_INT);
    det.Set(dr, 2, (int)r[0], CbXlsx.S_CTR);
    det.Set(dr, 3, (string)r[1], CbXlsx.S_TEXT);
    det.Set(dr, 4, (string)r[2], CbXlsx.S_TEXT);
    det.Set(dr, 5, (string)r[3], CbXlsx.S_TEXT);
    det.Set(dr, 6, (int)r[4], CbXlsx.S_INT);
    det.Set(dr, 7, Math.Round((double)r[5], 3), CbXlsx.S_NUM);
    det.Set(dr, 8, Math.Round((double)r[6], 3), CbXlsx.S_NUM);
    det.Set(dr, 9, Math.Round((double)r[7], 4), CbXlsx.S_NUM);
    det.Set(dr, 10, Math.Round((double)r[8], 3), CbXlsx.S_NUM);
    det.Set(dr, 11, Math.Round((double)r[9], 1), CbXlsx.S_NUM);
    det.Set(dr, 12, (string)r[10], CbXlsx.S_TEXT);
    dr++;
}
int last = dr - 1;
for (int k = 1; k <= 12; k++) det.Set(dr, k, null, CbXlsx.S_TOTL);
det.Set(dr, 3, "TOTAL", CbXlsx.S_TOTL);
string[] colL = { "F", "G", "H", "I", "J", "K" };
double[] colSums = {
    rows.Sum(q => (int)q[4]), rows.Sum(q => (double)q[5]), rows.Sum(q => (double)q[6]),
    rows.Sum(q => (double)q[7]), rows.Sum(q => (double)q[8]), rows.Sum(q => (double)q[9]) };
for (int k = 0; k < 6; k++)
    det.Formula(dr, 6 + k, "SUBTOTAL(109," + colL[k] + "5:" + colL[k] + last + ")", colSums[k], CbXlsx.S_TOTN);
det.AutoFilter = "A4:L" + last;

CbSheet sum = CbXlsx.Table("Summary", "Quantities of the selected elements   |   حصر العناصر المختارة", subtitle,
    new[] { "Category", "Type", "Count", "Length (m)", "Area (m2)", "Volume (m3)", "Formwork (m2)", "Steel (kg)" },
    new double[] { 22, 46, 9, 13, 13, 13, 14, 13 });
var groups = rows.GroupBy(q => (string)q[1] + "\u0001" + (string)q[2]).OrderBy(g => g.Key).ToList();
int sr = 5;
string rng = "Detail!$";
foreach (var g in groups)
{
    object[] first = g.First();
    sum.Set(sr, 1, (string)first[1], CbXlsx.S_TEXT);
    sum.Set(sr, 2, (string)first[2], CbXlsx.S_TEXT);
    string crit = "Detail!$C$5:$C$" + last + ",$A" + sr + ",Detail!$D$5:$D$" + last + ",$B" + sr;
    sum.Formula(sr, 3, "SUMIFS(Detail!$F$5:$F$" + last + "," + crit + ")", g.Sum(q => (int)q[4]), CbXlsx.S_INT);
    for (int k = 1; k < 6; k++)
    {
        int idx = 4 + k;                       // 5..9 in the object[] = length..steel
        string col = colL[k];
        sum.Formula(sr, 3 + k, "SUMIFS(Detail!$" + col + "$5:$" + col + "$" + last + "," + crit + ")", g.Sum(q => (double)q[idx]), CbXlsx.S_NUM);
    }
    sr++;
}
int sLast = sr - 1;
for (int k = 1; k <= 8; k++) sum.Set(sr, k, null, CbXlsx.S_TOTL);
sum.Set(sr, 2, "TOTAL", CbXlsx.S_TOTL);
string[] sumL = { "C", "D", "E", "F", "G", "H" };
for (int k = 0; k < 6; k++)
    sum.Formula(sr, 3 + k, "SUM(" + sumL[k] + "5:" + sumL[k] + sLast + ")", colSums[k], CbXlsx.S_TOTN);

List<string> notes = new List<string>();
notes.Add("# What was measured");
notes.Add("Scope: " + (Scope.Trim().StartsWith("S", StringComparison.OrdinalIgnoreCase) ? "the elements selected in Revit" : "category " + Category) +
          (lvName.Length > 0 ? "   |   level: " + lvName : "") + (tc.Length > 0 ? "   |   type contains: " + tc : ""));
notes.Add("Length, area and volume are Revit's own computed values. Wall and floor area is the net area (openings already deducted). Door / window area = width x height.");
notes.Add("Doors and windows are separate lines. If you select a wall AND its doors, the wall area is already net of the doors - do not subtract them again.");
notes.Add("Formwork and steel are given only for concrete elements (concrete columns / footings / beams, floors, RC walls). Formwork is " + (doForm ? "measured from the 3D faces (gross)." : "not measured (Formwork = No).") +
          "   Steel: " + (SteelKgPerM3 > 0 ? Cb.N(SteelKgPerM3) + " kg/m3 estimate x concrete volume." : "not calculated (SteelKgPerM3 = 0)."));
if (noVolume > 0) notes.Add("WARNING: " + noVolume + " element(s) have no volume (not solid / in a linked file). Their volume is 0.");
if (noLevel > 0) notes.Add("Note: " + noLevel + " element(s) have no level; they appear as '(no level)'.");
CbSheet notesSheet = CbXlsx.Notes("Notes", "Assumptions and checks   |   الافتراضات والمراجعة", notes);

string safe = string.IsNullOrWhiteSpace(FileName) ? "BOQ_Selected" : FileName.Trim();
foreach (char badChar in Path.GetInvalidFileNameChars()) safe = safe.Replace(badChar.ToString(), "_");
string saveDir = string.IsNullOrWhiteSpace(SaveFolder) ? OutDir : SaveFolder.Trim();
Directory.CreateDirectory(saveDir);
string outPath = Path.Combine(saveDir, safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx");
CbXlsx.Save(outPath, new List<CbSheet> { sum, det, notesSheet });

Log("Quantities saved: " + outPath);
foreach (var g in groups)
{
    object[] f0 = g.First();
    Log(g.Count().ToString().PadLeft(4) + " x " + (string)f0[1] + " | " + (string)f0[2] +
        "   len " + Cb.N(Math.Round(g.Sum(q => (double)q[5]), 2)) + " m   area " + Cb.N(Math.Round(g.Sum(q => (double)q[6]), 2)) +
        " m2   vol " + Cb.N(Math.Round(g.Sum(q => (double)q[7]), 3)) + " m3");
}
Log("TOTAL " + rows.Count + " element(s)   |   area " + Cb.N(Math.Round(colSums[2], 2)) + " m2   |   volume " + Cb.N(Math.Round(colSums[3], 3)) + " m3");
if (noVolume > 0) Log("Note: " + noVolume + " element(s) have no volume.");
