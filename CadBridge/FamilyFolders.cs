using System;
using System.Collections.Generic;
using System.IO;

namespace CadBridge
{
    // Remembers, for each kind of family list (columnfiles, beamfiles, footingfiles),
    // the folder that the user picked with the "Folder..." button.
    // Saved in  %APPDATA%\CadBridge\family-folders.txt   (one line per kind:  key=folder)
    public static class FamilyFolders
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool _loaded;

        static string StateFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "CadBridge", "family-folders.txt");
            }
        }

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(StateFile)) return;
                foreach (string line in File.ReadAllLines(StateFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    Map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        public static string Get(string key)
        {
            Load();
            string v;
            return Map.TryGetValue(key ?? "", out v) ? v : "";
        }

        public static void Set(string key, string folder)
        {
            Load();
            if (string.IsNullOrWhiteSpace(folder)) Map.Remove(key); else Map[key] = folder.Trim();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
                var lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in Map) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(StateFile, lines);
            }
            catch { }
        }
    }
}
