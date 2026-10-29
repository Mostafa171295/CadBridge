using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CadBridge
{
    // Lets a script read a .csv or .xlsx file:    var rows = TableReader.Read(@"C:\data\points.csv");
    // Every row is a string[]. Use TableReader.Num(text) to turn a cell into a number.
    public static class TableReader
    {
        public static List<string[]> Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("File not found: " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".xlsx") return ReadXlsx(path);
            if (ext == ".csv" || ext == ".txt") return ReadCsv(path);
            throw new Exception("Unsupported file type '" + ext + "'. Use .csv or .xlsx.");
        }

        public static double Num(string s)
        {
            s = (s ?? "").Trim().Replace(" ", "");
            if (s.Contains(",") && !s.Contains(".")) s = s.Replace(',', '.');   // accept 12,5 as well as 12.5
            return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- CSV

        static List<string[]> ReadCsv(string path)
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                text = sr.ReadToEnd();

            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                            .Where(l => l.Trim().Length > 0).ToList();
            var rows = new List<string[]>();
            if (lines.Count == 0) return rows;

            int semi = lines[0].Count(c => c == ';');
            int comma = lines[0].Count(c => c == ',');
            int tab = lines[0].Count(c => c == '\t');
            char delim = ',';
            if (semi > comma && semi >= tab) delim = ';';
            else if (tab > comma && tab > semi) delim = '\t';

            foreach (string line in lines) rows.Add(SplitLine(line, delim));
            return rows;
        }

        static string[] SplitLine(string line, char delim)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == delim) { res.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            res.Add(sb.ToString());
            return res.ToArray();
        }

        // ---------------------------------------------------------------- XLSX (first sheet)

        static List<string[]> ReadXlsx(string path)
        {
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = new List<string[]>();

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var shared = new List<string>();
                ZipArchiveEntry ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (Stream s = ssEntry.Open())
                    {
                        foreach (XElement si in XDocument.Load(s).Descendants(ns + "si"))
                            shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
                    }
                }

                ZipArchiveEntry sheet = zip.Entries
                    .Where(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (sheet == null) throw new Exception("No worksheet found in the Excel file.");

                using (Stream s = sheet.Open())
                {
                    foreach (XElement row in XDocument.Load(s).Descendants(ns + "row"))
                    {
                        var cells = new Dictionary<int, string>();
                        int max = -1;
                        int next = 0;
                        foreach (XElement c in row.Elements(ns + "c"))
                        {
                            string r = (string)c.Attribute("r");
                            int col = r != null ? ColIndex(r) : next;
                            next = col + 1;

                            string t = (string)c.Attribute("t");
                            string v = (string)c.Element(ns + "v");
                            string val = "";
                            if (t == "s" && v != null)
                            {
                                int idx = int.Parse(v, CultureInfo.InvariantCulture);
                                if (idx >= 0 && idx < shared.Count) val = shared[idx];
                            }
                            else if (t == "inlineStr")
                                val = string.Concat(c.Descendants(ns + "t").Select(x => x.Value));
                            else if (v != null)
                                val = v;

                            cells[col] = val;
                            if (col > max) max = col;
                        }

                        if (max < 0) continue;
                        var arr = new string[max + 1];
                        for (int i = 0; i <= max; i++) arr[i] = cells.ContainsKey(i) ? cells[i] : "";
                        rows.Add(arr);
                    }
                }
            }
            return rows;
        }

        static int ColIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (!char.IsLetter(ch)) break;
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            return col - 1;
        }
    }
}
