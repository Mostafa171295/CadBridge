// Revit STEP 1: draws the structural axes (grids). Sizes in mm.
//   Vertical grids   (they run along Y) are numbered   1, 2, 3 ...
//   Horizontal grids (they run along X) are lettered   A, B, C ...
// SpansX / SpansY = the distances between neighbouring grids, separated by commas:
//   4x5000          four spans of 5000                      (= 5 grids)
//   6000, 2x4500    one span of 6000, then two of 4500      (= 4 grids)
// Extend  = how far the grid lines go beyond the outer grids.
// OriginX / OriginY = where the first grid is. FirstNumber / FirstLetter = the first names.
// If a grid with the same name already exists it is left alone (nothing is duplicated).
// Available variables: uiapp, doc, OutDir, Log(...)

string SpansX = "{{SpansX=4x5000}}";
string SpansY = "{{SpansY=3x5000}}";
double Extend = {{Extend=1500}};
double OriginX = {{OriginX=0}};
double OriginY = {{OriginY=0}};
int FirstNumber = {{FirstNumber=1}};
string FirstLetter = "{{FirstLetter=A}}";

double Ft(double mm) { return mm / 304.8; }      // Revit works in feet internally

// "4x5000, 6000"  ->  5000, 5000, 5000, 5000, 6000
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
                throw new Exception(what + ": cannot read '" + raw.Trim() + "'. Use numbers like 5000 or 4x5000, separated by commas.");
            val = tok.Substring(mark + 1);
        }
        double d;
        if (!double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) || d <= 0)
            throw new Exception(what + ": cannot read '" + raw.Trim() + "'. Use numbers like 5000 or 4x5000, separated by commas.");
        for (int k = 0; k < count; k++) list.Add(d);
    }
    if (list.Count == 0) throw new Exception(what + " is empty.");
    return list;
}

// 0 -> A, 1 -> B ... 25 -> Z, 26 -> AA
string Letters(int index)
{
    string r = "";
    int n = index;
    do { r = ((char)('A' + n % 26)).ToString() + r; n = n / 26 - 1; } while (n >= 0);
    return r;
}
int LetterIndex(string text)
{
    int v = 0;
    foreach (char c in text.Trim().ToUpperInvariant())
    {
        if (c < 'A' || c > 'Z') throw new Exception("FirstLetter must be letters only, for example A.");
        v = v * 26 + (c - 'A' + 1);
    }
    if (v == 0) throw new Exception("FirstLetter is empty.");
    return v - 1;
}

List<double> stepsX = Spans(SpansX, "SpansX");
List<double> stepsY = Spans(SpansY, "SpansY");

var xs = new List<double> { OriginX };
foreach (double dx in stepsX) xs.Add(xs[xs.Count - 1] + dx);
var ys = new List<double> { OriginY };
foreach (double dy in stepsY) ys.Add(ys[ys.Count - 1] + dy);

double xMin = xs[0], xMax = xs[xs.Count - 1], yMin = ys[0], yMax = ys[ys.Count - 1];
int firstLetterIndex = LetterIndex(FirstLetter);

var existingNames = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Name));
int made = 0, skipped = 0;

using (Transaction t = new Transaction(doc, "CAD Bridge: grids"))
{
    t.Start();

    // vertical grids: 1, 2, 3 ...
    for (int j = 0; j < xs.Count; j++)
    {
        string nm = (FirstNumber + j).ToString();
        if (existingNames.Contains(nm)) { skipped++; Log("Grid " + nm + " already exists - skipped."); continue; }
        Line ln = Line.CreateBound(new XYZ(Ft(xs[j]), Ft(yMin - Extend), 0), new XYZ(Ft(xs[j]), Ft(yMax + Extend), 0));
        Grid gr = Grid.Create(doc, ln);
        gr.Name = nm;
        existingNames.Add(nm);
        made++;
    }

    // horizontal grids: A, B, C ...
    for (int i = 0; i < ys.Count; i++)
    {
        string nm = Letters(firstLetterIndex + i);
        if (existingNames.Contains(nm)) { skipped++; Log("Grid " + nm + " already exists - skipped."); continue; }
        Line ln = Line.CreateBound(new XYZ(Ft(xMin - Extend), Ft(ys[i]), 0), new XYZ(Ft(xMax + Extend), Ft(ys[i]), 0));
        Grid gr = Grid.Create(doc, ln);
        gr.Name = nm;
        existingNames.Add(nm);
        made++;
    }

    t.Commit();
}

Log("Created " + made + " grids: " + xs.Count + " vertical (" + FirstNumber + " ... " + (FirstNumber + xs.Count - 1) + ") and " +
    ys.Count + " horizontal (" + Letters(firstLetterIndex) + " ... " + Letters(firstLetterIndex + ys.Count - 1) + ").");
Log("Total length  X = " + num(xMax - xMin) + " mm   Y = " + num(yMax - yMin) + " mm.");
if (skipped > 0) Log("Skipped " + skipped + " grids that already existed.");

string num(double v) { return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); }
