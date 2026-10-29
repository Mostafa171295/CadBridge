using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Win32;

namespace CadBridge
{
    // Variables that a pasted AutoCAD / Civil 3D script can use directly
    public class AutoCadGlobals
    {
        public dynamic acad;          // the AutoCAD.Application COM object
        public dynamic civil;         // the Civil 3D application object (Civil 3D tab only, otherwise null)
        public string OutDir;
        public Action<string> Log;
    }

    // One class for both tabs: Civil 3D is AutoCAD underneath, it just adds the "civil" object.
    public class AutoCadConnector : ICadConnector
    {
        readonly bool _civil3d;
        dynamic _app;
        string _selected = "Auto";

        public AutoCadConnector(bool civil3d) { _civil3d = civil3d; }

        public string Name { get { return _civil3d ? "Civil 3D" : "AutoCAD"; } }
        public bool IsConnected { get { return _app != null; } }

        // "Auto" = whichever is running / registered, or "2021" ... "2026"
        public string SelectedVersion
        {
            get { return _selected; }
            set { _selected = value; _app = null; }
        }

        // Version-specific ProgIDs (AutoCAD 2026 = 25.1, 2025 = 25.0, 2024 = 24.3, 2023 = 24.2, 2022 = 24.1)
        static string ProgIdFor(string version)
        {
            switch (version)
            {
                case "2021": return "AutoCAD.Application.24";
                case "2022": return "AutoCAD.Application.24.1";
                case "2023": return "AutoCAD.Application.24.2";
                case "2024": return "AutoCAD.Application.24.3";
                case "2025": return "AutoCAD.Application.25.0";
                case "2026": return "AutoCAD.Application.25.1";
                default: return "AutoCAD.Application";
            }
        }

        public bool Connect(out string message)
        {
            try
            {
                string progId = ProgIdFor(_selected);
                object obj;
                try
                {
                    obj = Marshal.GetActiveObject(progId);
                }
                catch
                {
                    Type t = Type.GetTypeFromProgID(progId);
                    if (t == null)
                        throw new Exception(Name + " " + _selected + " is not installed on this PC (" + progId + " not found).");
                    obj = Activator.CreateInstance(t);
                }
                _app = obj;
                _app.Visible = true;
                string version = _app.Version;
                message = "Connected to " + Name + " (AutoCAD version " + version + ")";
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
                .AddReferences(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly,
                               typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly,
                               typeof(TableReader).Assembly)
                .AddImports("System", "System.IO", "System.Linq", "System.Collections.Generic", "CadBridge");

            object civilObj = null;
            if (_civil3d)
            {
                civilObj = ResolveCivil();
                if (civilObj == null)
                    log("Note: the Civil 3D object was not found. Is this really Civil 3D (not plain AutoCAD)? Scripts that use 'civil' will fail.");
            }

            var globals = new AutoCadGlobals { acad = _app, civil = civilObj, OutDir = outDir, Log = log };
            await CSharpScript.EvaluateAsync(code, options, globals, typeof(AutoCadGlobals));
        }

        // Civil 3D registers "AeccXUiLand.AeccApplication.<version>"; try the newest first
        object ResolveCivil()
        {
            try
            {
                const string prefix = "AeccXUiLand.AeccApplication.";
                var names = Registry.ClassesRoot.GetSubKeyNames()
                    .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(n => new { Name = n, Ver = ParseVer(n.Substring(prefix.Length)) })
                    .Where(x => x.Ver != null)
                    .OrderByDescending(x => x.Ver)
                    .Select(x => x.Name)
                    .ToList();

                foreach (string n in names)
                {
                    try
                    {
                        object o = _app.GetInterfaceObject(n);
                        if (o != null) return o;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        static Version ParseVer(string s)
        {
            Version v;
            return Version.TryParse(s, out v) ? v : null;
        }

        public void BuildAssembly(string outDir, string assemblyName, Action<string> log)
        {
            throw new NotSupportedException("Assemblies are a SolidWorks feature.");
        }
    }
}
