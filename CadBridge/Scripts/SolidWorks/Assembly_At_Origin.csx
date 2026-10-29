// Builds an assembly from every .SLDPRT in a folder. All parts are placed at the ORIGIN, so parts that
// were modeled in the SAME coordinate system land in their correct place (no mates needed).
// Available variables: swApp, OutDir, Log(...)

string Folder = @"{{Folder=}}";                  // empty = this project's Files folder
string AssemblyName = "{{AssemblyName=Assembly1}}";
bool FixAll = {{FixAll=true}};                   // true = lock every part in place

if (Folder.Trim() == "") Folder = OutDir;

string[] files = Directory.GetFiles(Folder, "*.SLDPRT")
    .Where(p => !Path.GetFileName(p).StartsWith("~$"))
    .OrderBy(p => p)
    .ToArray();
if (files.Length == 0) throw new Exception("No .SLDPRT files in " + Folder);

string tpl = swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
ModelDoc2 asmModel = (ModelDoc2)swApp.NewDocument(tpl, 0, 0, 0);
if (asmModel == null) throw new Exception("Could not create the assembly (check the default assembly template).");
AssemblyDoc asm = (AssemblyDoc)asmModel;

foreach (string file in files)
{
    int e1 = 0, w1 = 0;
    swApp.OpenDoc6(file, (int)swDocumentTypes_e.swDocPART,
        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e1, ref w1);

    Component2 comp = (Component2)asm.AddComponent5(file,
        (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
        "", false, "", 0, 0, 0);

    if (comp == null) { Log("FAILED to add: " + Path.GetFileName(file)); continue; }
    Log("Added at origin: " + Path.GetFileName(file));

    if (FixAll)
    {
        asmModel.ClearSelection2(true);
        comp.Select4(false, null, false);
        asm.FixComponent();
    }
}

string path = Path.Combine(Folder, AssemblyName + ".SLDASM");
int err = asmModel.SaveAs3(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
asmModel.ViewZoomtofit2();
Log("Assembly saved: " + path + " (err=" + err + ")");
