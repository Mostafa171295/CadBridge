// Revit STEP 16: EDIT elements that you pick in Revit (one element, or many), or all elements of one category.
// How to use:  1) In Revit select the element(s) (click, Ctrl+click, or a window selection).
//              2) Here choose Scope = Selected, choose the Action, fill only the boxes that action needs, press Run code.
//   Scope        : Selected = what is selected now in Revit.   Category = every element of the chosen Category.
//   Category     : used only when Scope = Category.
//   OnLevel      : optional filter - the exact level name (leave empty = any level).
//   TypeContains : optional filter - part of the type name, for example  200  or  Brick  (leave empty = any type).
//   Action       : Info          = prints what was found (id, category, type, level, size). Changes nothing.
//                  ChangeType    = changes the type of all of them to NewType, written like  Family : Type  (or just the Type name).
//                                  OR choose FamilyFile: an external family (.rfa) - press "Folder..." to choose YOUR family folder.
//                                  It is loaded into the project and the elements are switched to its first type (or to the type named in NewType).
//                                  Works for elements that are family instances (doors, windows, columns, furniture, equipment...).
//                                  Walls, floors, roofs, stairs and railings are system families: they have no .rfa file.
//                  SetParameter  = writes ParamValue into the parameter ParamName (ParamOn = Instance or Type).
//                                  Lengths are typed the way Revit shows them (mm in a metric project). Yes / No for check boxes.
//                  WriteId       = writes the Revit Element Id of each element into a text parameter (IdParam, default Comments; Mark also works)
//                                  so you can see it in Properties, in tags and in schedules. IdPrefix is optional text put before the number.
//                  Move          = moves by MoveX / MoveY / MoveZ (mm).
//                  Rotate        = rotates by Angle degrees around the centre of the group (anticlockwise seen from above).
//                  Copy          = copies Copies times, each copy shifted by MoveX / MoveY / MoveZ more.
//                  Delete        = deletes them. Only runs when ConfirmDelete = Yes. Things hosted on them (doors in a wall) go too.
// Everything is one Revit transaction, so Ctrl+Z in Revit undoes the whole run.
// Available variables: uiapp, doc, OutDir, Log(...)

string Scope = "{{Scope=Selected|Category}}";
string Category = "{{Category=Walls|Doors|Windows|Columns|Beams|Footings|Floors|Roofs|Railings|Stairs}}";
string OnLevel = "{{OnLevel=}}";
string TypeContains = "{{TypeContains=}}";
string Action = "{{Action=Info|WriteId|ChangeType|SetParameter|Move|Rotate|Copy|Delete}}";
string NewType = "{{NewType=}}";
string FamilyFile = @"{{FamilyFile=@anyFiles}}";          // list of .rfa files ("(none)" = do not load anything)
string ParamName = "{{ParamName=}}";
string ParamValue = "{{ParamValue=}}";
string ParamOn = "{{ParamOn=Instance|Type}}";
string IdParam = "{{IdParam=Comments}}";
string IdPrefix = "{{IdPrefix=}}";
double MoveX = {{MoveX=0}};
double MoveY = {{MoveY=0}};
double MoveZ = {{MoveZ=0}};
double Angle = {{Angle=0}};
int Copies = {{Copies=1}};
string ConfirmDelete = "{{ConfirmDelete=No|Yes}}";

// ---- 1) collect
List<Element> els = new List<Element>();
if (Scope.Trim().StartsWith("S", StringComparison.OrdinalIgnoreCase))
{
    foreach (ElementId sid in uiapp.ActiveUIDocument.Selection.GetElementIds())
    {
        Element e = doc.GetElement(sid);
        if (e != null && !(e is ElementType)) els.Add(e);
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
Log("Found " + els.Count + " element(s).");

string act = Action.Trim().ToLowerInvariant();
List<ElementId> ids = els.Select(e => e.Id).ToList();

// ---- 2) actions
if (act == "info")
{
    foreach (var g in els.GroupBy(e => (e.Category != null ? e.Category.Name : "?") + "  |  " + Cb.TypeName(doc, e)))
        Log(g.Count() + " x " + g.Key);
    int shown = 0;
    foreach (Element e in els)
    {
        if (shown++ >= 40) { Log("... (first 40 shown)"); break; }
        Level l = Cb.LevelOf(doc, e);
        string extra = "";
        Wall w = e as Wall;
        if (w != null)
        {
            Parameter pl = w.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            Parameter ph = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            extra = "  length " + (pl != null ? Math.Round(Cb.Mm(pl.AsDouble())) + " mm" : "?") + "  height " + (ph != null ? Math.Round(Cb.Mm(ph.AsDouble())) + " mm" : "?");
        }
        FamilyInstance fi = e as FamilyInstance;
        if (fi != null && fi.Host != null) extra += "  host wall id " + fi.Host.Id.IntegerValue;
        Log("Id " + e.Id.IntegerValue + "  " + Cb.TypeName(doc, e) + "  level " + (l != null ? l.Name : "-") + extra);
    }
    return;
}

if (act == "delete" && !Cb.Yes(ConfirmDelete))
    throw new Exception("Delete needs ConfirmDelete = Yes. Nothing was deleted.");

// external family: load it before the main transaction
string rfaPath = "";
if (act == "changetype")
{
    string ffText = (FamilyFile ?? "").Trim();
    if (ffText.Length > 0 && !ffText.StartsWith("("))
    {
        int fbar = ffText.IndexOf("  |  ");
        rfaPath = fbar >= 0 ? ffText.Substring(fbar + 5).Trim() : ffText;
        if (!File.Exists(rfaPath)) throw new Exception("Family file not found: " + rfaPath);
        string fn = Path.GetFileNameWithoutExtension(rfaPath);
        bool there = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().Any(f => f.Name == fn);
        if (!there)
        {
            using (Transaction tl = new Transaction(doc, "CAD Bridge: load family"))
            {
                tl.Start();
                Family loadedFam;
                if (!doc.LoadFamily(rfaPath, out loadedFam)) { tl.RollBack(); throw new Exception("Revit could not load the family: " + rfaPath); }
                tl.Commit();
            }
            Log("Family loaded into the project: " + fn);
        }
        else Log("Family is already in the project: " + fn);
    }
}

using (Transaction t = new Transaction(doc, "CAD Bridge: edit " + act))
{
    t.Start();

    if (act == "changetype")
    {
        string txt = (NewType ?? "").Trim();
        if (txt.Length == 0 && rfaPath.Length == 0) throw new Exception("Write the new type in NewType (like  Family : Type) or choose a FamilyFile.");
        string fam = "", tn = "";
        if (txt.Length > 0)
        {
            tn = txt;
            int cut = txt.IndexOf(" : ");
            if (cut >= 0) { fam = txt.Substring(0, cut).Trim(); tn = txt.Substring(cut + 3).Trim(); }
        }
        if (rfaPath.Length > 0) fam = Path.GetFileNameWithoutExtension(rfaPath);
        int ok = 0, bad = 0;
        foreach (Element e in els)
        {
            if (e.Category == null) { bad++; continue; }
            ElementId catId = e.Category.Id;
            ElementType nt = new FilteredElementCollector(doc).OfClass(typeof(ElementType)).Cast<ElementType>()
                .Where(q => q.Category != null && q.Category.Id.Equals(catId) && (tn.Length == 0 || q.Name == tn) && (fam.Length == 0 || q.FamilyName == fam))
                .OrderBy(q => q.Name).FirstOrDefault();
            if (nt == null) { bad++; continue; }
            FamilySymbol fs = nt as FamilySymbol;
            if (fs != null && !fs.IsActive) fs.Activate();
            try { e.ChangeTypeId(nt.Id); ok++; } catch { bad++; }
        }
        Log("Type changed on " + ok + " element(s)." + (bad > 0 ? " Could not change " + bad + " (type not found in the same category, or not allowed)." : ""));
    }
    else if (act == "setparameter")
    {
        string pn = (ParamName ?? "").Trim();
        if (pn.Length == 0) throw new Exception("Write the parameter name in ParamName (the same name you see in Revit Properties).");
        bool onType = ParamOn.Trim().StartsWith("T", StringComparison.OrdinalIgnoreCase);
        int ok = 0, noParam = 0, bad = 0;
        HashSet<int> doneTypes = new HashSet<int>();
        foreach (Element e in els)
        {
            Element target = e;
            if (onType)
            {
                target = Cb.TypeOf(doc, e);
                if (target == null) { noParam++; continue; }
                if (!doneTypes.Add(target.Id.IntegerValue)) continue;   // one type once
            }
            Parameter p = target.LookupParameter(pn);
            if (p == null) { noParam++; continue; }
            if (p.IsReadOnly) { bad++; continue; }
            bool done = false;
            try
            {
                if (p.StorageType == StorageType.String) done = p.Set(ParamValue);
                else if (p.StorageType == StorageType.Integer && (Cb.Yes(ParamValue) || ParamValue.Trim().StartsWith("N", StringComparison.OrdinalIgnoreCase)) && p.Definition.ParameterType == ParameterType.YesNo)
                    done = p.Set(Cb.Yes(ParamValue) ? 1 : 0);
                else if (p.StorageType == StorageType.Double || p.StorageType == StorageType.Integer)
                    done = p.SetValueString(ParamValue);
            }
            catch { done = false; }
            if (done) ok++; else bad++;
        }
        Log("Parameter '" + pn + "' set on " + ok + (onType ? " type(s)." : " element(s).") +
            (noParam > 0 ? " Not found on " + noParam + "." : "") + (bad > 0 ? " Could not set on " + bad + " (read-only or the value was not accepted)." : ""));
        if (onType) Log("Note: a type parameter changes every element of that type in the whole project.");
    }
    else if (act == "writeid")
    {
        string pn = (IdParam ?? "").Trim();
        if (pn.Length == 0) pn = "Comments";
        int ok = 0, bad = 0;
        foreach (Element e in els)
        {
            Parameter p = null;
            if (pn.Equals("Comments", StringComparison.OrdinalIgnoreCase)) p = e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            else if (pn.Equals("Mark", StringComparison.OrdinalIgnoreCase)) p = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p == null) p = e.LookupParameter(pn);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) { bad++; continue; }
            if (p.Set((IdPrefix ?? "") + e.Id.ToString())) ok++; else bad++;
        }
        Log("Element Id written into '" + pn + "' on " + ok + " element(s)." + (bad > 0 ? " Could not write on " + bad + " (no such text parameter, or read-only)." : ""));
    }
    else if (act == "move")
    {
        ElementTransformUtils.MoveElements(doc, ids, new XYZ(Cb.Ft(MoveX), Cb.Ft(MoveY), Cb.Ft(MoveZ)));
        Log("Moved " + ids.Count + " element(s) by " + Cb.N(MoveX) + ", " + Cb.N(MoveY) + ", " + Cb.N(MoveZ) + " mm.");
    }
    else if (act == "rotate")
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (Element e in els)
        {
            BoundingBoxXYZ bb = e.get_BoundingBox(null);
            if (bb == null) continue;
            minX = Math.Min(minX, bb.Min.X); minY = Math.Min(minY, bb.Min.Y);
            maxX = Math.Max(maxX, bb.Max.X); maxY = Math.Max(maxY, bb.Max.Y);
        }
        if (minX == double.MaxValue) throw new Exception("Cannot find the position of these elements.");
        XYZ c = new XYZ((minX + maxX) / 2.0, (minY + maxY) / 2.0, 0);
        Line axis = Line.CreateBound(c, c + XYZ.BasisZ);
        ElementTransformUtils.RotateElements(doc, ids, axis, Angle * Math.PI / 180.0);
        Log("Rotated " + ids.Count + " element(s) by " + Cb.N(Angle) + " degrees.");
    }
    else if (act == "copy")
    {
        int n = Math.Max(1, Copies);
        for (int k = 1; k <= n; k++)
            ElementTransformUtils.CopyElements(doc, ids, new XYZ(Cb.Ft(MoveX * k), Cb.Ft(MoveY * k), Cb.Ft(MoveZ * k)));
        Log("Made " + n + " copy(ies) of " + ids.Count + " element(s).");
    }
    else if (act == "delete")
    {
        ICollection<ElementId> gone = doc.Delete(ids);
        Log("Deleted " + gone.Count + " element(s) (including things hosted on them).");
    }
    else
        throw new Exception("Unknown Action: " + Action);

    t.Commit();
}
