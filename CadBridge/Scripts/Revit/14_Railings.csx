// Revit STEP 14: RAILINGS (الدرابزين) around slab edges, around slab openings, or around the grid area. Sizes in mm.
// Run 01_Grids, 02_Levels and 07_Slabs first. The boxes with a list are filled FROM REVIT after you press Connect.
//   RailingType : a railing type of the project (height, posts, balusters and rails come from the type).
//   LevelName   : the level whose slab gets the railing (for the roof use the Roof level).
//   Source      : SlabEdge     = the outer edge of the slab (balcony / terrace / roof edge),
//                 SlabOpenings = the edge of every hole in the slab (stairwell, lift shaft) - the guard around a void,
//                 GridRectangle = a rectangle around the grid area (see Offset); no slab is needed.
//   Offset      : for GridRectangle: how far OUTSIDE the outer grids the railing runs (mm, negative = inside).
//   BaseOffset  : extra height of the railing base above the slab top (mm). The slab top is found automatically.
// Use  07_Slabs  with Layout = OneSlab for SlabEdge (with one slab per bay the railing would also run on the inner edges).
// Running it twice does not duplicate: a railing with the same outline on the same level is skipped.
// Available variables: uiapp, doc, OutDir, Log(...)

string RailingType = "{{RailingType=@railingTypes}}";    // list from Revit
string LevelName = "{{LevelName=@levels}}";              // list from Revit
string Source = "{{Source=SlabEdge|SlabOpenings|GridRectangle}}";
double Offset = {{Offset=0}};
double BaseOffset = {{BaseOffset=0}};

List<Level> levels = Cb.Levels(doc);
Level lv = levels.FirstOrDefault(q => q.Name == (LevelName ?? "").Trim());
if (lv == null) throw new Exception("LevelName not found: '" + LevelName + "'. Press Connect to refresh the lists.");

Autodesk.Revit.DB.Architecture.RailingType rtype = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.RailingType))
    .Cast<Autodesk.Revit.DB.Architecture.RailingType>().FirstOrDefault(q => q.Name == (RailingType ?? "").Trim());
if (rtype == null) throw new Exception("Railing type not found: '" + RailingType + "'. Press Connect to refresh the lists.");

string src = (Source ?? "").Trim().ToUpperInvariant();
bool useGrid = src.StartsWith("G"), useOpenings = src.StartsWith("SLABO");

// ---- the outlines (every outline = one railing). Each is { loop, heightAboveLevel(ft) }
List<CurveLoop> loops = new List<CurveLoop>();
List<double> lifts = new List<double>();

if (useGrid)
{
    CbGrid grid = CbGrid.Read(doc);
    double off = Cb.Ft(Offset);
    double x0 = grid.Node[0, 0].X - off, x1 = grid.Node[0, grid.NV - 1].X + off;
    double y0 = grid.Node[0, 0].Y - off, y1 = grid.Node[grid.NH - 1, 0].Y + off;
    double zz = lv.Elevation;
    CurveLoop rect = new CurveLoop();
    rect.Append(Line.CreateBound(new XYZ(x0, y0, zz), new XYZ(x1, y0, zz)));
    rect.Append(Line.CreateBound(new XYZ(x1, y0, zz), new XYZ(x1, y1, zz)));
    rect.Append(Line.CreateBound(new XYZ(x1, y1, zz), new XYZ(x0, y1, zz)));
    rect.Append(Line.CreateBound(new XYZ(x0, y1, zz), new XYZ(x0, y0, zz)));
    loops.Add(rect);
    lifts.Add(0);
}
else
{
    int slabs = 0;
    ElementId foundationCat = new ElementId(BuiltInCategory.OST_StructuralFoundation);
    foreach (Floor fl in new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList())
    {
        Level fLv = Cb.LevelOf(doc, fl);
        if (fLv == null || !fLv.Id.Equals(lv.Id)) continue;
        if (fl.Category != null && fl.Category.Id.Equals(foundationCat)) continue;
        slabs++;

        // the biggest upward-facing face = the top of the slab
        PlanarFace top = null;
        foreach (Solid so in Cb.Solids(fl))
            foreach (Face f in so.Faces)
            {
                PlanarFace pf = f as PlanarFace;
                if (pf == null || pf.FaceNormal.Z < 0.9) continue;
                if (top == null || pf.Area > top.Area) top = pf;
            }
        if (top == null) continue;

        double dz = lv.Elevation - top.Origin.Z;                       // move the outline to the level plane
        Transform down = Transform.CreateTranslation(new XYZ(0, 0, dz));
        double lift = -dz;                                             // slab top above the level

        List<CurveLoop> found = top.GetEdgesAsCurveLoops().ToList();
        // the outer loop = the one with the biggest plan extent
        double Extent(CurveLoop cl)
        {
            double mnx = double.MaxValue, mny = double.MaxValue, mxx = double.MinValue, mxy = double.MinValue;
            foreach (Curve c in cl)
                for (int k = 0; k < 2; k++)
                {
                    XYZ p = c.GetEndPoint(k);
                    mnx = Math.Min(mnx, p.X); mxx = Math.Max(mxx, p.X); mny = Math.Min(mny, p.Y); mxy = Math.Max(mxy, p.Y);
                }
            return (mxx - mnx) * (mxy - mny);
        }
        CurveLoop outer = found.OrderByDescending(q => Extent(q)).First();
        foreach (CurveLoop cl in found)
        {
            bool isOuter = ReferenceEquals(cl, outer);
            if (useOpenings == isOuter) continue;                      // SlabEdge -> outer only, SlabOpenings -> holes only
            CurveLoop flat = new CurveLoop();
            foreach (Curve c in cl) flat.Append(c.CreateTransformed(down));
            loops.Add(flat);
            lifts.Add(lift);
        }
    }
    if (slabs == 0) throw new Exception("No slab found on level '" + lv.Name + "'. Run 07_Slabs first, or use Source = GridRectangle.");
    if (slabs > 1) Log("Note: " + slabs + " slabs on this level - every one gets its own outline (use 07_Slabs with Layout = OneSlab to avoid railings on inner edges).");
}
if (loops.Count == 0)
{
    Log(useOpenings ? "No opening was found in the slab of level " + lv.Name + "." : "No outline found.");
    return;
}

// ---- railings that are already there (same level, same centre)
List<Element> existing = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.Railing)).ToElements().ToList();
XYZ Centre(CurveLoop cl)
{
    double sx = 0, sy = 0; int n = 0;
    foreach (Curve c in cl) { XYZ p = c.GetEndPoint(0); sx += p.X; sy += p.Y; n++; }
    return n == 0 ? XYZ.Zero : new XYZ(sx / n, sy / n, 0);
}
bool AlreadyThere(CurveLoop cl)
{
    XYZ mid = Centre(cl);
    foreach (Element old in existing)
    {
        BoundingBoxXYZ ob = old.get_BoundingBox(null);
        if (ob == null) continue;
        Level ol = Cb.LevelOf(doc, old);
        if (ol != null && !ol.Id.Equals(lv.Id)) continue;
        // the railing's own box contains the centre of the outline's points, and it is about as big as the outline
        if (mid.X >= ob.Min.X - 0.3 && mid.X <= ob.Max.X + 0.3 && mid.Y >= ob.Min.Y - 0.3 && mid.Y <= ob.Max.Y + 0.3)
        {
            double len = 0; foreach (Curve c in cl) len += c.Length;
            XYZ ext = ob.Max - ob.Min;
            if (Math.Abs((ext.X + ext.Y) * 2.0 - len) < 3.0) return true;
        }
    }
    return false;
}

int made = 0, skippedCount = 0, failed = 0;
double totalLen = 0;
using (Transaction t = new Transaction(doc, "CAD Bridge: railings"))
{
    t.Start();
    for (int k = 0; k < loops.Count; k++)
    {
        if (AlreadyThere(loops[k])) { skippedCount++; continue; }
        try
        {
            Autodesk.Revit.DB.Architecture.Railing rl = Autodesk.Revit.DB.Architecture.Railing.Create(doc, loops[k], rtype.Id, lv.Id);
            Parameter pb = Cb.Find(rl, "Base Offset");
            double lift = lifts[k] + Cb.Ft(BaseOffset);
            if (pb != null && !pb.IsReadOnly && Math.Abs(lift) > 1e-9) pb.Set(lift);
            made++;
            foreach (Curve c in loops[k]) totalLen += c.Length;
        }
        catch (Exception ex)
        {
            failed++;
            Log("A railing could not be made - " + ex.Message);
        }
    }
    t.Commit();
}

Log("Railings made: " + made + " on level " + lv.Name + " | type: " + rtype.Name + " | length " + Cb.N(Math.Round(Cb.M(totalLen), 2)) + " m" +
    (skippedCount > 0 ? " | skipped (already there): " + skippedCount : "") + (failed > 0 ? " | failed: " + failed : "") + ".");
