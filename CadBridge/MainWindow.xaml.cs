using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CadBridge
{
    public partial class MainWindow : Window
    {
        const string SW_GUIDE =
            "How to use:   1) Open SolidWorks and wait until it loads.   2) Press Connect.\n" +
            "3) Double-click a script in the library, change the values in the Parameters bar, press Run code. The part is saved in the project's Files folder.\n" +
            "4) When all parts are ready: Build assembly (side by side) or load Assembly_At_Origin from the library.";

        const string ACAD_GUIDE =
            "How to use:   1) Open AutoCAD (or let Connect start it).   2) Leave Version on Auto, or pick your year if you have several installed.\n" +
            "3) Press Connect, double-click a script, set the values in the Parameters bar, press Run code. Drawings are saved in the project's Files folder.\n" +
            "In scripts:  acad = the AutoCAD application,  TableReader.Read(path) = read a .csv/.xlsx file.";

        const string CIVIL_GUIDE =
            "How to use:   1) Open Civil 3D with a drawing.   2) Leave Version on Auto, or pick your year.   3) Press Connect, then run a script.\n" +
            "In scripts:  acad = AutoCAD (Civil 3D is AutoCAD underneath),  civil = the Civil 3D application object.\n" +
            "All AutoCAD scripts also work here (copy them from the AutoCAD tab).";

        const string RVT_GUIDE =
            "First time only:   close Revit, pick your Version, and press Install add-in. It installs the add-in for that Revit year.\n" +
            "Then:   1) Open Revit and open a project.   2) Press Connect.   3) Pick a script, set the values, press Run code.\n" +
            "In scripts:  uiapp,  doc,  OutDir,  Log(...).  Use a Transaction when you change the model.\n" +
            "Build a structure from A to Z - run the numbered scripts in order:  01_Grids, 02_Levels, 03_Footings, 04_TieBeams, 05_Columns, 06_Beams, 07_Slabs, 08_Walls, 09_Roof, 10_Doors_Windows, 11_Rooms, 12_Stairs, 13_Slab_Openings, 14_Railings, 15_Fence_Walls  (open 00_Build_Order for the short list).\n" +
            "Quantities (Excel, the model is not changed):  20_Takeoff_Structure and 21_Takeoff_Architecture. Rates go in the yellow cells of the BOQ sheet.\n" +
            "Boxes with a Folder... button (footings, columns, beams) read the .rfa families from the folder you choose; the choice is remembered for each object.";

        static readonly string[] COMMON_WORDS =
        {
            "double", "int", "string", "bool", "var", "new", "foreach", "for", "if", "else", "while", "return",
            "throw", "try", "catch", "using", "true", "false", "null", "Math", "Path", "File", "Directory",
            "Combine", "Exception", "Log", "OutDir", "List", "Length", "Count", "ToString", "Contains",
            "StartsWith", "EndsWith", "Where", "OrderBy", "ToList", "ToArray", "Select", "Cast", "FirstOrDefault"
        };

        static readonly string[] SW_WORDS =
        {
            "swApp", "ModelDoc2", "SketchManager", "FeatureManager", "InsertSketch", "CreateLine", "CreateCircle",
            "CreateCenterLine", "CreateCornerRectangle", "FeatureExtrusion2", "FeatureRevolve2", "FeatureCut4",
            "SaveAs3", "NewDocument", "OpenDoc6", "CloseDoc", "SelectByID2", "ClearSelection2", "ShowNamedView2",
            "ViewZoomtofit2", "AssemblyDoc", "AddComponent5", "AddMate5", "Component2", "FirstFeature",
            "GetNextFeature", "GetTypeName2", "Select2", "ActiveDoc", "swEndConditions_e", "swSaveAsOptions_e"
        };

        static readonly string[] ACAD_WORDS =
        {
            "acad", "civil", "ModelSpace", "AddLine", "AddCircle", "AddArc", "AddText", "AddPoint",
            "AddLightWeightPolyline", "ZoomExtents", "SaveAs", "Documents", "ActiveDocument", "Open", "Close",
            "Plot", "PlotToFile", "SetVariable", "TableReader", "Read", "Num"
        };

        static readonly string[] REVIT_WORDS =
        {
            "uiapp", "doc", "FilteredElementCollector", "OfClass", "OfCategory", "Transaction", "Start", "Commit",
            "Wall", "Level", "XYZ", "Line", "CreateBound", "Create", "ElementId", "Element", "Floor",
            "GetElementCount", "Elevation", "Name", "WhereElementIsNotElementType", "ToElements", "Grid", "FamilyInstance",
            "FamilySymbol", "StructuralType", "NewFamilyInstance", "LoadFamily", "CurveLoop", "CurveArray", "RoofType", "FloorType", "WallType",
            "StairsEditScope", "StairsRun", "Railing", "NewOpening", "Cb", "CbGrid", "CbRow", "CbXlsx", "CbSheet", "CbBoq", "Levels", "TypeName", "LevelOf", "Solids", "FormworkFt2", "Save", "Table", "Formula"
        };

        readonly AutoCadConnector _acad = new AutoCadConnector(false);
        readonly AutoCadConnector _civil = new AutoCadConnector(true);
        readonly RevitConnector _revit = new RevitConnector();
        bool _loadingProjects;
        bool _themeLoading;

        public MainWindow()
        {
            InitializeComponent();

            // Light / Night mode (remembered between runs)
            _themeLoading = true;
            bool dark = ThemeManager.LoadSaved();
            ThemeToggle.IsChecked = dark;
            ThemeManager.Apply(dark, false);
            _themeLoading = false;
            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            ThemeManager.Changed += d => ThemeManager.ApplyTitleBar(this);

            ProjectManager.Init();
            RefreshProjects();

            Func<string> outDir = () => ProjectManager.FilesDir;
            Func<string> asmName = () => AsmNameBox.Text.Trim();
            string[] acadVersions = { "Auto", "2021", "2022", "2023", "2024", "2025", "2026" };
            string[] revitVersions = { "2021", "2022", "2023", "2024", "2025", "2026" };

            SwView.Init("SolidWorks", new SolidWorksConnector(), SW_GUIDE, "Box.csx", true,
                outDir, asmName, Log, null, null, null, COMMON_WORDS.Concat(SW_WORDS).ToArray());

            AcadView.Init("AutoCAD", _acad, ACAD_GUIDE, "Road_Cross_Section.csx", false,
                outDir, asmName, Log, acadVersions, "Auto", v => _acad.SelectedVersion = v,
                COMMON_WORDS.Concat(ACAD_WORDS).ToArray());

            CivilView.Init("Civil3D", _civil, CIVIL_GUIDE, "List_Surfaces_And_Alignments.csx", false,
                outDir, asmName, Log, acadVersions, "Auto", v => _civil.SelectedVersion = v,
                COMMON_WORDS.Concat(ACAD_WORDS).ToArray());

            RvView.Init("Revit", _revit, RVT_GUIDE, "00_Build_Order.csx", false,
                outDir, asmName, Log, revitVersions, RevitConnector.DetectNewestInstalledYear().ToString(),
                v => _revit.Year = int.Parse(v), COMMON_WORDS.Concat(REVIT_WORDS).ToArray());

            SwView.ConnectionChanged += () => UpdateChip(SwDot, SwStatus, "SolidWorks", SwView.IsConnected);
            AcadView.ConnectionChanged += () => UpdateChip(AcDot, AcStatus,
                _acad.SelectedVersion == "Auto" ? "AutoCAD" : "AutoCAD " + _acad.SelectedVersion, AcadView.IsConnected);
            CivilView.ConnectionChanged += () => UpdateChip(CvDot, CvStatus,
                _civil.SelectedVersion == "Auto" ? "Civil 3D" : "Civil 3D " + _civil.SelectedVersion, CivilView.IsConnected);
            RvView.ConnectionChanged += () => UpdateChip(RvDot, RvStatus, "Revit " + _revit.Year, RvView.IsConnected);

            Log("Welcome. Project: " + ProjectManager.Current + ". Choose a tab, press Connect, then run a script.", LogLevel.Info);
        }

        // ------------------------------------------------------------------ projects

        void RefreshProjects()
        {
            _loadingProjects = true;
            ProjectBox.Items.Clear();
            foreach (string p in ProjectManager.List()) ProjectBox.Items.Add(p);
            ProjectBox.SelectedItem = ProjectManager.Current;
            _loadingProjects = false;
        }

        void Project_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingProjects || ProjectBox.SelectedItem == null) return;
            ProjectManager.Select((string)ProjectBox.SelectedItem);
            Log("Project: " + ProjectManager.Current, LogLevel.Info);
        }

        void NewProject_Click(object sender, RoutedEventArgs e)
        {
            string name = InputDialog.Show(this, "New project", "Project name:", "");
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                string created = ProjectManager.Create(name);
                ProjectManager.Select(created);
                RefreshProjects();
                Log("Project created: " + created, LogLevel.Ok);
            }
            catch (Exception ex) { Log("Could not create the project: " + ex.Message, LogLevel.Error); }
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start("explorer.exe", ProjectManager.FilesDir); }
            catch (Exception ex) { Log("Could not open the folder: " + ex.Message, LogLevel.Error); }
        }

        // ------------------------------------------------------------------ chips + log

        void Theme_Toggled(object sender, RoutedEventArgs e)
        {
            if (_themeLoading) return;
            ThemeManager.Apply(ThemeToggle.IsChecked == true);
        }

        void UpdateChip(System.Windows.Shapes.Ellipse dot, TextBlock text, string name, bool on)
        {
            ThemeManager.StyleDot(dot, on, Color.FromRgb(0x6B, 0x72, 0x80));
            dot.ToolTip = on ? "Connected" : "Not connected";
            text.Text = name;
        }

        void Log(string message, LogLevel level)
        {
            // theme-aware: the runs follow the active skin, so old lines stay readable when you switch to Night mode
            string colorKey = level == LogLevel.Error ? "ErrorBrush"
                            : level == LogLevel.Ok ? "OkBrush"
                            : "TextBrush";

            var time = new Run(DateTime.Now.ToString("HH:mm:ss") + "   ");
            time.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
            var msg = new Run(message);
            msg.SetResourceReference(TextElement.ForegroundProperty, colorKey);

            var p = new Paragraph();
            p.Inlines.Add(time);
            p.Inlines.Add(msg);
            LogBox.Document.Blocks.Add(p);
            LogBox.ScrollToEnd();
        }

        void Log(string message) { Log(message, LogLevel.Info); }

        void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Document.Blocks.Clear();
        }
    }
}
