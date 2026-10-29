// Plots every .dwg in a folder to PDF in one go (uses each drawing's current plot settings).
// Available variables: acad, OutDir, Log(...)

string Folder = @"{{Folder=}}";                  // empty = this project's Files folder
if (Folder.Trim() == "") Folder = OutDir;

string[] files = Directory.GetFiles(Folder, "*.dwg");
if (files.Length == 0) throw new Exception("No .dwg files in " + Folder);

int ok = 0;
foreach (string file in files)
{
    try
    {
        var d = acad.Documents.Open(file);
        d.SetVariable("BACKGROUNDPLOT", 0);          // plot now, not in the background
        string pdf = Path.ChangeExtension(file, ".pdf");
        d.Plot.PlotToFile(pdf, "DWG To PDF.pc3");
        d.Close(false);
        ok++;
        Log("PDF: " + Path.GetFileName(pdf));
    }
    catch (Exception ex)
    {
        Log("Failed " + Path.GetFileName(file) + ": " + ex.Message);
    }
}
Log(ok + " of " + files.Length + " drawings converted.");
