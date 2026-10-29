using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadBridge
{
    // Small remembered settings (saved in %AppData%\CadBridge\view.txt):
    //   - show / hide the Guide text and the Code editor (same for every tab)
    //   - the last folder / file you picked with a Browse button (per parameter name)
    public static class ViewPrefs
    {
        static readonly Dictionary<string, string> _d = new Dictionary<string, string>();

        static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "CadBridge", "view.txt");
            }
        }

        static ViewPrefs()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) _d[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        public static event Action Changed;

        // hidden by default: more room for the parameters
        public static bool ShowGuide { get { return Get("showGuide") == "1"; } }
        public static bool ShowCode { get { return Get("showCode") == "1"; } }

        public static void SetShow(bool guide, bool code)
        {
            _d["showGuide"] = guide ? "1" : "0";
            _d["showCode"] = code ? "1" : "0";
            Save();
            if (Changed != null) Changed();
        }

        public static string Get(string key)
        {
            string v;
            return _d.TryGetValue(key, out v) ? v : null;
        }

        public static void Set(string key, string value)
        {
            _d[key] = value;
            Save();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, _d.Select(kv => kv.Key + "=" + kv.Value).ToArray());
            }
            catch { }
        }
    }
}
