using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CadBridge
{
    // Revit has no external COM interface, so:
    //  - the RevitBridge add-in lives inside Revit (this program compiles + installs it: "Install add-in"),
    //  - scripts are compiled HERE into a small DLL (against Revit's own API files),
    //  - the DLL is sent through a named pipe to the add-in, which runs it on Revit's main thread.
    // Revit 2021-2024 run on .NET Framework 4.8, Revit 2025-2026 on .NET 8:
    // everything is compiled against the matching framework.
    public class RevitConnector : ICadConnector, IAddinInstaller, IChoiceProvider, IFamilyFolderProvider
    {
        const string AddinGuid = "2E9A7C04-D448-41E9-A7C1-39807C124551";

        static readonly Dictionary<int, List<MetadataReference>> RefCache = new Dictionary<int, List<MetadataReference>>();

        int _year = 2021;
        bool _connected;

        public string Name { get { return "Revit " + _year; } }
        public bool IsConnected { get { return _connected; } }

        public int Year
        {
            get { return _year; }
            set { _year = value; _connected = false; }
        }

        string PipeName { get { return "CadBridgeRevit" + _year; } }
        string RevitDir { get { return @"C:\Program Files\Autodesk\Revit " + _year; } }

        public static int DetectNewestInstalledYear()
        {
            for (int y = 2026; y >= 2021; y--)
                if (Directory.Exists(@"C:\Program Files\Autodesk\Revit " + y)) return y;
            return 2021;
        }

        // ------------------------------------------------------------------ connect / run

        public bool Connect(out string message)
        {
            _connected = false;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut))
                {
                    pipe.Connect(2000);
                    var enc = new UTF8Encoding(false);
                    var writer = new StreamWriter(pipe, enc) { AutoFlush = true };
                    var reader = new StreamReader(pipe, enc);

                    writer.WriteLine("PING");
                    string reply = reader.ReadLine();
                    if (reply != null && reply.StartsWith("OK"))
                    {
                        _connected = true;
                        message = "Connected to " + reply.Substring(2).Trim();
                        return true;
                    }
                    message = "Unexpected reply from the Revit add-in.";
                    return false;
                }
            }
            catch (TimeoutException)
            {
                message = "Could not reach the Revit " + _year + " add-in. Is Revit " + _year +
                          " open? If this is the first time, close Revit, press Install add-in, then open Revit again.";
                return false;
            }
            catch (Exception ex)
            {
                message = "Connect failed: " + ex.Message;
                return false;
            }
        }

        public async Task RunScriptAsync(string code, string outDir, Action<string> log)
        {
            byte[] dll = Compile(code);

            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try { pipe.Connect(3000); }
                catch (TimeoutException)
                {
                    throw new Exception("Could not reach the Revit " + _year + " add-in. Is Revit open?");
                }

                var enc = new UTF8Encoding(false);
                var writer = new StreamWriter(pipe, enc) { AutoFlush = true };
                var reader = new StreamReader(pipe, enc);

                await writer.WriteLineAsync("RUN");
                await writer.WriteLineAsync(outDir);
                await writer.WriteLineAsync(Convert.ToBase64String(dll));

                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("LOG:")) log(line.Substring(4));
                    else if (line.StartsWith("ERROR:")) throw new Exception(line.Substring(6));
                    else if (line == "DONE") return;
                }
                throw new Exception("The Revit add-in closed the connection before the script finished.");
            }
        }


        // ------------------------------------------------------------------ lists for the drop-down boxes

        static string CategoryTypes(string cat)
        {
            return "new FilteredElementCollector(doc).OfCategory(BuiltInCategory." + cat + ").OfClass(typeof(FamilySymbol))" +
                   ".Cast<FamilySymbol>().Select(e => e.FamilyName + \" : \" + e.Name).Distinct().OrderBy(n => n).ToList()";
        }

        // C# expression (inside the Revit script) that returns a List<string>, or null for an unknown source
        static string ListExpression(string key)
        {
            switch (key)
            {
                case "levels":
                    return "new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(e => e.Elevation).Select(e => e.Name).ToList()";
                case "materials":
                    return "new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                case "materialclasses":
                    return "new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().Select(e => e.MaterialClass).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n).ToList()";
                case "columntypes": return CategoryTypes("OST_StructuralColumns");
                case "beamtypes": return CategoryTypes("OST_StructuralFraming");
                case "footingtypes": return CategoryTypes("OST_StructuralFoundation");
                case "stairtypes":
                    return "new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.StairsType)).Cast<Autodesk.Revit.DB.Architecture.StairsType>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                case "railingtypes":
                    return "new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Architecture.RailingType)).Cast<Autodesk.Revit.DB.Architecture.RailingType>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                case "doortypes": return CategoryTypes("OST_Doors");
                case "windowtypes": return CategoryTypes("OST_Windows");
                case "walltypes":
                    return "new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                case "rooftypes":
                    return "new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                case "floortypes":
                    return "new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().Select(e => e.Name).Distinct().OrderBy(n => n).ToList()";
                default: return null;
            }
        }

        // One small script reads every requested list in one go (so Revit is asked only once).
        public async Task<Dictionary<string, List<string>>> GetChoicesAsync(IList<string> sources)
        {
            var result = new Dictionary<string, List<string>>();
            var code = new StringBuilder();
            foreach (string s in sources)
            {
                string key = ParamParser.NormSource(s);
                if (result.ContainsKey(key)) continue;
                result[key] = new List<string>();
                string folder = FamilyFolderFor(key);
                if (folder != null) { result[key] = FamilyFiles(key, folder); continue; }   // read from disk, Revit not needed
                string expr = ListExpression(key);
                if (expr == null) continue;
                code.AppendLine("{ foreach (string v in " + expr + ") Log(\"" + key + "\\t\" + v); }");
            }
            if (code.Length == 0) return result;

            await RunScriptAsync(code.ToString(), Path.GetTempPath(), line =>
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) return;
                List<string> list;
                if (result.TryGetValue(line.Substring(0, tab), out list)) list.Add(line.Substring(tab + 1));
            });
            return result;
        }


        // ------------------------------------------------------------------ family files (.rfa) on this PC

        // @columnFiles -> the folder called "Structural Columns" inside Revit's library, and so on
        static string FamilyFolderFor(string key)
        {
            switch (key)
            {
                case "columnfiles": return "Structural Columns";
                case "beamfiles": return "Structural Framing";
                case "footingfiles": return "Structural Foundations";
                case "doorfiles": return "Doors";
                case "windowfiles": return "Windows";
                case "anyfiles": return "*";          // any family: your chosen folder, or Documents\CadBridge\Families
                default: return null;
            }
        }

        static void FindFolders(string dir, string name, int depth, List<string> found)
        {
            if (depth > 6) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (string d in subs)
            {
                if (Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase)) { found.Add(d); continue; }
                FindFolders(d, name, depth + 1, found);
            }
        }

        // ---- the "Folder..." button: every kind of family list can use a folder chosen by the user
        public bool IsFileSource(string source) { return FamilyFolderFor(ParamParser.NormSource(source)) != null; }
        public string GetCustomFolder(string source) { return FamilyFolders.Get(ParamParser.NormSource(source)); }
        public void SetCustomFolder(string source, string folder) { FamilyFolders.Set(ParamParser.NormSource(source), folder); }

        static void AddRfaFiles(string folder, List<string> files)
        {
            try
            {
                foreach (string f in Directory.GetFiles(folder, "*.rfa", SearchOption.AllDirectories))
                    if (!System.Text.RegularExpressions.Regex.IsMatch(f, @"\.\d{4}\.rfa$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        files.Add(f);       // skip Revit backup files like Name.0001.rfa
            }
            catch { }
        }

        // First item is "(none)" so nothing is loaded unless you pick a file.
        // Every other item looks like:  Name  |  full path of the .rfa file
        // Without a chosen folder it looks in Revit's library (ProgramData\Autodesk\RVT <year>\Libraries) and in
        // Documents\CadBridge\Families. When the user picked a folder (Folder... button) ONLY that folder is read.
        List<string> FamilyFiles(string key, string folderName)
        {
            var files = new List<string>();
            string custom = FamilyFolders.Get(key);

            if (custom.Length > 0)
            {
                if (Directory.Exists(custom)) AddRfaFiles(custom, files);
            }
            else if (folderName == "*")
            {
                string mine = Path.Combine(ProjectManager.DocsRoot, "Families");
                if (Directory.Exists(mine)) AddRfaFiles(mine, files);
            }
            else
            {
                var roots = new[]
                {
                    @"C:\ProgramData\Autodesk\RVT " + _year + @"\Libraries",
                    Path.Combine(ProjectManager.DocsRoot, "Families")
                };
                foreach (string root in roots)
                {
                    if (!Directory.Exists(root)) continue;
                    var folders = new List<string>();
                    FindFolders(root, folderName, 0, folders);
                    foreach (string folder in folders) AddRfaFiles(folder, files);
                }
            }

            var list = new List<string> { "(none)" };
            list.AddRange(files.Distinct(StringComparer.OrdinalIgnoreCase)
                               .OrderBy(f => Path.GetFileNameWithoutExtension(f)).ThenBy(f => f)
                               .Select(f => Path.GetFileNameWithoutExtension(f) + "  |  " + f));
            return list;
        }

        // ------------------------------------------------------------------ install the add-in

        public string InstallAddin()
        {
            string source = RevitBridgeSource.Code;

            byte[] dll = CompileSource(source, "RevitBridge");

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "Autodesk", "Revit", "Addins", _year.ToString());
            Directory.CreateDirectory(dir);
            string dllPath = Path.Combine(dir, "RevitBridge.dll");
            string addinPath = Path.Combine(dir, "RevitBridge.addin");

            try { File.WriteAllBytes(dllPath, dll); }
            catch (IOException)
            {
                throw new Exception("Could not write " + dllPath + ". Close Revit " + _year + " and try again.");
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("No permission to write in " + dir + ".");
            }

            string manifest =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<RevitAddIns>\r\n" +
                "  <AddIn Type=\"Application\">\r\n" +
                "    <Name>CAD Bridge for Revit</Name>\r\n" +
                "    <Assembly>" + SecurityElement.Escape(dllPath) + "</Assembly>\r\n" +
                "    <AddInId>" + AddinGuid + "</AddInId>\r\n" +
                "    <FullClassName>RevitBridge.BridgeApp</FullClassName>\r\n" +
                "    <VendorId>CADB</VendorId>\r\n" +
                "    <VendorDescription>CAD Bridge</VendorDescription>\r\n" +
                "  </AddIn>\r\n" +
                "</RevitAddIns>\r\n";
            File.WriteAllText(addinPath, manifest, new UTF8Encoding(false));

            return "Add-in installed for Revit " + _year + " in " + dir +
                   ". Now open (or restart) Revit, choose \"Always Load\" if it asks, then press Connect.";
        }

        // ------------------------------------------------------------------ compiling

        // the user's code becomes the body of UserScript.Run(...)
        // Scripts that use  Cb. / CbRow / CbGrid / CbXlsx / CbSheet / CbBoq  get the shared library (Lib\CbLib.cs.txt) compiled in.
        byte[] Compile(string userCode)
        {
            string src =
                "using System; using System.IO; using System.Linq; using System.Collections.Generic;\n" +
                "using Autodesk.Revit.DB; using Autodesk.Revit.UI;\n" +
                "public static class UserScript {\n" +
                "  public static void Run(UIApplication uiapp, Document doc, string OutDir, Action<string> Log) {\n" +
                "#line 1\n" +
                userCode + "\n" +
                "  }\n}\n";
            bool needLib = userCode.Contains("Cb.") || userCode.Contains("CbRow") || userCode.Contains("CbGrid") ||
                           userCode.Contains("CbXlsx") || userCode.Contains("CbSheet") || userCode.Contains("CbBoq") || userCode.Contains("CbWarning");
            return CompileSource(src, "UserScript_" + Guid.NewGuid().ToString("N"), needLib ? new[] { LibSource() } : null);
        }

        static string _libSource;
        static string LibSource()
        {
            if (_libSource != null) return _libSource;
            var asm = typeof(RevitConnector).Assembly;
            string resName = "CbLib.cs.txt";
            if (asm.GetManifestResourceStream(resName) == null)
            {
                // Visual Studio may add a namespace prefix (CadBridge.Lib.CbLib.cs.txt) - look for any name that ends the same way
                resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("CbLib.cs.txt", StringComparison.OrdinalIgnoreCase));
            }
            if (resName != null)
            {
                using (Stream st = asm.GetManifestResourceStream(resName))
                using (var rd = new StreamReader(st, Encoding.UTF8)) _libSource = rd.ReadToEnd();
                return _libSource;
            }
            // last chance: the file next to the .exe (Lib\CbLib.cs.txt)
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lib", "CbLib.cs.txt");
            if (File.Exists(file)) { _libSource = File.ReadAllText(file, Encoding.UTF8); return _libSource; }
            throw new Exception("Internal error: the shared library CbLib.cs.txt is missing from the program. Make sure Lib\\CbLib.cs.txt is in the project and Build Action = Embedded Resource, then Rebuild.");
        }

        byte[] CompileSource(string source, string assemblyName)
        {
            return CompileSource(source, assemblyName, null);
        }

        byte[] CompileSource(string source, string assemblyName, string[] extraSources)
        {
            string api = Path.Combine(RevitDir, "RevitAPI.dll");
            string apiUi = Path.Combine(RevitDir, "RevitAPIUI.dll");
            if (!File.Exists(api) || !File.Exists(apiUi))
                throw new Exception("RevitAPI.dll not found in " + RevitDir + ". Is Revit " + _year + " installed?");

            // the reference list is built once per Revit year (reading the big API files each time was slow)
            List<MetadataReference> refs;
            if (!RefCache.TryGetValue(_year, out refs))
            {
            refs = new List<MetadataReference>();
            if (_year >= 2025)
            {
                refs.AddRange(Net8References());
            }
            else
            {
                // Revit 2021-2024 = .NET Framework 4.8 = the framework this program runs on
                refs.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Drawing.Color).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Xml.XmlDocument).Assembly.Location));
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Windows.DependencyObject).Assembly.Location)); // WindowsBase
                refs.Add(MetadataReference.CreateFromFile(typeof(System.Windows.UIElement).Assembly.Location));        // PresentationCore
                refs.Add(MetadataReference.CreateFromFile(typeof(System.IO.Compression.ZipArchive).Assembly.Location)); // System.IO.Compression (Excel writer)
            }
            refs.Add(MetadataReference.CreateFromFile(api));
            refs.Add(MetadataReference.CreateFromFile(apiUi));
            RefCache[_year] = refs;
            }

            // scripts can use  #if REVIT2022_OR_NEWER ... #else ... #endif  (the API differs between Revit years)
            var symbols = new List<string> { "REVIT" + _year };
            for (int y = 2021; y <= 2026; y++)
                if (_year >= y) symbols.Add("REVIT" + y + "_OR_NEWER");
            var parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(symbols);
            var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, parseOptions) };
            if (extraSources != null)
                foreach (string extra in extraSources) trees.Add(CSharpSyntaxTree.ParseText(extra, parseOptions));
            var compilation = CSharpCompilation.Create(
                assemblyName,
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                             optimizationLevel: OptimizationLevel.Release));

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);
                if (!result.Success)
                {
                    var errors = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString());
                    throw new ScriptCompileException(string.Join(Environment.NewLine, errors));
                }
                return ms.ToArray();
            }
        }

        // ---- .NET 8 references (Revit 2025 / 2026) taken from the installed .NET 8 runtime ----

        static IEnumerable<MetadataReference> Net8References()
        {
            string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) root = @"C:\Program Files\dotnet";

            var list = new List<MetadataReference>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string family in new[] { "Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App" })
            {
                string dir = LatestVersionDir(Path.Combine(root, "shared", family), 8);
                if (dir == null)
                {
                    if (family == "Microsoft.NETCore.App")
                        throw new Exception(".NET 8 runtime not found under " + root +
                                            ". Revit 2025/2026 installs it; check C:\\Program Files\\dotnet\\shared.");
                    continue;
                }

                foreach (string f in Directory.GetFiles(dir, "*.dll"))
                {
                    string name = ManagedName(f);
                    if (name == null || !seen.Add(name)) continue;   // skip native DLLs and duplicates
                    list.Add(MetadataReference.CreateFromFile(f));
                }
            }
            return list;
        }

        static string LatestVersionDir(string parent, int major)
        {
            if (!Directory.Exists(parent)) return null;
            return Directory.GetDirectories(parent, major + ".*")
                .Select(d => new { Dir = d, Ver = ParseVersion(Path.GetFileName(d)) })
                .Where(x => x.Ver != null)
                .OrderByDescending(x => x.Ver)
                .Select(x => x.Dir)
                .FirstOrDefault();
        }

        static Version ParseVersion(string s)
        {
            Version v;
            return Version.TryParse(s, out v) ? v : null;   // preview folders like 8.0.0-rc.1 are skipped
        }

        static string ManagedName(string path)
        {
            try { return AssemblyName.GetAssemblyName(path).Name; }
            catch { return null; }
        }

        public void BuildAssembly(string outDir, string assemblyName, Action<string> log)
        {
            throw new NotSupportedException("Assemblies are a SolidWorks feature.");
        }
    }
}
