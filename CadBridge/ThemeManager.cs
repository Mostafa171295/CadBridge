using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace CadBridge
{
    // Light / Night mode.  The two palettes live in Skins\Light.xaml and Skins\Dark.xaml (same keys).
    // Switching = replacing that dictionary; everything bound with DynamicResource updates by itself.
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }
        public static event Action<bool> Changed;

        static string SaveFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "CadBridge", "theme.txt");
            }
        }

        public static bool LoadSaved()
        {
            try { return File.Exists(SaveFile) && File.ReadAllText(SaveFile).Trim() == "dark"; }
            catch { return false; }
        }

        public static void Apply(bool dark, bool save = true)
        {
            var merged = Application.Current.Resources.MergedDictionaries;

            for (int i = merged.Count - 1; i >= 0; i--)
            {
                Uri src = merged[i].Source;
                if (src != null && src.OriginalString.IndexOf("Skins/", StringComparison.OrdinalIgnoreCase) >= 0)
                    merged.RemoveAt(i);
            }
            merged.Insert(0, new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Skins/" + (dark ? "Dark" : "Light") + ".xaml", UriKind.Absolute)
            });

            IsDark = dark;

            if (save)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SaveFile));
                    File.WriteAllText(SaveFile, dark ? "dark" : "light");
                }
                catch { }
            }

            if (Changed != null) Changed(dark);
        }

        // current value of a themed brush (for code that cannot use DynamicResource)
        public static Brush Res(string key)
        {
            return (Brush)Application.Current.Resources[key];
        }

        // ------------------------------------------------------------------ small helpers

        // status dot: green + glow when connected, grey when not
        public static void StyleDot(System.Windows.Shapes.Ellipse dot, bool on, Color offColor)
        {
            Color green = Color.FromRgb(0x22, 0xC5, 0x5E);
            dot.Fill = new SolidColorBrush(on ? green : offColor);
            dot.Effect = on
                ? new DropShadowEffect { Color = green, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.95 }
                : null;
        }

        // makes the Windows title bar dark / light too (Windows 10 1809+ and Windows 11; ignored elsewhere)
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyTitleBar(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int v = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(h, 20, ref v, sizeof(int)) != 0)
                    DwmSetWindowAttribute(h, 19, ref v, sizeof(int));
            }
            catch { }
        }

        // ------------------------------------------------------------------ code colours (AvalonEdit)

        static IHighlightingDefinition _light, _dark;

        public static IHighlightingDefinition GetHighlighting()
        {
            if (IsDark) { if (_dark == null) _dark = Build(true); return _dark; }
            if (_light == null) _light = Build(false);
            return _light;
        }

        const string XSHD = @"<?xml version=""1.0""?>
<SyntaxDefinition name=""CadBridgeCS"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""Comment"" foreground=""$COMMENT$"" />
  <Color name=""String""  foreground=""$STRING$"" />
  <Color name=""Number""  foreground=""$NUMBER$"" />
  <Color name=""Keyword"" foreground=""$KEYWORD$"" fontWeight=""bold"" />
  <Color name=""Type""    foreground=""$TYPE$"" />
  <RuleSet>
    <Span color=""Comment"" begin=""//"" />
    <Span color=""Comment"" multiline=""true"" begin=""/\*"" end=""\*/"" />
    <Span color=""String"" multiline=""true"">
      <Begin>@""</Begin>
      <End>""</End>
      <RuleSet><Span begin='""""' end="""" /></RuleSet>
    </Span>
    <Span color=""String"">
      <Begin>""</Begin>
      <End>""</End>
      <RuleSet><Span begin=""\\"" end=""."" /></RuleSet>
    </Span>
    <Span color=""String"">
      <Begin>'</Begin>
      <End>'</End>
      <RuleSet><Span begin=""\\"" end=""."" /></RuleSet>
    </Span>
    <Keywords color=""Keyword"">
      <Word>abstract</Word><Word>as</Word><Word>async</Word><Word>await</Word><Word>base</Word><Word>break</Word>
      <Word>case</Word><Word>catch</Word><Word>checked</Word><Word>class</Word><Word>const</Word><Word>continue</Word>
      <Word>default</Word><Word>delegate</Word><Word>do</Word><Word>else</Word><Word>enum</Word><Word>event</Word>
      <Word>explicit</Word><Word>extern</Word><Word>false</Word><Word>finally</Word><Word>fixed</Word><Word>for</Word>
      <Word>foreach</Word><Word>goto</Word><Word>if</Word><Word>implicit</Word><Word>in</Word><Word>interface</Word>
      <Word>internal</Word><Word>is</Word><Word>lock</Word><Word>namespace</Word><Word>new</Word><Word>null</Word>
      <Word>operator</Word><Word>out</Word><Word>override</Word><Word>params</Word><Word>private</Word>
      <Word>protected</Word><Word>public</Word><Word>readonly</Word><Word>ref</Word><Word>return</Word><Word>sealed</Word>
      <Word>sizeof</Word><Word>static</Word><Word>struct</Word><Word>switch</Word><Word>this</Word><Word>throw</Word>
      <Word>true</Word><Word>try</Word><Word>typeof</Word><Word>unchecked</Word><Word>unsafe</Word><Word>using</Word>
      <Word>var</Word><Word>virtual</Word><Word>volatile</Word><Word>while</Word><Word>yield</Word>
      <Word>get</Word><Word>set</Word>
    </Keywords>
    <Keywords color=""Type"">
      <Word>bool</Word><Word>byte</Word><Word>char</Word><Word>decimal</Word><Word>double</Word><Word>dynamic</Word>
      <Word>float</Word><Word>int</Word><Word>long</Word><Word>object</Word><Word>sbyte</Word><Word>short</Word>
      <Word>string</Word><Word>uint</Word><Word>ulong</Word><Word>ushort</Word><Word>void</Word>
    </Keywords>
    <Rule color=""Type"">\b[A-Z][A-Za-z0-9_]*\b</Rule>
    <Rule color=""Number"">\b0[xX][0-9a-fA-F]+|(\b\d+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?</Rule>
  </RuleSet>
</SyntaxDefinition>";

        static IHighlightingDefinition Build(bool dark)
        {
            string xml = XSHD
                .Replace("$COMMENT$", dark ? "#6B9A6B" : "#15803D")
                .Replace("$STRING$",  dark ? "#F0A779" : "#B4410A")
                .Replace("$NUMBER$",  dark ? "#B5E3B0" : "#0F766E")
                .Replace("$KEYWORD$", dark ? "#6CA8FF" : "#1D4ED8")
                .Replace("$TYPE$",    dark ? "#5FD6C4" : "#0E7490");
            try
            {
                using (var reader = XmlReader.Create(new StringReader(xml)))
                    return HighlightingLoader.Load(reader, HighlightingManager.Instance);
            }
            catch
            {
                return HighlightingManager.Instance.GetDefinition("C#");   // never block the UI over colours
            }
        }
    }
}
