using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
#if HAS_SOLIDWORKS
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
#endif

namespace CadBridge
{
#if HAS_SOLIDWORKS
    // Variables that the pasted script can use directly
    public class ScriptGlobals
    {
        public SldWorks swApp;
        public string OutDir;
        public Action<string> Log;
    }

    public class SolidWorksConnector : ICadConnector
    {
        SldWorks _app;

        public string Name { get { return "SolidWorks"; } }
        public bool IsConnected { get { return _app != null; } }

        public bool Connect(out string message)
        {
            try
            {
                try
                {
                    _app = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
                }
                catch
                {
                    Type t = Type.GetTypeFromProgID("SldWorks.Application");
                    if (t == null) throw new Exception("SolidWorks is not installed on this PC (SldWorks.Application is not registered).");
                    _app = (SldWorks)Activator.CreateInstance(t);
                }
                _app.Visible = true;
                message = "Connected to SolidWorks " + _app.RevisionNumber();
                return true;
            }
            catch (Exception ex)
            {
                _app = null;
                message = "Connect failed: " + ex.Message;
                return false;
            }
        }

        public async Task RunScriptAsync(string code, string outDir, Action<string> log)
        {
            var options = ScriptOptions.Default
                .AddReferences(typeof(SldWorks).Assembly, typeof(swDocumentTypes_e).Assembly,
                               typeof(TableReader).Assembly)
                .AddImports("System", "System.IO", "System.Linq", "System.Collections.Generic", "CadBridge",
                            "SolidWorks.Interop.sldworks", "SolidWorks.Interop.swconst");

            var globals = new ScriptGlobals { swApp = _app, OutDir = outDir, Log = log };
            await CSharpScript.EvaluateAsync(code, options, globals, typeof(ScriptGlobals));
        }

        // Inserts every .SLDPRT in outDir into a new assembly and saves it.
        // Mates are the next phase: for now parts are placed side by side, first one fixed.
        public void BuildAssembly(string outDir, string assemblyName, Action<string> log)
        {
            // skip the hidden "~$Name.SLDPRT" lock files SolidWorks keeps next to open parts
            string[] files = Directory.GetFiles(outDir, "*.SLDPRT")
                .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                .OrderBy(f => f)
                .ToArray();
            if (files.Length == 0) { log("No .SLDPRT files in " + outDir); return; }

            string tpl = _app.GetUserPreferenceStringValue(
                (int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            ModelDoc2 asmModel = (ModelDoc2)_app.NewDocument(tpl, 0, 0, 0);
            if (asmModel == null) { log("Could not create assembly (check default assembly template)."); return; }
            AssemblyDoc asm = (AssemblyDoc)asmModel;

            double x = 0;
            foreach (string f in files)
            {
                int err = 0, warn = 0;
                _app.OpenDoc6(f, (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);

                Component2 c = (Component2)asm.AddComponent5(f,
                    (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                    "", false, "", x, 0, 0);
                log((c != null ? "Added: " : "FAILED to add: ") + Path.GetFileName(f));
                x += 0.1; // 100 mm spacing
            }

            string path = Path.Combine(outDir, assemblyName + ".SLDASM");
            int e = asmModel.SaveAs3(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent);
            asmModel.ViewZoomtofit2();
            log("Assembly saved: " + path + " (err=" + e + ")");
        }
    }
#else
    // Built without the SolidWorks DLLs: the tab stays, but it cannot connect.
    public class SolidWorksConnector : ICadConnector
    {
        const string Msg = "This build has no SolidWorks support (the SolidWorks interop DLLs were not found when it was built). " +
                           "Install SolidWorks, or set SolidWorksApiDir in CadBridge.csproj, then build again.";

        public string Name { get { return "SolidWorks"; } }
        public bool IsConnected { get { return false; } }

        public bool Connect(out string message) { message = Msg; return false; }

        public Task RunScriptAsync(string code, string outDir, Action<string> log)
        {
            throw new NotSupportedException(Msg);
        }

        public void BuildAssembly(string outDir, string assemblyName, Action<string> log)
        {
            throw new NotSupportedException(Msg);
        }
    }
#endif
}
