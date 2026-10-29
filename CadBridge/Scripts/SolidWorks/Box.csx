// Box: draws a rectangle on the Top plane and extrudes it. Sizes in mm.
// Change the values in the Parameters bar, then press Run code.
// Available variables: swApp, OutDir, Log(...)

double Length = {{Length=100}};
double Width = {{Width=50}};
double Height = {{Height=30}};
string PartName = "{{PartName=Box}}";

double MM(double v) { return v / 1000.0; }

string tpl = swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
ModelDoc2 model = (ModelDoc2)swApp.NewDocument(tpl, 0, 0, 0);
if (model == null) throw new Exception("Could not create part (check default part template).");

// planes in the default template: 0 = Front, 1 = Top, 2 = Right
bool SelectPlane(int index)
{
    int n = 0;
    Feature f = (Feature)model.FirstFeature();
    while (f != null)
    {
        if (f.GetTypeName2() == "RefPlane")
        {
            if (n == index) return f.Select2(false, 0);
            n++;
        }
        f = (Feature)f.GetNextFeature();
    }
    return false;
}

model.ClearSelection2(true);
SelectPlane(1);
model.SketchManager.InsertSketch(true);
model.SketchManager.CreateCornerRectangle(0, 0, 0, MM(Length), MM(Width), 0);

Feature feat = model.FeatureManager.FeatureExtrusion2(true, false, false,
    (int)swEndConditions_e.swEndCondBlind, 0, MM(Height), 0.01, false, false, false, false,
    0.0174532925199433, 0.0174532925199433, false, false, false, false,
    true, true, true, 0, 0, false) as Feature;
if (feat == null) throw new Exception("Extrude failed.");

Directory.CreateDirectory(OutDir);
string path = Path.Combine(OutDir, PartName + ".SLDPRT");
int err = model.SaveAs3(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
model.ShowNamedView2("*Isometric", 7);
model.ViewZoomtofit2();
Log("Saved: " + path + " (err=" + err + ")");
