// Steel I-girder (bridge beam): draws the cross-section and extrudes it along the span.
// Sizes in mm. Change the values in the Parameters bar, then press Run code.
// Available variables: swApp, OutDir, Log(...)

double Span = {{Span=12000}};
double FlangeWidth = {{FlangeWidth=400}};
double FlangeThick = {{FlangeThick=25}};
double WebHeight = {{WebHeight=1000}};
double WebThick = {{WebThick=14}};
string PartName = "{{PartName=Girder}}";

if (WebThick >= FlangeWidth) throw new Exception("WebThick must be smaller than FlangeWidth.");

double MM(double v) { return v / 1000.0; }

string tpl = swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
ModelDoc2 model = (ModelDoc2)swApp.NewDocument(tpl, 0, 0, 0);
if (model == null) throw new Exception("Could not create part (check default part template).");

// first RefPlane in the tree = Front Plane (XY); the girder is extruded along Z
bool SelectFrontPlane()
{
    Feature f = (Feature)model.FirstFeature();
    while (f != null)
    {
        if (f.GetTypeName2() == "RefPlane") return f.Select2(false, 0);
        f = (Feature)f.GetNextFeature();
    }
    return false;
}

double hb = FlangeWidth / 2.0, hw = WebThick / 2.0, tf = FlangeThick, wh = WebHeight;

double[][] pts = new double[][]
{
    new double[] { -hb, 0 },            new double[] { hb, 0 },
    new double[] { hb, tf },            new double[] { hw, tf },
    new double[] { hw, tf + wh },       new double[] { hb, tf + wh },
    new double[] { hb, 2 * tf + wh },   new double[] { -hb, 2 * tf + wh },
    new double[] { -hb, tf + wh },      new double[] { -hw, tf + wh },
    new double[] { -hw, tf },           new double[] { -hb, tf }
};

model.ClearSelection2(true);
SelectFrontPlane();
model.SketchManager.InsertSketch(true);
for (int i = 0; i < pts.Length; i++)
{
    double[] a = pts[i];
    double[] b = pts[(i + 1) % pts.Length];
    model.SketchManager.CreateLine(MM(a[0]), MM(a[1]), 0, MM(b[0]), MM(b[1]), 0);
}

Feature feat = model.FeatureManager.FeatureExtrusion2(true, false, false,
    (int)swEndConditions_e.swEndCondBlind, 0, MM(Span), 0.01, false, false, false, false,
    0.0174532925199433, 0.0174532925199433, false, false, false, false,
    true, true, true, 0, 0, false) as Feature;
if (feat == null) throw new Exception("Extrude failed.");

Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, PartName + ".SLDPRT");
int err = model.SaveAs3(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
model.ShowNamedView2("*Isometric", 7);
model.ViewZoomtofit2();
Log("Saved: " + path + " (err=" + err + ")");
