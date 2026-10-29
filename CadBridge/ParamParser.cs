using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadBridge
{
    // A script can contain placeholders like  {{Span=12000}}  (name = default value).
    // The program shows one input box per name; on Run every placeholder is replaced by the typed value.
    public static class ParamParser
    {
        static readonly Regex Rx =
            new Regex(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([^}]*?)\s*\}\}", RegexOptions.Compiled);

        // unique names, in order of first appearance, with their default value
        public static List<KeyValuePair<string, string>> Find(string code)
        {
            var list = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>();
            foreach (Match m in Rx.Matches(code ?? ""))
            {
                string name = m.Groups[1].Value;
                if (seen.Add(name))
                    list.Add(new KeyValuePair<string, string>(name, m.Groups[2].Value));
            }
            return list;
        }

        public static string Apply(string code, IDictionary<string, string> values)
        {
            return Rx.Replace(code ?? "", m =>
            {
                string v;
                return values.TryGetValue(m.Groups[1].Value, out v) ? v : Fallback(m.Groups[2].Value);
            });
        }

        // ---- drop-down boxes
        //   {{Level=@levels}}        -> list read from the CAD program (Revit): @levels @materials @columnTypes ...
        //   {{Scope=Type|Instance}}  -> fixed list of choices
        //   {{Span=12000}}           -> normal text box
        public static string NormSource(string s)
        {
            return (s ?? "").Trim().TrimStart('@').Trim().ToLowerInvariant();
        }

        public static void Describe(string raw, out string source, out List<string> options)
        {
            source = null; options = null;
            raw = (raw ?? "").Trim();
            if (raw.StartsWith("@")) { source = NormSource(raw); return; }
            if (raw.Contains("|"))
                options = raw.Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        // value used when the person never touched the box
        public static string Fallback(string raw)
        {
            string src; List<string> opts;
            Describe(raw, out src, out opts);
            if (src != null) return "";
            if (opts != null && opts.Count > 0) return opts[0];
            return raw;
        }
    }
}
