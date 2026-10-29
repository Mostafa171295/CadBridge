using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadBridge
{
    // A project = a folder with  Files\  (everything the scripts save)  and  Scripts\<Tab>\  (your own scripts).
    public static class ProjectManager
    {
        public static string DocsRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CadBridge"); }
        }
        public static string ProjectsRoot { get { return Path.Combine(DocsRoot, "Projects"); } }
        public static string GlobalScriptsRoot { get { return Path.Combine(DocsRoot, "Scripts"); } }

        static string StateFile
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                    "CadBridge", "last-project.txt");
            }
        }

        public static string Current { get; private set; }
        public static event Action Changed;

        public static void Init()
        {
            Directory.CreateDirectory(ProjectsRoot);
            Directory.CreateDirectory(GlobalScriptsRoot);
            if (List().Count == 0) Create("My first project");

            string last = null;
            try { if (File.Exists(StateFile)) last = File.ReadAllText(StateFile).Trim(); }
            catch { }

            List<string> all = List();
            Current = (last != null && all.Contains(last)) ? last : all[0];
            EnsureDirs(Current);
        }

        public static List<string> List()
        {
            return Directory.GetDirectories(ProjectsRoot)
                            .Select(d => Path.GetFileName(d))
                            .OrderBy(n => n)
                            .ToList();
        }

        public static string FilesDir
        {
            get { EnsureDirs(Current); return Path.Combine(ProjectsRoot, Current, "Files"); }
        }

        public static string ScriptsDir(string tab)
        {
            string p = Path.Combine(ProjectsRoot, Current, "Scripts", tab);
            Directory.CreateDirectory(p);
            return p;
        }

        public static string GlobalScriptsDir(string tab)
        {
            string p = Path.Combine(GlobalScriptsRoot, tab);
            Directory.CreateDirectory(p);
            return p;
        }

        public static void Select(string name)
        {
            if (name == Current) return;
            Current = name;
            EnsureDirs(name);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
                File.WriteAllText(StateFile, name);
            }
            catch { }
            if (Changed != null) Changed();
        }

        public static string Create(string rawName)
        {
            string name = Sanitize(rawName);
            if (name.Length == 0) throw new Exception("The project name is empty or has only invalid characters.");
            EnsureDirs(name);
            return name;
        }

        public static string Sanitize(string s)
        {
            s = s ?? "";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
            return s.Trim();
        }

        static void EnsureDirs(string name)
        {
            Directory.CreateDirectory(Path.Combine(ProjectsRoot, name, "Files"));
            Directory.CreateDirectory(Path.Combine(ProjectsRoot, name, "Scripts"));
        }
    }
}
