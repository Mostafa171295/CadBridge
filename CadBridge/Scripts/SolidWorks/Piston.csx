// Piston (parametric). Change the values in the Parameters bar, then press Run code.
// All sizes in mm. Available variables: swApp, OutDir, Log(...)

double Bore = {{Bore=52}};               // outer diameter
double Height = {{Height=40}};
double Wall = {{Wall=4}};                // wall thickness, must be more than 3 (ring grooves are 2.5 deep)
double PinDia = {{PinDia=13}};
double PinHeight = {{PinHeight=16}};     // height of the pin axis above the bottom
string PartName = "{{PartName=Piston}}";

double MM(double v) { return v / 1000.0; }

double R = Bore / 2.0;                   // outer radius
double Ri = R - Wall;                    // inner radius
double Rg = R - 2.5;                     // radius at the bottom of the ring grooves
double BossR = (PinDia + 9) / 2.0;
double BossHalfLen = Ri + 1;

if (Wall <= 3) throw new Exception("Wall must be more than 3 mm.");
if (Math.Sqrt(BossR * BossR + BossHalfLen * BossHalfLen) >= R)
    throw new Exception("The pin boss would stick out of the piston. Use a smaller PinDia or a thicker Wall.");
if (PinHeight + BossR > Height - 12)
    throw new Exception("PinHeight is too high: the pin boss would hit the ring grooves.");

string tpl = swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
ModelDoc2 model = (ModelDoc2)swApp.NewDocument(tpl, 0, 0, 0);
if (model == null) throw new Exception("Could not create part (check default part template).");

// first RefPlane in the tree = Front Plane
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

// ---------- 1) Revolve: outer body + cavity + 3 ring grooves ----------
model.ClearSelection2(true);
SelectFrontPlane();
model.SketchManager.InsertSketch(true);

SketchSegment axisSeg = model.SketchManager.CreateCenterLine(0, 0, 0, 0, MM(Height), 0);

// half profile (radius, height), closed
var pts = new List<double[]>
{
    new double[] { Ri, 0 }, new double[] { R, 0 },
    new double[] { R, Height - 11.8 }, new double[] { Rg, Height - 11.8 }, new double[] { Rg, Height - 9 }, new double[] { R, Height - 9 },
    new double[] { R, Height - 7.7 }, new double[] { Rg, Height - 7.7 }, new double[] { Rg, Height - 6.5 }, new double[] { R, Height - 6.5 },
    new double[] { R, Height - 5.2 }, new double[] { Rg, Height - 5.2 }, new double[] { Rg, Height - 4 }, new double[] { R, Height - 4 },
    new double[] { R, Height }, new double[] { 0, Height }, new double[] { 0, Height - 5 }, new double[] { Ri, Height - 5 }
};
for (int i = 0; i < pts.Count; i++)
{
    double[] a = pts[i];
    double[] b = pts[(i + 1) % pts.Count];
    model.SketchManager.CreateLine(MM(a[0]), MM(a[1]), 0, MM(b[0]), MM(b[1]), 0);
}

model.ClearSelection2(true);
SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;
SelectData sd = (SelectData)selMgr.CreateSelectData();
sd.Mark = 4;                              // 4 = revolve axis
axisSeg.Select4(false, sd);

Feature feat = model.FeatureManager.FeatureRevolve2(true, true, false, false, false, false,
    0, 0, 2 * Math.PI, 0, false, false, 0.01, 0.01, 0, 0, 0, true, true, true) as Feature;
if (feat == null) throw new Exception("Revolve failed (step 1).");

// ---------- 2) Pin boss ----------
model.ClearSelection2(true);
SelectFrontPlane();
model.SketchManager.InsertSketch(true);
model.SketchManager.CreateCircle(0, MM(PinHeight), 0, MM(BossR), MM(PinHeight), 0);

feat = model.FeatureManager.FeatureExtrusion2(true, false, false,
    (int)swEndConditions_e.swEndCondMidPlane, 0, MM(2 * BossHalfLen), 0.01, false, false, false, false,
    0.0174532925199433, 0.0174532925199433, false, false, false, false,
    true, true, true, 0, 0, false) as Feature;
if (feat == null) throw new Exception("Pin boss failed (step 2).");

// ---------- 3) Pin hole, through all ----------
model.ClearSelection2(true);
SelectFrontPlane();
model.SketchManager.InsertSketch(true);
model.SketchManager.CreateCircle(0, MM(PinHeight), 0, MM(PinDia / 2.0), MM(PinHeight), 0);

feat = model.FeatureManager.FeatureCut4(false, false, false,
    (int)swEndConditions_e.swEndCondThroughAll, (int)swEndConditions_e.swEndCondThroughAll,
    0.01, 0.01, false, false, false, false,
    0.0174532925199433, 0.0174532925199433, false, false, false, false, false,
    true, true, true, true, false, 0, 0, false, false) as Feature;
if (feat == null) throw new Exception("Pin hole failed (step 3).");

// ---------- Save ----------
Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, PartName + ".SLDPRT");
int err = model.SaveAs3(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
model.ShowNamedView2("*Isometric", 7);
model.ViewZoomtofit2();
Log("Saved: " + path + " (err=" + err + ")");
