// Draws points from a CSV or Excel (.xlsx) file. Columns: Label, X, Y, Z (Z is optional).
// Type the file path in the TableFile box, then press Run code.
// Available variables: acad, OutDir, Log(...), and TableReader.Read(path) for reading tables.

string TableFile = @"{{TableFile=}}";            // e.g. C:\data\points.xlsx
bool HasHeader = {{HasHeader=true}};             // true = the first row is a title row
double TextHeight = {{TextHeight=0.5}};
bool Connect = {{Connect=true}};                 // true = join the points with a polyline
string DrawingName = "{{DrawingName=Points}}";

if (TableFile.Trim() == "") throw new Exception("Type the path of your .csv or .xlsx file in the TableFile box.");

var rows = TableReader.Read(TableFile);
if (HasHeader && rows.Count > 0) rows.RemoveAt(0);

var doc = acad.Documents.Add();
var ms = doc.ModelSpace;

var xy = new List<double>();
int count = 0;
foreach (string[] r in rows)
{
    if (r.Length < 3) continue;
    double x = 0, y = 0, z = 0;
    try
    {
        x = TableReader.Num(r[1]);
        y = TableReader.Num(r[2]);
        if (r.Length > 3 && r[3].Trim() != "") z = TableReader.Num(r[3]);
    }
    catch { continue; }                          // skip rows that are not numbers

    ms.AddPoint(new double[] { x, y, z });
    ms.AddText(r[0], new double[] { x + TextHeight, y + TextHeight, 0 }, TextHeight);
    xy.Add(x);
    xy.Add(y);
    count++;
}

if (Connect && count > 1) ms.AddLightWeightPolyline(xy.ToArray());
acad.ZoomExtents();

Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, DrawingName + ".dwg");
doc.SaveAs(path);
Log(count + " points drawn. Saved: " + path);
