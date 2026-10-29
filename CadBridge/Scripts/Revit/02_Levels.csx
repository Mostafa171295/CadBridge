// Revit STEP 2: creates the levels of the building. Sizes in mm.
//   Heights = the height of each storey from the ground upward, separated by commas (same format as the grid spans):
//             4x3200  ->  Ground, Level 1, Level 2, Level 3 and Roof   (the last height reaches the Roof level)
//   FoundationDepth = how far BELOW the ground level the Foundation level is (0 = do not create it).
//   GroundName / Prefix / RoofName / FoundationName = the names (Prefix + number: Level 1, Level 2 ...).
//   CreatePlans = Yes -> a structural plan view is made for every NEW level.
// A level that already has the same name is moved to the wanted elevation instead of being duplicated
// (so the default "Level 1" / "Level 2" of a new project can be reused by naming them like that).
// Available variables: uiapp, doc, OutDir, Log(...)

string Heights = "{{Heights=4x3200}}";
double GroundElevation = {{GroundElevation=0}};
double FoundationDepth = {{FoundationDepth=1500}};
string GroundName = "{{GroundName=Ground}}";
string Prefix = "{{Prefix=Level}}";
string RoofName = "{{RoofName=Roof}}";
string FoundationName = "{{FoundationName=Foundation}}";
string CreatePlans = "{{CreatePlans=Yes|No}}";

double Ft(double mm) { return mm / 304.8; }      // Revit works in feet internally

List<double> Spans(string text, string what)
{
    var list = new List<double>();
    foreach (string raw in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
    {
        string tok = raw.Trim().ToLowerInvariant().Replace(" ", "");
        if (tok.Length == 0) continue;
        int count = 1;
        string val = tok;
        int mark = tok.IndexOf('x');
        if (mark > 0)
        {
            if (!int.TryParse(tok.Substring(0, mark), out count) || count < 1)
                throw new Exception(what + ": cannot read '" + raw.Trim() + "'. Use numbers like 3200 or 4x3200, separated by commas.");
            val = tok.Substring(mark + 1);
        }
        double d;
        if (!double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) || d <= 0)
            throw new Exception(what + ": cannot read '" + raw.Trim() + "'. Use numbers like 3200 or 4x3200, separated by commas.");
        for (int k = 0; k < count; k++) list.Add(d);
    }
    if (list.Count == 0) throw new Exception(what + " is empty.");
    return list;
}

List<double> storeyHeights = Spans(Heights, "Heights");

// the wanted levels: name + elevation (mm), lowest first
var wanted = new List<KeyValuePair<string, double>>();
if (FoundationDepth > 0) wanted.Add(new KeyValuePair<string, double>(FoundationName.Trim(), GroundElevation - FoundationDepth));
wanted.Add(new KeyValuePair<string, double>(GroundName.Trim(), GroundElevation));
double zNow = GroundElevation;
for (int k = 0; k < storeyHeights.Count; k++)
{
    zNow += storeyHeights[k];
    string nm = (k == storeyHeights.Count - 1) ? RoofName.Trim() : Prefix.Trim() + " " + (k + 1);
    wanted.Add(new KeyValuePair<string, double>(nm, zNow));
}

var dupNames = wanted.GroupBy(w => w.Key).Where(gp => gp.Count() > 1).Select(gp => gp.Key).ToList();
if (dupNames.Count > 0) throw new Exception("These level names are used twice: " + string.Join(", ", dupNames) + ". Change GroundName / Prefix / RoofName.");

var existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();

// a view type for the new plan views (structural plan, or floor plan as a second choice)
ViewFamilyType planType = null;
if (CreatePlans.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase))
{
    var vTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
    planType = vTypes.FirstOrDefault(v => v.ViewFamily == ViewFamily.StructuralPlan) ?? vTypes.FirstOrDefault(v => v.ViewFamily == ViewFamily.FloorPlan);
}

int created = 0, moved = 0, kept = 0, plans = 0;
using (Transaction t = new Transaction(doc, "CAD Bridge: levels"))
{
    t.Start();
    foreach (KeyValuePair<string, double> w in wanted)
    {
        Level lv = existing.FirstOrDefault(l => l.Name == w.Key);
        if (lv != null)
        {
            if (Math.Abs(lv.Elevation - Ft(w.Value)) > Ft(0.5)) { lv.Elevation = Ft(w.Value); moved++; Log("Level '" + w.Key + "' moved to " + num(w.Value) + " mm."); }
            else kept++;
            continue;
        }

        lv = Level.Create(doc, Ft(w.Value));
        lv.Name = w.Key;
        created++;

        if (planType != null)
        {
            try { ViewPlan.Create(doc, planType.Id, lv.Id); plans++; }
            catch (Exception ex) { Log("Could not make the plan view of '" + w.Key + "': " + ex.Message); }
        }
    }
    t.Commit();
}

foreach (KeyValuePair<string, double> w in wanted) Log("   " + w.Key + "   " + num(w.Value) + " mm");
Log("Levels: " + created + " created, " + moved + " moved, " + kept + " already right. Plan views made: " + plans + ".");

string num(double v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
