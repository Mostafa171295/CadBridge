// Revit STEP 12: STAIRS between levels (real Revit stairs, by component), with the opening in the slab above. Sizes in mm.
// Run 01_Grids and 02_Levels first (and 07_Slabs if you want the opening to cut a slab).
// The boxes with a list are filled FROM REVIT after you press Connect.
//   StairType   : a stairs type of the project (its treads, risers, stringers and railings come from the type).
//   FromLevel / ToLevel : one stair is made for every storey from FromLevel up to ToLevel (ToLevel must be higher), at the same place.
//   Shape       : Straight = one run | UTurn = two parallel runs and a landing (dog-leg) | LTurn = two runs at 90 degrees and a landing.
//   Direction   : the direction of the FIRST run on the plan: +Y (up), -Y, +X (right), -X.
//   Turn        : for UTurn / LTurn: the second run goes to the Left or to the Right of the first run.
//   AtGrid      : where the start (middle of the first step) is, as  horizontal-vertical grid names, for example  B-2.
//                 Leave it empty to use the absolute OffsetX / OffsetY (mm); otherwise OffsetX / OffsetY are measured from that grid crossing.
//   Width       : width of every run.   Riser / Tread : target riser height and tread depth. The number of risers is
//                 the storey height / Riser, rounded, so the real riser height is slightly different (it is printed in the log).
//   LandingDepth: depth of the landing of a UTurn (the landing of an LTurn is Width x Width).   Gap : the space between the two runs of a UTurn.
//   Opening     : Yes = a shaft opening is cut in the slab above (only the part where the head room is less than Headroom).
//                 It is a rectangle around the stairs - edit it in Revit if you need another shape.   Clearance : extra space around it.
// Running it twice does not duplicate: a stair that already starts at this place and level is skipped.
// IMPORTANT: the multi-run stairs (UTurn / LTurn) use Revit API members that differ a little between Revit years.
//            Try them on a copy of the project first and look at the result in 3D. Straight stairs are the safest.
// Available variables: uiapp, doc, OutDir, Log(...)

string StairType = "{{StairType=@stairTypes}}";          // list from Revit
string FromLevel = "{{FromLevel=@levels}}";              // list from Revit
string ToLevel = "{{ToLevel=@levels}}";                  // list from Revit
string Shape = "{{Shape=Straight|UTurn|LTurn}}";
string Direction = "{{Direction=+Y|-Y|+X|-X}}";
string Turn = "{{Turn=Left|Right}}";
string AtGrid = "{{AtGrid=}}";
double OffsetX = {{OffsetX=0}};
double OffsetY = {{OffsetY=0}};
double Width = {{Width=1200}};
double Riser = {{Riser=170}};
double Tread = {{Tread=280}};
double LandingDepth = {{LandingDepth=1200}};
double Gap = {{Gap=100}};
string Opening = "{{Opening=Yes|No}}";
double Headroom = {{Headroom=2100}};
double Clearance = {{Clearance=50}};

List<Level> levels = Cb.Levels(doc);
if (levels.Count < 2) throw new Exception("At least two levels are needed. Run 02_Levels first.");
int LevelIdx(string name, string what)
{
    int found = levels.FindIndex(q => q.Name == (name ?? "").Trim());
    if (found < 0)
        throw new Exception(what + " not found: '" + name + "'. Press Connect to refresh the lists. Available: " + string.Join(", ", levels.Select(q => q.Name)));
    return found;
}
int i0 = LevelIdx(FromLevel, "FromLevel");
int i1 = LevelIdx(ToLevel, "ToLevel");
if (i1 <= i0) throw new Exception("ToLevel must be higher than FromLevel (the stairs go from the lower level up to the higher one).");

// ---- where
XYZ origin = new XYZ(0, 0, 0);
if (AtGrid.Trim().Length > 0)
{
    CbGrid grid = CbGrid.Read(doc);
    XYZ node = grid.At(AtGrid);
    if (node == null) throw new Exception("AtGrid '" + AtGrid + "' not found. Use a horizontal and a vertical grid name, for example B-2.");
    origin = node;
}
origin = new XYZ(origin.X + Cb.Ft(OffsetX), origin.Y + Cb.Ft(OffsetY), 0);

string dirText = Direction.Trim().ToUpperInvariant();
XYZ d = dirText == "+X" ? new XYZ(1, 0, 0) : dirText == "-X" ? new XYZ(-1, 0, 0) : dirText == "-Y" ? new XYZ(0, -1, 0) : new XYZ(0, 1, 0);
XYZ nl = new XYZ(-d.Y, d.X, 0);                                           // the left side of the walking direction
double s = Turn.Trim().StartsWith("R", StringComparison.OrdinalIgnoreCase) ? -1.0 : 1.0;
string shapeText = Shape.Trim().ToUpperInvariant();
bool wantU = shapeText.StartsWith("U"), wantL = shapeText.StartsWith("L");

// point from (u along the first run, v to the left of it)
XYZ P(double u, double v, double z) { return new XYZ(origin.X + d.X * u + nl.X * v, origin.Y + d.Y * u + nl.Y * v, z); }

// ---- stair type
ElementId typeId = ElementId.InvalidElementId;
if (StairType.Trim().Length > 0)
{
    Autodesk.Revit.DB.Architecture.StairsType stType = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.StairsType))
        .Cast<Autodesk.Revit.DB.Architecture.StairsType>().FirstOrDefault(q => q.Name == StairType.Trim());
    if (stType == null) throw new Exception("Stair type not found: " + StairType + ". Press Connect to refresh the lists.");
    typeId = stType.Id;
}

List<Element> existingStairs = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.Stairs)).ToElements().ToList();
bool AlreadyThere(Level baseLv)
{
    foreach (Element old in existingStairs)
    {
        BoundingBoxXYZ ob = old.get_BoundingBox(null);
        if (ob == null) continue;
        if (Math.Abs(ob.Min.Z - baseLv.Elevation) > 1.0) continue;
        if (origin.X >= ob.Min.X - 0.3 && origin.X <= ob.Max.X + 0.3 && origin.Y >= ob.Min.Y - 0.3 && origin.Y <= ob.Max.Y + 0.3) return true;
    }
    return false;
}

double W = Cb.Ft(Width), T = Cb.Ft(Tread), LD = Cb.Ft(LandingDepth), GP = Cb.Ft(Gap), clr = Cb.Ft(Clearance);
int made = 0, skippedCount = 0, failed = 0;

for (int idx = i0; idx < i1; idx++)
{
    Level baseLv = levels[idx], topLv = levels[idx + 1];
    if (AlreadyThere(baseLv)) { skippedCount++; Log("Level " + baseLv.Name + ": a stair already starts here - skipped."); continue; }

    double htFt = topLv.Elevation - baseLv.Elevation;
    int N = Math.Max(2, (int)Math.Round(htFt / Cb.Ft(Riser)));
    double riserFt = htFt / N;
    bool turn = (wantU || wantL) && N >= 4;
    if ((wantU || wantL) && !turn) Log("Level " + baseLv.Name + ": the storey is too low for two runs - a straight stair is made.");
    int n1 = turn ? (N + 1) / 2 : N;
    int n2 = N - n1;
    double uL = (n1 - 1) * T;                       // where the first run ends (landing starts)
    double z = baseLv.Elevation;

    // steps of the top-most part that pass under the slab above (head room < Headroom)
    int clearSteps = (int)Math.Floor((htFt - Cb.Ft(Headroom)) / riserFt);
    int need1 = Math.Min(n1 - 1, Math.Max(0, n1 - clearSteps));
    int need2 = Math.Min(n2 - 1, Math.Max(0, n2 + n1 - clearSteps));   // second run: all of it when it is above the head-room line

    // the corners of everything that needs the opening, in (u, v)
    List<double[]> openPts = new List<double[]>();
    void Box(double u0, double u1, double v0, double v1)
    {
        openPts.Add(new[] { u0, v0 }); openPts.Add(new[] { u1, v0 }); openPts.Add(new[] { u1, v1 }); openPts.Add(new[] { u0, v1 });
    }

    var scope = new Autodesk.Revit.DB.Architecture.StairsEditScope(doc, "CAD Bridge: stairs " + baseLv.Name);
    bool committed = false;
    ElementId stairsId = ElementId.InvalidElementId;
    try
    {
        stairsId = scope.Start(baseLv.Id, topLv.Id);
        using (Transaction t = new Transaction(doc, "CAD Bridge: stairs runs"))
        {
            t.Start();
            Element stairsEl = doc.GetElement(stairsId);
            if (typeId != ElementId.InvalidElementId) { try { stairsEl.ChangeTypeId(typeId); } catch { Log("Note: could not change the stair type."); } }
            Cb.SetProp(stairsEl, "DesiredRisersNumber", N);
            Cb.SetProp(stairsEl, "ActualTreadDepth", T);

            if (!turn)
            {
                Line path = Line.CreateBound(P(0, 0, z), P((N - 1) * T, 0, z));
                var run = Autodesk.Revit.DB.Architecture.StairsRun.CreateStraightRun(doc, stairsId, path, Autodesk.Revit.DB.Architecture.StairsRunJustification.Center);
                Cb.SetProp(run, "ActualRunWidth", W);
                Box((N - 1) * T - need1 * T, (N - 1) * T, -W / 2 - clr, W / 2 + clr);
            }
            else
            {
                // first run
                Line path1 = Line.CreateBound(P(0, 0, z), P(uL, 0, z));
                var run1 = Autodesk.Revit.DB.Architecture.StairsRun.CreateStraightRun(doc, stairsId, path1, Autodesk.Revit.DB.Architecture.StairsRunJustification.Center);
                Cb.SetProp(run1, "ActualRunWidth", W);
                Cb.SetProp(run1, "BaseElevation", 0.0);
                Cb.SetProp(run1, "TopElevation", n1 * riserFt);

                // landing + second run
                double v2, lu0 = uL, lu1, lv0, lv1;
                Line path2;
                if (wantU)
                {
                    v2 = s * (W + GP);
                    lu1 = uL + LD;
                    lv0 = Math.Min(-W / 2, v2 - W / 2); lv1 = Math.Max(W / 2, v2 + W / 2);
                    path2 = Line.CreateBound(P(uL, v2, z), P(uL - (n2 - 1) * T, v2, z));
                    Box(uL - (n2 - 1) * T, uL, v2 - W / 2 - clr, v2 + W / 2 + clr);
                }
                else
                {
                    lu1 = uL + W;
                    lv0 = -W / 2; lv1 = W / 2;
                    double vEnd = s * (W / 2 + (n2 - 1) * T);
                    path2 = Line.CreateBound(P(uL + W / 2, s * W / 2, z), P(uL + W / 2, vEnd, z));
                    Box(uL, uL + W, Math.Min(s * W / 2, vEnd) - clr, Math.Max(s * W / 2, vEnd) + clr);
                }
                var run2 = Autodesk.Revit.DB.Architecture.StairsRun.CreateStraightRun(doc, stairsId, path2, Autodesk.Revit.DB.Architecture.StairsRunJustification.Center);
                Cb.SetProp(run2, "ActualRunWidth", W);
                Cb.SetProp(run2, "BaseElevation", n1 * riserFt);
                Cb.SetProp(run2, "TopElevation", htFt);

                CurveLoop landing = new CurveLoop();
                landing.Append(Line.CreateBound(P(lu0, lv0, z), P(lu1, lv0, z)));
                landing.Append(Line.CreateBound(P(lu1, lv0, z), P(lu1, lv1, z)));
                landing.Append(Line.CreateBound(P(lu1, lv1, z), P(lu0, lv1, z)));
                landing.Append(Line.CreateBound(P(lu0, lv1, z), P(lu0, lv0, z)));
                Autodesk.Revit.DB.Architecture.StairsLanding.CreateSketchedLanding(doc, stairsId, landing, n1 * riserFt);

                Box(uL, lu1, lv0 - clr, lv1 + clr);                                   // the landing
                if (need1 > 0) Box(uL - need1 * T, uL, -W / 2 - clr, W / 2 + clr);    // the end of the first run under the slab
            }
            t.Commit();
        }
        scope.Commit(new CbWarningSwallower());
        committed = true;
    }
    catch (Exception ex)
    {
        failed++;
        Log("Level " + baseLv.Name + ": the stair could not be made - " + ex.Message);
        if (!committed) { try { scope.Cancel(); } catch { } }
    }
    finally
    {
        try { scope.Dispose(); } catch { }
    }
    if (!committed) continue;
    made++;

    // what Revit really made
    Element made1 = doc.GetElement(stairsId);
    double realRisers = Cb.GetNum(made1, "ActualRisersNumber");
    double realRiserMm = Cb.Mm(Cb.GetNum(made1, "ActualRiserHeight"));
    Log("Level " + baseLv.Name + " -> " + topLv.Name + ": " + (turn ? (wantU ? "U-turn" : "L-turn") : "straight") + " stair, " + N + " risers planned" +
        (realRisers > 0 ? ", Revit reports " + Cb.N(realRisers) + " risers of " + Cb.N(Math.Round(realRiserMm, 1)) + " mm" : "") + ", storey " + Cb.N(Math.Round(Cb.Mm(htFt))) + " mm.");
    if (realRisers > 0 && Math.Abs(realRisers - N) > 0.5)
        Log("  WARNING: the number of risers differs from the plan. Check the runs in 3D (the elevation of the second run, the landing).");

    // the opening in the slab above
    if (Cb.Yes(Opening) && openPts.Count > 0)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (double[] uv in openPts)
        {
            XYZ wp = P(uv[0], uv[1], z);
            minX = Math.Min(minX, wp.X); maxX = Math.Max(maxX, wp.X);
            minY = Math.Min(minY, wp.Y); maxY = Math.Max(maxY, wp.Y);
        }
        try
        {
            using (Transaction t2 = new Transaction(doc, "CAD Bridge: stair opening"))
            {
                t2.Start();
                CurveArray profile = new CurveArray();
                profile.Append(Line.CreateBound(new XYZ(minX, minY, z), new XYZ(maxX, minY, z)));
                profile.Append(Line.CreateBound(new XYZ(maxX, minY, z), new XYZ(maxX, maxY, z)));
                profile.Append(Line.CreateBound(new XYZ(maxX, maxY, z), new XYZ(minX, maxY, z)));
                profile.Append(Line.CreateBound(new XYZ(minX, maxY, z), new XYZ(minX, minY, z)));
                doc.Create.NewOpening(baseLv, topLv, profile);
                t2.Commit();
            }
            Log("  Opening cut: " + Cb.N(Math.Round(Cb.Mm(maxX - minX))) + " x " + Cb.N(Math.Round(Cb.Mm(maxY - minY))) + " mm (shaft from " + baseLv.Name + " to " + topLv.Name + ").");
        }
        catch (Exception ex2) { Log("  The opening could not be made - " + ex2.Message); }
    }
}

Log("Stairs made: " + made + (skippedCount > 0 ? " | skipped: " + skippedCount : "") + (failed > 0 ? " | failed: " + failed : "") + ".");
if (made > 0) Log("Check the stairs in 3D. Railings: use 14_Railings, or the railing settings of the stair type.");
