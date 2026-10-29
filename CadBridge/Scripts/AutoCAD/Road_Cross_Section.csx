// Road cross-section: surface line with crossfall, lane marks and labels. Units: meters.
// Change the values in the Parameters bar, then press Run code.
// Available variables: acad, OutDir, Log(...)

int LanesPerSide = {{LanesPerSide=2}};
double LaneWidth = {{LaneWidth=3.65}};
double Shoulder = {{Shoulder=2.5}};              // each side
double CrossfallPercent = {{CrossfallPercent=2}};
string DrawingName = "{{DrawingName=Road_Section}}";

string F(double v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

double slope = CrossfallPercent / 100.0;
double carr = LanesPerSide * LaneWidth;          // half width of the carriageway
double half = carr + Shoulder;                   // half width including shoulders

// x positions from the left edge to the right edge
var xs = new List<double>();
xs.Add(-half);
for (int k = 0; k <= LanesPerSide; k++) xs.Add(-carr + k * LaneWidth);   // left lanes up to the centre line
for (int k = 1; k <= LanesPerSide; k++) xs.Add(k * LaneWidth);           // right lanes
xs.Add(half);

var doc = acad.Documents.Add();
var ms = doc.ModelSpace;

// surface line (crown in the middle, falling to both edges)
double[] pts = new double[xs.Count * 2];
for (int i = 0; i < xs.Count; i++)
{
    pts[i * 2] = xs[i];
    pts[i * 2 + 1] = (half - Math.Abs(xs[i])) * slope;
}
ms.AddLightWeightPolyline(pts);

// small ticks at every lane boundary
foreach (double x in xs)
{
    double y = (half - Math.Abs(x)) * slope;
    ms.AddLine(new double[] { x, y, 0 }, new double[] { x, y - 0.3, 0 });
}

// labels
double ty = -0.8;
for (int k = 0; k < LanesPerSide; k++)
{
    ms.AddText("Lane " + F(LaneWidth) + " m", new double[] { -carr + k * LaneWidth + 0.2, ty, 0 }, 0.25);
    ms.AddText("Lane " + F(LaneWidth) + " m", new double[] { k * LaneWidth + 0.2, ty, 0 }, 0.25);
}
ms.AddText("Total width " + F(2 * half) + " m    Crossfall " + F(CrossfallPercent) + " %",
           new double[] { -half, ty - 0.6, 0 }, 0.3);

acad.ZoomExtents();
Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, DrawingName + ".dwg");
doc.SaveAs(path);
Log("Saved: " + path);
