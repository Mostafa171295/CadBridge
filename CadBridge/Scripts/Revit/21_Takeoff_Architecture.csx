// Revit STEP 21: ARCHITECTURAL BILL OF QUANTITIES  (حصر الأعمال المعمارية)  ->  Excel file. The model is NOT changed.
// Saved in the project's Files folder:  <FileName>_<date>_<time>.xlsx
// Sheets:  BOQ (bill with rate / amount columns)  |  Walls  |  Rooms (finish schedule)  |  Openings (doors, windows, lintels)  |  Notes
//
// WHAT IS MEASURED
//   Masonry walls  : every wall that is NOT reinforced concrete (RC walls are in 20_Takeoff_Structure). Net area = Revit's wall area,
//                    already reduced by doors / windows. Grouped by exterior / interior and by wall type (with thickness).
//   Plaster        : internal = the rooms (perimeter x height - openings), external = net area of the exterior-function walls (one face).
//                    Without rooms the script falls back to 2 x interior walls + 1 x exterior walls and says so in the Notes sheet.
//   Finishes       : taken from the ROOMS (Floor / Wall / Ceiling / Base finish text of each room): floors m2, wall finish m2,
//                    ceilings m2, skirting m (room perimeter - door widths).  Create the rooms with 11_Rooms.
//   Openings       : doors and windows by type and size (count, area), window sills (m), door thresholds (m),
//                    RC lintels over openings in masonry walls (length m, volume m3).
//   Roofs / floors : roof area by type, and non-structural (finish) floors by type.
//   Stairs / railings : stairs by type (number of stairs and risers), railings by type (length in metres).
// OPTIONS
//   Lintels = No           : do not calculate lintels.     LintelBearing = bearing on each side (mm).   LintelHeight = lintel depth (mm).
//   RoomHeightFallback     : room height (mm) used when Revit reports no height for a room.
//   RcWallWords            : wall types whose name contains one of these words are treated as concrete (and skipped here).
//   SaveFolder   : the folder where the Excel file is saved. Press Browse... to choose it (empty = the project's Files folder).
// Available variables: uiapp, doc, OutDir, Log(...)

string ProjectName = "{{ProjectName=}}";
string FileName = "{{FileName=BOQ_Architecture}}";
string SaveFolder = @"{{SaveFolder=}}";                   // where the Excel file is saved (empty = the project's Files folder) - use the Browse button
string Lintels = "{{Lintels=Yes|No}}";
double LintelBearing = {{LintelBearing=200}};
double LintelHeight = {{LintelHeight=200}};
double RoomHeightFallback = {{RoomHeightFallback=3000}};
string RcWallWords = "{{RcWallWords=RC,Concrete,Shear,خرسان}}";

string[] rcWords = Cb.Words(RcWallWords);
bool lintelsOn = Cb.Yes(Lintels);
List<string> warnings = new List<string>();

// ---------------------------------------------------------------- masonry walls
List<CbRow> walls = new List<CbRow>();
double extArea = 0, intArea = 0;
int rcSkipped = 0, noAreaWalls = 0;

foreach (Wall w in new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList())
{
    WallType wt = doc.GetElement(w.GetTypeId()) as WallType;
    if (wt != null && wt.Kind == WallKind.Curtain) continue;
    if (Cb.IsRcWall(doc, w, rcWords)) { rcSkipped++; continue; }
    double areaM2 = Cb.M2(Cb.Dbl(w, BuiltInParameter.HOST_AREA_COMPUTED));
    if (areaM2 <= 0) { noAreaWalls++; continue; }
    bool isExt = wt != null && wt.Function == WallFunction.Exterior;
    Level wl = Cb.LevelOf(doc, w);
    CbRow wr = new CbRow();
    wr.Id = w.Id.ToString();
    wr.Grp = isExt ? "External walls" : "Internal walls";
    wr.Typ = Cb.TypeName(doc, w);
    wr.Lvl = wl == null ? "(no level)" : wl.Name;
    wr.LvlElev = wl == null ? -1e9 : wl.Elevation;
    LocationCurve wlc = w.Location as LocationCurve;
    wr.Len = wlc == null ? 0 : Cb.M(wlc.Curve.Length);
    wr.Width = Cb.Mm(w.Width);
    wr.Area = areaM2;
    wr.Vol = Cb.M3(Cb.Dbl(w, BuiltInParameter.HOST_VOLUME_COMPUTED));
    walls.Add(wr);
    if (isExt) extArea += areaM2; else intArea += areaM2;
}
walls = walls.OrderBy(q => q.Grp).ThenBy(q => q.Typ).ThenBy(q => q.LvlElev).ToList();

// ---------------------------------------------------------------- doors and windows
List<FamilyInstance> doors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
List<FamilyInstance> wins = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Windows).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
List<CbRow> openRows = new List<CbRow>();
Dictionary<string, double[]> roomOpen = new Dictionary<string, double[]>();      // room id -> [door widths ft, opening area ft2]
int zeroSize = 0;

void CreditRoom(Autodesk.Revit.DB.Architecture.Room rm, double wFt, double aFt2, bool isDoor)
{
    if (rm == null) return;
    string key = rm.Id.ToString();
    double[] acc;
    if (!roomOpen.TryGetValue(key, out acc)) { acc = new double[2]; roomOpen[key] = acc; }
    if (isDoor) acc[0] += wFt;
    acc[1] += aFt2;
}

void ProcessOpenings(List<FamilyInstance> list, bool isDoor)
{
    foreach (FamilyInstance op in list)
    {
        double wFt = Cb.OpenSize(doc, op, true), hFt = Cb.OpenSize(doc, op, false);
        if (wFt <= 0 || hFt <= 0) zeroSize++;
        Level ol = Cb.LevelOf(doc, op);
        CbRow orow = new CbRow();
        orow.Id = op.Id.ToString();
        orow.Grp = isDoor ? "Doors" : "Windows";
        orow.Typ = Cb.TypeName(doc, op);
        orow.Lvl = ol == null ? "(no level)" : ol.Name;
        orow.LvlElev = ol == null ? -1e9 : ol.Elevation;
        orow.Width = Cb.Mm(wFt);
        orow.Height = Cb.Mm(hFt);
        orow.Area = Cb.M2(wFt * hFt);
        Wall host = op.Host as Wall;
        if (lintelsOn && host != null && wFt > 0 && !Cb.IsRcWall(doc, host, rcWords))
        {
            double lenFt = wFt + 2.0 * Cb.Ft(LintelBearing);
            orow.Len = Cb.M(lenFt);
            orow.Vol = Cb.M3(lenFt * host.Width * Cb.Ft(LintelHeight));
        }
        openRows.Add(orow);
        try
        {
            CreditRoom(op.FromRoom, wFt, wFt * hFt, isDoor);
            CreditRoom(op.ToRoom, wFt, wFt * hFt, isDoor);
        }
        catch { }
    }
}
ProcessOpenings(doors, true);
ProcessOpenings(wins, false);

// ---------------------------------------------------------------- rooms
List<Autodesk.Revit.DB.Architecture.Room> roomList = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms)
    .WhereElementIsNotElementType().ToElements().OfType<Autodesk.Revit.DB.Architecture.Room>().Where(q => q.Area > 0).ToList();
roomList = roomList.OrderBy(q => { Level rl = Cb.LevelOf(doc, q); return rl == null ? -1e9 : rl.Elevation; }).ThenBy(q => q.Number).ToList();

string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
string subtitle = (ProjectName.Trim().Length > 0 ? ProjectName.Trim() + "   |   " : "") + "Revit model: " + doc.Title + "   |   " + stamp +
                  "   |   Units: m, m², m³";

CbSheet rooms = CbXlsx.Table("Rooms", "Room finish schedule   |   جدول تشطيبات الغرف", subtitle,
    new[] { "#", "Number", "Name", "Level", "Floor area (m²)", "Perimeter (m)", "Height (m)", "Openings (m²)", "Wall finish area (m²)", "Skirting (m)",
            "Floor finish", "Wall finish", "Ceiling finish", "Base finish" },
    new double[] { 5, 10, 24, 14, 13, 12, 10, 12, 15, 12, 22, 22, 22, 22 });
Dictionary<string, double> floorFin = new Dictionary<string, double>();
Dictionary<string, double> wallFin = new Dictionary<string, double>();
Dictionary<string, double> ceilFin = new Dictionary<string, double>();
Dictionary<string, double> baseFin = new Dictionary<string, double>();
double sumFloor = 0, sumWallRooms = 0, sumSkirt = 0;
int fallbackHeight = 0, roomRow = 5;

void Acc(Dictionary<string, double> dict, string key, double val)
{
    string kk = string.IsNullOrWhiteSpace(key) ? "(not set)" : key.Trim();
    double cur;
    dict.TryGetValue(kk, out cur);
    dict[kk] = cur + val;
}

foreach (Autodesk.Revit.DB.Architecture.Room rm in roomList)
{
    double[] oa;
    roomOpen.TryGetValue(rm.Id.ToString(), out oa);
    if (oa == null) oa = new double[2];
    double hM = Cb.M(rm.UnboundedHeight);
    if (hM < 0.5) { hM = RoomHeightFallback / 1000.0; fallbackHeight++; }
    double perim = Cb.M(rm.Perimeter), areaR = Cb.M2(rm.Area), openA = Cb.M2(oa[1]), doorW = Cb.M(oa[0]);
    double wallA = Math.Max(0, perim * hM - openA);
    double skirt = Math.Max(0, perim - doorW);
    string fFloor = Cb.Txt(rm, BuiltInParameter.ROOM_FINISH_FLOOR);
    string fWall = Cb.Txt(rm, BuiltInParameter.ROOM_FINISH_WALL);
    string fCeil = Cb.Txt(rm, BuiltInParameter.ROOM_FINISH_CEILING);
    string fBase = Cb.Txt(rm, BuiltInParameter.ROOM_FINISH_BASE);
    Level rlv = Cb.LevelOf(doc, rm);

    rooms.Set(roomRow, 1, roomRow - 4, CbXlsx.S_INT);
    rooms.Set(roomRow, 2, rm.Number, CbXlsx.S_CTR);
    rooms.Set(roomRow, 3, Cb.Txt(rm, BuiltInParameter.ROOM_NAME), CbXlsx.S_TEXT);
    rooms.Set(roomRow, 4, rlv == null ? "(no level)" : rlv.Name, CbXlsx.S_TEXT);
    rooms.Set(roomRow, 5, Math.Round(areaR, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 6, Math.Round(perim, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 7, Math.Round(hM, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 8, Math.Round(openA, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 9, Math.Round(wallA, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 10, Math.Round(skirt, 3), CbXlsx.S_NUM);
    rooms.Set(roomRow, 11, fFloor, CbXlsx.S_TEXT);
    rooms.Set(roomRow, 12, fWall, CbXlsx.S_TEXT);
    rooms.Set(roomRow, 13, fCeil, CbXlsx.S_TEXT);
    rooms.Set(roomRow, 14, fBase, CbXlsx.S_TEXT);
    roomRow++;

    sumFloor += areaR; sumWallRooms += wallA; sumSkirt += skirt;
    Acc(floorFin, fFloor, areaR);
    Acc(wallFin, fWall, wallA);
    Acc(ceilFin, fCeil, areaR);
    Acc(baseFin, fBase, skirt);
}
if (roomList.Count > 0)
{
    int rLast = roomRow - 1;
    for (int k = 1; k <= 14; k++) rooms.Set(roomRow, k, null, CbXlsx.S_TOTL);
    rooms.Set(roomRow, 3, "TOTAL", CbXlsx.S_TOTL);
    rooms.Formula(roomRow, 5, "SUBTOTAL(109,E5:E" + rLast + ")", sumFloor, CbXlsx.S_TOTN);
    rooms.Formula(roomRow, 9, "SUBTOTAL(109,I5:I" + rLast + ")", sumWallRooms, CbXlsx.S_TOTN);
    rooms.Formula(roomRow, 10, "SUBTOTAL(109,J5:J" + rLast + ")", sumSkirt, CbXlsx.S_TOTN);
    rooms.AutoFilter = "A4:N" + rLast;
}

// ---------------------------------------------------------------- roofs and finish floors
List<CbRow> roofs = new List<CbRow>();
foreach (Element rf in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Roofs).WhereElementIsNotElementType().ToElements())
{
    double ra = Cb.M2(Cb.Dbl(rf, BuiltInParameter.HOST_AREA_COMPUTED));
    if (ra <= 0) continue;
    CbRow rr = new CbRow();
    rr.Id = rf.Id.ToString(); rr.Grp = "Roofs"; rr.Typ = Cb.TypeName(doc, rf); rr.Area = ra;
    roofs.Add(rr);
}
List<CbRow> finFloors = new List<CbRow>();
foreach (Floor ff in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList())
{
    Parameter pS = ff.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
    if (pS == null || !pS.HasValue || pS.AsInteger() != 0) continue;       // structural floors belong to 20_Takeoff_Structure
    double fa = Cb.M2(Cb.Dbl(ff, BuiltInParameter.HOST_AREA_COMPUTED));
    if (fa <= 0) continue;
    CbRow fr = new CbRow();
    fr.Id = ff.Id.ToString(); fr.Grp = "Finish floors"; fr.Typ = Cb.TypeName(doc, ff); fr.Area = fa;
    finFloors.Add(fr);
}

// ---------------------------------------------------------------- stairs and railings
List<CbRow> stairRows = new List<CbRow>();
foreach (Element st in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.Stairs)).ToElements())
{
    CbRow sr = new CbRow();
    sr.Id = st.Id.ToString(); sr.Grp = "Stairs"; sr.Typ = Cb.TypeName(doc, st);
    sr.Count = (int)Math.Round(Cb.GetNum(st, "ActualRisersNumber"));
    stairRows.Add(sr);
}
List<CbRow> railRows = new List<CbRow>();
foreach (Element rl in new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.Railing)).ToElements())
{
    double railFt = 0;
    try
    {
        System.Reflection.MethodInfo gp0 = rl.GetType().GetMethod("GetPath", Type.EmptyTypes);
        IEnumerable<Curve> pathCurves = gp0 == null ? null : gp0.Invoke(rl, null) as IEnumerable<Curve>;
        if (pathCurves != null) foreach (Curve pc in pathCurves) railFt += pc.Length;
    }
    catch { }
    if (railFt <= 0) continue;
    CbRow rr2 = new CbRow();
    rr2.Id = rl.Id.ToString(); rr2.Grp = "Railings"; rr2.Typ = Cb.TypeName(doc, rl); rr2.Len = Cb.M(railFt);
    railRows.Add(rr2);
}

// ---------------------------------------------------------------- numbers for the bill
bool haveRooms = roomList.Count > 0;
double plasterInt = haveRooms ? sumWallRooms : 2.0 * intArea + extArea;
double plasterExt = extArea;
double lintelLen = openRows.Sum(q => q.Len), lintelVol = openRows.Sum(q => q.Vol);

string DimText(CbRow q) { return Cb.N(Math.Round(q.Width)) + " x " + Cb.N(Math.Round(q.Height)) + " mm"; }

CbBoq boq = new CbBoq("Bill of Quantities - Architectural Works   |   حصر الأعمال المعمارية", subtitle);

// 1. masonry
boq.Section("Masonry walls", "أعمال المباني");
foreach (var gp in walls.GroupBy(q => q.Grp + "|" + q.Typ + "|" + Math.Round(q.Width)).OrderBy(q => q.Key))
{
    CbRow first = gp.First();
    boq.Item(first.Grp + ": " + first.Typ + ", " + Cb.N(Math.Round(first.Width)) + " mm thick",
             (first.Grp == "External walls" ? "مباني خارجية" : "مباني داخلية") + " - سمك " + Cb.N(Math.Round(first.Width)) + " مم",
             "m²", Math.Round(gp.Sum(q => q.Area), 2), null,
             gp.Count() + " wall(s), volume " + Cb.N(Math.Round(gp.Sum(q => q.Vol), 2)) + " m³, length " + Cb.N(Math.Round(gp.Sum(q => q.Len), 1)) + " m. Net of door / window openings.");
}
boq.EndSection();

// 2. plaster
boq.Section("Plastering", "أعمال المحارة");
boq.Item("Internal plaster to walls", "محارة داخلية للحوائط", "m²", Math.Round(plasterInt, 2), null,
         haveRooms ? "Rooms: perimeter x height - door / window openings (" + roomList.Count + " rooms)."
                   : "NO ROOMS in the model: 2 x interior walls + 1 x exterior walls (inner face). Create rooms (11_Rooms) for a better figure.");
boq.Item("External plaster to walls", "محارة خارجية", "m²", Math.Round(plasterExt, 2), null, "Net area of the exterior-function walls, one face.");
if (haveRooms) boq.Item("Plaster to ceilings", "محارة للأسقف", "m²", Math.Round(sumFloor, 2), null, "Room floor area.");
boq.EndSection();

// 3. finishes from the rooms
if (haveRooms)
{
    boq.Section("Floor finishes", "تشطيبات الأرضيات");
    foreach (var kv in floorFin.OrderBy(q => q.Key))
        boq.Item("Floor finish: " + kv.Key, "أرضيات: " + kv.Key, "m²", Math.Round(kv.Value, 2), null, "Sum of the room areas with this floor finish.");
    boq.EndSection();

    boq.Section("Skirting", "الوزرات");
    foreach (var kv in baseFin.OrderBy(q => q.Key))
        boq.Item("Skirting: " + kv.Key, "وزرة: " + kv.Key, "m", Math.Round(kv.Value, 2), null, "Room perimeter - door widths.");
    boq.EndSection();

    boq.Section("Wall finishes", "تشطيبات الحوائط الداخلية");
    foreach (var kv in wallFin.OrderBy(q => q.Key))
        boq.Item("Wall finish: " + kv.Key, "تشطيب حوائط: " + kv.Key, "m²", Math.Round(kv.Value, 2), null, "Room perimeter x height - openings.");
    boq.EndSection();

    boq.Section("Ceiling finishes", "تشطيبات الأسقف");
    foreach (var kv in ceilFin.OrderBy(q => q.Key))
        boq.Item("Ceiling finish: " + kv.Key, "أسقف: " + kv.Key, "m²", Math.Round(kv.Value, 2), null, "Sum of the room areas with this ceiling finish.");
    boq.EndSection();
}

// 4. doors, windows, lintels
boq.Section("Doors, windows and lintels", "الأبواب والشبابيك والعتبات");
foreach (var gp in openRows.Where(q => q.Grp == "Doors").GroupBy(q => q.Typ + "|" + Math.Round(q.Width) + "x" + Math.Round(q.Height)).OrderBy(q => q.Key))
{
    CbRow first = gp.First();
    boq.Item("Door: " + first.Typ + " (" + DimText(first) + ")", "باب " + DimText(first), "no.", gp.Count(), null,
             "Leaf area " + Cb.N(Math.Round(gp.Sum(q => q.Area), 2)) + " m².");
}
foreach (var gp in openRows.Where(q => q.Grp == "Windows").GroupBy(q => q.Typ + "|" + Math.Round(q.Width) + "x" + Math.Round(q.Height)).OrderBy(q => q.Key))
{
    CbRow first = gp.First();
    boq.Item("Window: " + first.Typ + " (" + DimText(first) + ")", "شباك " + DimText(first), "no.", gp.Count(), null,
             "Glazed / frame area " + Cb.N(Math.Round(gp.Sum(q => q.Area), 2)) + " m².");
}
double winSill = openRows.Where(q => q.Grp == "Windows").Sum(q => q.Width) / 1000.0;
double doorSill = openRows.Where(q => q.Grp == "Doors").Sum(q => q.Width) / 1000.0;
if (winSill > 0) boq.Item("Window sills", "أحلاس الشبابيك (رخام)", "m", Math.Round(winSill, 2), null, "Sum of the window widths.");
if (doorSill > 0) boq.Item("Door thresholds", "عتبات الأبواب (رخام)", "m", Math.Round(doorSill, 2), null, "Sum of the door widths.");
if (lintelsOn && lintelVol > 0)
{
    boq.Item("R.C. lintels over openings in masonry walls", "خرسانة مسلحة للعتبات", "m³", Math.Round(lintelVol, 3), null,
             "Opening width + 2 x " + Cb.N(LintelBearing) + " mm bearing, wall thickness x " + Cb.N(LintelHeight) + " mm deep.");
    boq.Item("R.C. lintels - length", "أطوال العتبات", "m", Math.Round(lintelLen, 2), null, "For the formwork and steel of the lintels.");
}
boq.EndSection();

// 5. roofs and finish floors
boq.Section("Roofs and other floors", "الأسقف وأرضيات أخرى");
foreach (var gp in roofs.GroupBy(q => q.Typ).OrderBy(q => q.Key))
    boq.Item("Roof: " + gp.Key, "سقف: " + gp.Key, "m²", Math.Round(gp.Sum(q => q.Area), 2), null, gp.Count() + " roof(s), sloped area.");
double roofTotal = roofs.Sum(q => q.Area);
if (roofTotal > 0)
    boq.Item("Roof waterproofing and insulation", "عزل مائي وحراري للسطح", "m²", Math.Round(roofTotal, 2), null, "Roof area; add upturns / parapets by hand.");
foreach (var gp in finFloors.GroupBy(q => q.Typ).OrderBy(q => q.Key))
    boq.Item("Floor: " + gp.Key, "أرضية: " + gp.Key, "m²", Math.Round(gp.Sum(q => q.Area), 2), null, gp.Count() + " floor element(s) (non-structural).");
boq.EndSection();

// 6. stairs and railings
boq.Section("Stairs and railings", "السلالم والدرابزين");
foreach (var gp in stairRows.GroupBy(q => q.Typ).OrderBy(q => q.Key))
    boq.Item("Stair: " + gp.Key, "سلم: " + gp.Key, "no.", gp.Count(), null,
             gp.Sum(q => q.Count) + " risers in total. Concrete, finishes and the handrail of the stair are not measured from the stair object - add them by hand.");
foreach (var gp in railRows.GroupBy(q => q.Typ).OrderBy(q => q.Key))
    boq.Item("Railing: " + gp.Key, "درابزين: " + gp.Key, "m", Math.Round(gp.Sum(q => q.Len), 2), null, gp.Count() + " railing(s), length of the path.");
boq.EndSection();
boq.Finish();

// ---------------------------------------------------------------- Walls and Openings sheets
CbSheet wsh = CbXlsx.Table("Walls", "Masonry walls   |   الحوائط", subtitle,
    new[] { "#", "Element ID", "Group", "Type (Family : Type)", "Level", "Length (m)", "Thickness (mm)", "Net area (m²)", "Volume (m³)" },
    new double[] { 5, 12, 16, 52, 16, 12, 14, 14, 14 });
int wr2 = 5;
foreach (CbRow rw in walls)
{
    wsh.Set(wr2, 1, wr2 - 4, CbXlsx.S_INT);
    wsh.Set(wr2, 2, rw.Id, CbXlsx.S_CTR);
    wsh.Set(wr2, 3, rw.Grp, CbXlsx.S_TEXT);
    wsh.Set(wr2, 4, rw.Typ, CbXlsx.S_TEXT);
    wsh.Set(wr2, 5, rw.Lvl, CbXlsx.S_TEXT);
    wsh.Set(wr2, 6, Math.Round(rw.Len, 3), CbXlsx.S_NUM);
    wsh.Set(wr2, 7, Math.Round(rw.Width, 1), CbXlsx.S_NUM);
    wsh.Set(wr2, 8, Math.Round(rw.Area, 3), CbXlsx.S_NUM);
    wsh.Set(wr2, 9, Math.Round(rw.Vol, 4), CbXlsx.S_NUM);
    wr2++;
}
if (walls.Count > 0)
{
    for (int k = 1; k <= 9; k++) wsh.Set(wr2, k, null, CbXlsx.S_TOTL);
    wsh.Set(wr2, 3, "TOTAL", CbXlsx.S_TOTL);
    wsh.Formula(wr2, 6, "SUBTOTAL(109,F5:F" + (wr2 - 1) + ")", walls.Sum(q => q.Len), CbXlsx.S_TOTN);
    wsh.Formula(wr2, 8, "SUBTOTAL(109,H5:H" + (wr2 - 1) + ")", walls.Sum(q => q.Area), CbXlsx.S_TOTN);
    wsh.Formula(wr2, 9, "SUBTOTAL(109,I5:I" + (wr2 - 1) + ")", walls.Sum(q => q.Vol), CbXlsx.S_TOTN);
    wsh.AutoFilter = "A4:I" + (wr2 - 1);
}

CbSheet osh = CbXlsx.Table("Openings", "Doors and windows   |   الأبواب والشبابيك", subtitle,
    new[] { "#", "Category", "Type (Family : Type)", "Width (mm)", "Height (mm)", "Count", "Area each (m²)", "Total area (m²)", "Lintel length (m)", "Lintel volume (m³)" },
    new double[] { 5, 12, 52, 12, 12, 9, 14, 14, 15, 15 });
int or2 = 5;
foreach (var gp in openRows.GroupBy(q => q.Grp + "|" + q.Typ + "|" + Math.Round(q.Width) + "x" + Math.Round(q.Height)).OrderBy(q => q.Key))
{
    CbRow first = gp.First();
    osh.Set(or2, 1, or2 - 4, CbXlsx.S_INT);
    osh.Set(or2, 2, first.Grp, CbXlsx.S_TEXT);
    osh.Set(or2, 3, first.Typ, CbXlsx.S_TEXT);
    osh.Set(or2, 4, Math.Round(first.Width), CbXlsx.S_INT);
    osh.Set(or2, 5, Math.Round(first.Height), CbXlsx.S_INT);
    osh.Set(or2, 6, gp.Count(), CbXlsx.S_INT);
    osh.Set(or2, 7, Math.Round(first.Area, 3), CbXlsx.S_NUM);
    osh.Formula(or2, 8, "F" + or2 + "*G" + or2, gp.Count() * first.Area, CbXlsx.S_NUM);
    osh.Set(or2, 9, Math.Round(gp.Sum(q => q.Len), 3), CbXlsx.S_NUM);
    osh.Set(or2, 10, Math.Round(gp.Sum(q => q.Vol), 4), CbXlsx.S_NUM);
    or2++;
}
if (or2 > 5)
{
    for (int k = 1; k <= 10; k++) osh.Set(or2, k, null, CbXlsx.S_TOTL);
    osh.Set(or2, 3, "TOTAL", CbXlsx.S_TOTL);
    osh.Formula(or2, 6, "SUM(F5:F" + (or2 - 1) + ")", openRows.Count, CbXlsx.S_TOTI);
    osh.Formula(or2, 8, "SUM(H5:H" + (or2 - 1) + ")", openRows.Sum(q => q.Area), CbXlsx.S_TOTN);
    osh.Formula(or2, 9, "SUM(I5:I" + (or2 - 1) + ")", lintelLen, CbXlsx.S_TOTN);
    osh.Formula(or2, 10, "SUM(J5:J" + (or2 - 1) + ")", lintelVol, CbXlsx.S_TOTN);
}

// ---------------------------------------------------------------- Notes
if (!haveRooms) warnings.Add("No rooms with an area were found. Internal plaster is approximated from the walls and there are no floor / ceiling / skirting items. Run 11_Rooms (or place rooms in Revit) and run this script again.");
if (fallbackHeight > 0) warnings.Add(fallbackHeight + " room(s) have no height in Revit; the fallback " + Cb.N(RoomHeightFallback) + " mm was used. Set the room's Upper Limit / Limit Offset.");
if (zeroSize > 0) warnings.Add(zeroSize + " door / window family(ies) have no readable Width / Height parameter; their area is 0.");
if (noAreaWalls > 0) warnings.Add(noAreaWalls + " wall(s) have no computed area and were skipped.");
if (haveRooms && floorFin.Count == 1 && floorFin.ContainsKey("(not set)")) warnings.Add("The rooms have no Floor / Wall / Ceiling / Base Finish text. Fill those parameters (or use 11_Rooms) to get the finishes per type.");

List<string> notes = new List<string>();
notes.Add("# Summary");
notes.Add("Masonry walls: " + walls.Count + "   |   Rooms: " + roomList.Count + "   |   Doors: " + doors.Count + "   |   Windows: " + wins.Count + "   |   Roof area: " + Cb.N(Math.Round(roofTotal, 2)) + " m²   |   Stairs: " + stairRows.Count + "   |   Railings: " + railRows.Count);
notes.Add("Walls skipped because they are reinforced concrete: " + rcSkipped + " (they are measured in 20_Takeoff_Structure).");
notes.Add("# Method");
notes.Add("Masonry: Revit's net wall area (door / window openings already deducted) and wall volume, by exterior / interior and wall type.");
notes.Add("Internal plaster and wall finishes: for every room, perimeter x room height - the area of the doors / windows that open into that room. A door between two rooms is deducted from both.");
notes.Add("Skirting: room perimeter - door widths.   Ceilings: room floor area.   Floors: room floor area.");
notes.Add("External plaster: net area of walls whose type function is Exterior (one face). Reveals, returns and parapets are not added.");
notes.Add("Lintels: " + (lintelsOn ? "opening width + 2 x " + Cb.N(LintelBearing) + " mm, wall thickness x " + Cb.N(LintelHeight) + " mm deep, only for openings in masonry walls." : "not calculated."));
notes.Add("# Checks");
if (warnings.Count == 0) notes.Add("No warnings.");
foreach (string wmsg in warnings) notes.Add("WARNING: " + wmsg);
notes.Add("# How to use");
notes.Add("Type the unit rates in the yellow cells of the BOQ sheet; Excel calculates the amounts and totals. The Rooms, Walls and Openings sheets can be filtered to check any figure.");
CbSheet notesSheet = CbXlsx.Notes("Notes", "Assumptions and checks   |   الافتراضات والمراجعة", notes);

// ---------------------------------------------------------------- save
string safe = string.IsNullOrWhiteSpace(FileName) ? "BOQ_Architecture" : FileName.Trim();
foreach (char badChar in Path.GetInvalidFileNameChars()) safe = safe.Replace(badChar.ToString(), "_");
string saveDir = string.IsNullOrWhiteSpace(SaveFolder) ? OutDir : SaveFolder.Trim();
Directory.CreateDirectory(saveDir);
string outPath = Path.Combine(saveDir, safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".xlsx");
CbXlsx.Save(outPath, new List<CbSheet> { boq.Sheet, wsh, rooms, osh, notesSheet });

Log("Architectural BOQ saved: " + outPath);
Log("Masonry " + Cb.N(Math.Round(walls.Sum(q => q.Area), 2)) + " m2 (" + walls.Count + " walls)   |   internal plaster " + Cb.N(Math.Round(plasterInt, 2)) +
    " m2   |   external plaster " + Cb.N(Math.Round(plasterExt, 2)) + " m2");
Log("Doors " + doors.Count + "   |   windows " + wins.Count + "   |   rooms " + roomList.Count + "   |   floor finish area " + Cb.N(Math.Round(sumFloor, 2)) + " m2   |   roof " +
    Cb.N(Math.Round(roofTotal, 2)) + " m2");
foreach (string wmsg2 in warnings) Log("WARNING: " + wmsg2);
