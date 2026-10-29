// Piston front view with 3 ring grooves and the pin hole. Sizes in mm.
// Change the values in the Parameters bar, then press Run code.
// Available variables: acad, OutDir, Log(...)

double Bore = {{Bore=52}};
double Height = {{Height=40}};
double PinDia = {{PinDia=13}};
double PinHeight = {{PinHeight=16}};
string DrawingName = "{{DrawingName=Piston_View}}";

double R = Bore / 2.0;
double Rg = R - 2.5;

// right half of the outline from bottom to top, grooves included
double[] rx = { R, R, Rg, Rg, R, R, Rg, Rg, R, R, Rg, Rg, R, R };
double[] ry = { 0, Height - 11.8, Height - 11.8, Height - 9, Height - 9, Height - 7.7, Height - 7.7,
                Height - 6.5, Height - 6.5, Height - 5.2, Height - 5.2, Height - 4, Height - 4, Height };
int n = rx.Length;

// full closed outline: up the right side, then down the mirrored left side
double[] pts = new double[n * 4];
for (int i = 0; i < n; i++)
{
    pts[i * 2] = rx[i];
    pts[i * 2 + 1] = ry[i];
}
for (int i = 0; i < n; i++)
{
    int k = n + i;
    pts[k * 2] = -rx[n - 1 - i];
    pts[k * 2 + 1] = ry[n - 1 - i];
}

var doc = acad.Documents.Add();
var ms = doc.ModelSpace;

var outline = ms.AddLightWeightPolyline(pts);
outline.Closed = true;
ms.AddCircle(new double[] { 0, PinHeight, 0 }, PinDia / 2.0);
ms.AddLine(new double[] { 0, -5, 0 }, new double[] { 0, Height + 5, 0 });

acad.ZoomExtents();
Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, DrawingName + ".dwg");
doc.SaveAs(path);
Log("Saved: " + path);
