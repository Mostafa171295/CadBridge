using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Win32;

namespace CadBridge
{
    internal class ScriptItem
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public bool IsProject { get; set; }
        public string Display { get { return IsProject ? Name + "   (project)" : Name; } }
    }

    // one entry of the auto-complete list
    internal class WordCompletion : ICompletionData
    {
        public WordCompletion(string text) { Text = text; }
        public System.Windows.Media.ImageSource Image { get { return null; } }
        public string Text { get; private set; }
        public object Content { get { return Text; } }
        public object Description { get { return null; } }
        public double Priority { get { return 0; } }
        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            textArea.Document.Replace(completionSegment, Text);
        }
    }

    public partial class CadTabView : UserControl
    {
        ICadConnector _cad;
        IAddinInstaller _installer;
        string _tab = "Tab";
        string _currentName;
        Func<string> _outDir, _asmName;
        Action<string, LogLevel> _log;
        Action<string> _onVersion;

        readonly Dictionary<string, string> _paramValues = new Dictionary<string, string>();
        readonly DispatcherTimer _paramTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        // lists read from the CAD program for the drop-down boxes  (@levels, @materials, ...)
        readonly Dictionary<string, List<string>> _choices = new Dictionary<string, List<string>>();
        bool _loadingChoices;

        CompletionWindow _completion;
        string[] _words = new string[0];

        public event Action ConnectionChanged;
        public bool IsConnected { get { return _cad != null && _cad.IsConnected; } }

        public CadTabView()
        {
            InitializeComponent();
            // the guide / toolbar / parameters part never takes more than half of the height (it scrolls), so the editor is always visible
            SizeChanged += (s, e) => UpdateTopMax();

            _paramTimer.Tick += (s, e) => { _paramTimer.Stop(); RebuildParams(); };
            CodeBox.TextChanged += (s, e) => { _paramTimer.Stop(); _paramTimer.Start(); };
            CodeBox.TextArea.TextEntered += TextArea_TextEntered;

            ApplyEditorTheme();
            ThemeManager.Changed += d => ApplyEditorTheme();

            ApplyView();
            ViewPrefs.Changed += ApplyView;
        }

        // ------------------------------------------------------------------ show / hide Guide and Code
        bool _viewApplying;

        void View_Toggled(object sender, RoutedEventArgs e)
        {
            if (_viewApplying) return;
            ViewPrefs.SetShow(GuideBtn.IsChecked == true, CodeBtn.IsChecked == true);   // same for every tab
        }

        void ApplyView()
        {
            _viewApplying = true;
            bool guide = ViewPrefs.ShowGuide, code = ViewPrefs.ShowCode;
            GuideBtn.IsChecked = guide;
            CodeBtn.IsChecked = code;
            _viewApplying = false;

            GuideBorder.Visibility = guide ? Visibility.Visible : Visibility.Collapsed;
            Visibility v = code ? Visibility.Visible : Visibility.Collapsed;
            EditorHalo.Visibility = v;
            EditorBox.Visibility = v;

            // editor hidden: the top part (toolbar + parameters) takes all the height
            TopRow.Height = code ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            EditorRow.Height = code ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            EditorRow.MinHeight = code ? 150 : 0;
            UpdateTopMax();
        }

        void UpdateTopMax()
        {
            // with the editor visible the top part never takes more than half of the height (it scrolls)
            TopRow.MaxHeight = ViewPrefs.ShowCode ? Math.Max(150, ActualHeight * 0.5) : double.PositiveInfinity;
        }

        // code colours, selection and current-line highlight follow Light / Night mode
        void ApplyEditorTheme()
        {
            bool dark = ThemeManager.IsDark;
            CodeBox.SyntaxHighlighting = ThemeManager.GetHighlighting();

            Color accent = dark ? Color.FromRgb(0x3B, 0x82, 0xF6) : Color.FromRgb(0x25, 0x63, 0xEB);
            CodeBox.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x60 : (byte)0x40, accent.R, accent.G, accent.B));
            CodeBox.TextArea.SelectionForeground = null;
            CodeBox.TextArea.SelectionBorder = null;

            CodeBox.Options.HighlightCurrentLine = true;
            CodeBox.TextArea.TextView.CurrentLineBackground =
                new SolidColorBrush(Color.FromArgb(dark ? (byte)0x22 : (byte)0x14, accent.R, accent.G, accent.B));
            CodeBox.TextArea.TextView.CurrentLineBorder =
                new Pen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x55 : (byte)0x40, accent.R, accent.G, accent.B)), 1);
        }

        // tabName = "SolidWorks", "AutoCAD", "Civil3D" or "Revit"  (names the script folders)
        public void Init(string tabName, ICadConnector cad, string guide, string defaultScript, bool showAssembly,
                         Func<string> outDir, Func<string> asmName, Action<string, LogLevel> log,
                         IList<string> versions = null, string selectedVersion = null,
                         Action<string> onVersion = null, IList<string> words = null)
        {
            _tab = tabName; _cad = cad; _outDir = outDir; _asmName = asmName; _log = log;
            GuideText.Text = guide;
            AsmBtn.Visibility = showAssembly ? Visibility.Visible : Visibility.Collapsed;
            if (words != null) _words = words.ToArray();

            // connectors that need an add-in inside the CAD program (Revit) get an "Install add-in" button
            _installer = cad as IAddinInstaller;
            InstallBtn.Visibility = _installer != null ? Visibility.Visible : Visibility.Collapsed;

            // optional version picker (AutoCAD / Civil 3D / Revit)
            if (versions != null && versions.Count > 0)
            {
                VersionPanel.Visibility = Visibility.Visible;
                foreach (string v in versions) VersionBox.Items.Add(v);
                _onVersion = onVersion;
                VersionBox.SelectedItem = selectedVersion ?? versions[0];   // fires Version_Changed
            }

            // copy the bundled example scripts into  Documents\CadBridge\Scripts\<tab>  (only the ones that are missing)
            try
            {
                string globalDir = ProjectManager.GlobalScriptsDir(_tab);
                string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", _tab);
                if (Directory.Exists(bundled))
                    foreach (string f in Directory.GetFiles(bundled, "*.csx"))
                    {
                        string dest = Path.Combine(globalDir, Path.GetFileName(f));
                        if (!File.Exists(dest)) File.Copy(f, dest);
                    }

                string first = defaultScript == null ? null : Path.Combine(globalDir, defaultScript);
                if (first != null && File.Exists(first))
                    LoadScript(first, Path.GetFileNameWithoutExtension(first), false);
            }
            catch (Exception ex) { _log("Could not load the example scripts: " + ex.Message, LogLevel.Error); }

            RefreshList();
            ProjectManager.Changed += RefreshList;
        }

        // ------------------------------------------------------------------ script library

        void RefreshList()
        {
            var items = new List<ScriptItem>();
            try
            {
                foreach (string f in Directory.GetFiles(ProjectManager.ScriptsDir(_tab), "*.csx").OrderBy(x => x))
                    items.Add(new ScriptItem { Name = Path.GetFileNameWithoutExtension(f), FilePath = f, IsProject = true });
                foreach (string f in Directory.GetFiles(ProjectManager.GlobalScriptsDir(_tab), "*.csx").OrderBy(x => x))
                    items.Add(new ScriptItem { Name = Path.GetFileNameWithoutExtension(f), FilePath = f, IsProject = false });
            }
            catch (Exception ex) { _log("Could not read the script folders: " + ex.Message, LogLevel.Error); }
            ScriptList.ItemsSource = items;
        }

        void LoadScript(string path, string name, bool announce)
        {
            try
            {
                _paramValues.Clear();                       // a new script starts with its own default values
                CodeBox.Text = File.ReadAllText(path);
                _currentName = name;
                if (announce) _log("Loaded script: " + name, LogLevel.Info);
            }
            catch (Exception ex) { _log("Could not load the script: " + ex.Message, LogLevel.Error); }
        }

        void ScriptList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = ScriptList.SelectedItem as ScriptItem;
            if (item != null) LoadScript(item.FilePath, item.Name, true);
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = InputDialog.Show(Window.GetWindow(this), "Save script",
                "Script name (it is saved inside the current project):", _currentName ?? "MyScript");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = ProjectManager.Sanitize(name);
            if (name.Length == 0) return;

            try
            {
                string path = Path.Combine(ProjectManager.ScriptsDir(_tab), name + ".csx");
                if (File.Exists(path) &&
                    MessageBox.Show("A script named '" + name + "' already exists in this project. Replace it?",
                                    "Save script", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                File.WriteAllText(path, CodeBox.Text);
                _currentName = name;
                RefreshList();
                _log("Saved script: " + path, LogLevel.Ok);
            }
            catch (Exception ex) { _log("Could not save the script: " + ex.Message, LogLevel.Error); }
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "C# script (*.csx)|*.csx|All files (*.*)|*.*",
                InitialDirectory = ProjectManager.ScriptsDir(_tab)
            };
            if (dlg.ShowDialog() == true)
                LoadScript(dlg.FileName, Path.GetFileNameWithoutExtension(dlg.FileName), true);
        }

        void Delete_Click(object sender, RoutedEventArgs e)
        {
            var item = ScriptList.SelectedItem as ScriptItem;
            if (item == null) { _log("Select a script in the list first.", LogLevel.Error); return; }
            if (!item.IsProject)
            {
                _log("Only your own project scripts can be deleted here. The built-in examples stay.", LogLevel.Error);
                return;
            }
            if (MessageBox.Show("Delete the script '" + item.Name + "' from this project?", "Delete script",
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                File.Delete(item.FilePath);
                RefreshList();
                _log("Deleted script: " + item.Name, LogLevel.Info);
            }
            catch (Exception ex) { _log("Could not delete: " + ex.Message, LogLevel.Error); }
        }

        // ------------------------------------------------------------------ parameters  {{Name=default}}

        void RebuildParams()
        {
            List<KeyValuePair<string, string>> found = ParamParser.Find(CodeBox.Text);
            ParamPanel.Children.Clear();
            var missing = new List<string>();

            foreach (KeyValuePair<string, string> kv in found)
            {
                string name = kv.Key;
                string def = kv.Value;

                string source; List<string> options;
                ParamParser.Describe(def, out source, out options);

                // the list of choices: fixed (a|b|c) or read from the CAD program (@levels)
                List<string> items = options;
                if (source != null)
                {
                    List<string> got;
                    if (_choices.TryGetValue(source, out got)) items = got;
                    else if (!missing.Contains(source)) missing.Add(source);
                }

                string current;
                bool has = _paramValues.TryGetValue(name, out current);
                if (!has)
                {
                    current = (items != null && items.Count > 0) ? items[0] : ParamParser.Fallback(def);
                    if (options == null && source == null && (name.EndsWith("Folder") || name.EndsWith("File")))
                    {
                        string last = ViewPrefs.Get("param:" + name);      // the folder / file you chose last time
                        if (!string.IsNullOrEmpty(last)) current = last;
                    }
                }
                if (source == null || (items != null && items.Count > 0)) _paramValues[name] = current;

                var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
                var label = new TextBlock { Text = name, FontSize = 11 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
                panel.Children.Add(label);

                if (options != null || source != null)
                {
                    var combo = new ComboBox
                    {
                        IsEditable = (options == null),      // fixed lists: pick only.  Lists from Revit: pick or type.
                        Width = (options != null) ? 130 : 320,
                        Height = 28,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    if (items != null) foreach (string it in items) combo.Items.Add(it);

                    if (options != null)
                    {
                        combo.SelectedItem = options.Contains(current) ? current : options[0];
                        combo.SelectionChanged += (s, e) =>
                        {
                            if (combo.SelectedItem != null) _paramValues[name] = (string)combo.SelectedItem;
                        };
                    }
                    else
                    {
                        combo.Text = current;
                        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                            new TextChangedEventHandler((s, e) => { _paramValues[name] = combo.Text; }));
                    }

                    var folders = _cad as IFamilyFolderProvider;
                    if (source != null && folders != null && folders.IsFileSource(source))
                    {
                        // family-file list: the combo + "Folder..." button (+ "Default" when a folder was chosen)
                        string src = source;
                        string custom = folders.GetCustomFolder(src);
                        combo.Width = 250;

                        var row = new StackPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(combo);

                        var pick = new Button
                        {
                            Content = "Folder...",
                            Height = 28,
                            Padding = new Thickness(10, 0, 10, 0),
                            Margin = new Thickness(6, 0, 0, 0),
                            Style = (Style)FindResource("SecondaryButton"),
                            ToolTip = "Choose the folder that contains your .rfa family files for this object"
                        };
                        pick.Height = 28;
                        pick.Click += (s2, e2) => PickFamilyFolder(src);
                        row.Children.Add(pick);

                        if (custom.Length > 0)
                        {
                            var reset = new Button
                            {
                                Content = "Default",
                                Height = 28,
                                Padding = new Thickness(10, 0, 10, 0),
                                Margin = new Thickness(4, 0, 0, 0),
                                Style = (Style)FindResource("SecondaryButton"),
                                ToolTip = "Go back to Revit's library folders"
                            };
                            reset.Height = 28;
                            reset.Click += (s2, e2) => ResetFamilyFolder(src);
                            row.Children.Add(reset);
                        }
                        panel.Children.Add(row);

                        bool missingFolder = custom.Length > 0 && !Directory.Exists(custom);
                        var folderLbl = new TextBlock
                        {
                            Text = custom.Length == 0 ? "Folder: Revit library (default)"
                                 : (missingFolder ? "Folder not found: " + custom : "Folder: " + custom),
                            FontSize = 10,
                            MaxWidth = 380,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            ToolTip = custom.Length > 0 ? (object)custom : null
                        };
                        folderLbl.SetResourceReference(TextBlock.ForegroundProperty, missingFolder ? "ErrorBrush" : "MutedBrush");
                        panel.Children.Add(folderLbl);
                    }
                    else panel.Children.Add(combo);
                }
                else
                {
                    bool isFolder = name.EndsWith("Folder"), isFile = name.EndsWith("File");
                    bool wide = def.Length > 14 || isFile || isFolder;
                    var box = new TextBox
                    {
                        Text = current,
                        Width = wide ? 320 : 130,
                        Height = 28,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Padding = new Thickness(6, 0, 6, 0)
                    };
                    box.TextChanged += (s, e) => _paramValues[name] = box.Text;

                    if (isFolder || isFile)
                    {
                        // text box + Browse... button (choose where to save / which file to read)
                        box.LostFocus += (s, e) => ViewPrefs.Set("param:" + name, box.Text);
                        var browse = new Button
                        {
                            Content = "Browse...",
                            Height = 28,
                            Padding = new Thickness(12, 0, 12, 0),
                            Margin = new Thickness(6, 0, 0, 0),
                            Style = (Style)FindResource("SecondaryButton"),
                            ToolTip = isFolder ? "Choose a folder" : "Choose a file"
                        };
                        browse.Click += (s, e) => BrowseInto(box, name, isFolder);
                        var row = new StackPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(box);
                        row.Children.Add(browse);
                        panel.Children.Add(row);
                    }
                    else panel.Children.Add(box);
                }
                ParamPanel.Children.Add(panel);
            }

            ParamBorder.Visibility = found.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (missing.Count > 0) LoadChoices(missing);
        }

        // Reads the lists used by  {{Name=@source}}  and fills the drop-down boxes.
        // Lists from Revit (levels, types...) need a connection; lists of family files only read this PC's disk.
        async void LoadChoices(List<string> sources)
        {
            var provider = _cad as IChoiceProvider;
            if (provider == null || _loadingChoices) return;
            var folders = _cad as IFamilyFolderProvider;

            var wanted = sources.Where(x => _cad.IsConnected || (folders != null && folders.IsFileSource(x))).ToList();
            if (wanted.Count == 0) return;

            _loadingChoices = true;
            SetBusy(true);
            try
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                Dictionary<string, List<string>> got = await provider.GetChoicesAsync(wanted);
                foreach (KeyValuePair<string, List<string>> kv in got)
                {
                    _choices[kv.Key] = kv.Value;
                    if (folders != null && folders.IsFileSource(kv.Key))
                        _log(kv.Value.Count > 1
                                ? (kv.Value.Count - 1) + " family files found for " + kv.Key
                                : "No .rfa files found for " + kv.Key + " - use the Folder... button to choose your family folder.",
                             LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                _log("Could not read the lists from " + _cad.Name + ": " + ex.Message, LogLevel.Error);
            }
            finally
            {
                foreach (string x in wanted)                        // never ask twice for the same list
                    if (!_choices.ContainsKey(x)) _choices[x] = new List<string>();
                _loadingChoices = false;
                SetBusy(false);
            }
            RebuildParams();
        }

        // Browse... button next to a parameter whose name ends with Folder or File (SaveFolder, Folder, TableFile...)
        void BrowseInto(TextBox box, string name, bool isFolder)
        {
            string chosen = null;
            string cur = box.Text.Trim();

            if (isFolder)
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Choose the folder for  " + name;
                    dlg.SelectedPath = Directory.Exists(cur) ? cur : ProjectManager.FilesDir;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) chosen = dlg.SelectedPath;
                }
            }
            else
            {
                var dlg = new OpenFileDialog { Filter = "Excel / CSV (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt|All files (*.*)|*.*" };
                try
                {
                    string dir = cur.Length > 0 ? Path.GetDirectoryName(cur) : null;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                }
                catch { }
                if (dlg.ShowDialog() == true) chosen = dlg.FileName;
            }

            if (chosen == null) return;
            box.Text = chosen;                                   // TextChanged keeps the parameter value in sync
            ViewPrefs.Set("param:" + name, chosen);              // and it is remembered for next time
        }

        // ------------------------------------------------------------------ family folder button

        void PickFamilyFolder(string src)
        {
            var folders = _cad as IFamilyFolderProvider;
            if (folders == null) return;

            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Choose the folder that contains the family files (.rfa) for this object";
                string cur = folders.GetCustomFolder(src);
                string start = Directory.Exists(cur) ? cur : Path.Combine(ProjectManager.DocsRoot, "Families");
                if (Directory.Exists(start)) dlg.SelectedPath = start;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                folders.SetCustomFolder(src, dlg.SelectedPath);
                _log("Family folder for " + src + ": " + dlg.SelectedPath, LogLevel.Ok);
            }
            ReloadFileList(src);
        }

        void ResetFamilyFolder(string src)
        {
            var folders = _cad as IFamilyFolderProvider;
            if (folders == null) return;
            folders.SetCustomFolder(src, null);
            _log("Family folder for " + src + ": back to Revit's library folders.", LogLevel.Info);
            ReloadFileList(src);
        }

        // read the list again and put the boxes that use it back to "(none)"
        void ReloadFileList(string src)
        {
            _choices.Remove(src);
            foreach (KeyValuePair<string, string> kv in ParamParser.Find(CodeBox.Text))
            {
                string s2; List<string> o2;
                ParamParser.Describe(kv.Value, out s2, out o2);
                if (s2 == src) _paramValues.Remove(kv.Key);
            }
            RebuildParams();
        }

        // ------------------------------------------------------------------ auto-complete

        void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (_completion != null || _words.Length == 0) return;
            if (string.IsNullOrEmpty(e.Text) || !char.IsLetter(e.Text[0])) return;

            int off = CodeBox.CaretOffset;
            if (off >= 2)
            {
                char prev = CodeBox.Document.GetCharAt(off - 2);
                if (char.IsLetterOrDigit(prev) || prev == '_') return;      // only when a new word starts
            }
            if (!_words.Any(w => w.StartsWith(e.Text, StringComparison.OrdinalIgnoreCase))) return;

            _completion = new CompletionWindow(CodeBox.TextArea);
            _completion.StartOffset = off - 1;
            foreach (string w in _words) _completion.CompletionList.CompletionData.Add(new WordCompletion(w));
            _completion.CompletionList.SelectItem(e.Text);
            try
            {
                Brush bg = ThemeManager.Res("SurfaceBrush"), fg = ThemeManager.Res("TextBrush"), line = ThemeManager.Res("AccentBrush");
                _completion.Background = bg;
                _completion.Foreground = fg;
                _completion.BorderBrush = line;
                _completion.BorderThickness = new Thickness(1);
                _completion.CompletionList.Background = bg;
                _completion.CompletionList.Foreground = fg;
                if (_completion.CompletionList.ListBox != null)
                {
                    _completion.CompletionList.ListBox.Background = bg;
                    _completion.CompletionList.ListBox.Foreground = fg;
                    _completion.CompletionList.ListBox.BorderThickness = new Thickness(0);
                }
            }
            catch { }
            _completion.Closed += (s, a) => { _completion = null; };
            _completion.Show();
        }

        // ------------------------------------------------------------------ connect / run / install / assembly

        public void ResetStatus()
        {
            ThemeManager.StyleDot(Dot, false, Color.FromRgb(0x9C, 0xA3, 0xAF));
            StatusText.Text = "Not connected";
            if (ConnectionChanged != null) ConnectionChanged();
        }

        void Version_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_onVersion == null || VersionBox.SelectedItem == null) return;
            _onVersion((string)VersionBox.SelectedItem);
            ResetStatus();   // a different version means a new connection is needed
        }

        void SetBusy(bool busy)
        {
            RunBtn.IsEnabled = !busy; ConnectBtn.IsEnabled = !busy; AsmBtn.IsEnabled = !busy;
            InstallBtn.IsEnabled = !busy; VersionBox.IsEnabled = !busy;
        }

        void Connect_Click(object sender, RoutedEventArgs e)
        {
            string msg;
            bool ok = _cad.Connect(out msg);
            ThemeManager.StyleDot(Dot, ok, Color.FromRgb(0x9C, 0xA3, 0xAF));
            StatusText.Text = ok ? "Connected" : "Not connected";
            _log(msg, ok ? LogLevel.Ok : LogLevel.Error);
            if (ConnectionChanged != null) ConnectionChanged();

            if (ok)
            {
                _choices.Clear();      // read the lists again from the CAD program (Connect = refresh)
                RebuildParams();
            }
        }

        async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (!_cad.IsConnected) { _log("Press Connect first.", LogLevel.Error); return; }
            SetBusy(true);
            try
            {
                string code = ParamParser.Apply(CodeBox.Text, _paramValues);   // fill in the {{Name=default}} values
                Directory.CreateDirectory(_outDir());
                _log("Running on " + _cad.Name + "... the window may freeze for a few seconds.", LogLevel.Info);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await _cad.RunScriptAsync(code, _outDir(), m => _log(m, LogLevel.Info));
                _log("Done.", LogLevel.Ok);
            }
            catch (CompilationErrorException ex)
            {
                _log("Compile error:" + Environment.NewLine + string.Join(Environment.NewLine, ex.Diagnostics), LogLevel.Error);
            }
            catch (ScriptCompileException ex)
            {
                _log("Compile error:" + Environment.NewLine + ex.Message, LogLevel.Error);
            }
            catch (Exception ex)
            {
                _log("Runtime error: " + ex.Message, LogLevel.Error);
            }
            finally { SetBusy(false); }
        }

        async void Install_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            try
            {
                _log("Installing the add-in... this takes a few seconds (close the CAD program first).", LogLevel.Info);
                await Dispatcher.Yield(DispatcherPriority.Background);
                string msg = _installer.InstallAddin();
                _log(msg, LogLevel.Ok);
            }
            catch (ScriptCompileException ex)
            {
                _log("Install failed (compile error):" + Environment.NewLine + ex.Message, LogLevel.Error);
            }
            catch (Exception ex)
            {
                _log("Install failed: " + ex.Message, LogLevel.Error);
            }
            finally { SetBusy(false); }
        }

        void Asm_Click(object sender, RoutedEventArgs e)
        {
            if (!_cad.IsConnected) { _log("Press Connect first.", LogLevel.Error); return; }
            try { _cad.BuildAssembly(_outDir(), _asmName(), m => _log(m, LogLevel.Info)); }
            catch (Exception ex) { _log("Assembly error: " + ex.Message, LogLevel.Error); }
        }

        void View_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5 && RunBtn.IsEnabled)
            {
                e.Handled = true;
                Run_Click(sender, e);
            }
        }
    }
}
