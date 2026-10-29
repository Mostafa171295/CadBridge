// Revit STEP 20: STRUCTURAL BILL OF QUANTITIES  (حصر الأعمال الإنشائية)  ->  Excel file. The model is NOT changed.
// The file is saved in the project's Files folder:  <FileName>_<date>_<time>.xlsx   (button "Open files folder").
// Sheets:  BOQ (bill with unit-rate and amount columns, live formulas)  |  ByLevel (volume / formwork / steel per level)
//          Detail (one line per element - filter it in Excel)  |  Notes (method, assumptions, warnings)
//
// WHAT IS MEASURED (from the real Revit elements, so it follows every change in the model):
//   Footings, Raft (foundation slab), Tie beams (beams on the FoundationLevel), Columns, Beams, Slabs (structural floors),
//   RC walls (bearing / shear walls, or wall types whose name contains one of RcWallWords).
//   Concrete volume = Revit's computed volume (already net of joins and openings).  Steel / timber families are left out.
//   Formwork       = faces that touch fresh concrete, taken from the 3D geometry:
//                    columns / footings / walls: sides only | beams: sides + soffit | slabs: soffit + edges.  (gross, joints not deducted)
//   Reinforcement  = volume x kg/m3 ratio of each element kind (an ESTIMATE - replace it with bar schedules when you have them).
//   Plain concrete, excavation and backfilling are calculated from the footings (see the options below).
//
// OPTIONS
//   FoundationLevel / GroundLevel : the level of the foundation and the natural ground level (excavation goes from Ground down to the footing bottom).
//   Excavation, ExcavationMode    : PerFooting = one pit for every footing (footing + PCExtra + WorkingSpace on every side),
//                                   Bulk = one big pit under all footings.
//   PlainConcrete, PCThickness, PCExtra : blinding under the footings (thickness, and how far it sticks out of the footing, mm).
//   Formwork = No                 : skip the formwork (faster on very big models).
//   FootingKg ... WallKg          : steel ratio in kg per m3 of concrete for each kind of element.  RebarWastePct adds waste / laps.
//   RcWallWords                   : words (separated by commas) that mark a wall type as reinforced concrete.
//   SaveFolder   : the folder where the Excel file is saved. Press Browse... to choose it (empty = the project's Files folder).
// Available variables: uiapp, doc, OutDir, Log(...)

string ProjectName = "{{ProjectName=}}";
string FileName = "{{FileName=BOQ_Structure}}";
string SaveFolder = @"{{SaveFolder=}}";                   // where the Excel file is saved (empty = the project's Files folder) - use the Browse button
string FoundationLevel = "{{FoundationLevel=@levels}}";   // list from Revit
string GroundLevel = "{{GroundLevel=@levels}}";           // list from Revit
string Excavation = "{{Excavation=Yes|No}}";
string ExcavationMode = "{{ExcavationMode=PerFooting|Bulk}}";
double WorkingSpace = {{WorkingSpace=500}};
string PlainConcrete = "{{PlainConcrete=Yes|No}}";
double PCThickness = {{PCThickness=100}};
double PCExtra = {{PCExtra=100}};
string Formwork = "{{Formwork=Yes|No}}";
double FootingKg = {{FootingKg=80}};
double TieKg = {{TieKg=110}};
double ColumnKg = {{ColumnKg=150}};
double BeamKg = {{BeamKg=120}};
double SlabKg = {{SlabKg=90}};
double WallKg = {{WallKg=100}};
double RebarWastePct = {{RebarWastePct=0}};
string RcWallWords = "{{RcWallWords=RC,Concrete,Shear,خرسان}}";

bool doForm = Cb.Yes(Formwork);
bool doPC = Cb.Yes(PlainConcrete);
bool doExc = Cb.Yes(Excavation);
bool bulk = ExcavationMode.Trim().StartsWith("B", StringComparison.OrdinalIgnoreCase);

// ---------------------------------------------------------------- levels
List<Level> levels = Cb.Levels(doc);
if (levels.Count == 0) throw new Exception("No level found in this project.");
Level FindLevel(string name) { string nm = (name ?? "").Trim(); return levels.FirstOrDefault(q => q.Name == nm); }
Level foundLv = FindLevel(FoundationLevel) ?? levels[0];
Level groundLv = FindLevel(GroundLevel);
if (groundLv == null || groundLv.Elevation <= foundLv.Elevation + 0.001)
    groundLv = levels.FirstOrDefault(q => q.Elevation > foundLv.Elevation + 0.001) ?? foundLv;
double gEl = groundLv.Elevation;      // feet

// ---------------------------------------------------------------- collect the elements
List<CbRow> rows = new List<CbRow>();
int zeroVol = 0, noLevel = 0, steelSkipped = 0;

void AddRow(Element e, string group, string kind, double kg)
{
    double volFt3 = Cb.Dbl(e, BuiltInParameter.HOST_VOLUME_COMPUTED);
    List<Solid> sol = null;
    if (doForm || volFt3 <= 0) sol = Cb.Solids(e);
    if (volFt3 <= 0 && sol != null) volFt3 = sol.Sum(q => q.Volume);
    if (volFt3 <= 1e-6) { zeroVol++; return; }

    Level lv = Cb.LevelOf(doc, e);
    if (lv == null) noLevel++;
    BoundingBoxXYZ bb = e.get_BoundingBox(null);

    CbRow item = new CbRow();
    item.Id = e.Id.ToString();
    item.Grp = group;
    item.Typ = Cb.TypeName(doc, e);
    item.Lvl = lv == null ? "(no level)" : lv.Name;
    item.LvlElev = lv == null ? -1e9 : lv.Elevation;
    item.Vol = Cb.M3(volFt3);
    item.Area = Cb.M2(Cb.Dbl(e, BuiltInParameter.HOST_AREA_COMPUTED));

    XYZ axis = null;
    LocationCurve lc = e.Location as LocationCurve;
    if (lc != null)
    {
        item.Len = Cb.M(lc.Curve.Length);
        Line ln = lc.Curve as Line;
        if (ln != null) axis = ln.Direction;
    }
    else if (bb != null && kind == "column") item.Len = Cb.M(bb.Max.Z - bb.Min.Z);

    if (bb != null)
    {
        item.MinX = bb.Min.X; item.MaxX = bb.Max.X;
        item.MinY = bb.Min.Y; item.MaxY = bb.Max.Y;
        item.MinZ = bb.Min.Z; item.MaxZ = bb.Max.Z;
    }
    if (doForm && sol != null) item.Form = Cb.M2(Cb.FormworkFt2(sol, kind, axis));
    item.Rebar = item.Vol * kg * (1.0 + RebarWastePct / 100.0);
    rows.Add(item);
}

foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
             .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList())
{
    if (!Cb.IsConcrete(fi)) { steelSkipped++; continue; }
    AddRow(fi, "Footings", "footing", FootingKg);
}

foreach (Floor fl in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
             .OfClass(typeof(Floor)).Cast<Floor>().ToList())
    AddRow(fl, "Raft", "slab", SlabKg);

foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
             .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList())
{
    if (!Cb.IsConcrete(fi)) { steelSkipped++; continue; }
    AddRow(fi, "Columns", "column", ColumnKg);
}

foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
             .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList())
{
    if (!Cb.IsConcrete(fi)) { steelSkipped++; continue; }
    Level blv = Cb.LevelOf(doc, fi);
    bool isTie = blv != null && blv.Id.Equals(foundLv.Id);
    AddRow(fi, isTie ? "Tie beams" : "Beams", "beam", isTie ? TieKg : BeamKg);
}

ElementId foundationCat = new ElementId(BuiltInCategory.OST_StructuralFoundation);
foreach (Floor fl in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList())
{
    if (fl.Category != null && fl.Category.Id.Equals(foundationCat)) continue;      // already counted as Raft
    Parameter pStruct = fl.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
    if (pStruct != null && pStruct.HasValue && pStruct.AsInteger() == 0) continue;  // finish floors belong to the architecture takeoff
    AddRow(fl, "Slabs", "slab", SlabKg);
}

string[] rcWords = Cb.Words(RcWallWords);
foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList())
{
    if (!Cb.IsRcWall(doc, w, rcWords)) continue;
    AddRow(w, "RC walls", "wall", WallKg);
}

if (rows.Count == 0)
    throw new Exception("No concrete elements with a volume were found. Model the structure first (scripts 01 - 09) or check that the families are concrete.");

// ---------------------------------------------------------------- earthworks and plain concrete (from the footings)
double Frac(CbRow q)        // the part of an element that is below the ground level (0 ... 1)
{
    double h = q.MaxZ - q.MinZ;
    if (h < 1e-6) return 0;
    double f = (gEl - q.MinZ) / h;
    return f < 0 ? 0 : (f > 1 ? 1 : f);
}

List<CbRow> foot = rows.Where(q => q.Grp == "Footings").ToList();
double pcT = Cb.Ft(PCThickness), pcX = Cb.Ft(PCExtra), ws = Cb.Ft(WorkingSpace);
double pcVol = 0, excVol = 0, backfill = 0, excDepthM = 0;

if (doPC && foot.Count > 0)
    foreach (CbRow ft in foot)
        pcVol += Cb.M3((ft.MaxX - ft.MinX + 2 * pcX) * (ft.MaxY - ft.MinY + 2 * pcX) * pcT);

if (doExc && foot.Count > 0)
{
    double lowest = foot.Min(q => q.MinZ) - (doPC ? pcT : 0);
    excDepthM = Cb.M(gEl - lowest);
    if (bulk)
    {
        double x0 = foot.Min(q => q.MinX) - pcX - ws, x1 = foot.Max(q => q.MaxX) + pcX + ws;
        double y0 = foot.Min(q => q.MinY) - pcX - ws, y1 = foot.Max(q => q.MaxY) + pcX + ws;
        if (gEl - lowest > 0) excVol = Cb.M3((x1 - x0) * (y1 - y0) * (gEl - lowest));
    }
    else
    {
        foreach (CbRow ft in foot)
        {
            double depth = gEl - (ft.MinZ - (doPC ? pcT : 0));
            if (depth <= 0) continue;
            excVol += Cb.M3((ft.MaxX - ft.MinX + 2 * (pcX + ws)) * (ft.MaxY - ft.MinY + 2 * (pcX + ws)) * depth);
        }
    }
    double below = 0;
    foreach (CbRow rw in rows)
    {
        bool inPit = rw.Grp == "Footings" || rw.Grp == "Columns" ||
                     (bulk && (rw.Grp == "Tie beams" || rw.Grp == "RC walls" || rw.Grp == "Raft"));
        if (inPit) below += rw.Vol * Frac(rw);
    }
    backfill = Math.Max(0, excVol - pcVol - below);
}

// ---------------------------------------------------------------- Excel
string[] order = { "Footings", "Raft", "Tie beams", "Columns", "Beams", "Slabs", "RC walls" };
int GroupOrder(string g) { int k = Array.IndexOf(order, g); return k < 0 ? 99 : k; }
rows = rows.OrderBy(q => GroupOrder(q.Grp)).ThenBy(q => q.LvlElev).ThenBy(q => q.Typ).ThenBy(q => q.Id).ToList();

var meta = new[]
{
    Tuple.Create("Footings", "R.C. isolated footings", "خرسانة مسلحة للقواعد المنفصلة", "القواعد"),
    Tuple.Create("Raft", "R.C. raft / foundation slab", "خرسانة مسلحة للبلاطة المسلحة (اللبشة)", "اللبشة"),
    Tuple.Create("Tie beams", "R.C. tie beams (grade beams)", "خرسانة مسلحة للميدات", "الميدات"),
    Tuple.Create("Columns", "R.C. columns", "خرسانة مسلحة للأعمدة", "الأعمدة"),
    Tuple.Create("Beams", "R.C. beams", "خرسانة مسلحة للكمرات", "الكمرات"),
    Tuple.Create("Slabs", "R.C. slabs", "خرسانة مسلحة للبلاطات", "البلاطات"),
    Tuple.Create("RC walls", "R.C. walls / shear walls", "خرسانة مسلحة للحوائط وحوائط القص", "الحوائط")
};
var present = meta.Where(q => rows.Any(z => z.Grp == q.Item1)).ToList();

double KgOf(string g)
{
    if (g == "Footings") return FootingKg;
    if (g == "Tie beams") return TieKg;
    if (g == "Columns") return ColumnKg;
    if (g == "Beams") return BeamKg;
    if (g == "RC walls") return WallKg;
    return SlabKg;                      // Slabs and Raft
}

int last = 4 + rows.Count;              // last data row of the Detail sheet
string Rng(string colLetter) { return "Detail!$" + colLetter + "$5:$" + colLetter + "$" + last; }
string SumIf(string colLetter, string grp) { return "SUMIFS(" + Rng(colLetter) + "," + Rng("C") + ",\"" + grp + "\")"; }

string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
string subtitle = (ProjectName.Trim().Length > 0 ? ProjectName.Trim() + "   |   " : "") + "Revit model: " + doc.Title + "   |   " + stamp +
                  "   |   Units: m, m², m³, kg, ton";

// ---- BOQ sheet
CbBoq boq = new CbBoq("Bill of Quantities - Structural Works   |   حصر الأعمال الإنشائية", subtitle);

if (doExc && foot.Count > 0)
{
    boq.Section("Earthworks", "أعمال الحفر والردم");
    boq.Item("Excavation to foundation level (" + (bulk ? "bulk pit" : "pit for every footing") + ", average depth " + Cb.N(Math.Round(excDepthM, 2)) + " m)",
             "حفر لمنسوب التأسيس", "m³", Math.Round(excVol, 2), null,
             "From ground level '" + groundLv.Name + "' to the footing bottom" + (doPC ? " minus the plain concrete" : "") +
             "; plan = footing + " + Cb.N(PCExtra) + " mm + working space " + Cb.N(WorkingSpace) + " mm each side.");
    boq.Item("Backfilling around the foundations", "ردم حول الأساسات", "m³", Math.Round(backfill, 2), null,
             "Excavation - plain concrete - concrete below ground level (" + (bulk ? "footings, ties, columns, walls" : "footings and column necks") + ").");
    boq.EndSection();
}
if (doPC && foot.Count > 0)
{
    boq.Section("Plain concrete", "الخرسانة العادية");
    boq.Item("Plain concrete under footings, " + Cb.N(PCThickness) + " mm thick", "خرسانة عادية تحت القواعد بسمك " + Cb.N(PCThickness) + " مم", "m³",
             Math.Round(pcVol, 2), null, "Footing plan + " + Cb.N(PCExtra) + " mm on every side.");
    boq.EndSection();
}

boq.Section("Reinforced concrete - concrete only", "الخرسانة المسلحة - الخرسانة فقط");
foreach (var mt in present)
{
    double v = rows.Where(q => q.Grp == mt.Item1).Sum(q => q.Vol);
    boq.Item(mt.Item2, mt.Item3, "m³", Math.Round(v, 2), SumIf("H", mt.Item1),
             rows.Count(q => q.Grp == mt.Item1) + " element(s) - Revit net volume.");
}
boq.EndSection();

if (doForm)
{
    boq.Section("Formwork", "أعمال النجارة (الشدات)");
    foreach (var mt in present)
    {
        double f = rows.Where(q => q.Grp == mt.Item1).Sum(q => q.Form);
        boq.Item("Formwork to " + mt.Item1.ToLower(), "نجارة مسلحة لـ" + mt.Item4, "m²", Math.Round(f, 2), SumIf("I", mt.Item1),
                 "Gross contact area from the 3D faces; joints between members are not deducted.");
    }
    boq.EndSection();
}

boq.Section("Reinforcing steel (estimate)", "حديد التسليح (تقديري)");
foreach (var mt in present)
{
    double kgs = rows.Where(q => q.Grp == mt.Item1).Sum(q => q.Rebar);
    boq.Item("Reinforcing steel in " + mt.Item1.ToLower(), "حديد تسليح " + mt.Item4, "ton", Math.Round(kgs / 1000.0, 3), SumIf("J", mt.Item1) + "/1000",
             "Ratio " + Cb.N(KgOf(mt.Item1)) + " kg/m³" + (RebarWastePct > 0 ? " + " + Cb.N(RebarWastePct) + " % waste" : "") + ".");
}
boq.EndSection();
boq.Finish();

// ---- Detail sheet
CbSheet det = CbXlsx.Table("Detail", "Element list   |   قائمة العناصر", subtitle,
    new[] { "#", "Element ID", "Group", "Type (Family : Type)", "Level", "Length (m)", "Area (m²)", "Volume (m³)", "Formwork (m²)", "Rebar (kg)" },
    new double[] { 6, 12, 14, 52, 18, 12, 12, 14, 15, 14 });
int dr = 5;
foreach (CbRow rw in rows)
{
    det.Set(dr, 1, dr - 4, CbXlsx.S_INT);
    det.Set(dr, 2, rw.Id, CbXlsx.S_CTR);
    det.Set(dr, 3, rw.Grp, CbXlsx.S_TEXT);
    det.Set(dr, 4, rw.Typ, CbXlsx.S_TEXT);
    det.Set(dr, 5, rw.Lvl, CbXlsx.S_TEXT);
    det.Set(dr, 6, Math.Round(rw.Len, 3), CbXlsx.S_NUM);
    det.Set(dr, 7, Math.Round(rw.Area, 3), CbXlsx.S_NUM);
    det.Set(dr, 8, Math.Round(rw.Vol, 4), CbXlsx.S_NUM);
    det.Set(dr, 9, Math.Round(rw.Form, 3), CbXlsx.S_NUM);
    det.Set(dr, 10, Math.Round(rw.Rebar, 2), CbXlsx.S_NUM);
    dr++;
}
for (int k = 1; k <= 7; k++) det.Set(dr, k, null, CbXlsx.S_TOTL);
det.Set(dr, 3, "TOTAL", CbXlsx.S_TOTL);
det.Formula(dr, 8, "SUBTOTAL(109,H5:H" + last + ")", rows.Sum(q => q.Vol), CbXlsx.S_TOTN);
det.Formula(dr, 9, "SUBTOTAL(109,I5:I" + last + ")", rows.Sum(q => q.Form), CbXlsx.S_TOTN);
det.Formula(dr, 10, "SUBTOTAL(109,J5:J" + last + ")", rows.Sum(q => q.Rebar), CbXlsx.S_TOTN);
det.AutoFilter = "A4:J" + last;

// ---- ByLevel sheet (volume, formwork and steel of every group on every level)
List<string> lvNames = rows.OrderBy(q => q.LvlElev).Select(q => q.Lvl).Distinct().ToList();
List<string> grpNames = present.Select(q => q.Item1).ToList();
CbSheet bl = new CbSheet("ByLevel");
bl.Width(1, 24);
for (int k = 0; k < lvNames.Count + 1; k++) bl.Width(2 + k, 15);
bl.Set(1, 1, "Quantities per level   |   الكميات لكل دور", CbXlsx.S_TITLE);
bl.Set(2, 1, subtitle, CbXlsx.S_NOTE);
int br = 4;

void LevelBlock(string title, string colLetter, double div, Func<CbRow, double> sel)
{
    bl.Set(br, 1, title, CbXlsx.S_SECT);
    for (int k2 = 0; k2 <= lvNames.Count; k2++) bl.Set(br, 2 + k2, null, CbXlsx.S_SECT);
    br++;
    bl.Set(br, 1, "Group", CbXlsx.S_HEAD);
    for (int k2 = 0; k2 < lvNames.Count; k2++) bl.Set(br, 2 + k2, lvNames[k2], CbXlsx.S_HEAD);
    bl.Set(br, 2 + lvNames.Count, "Total", CbXlsx.S_HEAD);
    int hdr = br;
    br++;
    int firstRow = br;
    foreach (string gname in grpNames)
    {
        bl.Set(br, 1, gname, CbXlsx.S_TEXT);
        for (int k2 = 0; k2 < lvNames.Count; k2++)
        {
            string lvName = lvNames[k2];
            double cv = rows.Where(q => q.Grp == gname && q.Lvl == lvName).Sum(sel) / div;
            string fx = "SUMIFS(" + Rng(colLetter) + "," + Rng("C") + ",$A" + br + "," + Rng("E") + "," + CbXlsx.Col(2 + k2) + "$" + hdr + ")" +
                        (div == 1.0 ? "" : "/" + Cb.N(div));
            bl.Formula(br, 2 + k2, fx, cv, CbXlsx.S_NUM);
        }
        bl.Formula(br, 2 + lvNames.Count, "SUM(B" + br + ":" + CbXlsx.Col(1 + lvNames.Count) + br + ")",
                   rows.Where(q => q.Grp == gname).Sum(sel) / div, CbXlsx.S_TOTN);
        br++;
    }
    bl.Set(br, 1, "Total", CbXlsx.S_TOTL);
    for (int k2 = 0; k2 <= lvNames.Count; k2++)
    {
        string cl = CbXlsx.Col(2 + k2);
        double colSum = k2 < lvNames.Count ? rows.Where(q => q.Lvl == lvNames[k2] && grpNames.Contains(q.Grp)).Sum(sel) / div
                                           : rows.Sum(sel) / div;
        bl.Formula(br, 2 + k2, "SUM(" + cl + firstRow + ":" + cl + (br - 1) + ")", colSum, CbXlsx.S_TOTN);
    }
    br += 3;
}
LevelBlock("Concrete volume (m³)   |   حجم الخرسانة", "H", 1.0, q => q.Vol);
if (doForm) LevelBlock("Formwork (m²)   |   النجارة", "I", 1.0, q => q.Form);
LevelBlock("Reinforcing steel (ton)   |   الحديد", "J", 1000.0, q => q.Rebar);

// ---- Notes sheet
double totV = rows.Sum(q => q.Vol), totF = rows.Sum(q => q.Form), totS = rows.Sum(q => q.Rebar);
List<string> notes = new List<string>();
notes.Add("# Summary");
notes.Add("Elements measured: " + rows.Count + "   |   Concrete: " + Cb.N(Math.Round(totV, 2)) + " m³   |   Formwork: " + Cb.N(Math.Round(totF, 2)) +
          " m²   |   Steel: " + Cb.N(Math.Round(totS / 1000.0, 2)) + " ton   (" + (totV > 0 ? Cb.N(Math.Round(totS / totV, 1)) : "0") + " kg/m³)");
notes.Add("Foundation level: " + foundLv.Name + "   |   Ground level: " + groundLv.Name);
notes.Add("# Method");
notes.Add("Concrete volume: Revit's computed volume of every element (net of joins and openings).");
notes.Add("Formwork: contact faces taken from the 3D geometry. Columns, footings, walls = sides; beams = sides + soffit; slabs = soffit + edges. Gross area - the overlap at joints is not deducted.");
notes.Add("Reinforcement: ESTIMATE = concrete volume x ratio. Footings " + Cb.N(FootingKg) + ", tie beams " + Cb.N(TieKg) + ", columns " + Cb.N(ColumnKg) +
          ", beams " + Cb.N(BeamKg) + ", slabs / raft " + Cb.N(SlabKg) + ", walls " + Cb.N(WallKg) + " kg/m³" + (RebarWastePct > 0 ? ", waste " + Cb.N(RebarWastePct) + " %." : "."));
notes.Add("Plain concrete: " + (doPC ? "thickness " + Cb.N(PCThickness) + " mm, projecting " + Cb.N(PCExtra) + " mm beyond every footing." : "not calculated."));
notes.Add("Excavation: " + (doExc ? (bulk ? "one bulk pit under all footings" : "a pit under every footing (pits that overlap are counted twice; tie-beam trenches are not included)") +
          ", working space " + Cb.N(WorkingSpace) + " mm, from '" + groundLv.Name + "' down to the footing bottom." : "not calculated."));
notes.Add("Backfilling: excavation - plain concrete - the concrete that lies below the ground level.");
notes.Add("Tie beams = structural framing whose reference level is '" + foundLv.Name + "'. Walls count as RC when they are bearing / shear or their type name contains: " + string.Join(", ", rcWords) + ".");
notes.Add("# Checks");
notes.Add("Steel / timber families left out: " + steelSkipped + "   |   Elements without volume (skipped): " + zeroVol + "   |   Elements without a level: " + noLevel);
if (zeroVol > 0) notes.Add("WARNING: " + zeroVol + " element(s) have no volume (not cut / not solid / in a linked file). They are NOT in the quantities.");
if (noLevel > 0) notes.Add("WARNING: " + noLevel + " element(s) have no level; they appear under '(no level)'.");
if (foot.Count == 0) notes.Add("No isolated footings found, so earthworks and plain concrete were not calculated.");
notes.Add("# How to use");
notes.Add("Type the unit rates in the yellow cells of the BOQ sheet. Amounts and totals are calculated by Excel. The Detail sheet can be filtered; the BOQ and ByLevel quantities follow it automatically (they read the Detail sheet).");
CbSheet notesSheet = CbXlsx.Notes("Notes", "Assumptions and checks   |   الافتراضات والمراجعة", notes);

// ---------------------------------------------------------------- save
string safe = string.IsNullOrWhiteSpace(FileName) ? "BOQ_Structure" : FileName.Trim();
foreach (char badChar in Path.GetInvalidFileNameChars()) safe = safe.Replace(badChar.ToString(), "_");
string saveDir = string.IsNullOrWhiteSpace(SaveFolder) ? OutDir : SaveFolder.Trim();
Directory.CreateDirectory(saveDir);
string outPath = Path.Combine(saveDir, safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx");
CbXlsx.Save(outPath, new List<CbSheet> { boq.Sheet, bl, det, notesSheet });

Log("Structural BOQ saved: " + outPath);
foreach (var mt in present)
    Log(mt.Item1.PadRight(10) + "  " + rows.Count(q => q.Grp == mt.Item1).ToString().PadLeft(4) + " elements   concrete " +
        Cb.N(Math.Round(rows.Where(q => q.Grp == mt.Item1).Sum(q => q.Vol), 2)) + " m3" +
        (doForm ? "   formwork " + Cb.N(Math.Round(rows.Where(q => q.Grp == mt.Item1).Sum(q => q.Form), 2)) + " m2" : ""));
Log("TOTAL concrete " + Cb.N(Math.Round(totV, 2)) + " m3   |   steel " + Cb.N(Math.Round(totS / 1000.0, 2)) + " ton" +
    (doPC && foot.Count > 0 ? "   |   plain concrete " + Cb.N(Math.Round(pcVol, 2)) + " m3" : "") +
    (doExc && foot.Count > 0 ? "   |   excavation " + Cb.N(Math.Round(excVol, 2)) + " m3   |   backfill " + Cb.N(Math.Round(backfill, 2)) + " m3" : ""));
if (zeroVol > 0) Log("Note: " + zeroVol + " element(s) have no volume and were skipped (see the Notes sheet).");
