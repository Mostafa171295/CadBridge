// Exports every SolidWorks part (and assembly) in a folder to another format in one go.
// Format examples: STEP, IGES, STL, X_T.   Available variables: swApp, OutDir, Log(...)

string Folder = @"{{Folder=}}";                  // empty = this project's Files folder
string Format = "{{Format=STEP}}";
bool IncludeAssemblies = {{IncludeAssemblies=true}};

if (Folder.Trim() == "") Folder = OutDir;
string ext = "." + Format.Trim().TrimStart('.').ToLower();

var files = Directory.GetFiles(Folder)
    .Where(p =>
    {
        string n = Path.GetFileName(p);
        string x = Path.GetExtension(p).ToLower();
        if (n.StartsWith("~$")) return false;
        return x == ".sldprt" || (IncludeAssemblies && x == ".sldasm");
    })
    .OrderBy(p => p)
    .ToList();
if (files.Count == 0) throw new Exception("No SolidWorks files found in " + Folder);

int done = 0;
foreach (string file in files)
{
    bool isAsm = Path.GetExtension(file).ToLower() == ".sldasm";
    int e1 = 0, w1 = 0;
    ModelDoc2 m = swApp.OpenDoc6(file,
        isAsm ? (int)swDocumentTypes_e.swDocASSEMBLY : (int)swDocumentTypes_e.swDocPART,
        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref e1, ref w1);
    if (m == null) { Log("Could not open " + Path.GetFileName(file) + " (error " + e1 + ")"); continue; }

    string target = Path.ChangeExtension(file, ext);
    int err = m.SaveAs3(target, 0,
        (int)swSaveAsOptions_e.swSaveAsOptions_Silent | (int)swSaveAsOptions_e.swSaveAsOptions_Copy);
    Log((err == 0 ? "Exported: " : "Export problem (" + err + "): ") + Path.GetFileName(target));
    if (err == 0) done++;
    swApp.CloseDoc(m.GetTitle());
}
Log(done + " of " + files.Count + " files exported.");
