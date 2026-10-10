// CollectionsConfig.cs
//
// Reads the collections.conf text pasted into the settings page: [global] and [collection NAME] sections with
// 'key value' lines. The format is described with examples in the README.
//
// NOTE: '#' starts a comment, also at the end of a line (after a space). That is why colours are written as r,g,b
// and never as #rrggbb.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UltimateCollections.Core
{
    // Setting names. Sources are the ways to fill a collection; Online ones need a download (title only looks at the
    // library). Repeatable keys may appear more than once in a section and mean ANY.
    public static class Keys
    {
        public static readonly string[] Sources = { "tmdb_keyword", "tmdb_actor", "mdblist", "release_years", "title" };
        public static readonly string[] Online = { "tmdb_keyword", "tmdb_actor", "mdblist", "release_years" };   // need a download
        public static readonly string[] Discover = { "min_rating", "min_votes" };
        public static readonly HashSet<string> Repeatable =
            new HashSet<string>(Sources.Concat(new[] { "exclude_genre", "exclude_keyword" }));
        // Settings that change WHAT is downloaded; they make up the list fingerprint (see ListCache). Changing one
        // fetches that list again. Poster settings stay out of here on purpose.
        public static readonly string[] Signature =
            Online.Concat(new[] { "exclude_genre", "exclude_keyword", "tv", "max_pages" }).Concat(Discover).ToArray();
    }

    // One [collection NAME] section. Repeatable keys go into Lists, everything else into Values (last one wins).
    // Line is kept so error messages can point at the section.
    public sealed class CollectionDefinition
    {
        public string Name { get; }
        public int Line { get; }
        public Dictionary<string, List<string>> Lists { get; } = new Dictionary<string, List<string>>();
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();

        public CollectionDefinition(string name, int line)
        {
            Name = name;
            Line = line;
        }

        public IReadOnlyList<string> GetList(string key) =>
            Lists.TryGetValue(key, out var l) ? (IReadOnlyList<string>)l : Array.Empty<string>();

        public string Get(string key) => Values.TryGetValue(key, out var v) ? v : null;

        public bool Has(string key) => Lists.ContainsKey(key) || Values.ContainsKey(key);

        public bool WantsTv => new[] { "yes", "true", "on", "1" }.Contains((Get("tv") ?? "no").ToLowerInvariant());

        public bool NeedsDownload => Keys.Online.Any(k => GetList(k).Count > 0);
    }

    public sealed class CollectionsConfig
    {
        public Dictionary<string, string> Global { get; } = new Dictionary<string, string>();
        public List<CollectionDefinition> Collections { get; } = new List<CollectionDefinition>();

        public int GlobalInt(string key, int dflt) => Global.TryGetValue(key, out var v) ? int.Parse(v) : dflt;

        public double GlobalDouble(string key, double dflt) =>
            Global.TryGetValue(key, out var v) ? double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) : dflt;
    }

    public sealed class ConfigException : Exception
    {
        public ConfigException(string message) : base(message) { }
    }

    // ---- parser ----

    // [global] / [collection NAME] sections with 'key value' lines. '#' starts a comment. List keys may repeat.
    // Anything we cannot make sense of throws a ConfigException with the line number, so a typo stops the run
    // instead of quietly emptying a collection.
    public static class ConfigParser
    {
        static readonly Regex TrailingComment = new Regex(@"\s+#.*$");
        static readonly Regex SectionHeader = new Regex(@"^\[(\w+)(?:\s+(.+?))?\]$");

        public static CollectionsConfig Parse(string text)
        {
            var cfg = new CollectionsConfig();
            Dictionary<string, string> globals = null;
            CollectionDefinition cur = null;
            var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            for (var n = 1; n <= lines.Length; n++)
            {
                var line = TrailingComment.Replace(lines[n - 1], "").Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                var m = SectionHeader.Match(line);
                if (m.Success)
                {
                    var kind = m.Groups[1].Value;
                    var name = m.Groups[2].Success ? m.Groups[2].Value.Trim() : "";
                    if (kind == "global")
                    {
                        globals = cfg.Global;
                        cur = null;
                    }
                    else if (kind == "collection" && name.Length > 0)
                    {
                        globals = null;
                        cur = new CollectionDefinition(name, n);
                        cfg.Collections.Add(cur);
                    }
                    else
                        throw new ConfigException($"line {n}: unknown section {line}");
                    continue;
                }
                if (globals == null && cur == null)
                    throw new ConfigException($"line {n}: setting outside a section");
                var sp = line.IndexOfAny(new[] { ' ', '\t' });
                var key = sp < 0 ? line : line.Substring(0, sp);
                var val = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                if (globals != null)
                    globals[key] = val;
                else if (Keys.Repeatable.Contains(key))
                {
                    if (!cur.Lists.TryGetValue(key, out var list))
                        cur.Lists[key] = list = new List<string>();
                    list.Add(val);
                }
                else
                    cur.Values[key] = val;
            }

            // Checks that need the whole file: names must be unique (the name is the key in state and the saved
            // lists), and a collection without any source would always be empty.
            var dup = cfg.Collections.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(x => x).ToList();
            if (dup.Count > 0)
                throw new ConfigException("duplicate collection names: " + string.Join(", ", dup));
            foreach (var c in cfg.Collections)
                if (!Keys.Sources.Any(c.Has))
                    throw new ConfigException($"collection '{c.Name}' (line {c.Line}) has no source ({string.Join(", ", Keys.Sources.OrderBy(x => x))})");
            return cfg;
        }
    }

    // ---- seasons ----

    public static class Season
    {
        // 'MM-DD MM-DD', both days included, may wrap the new year (12-26 01-04). Compared as MMDD strings, which
        // sort the same as dates within a year; when the start is after the end the window wraps.
        public static bool InSeason(string spec, DateTime today)
        {
            var parts = spec.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new ConfigException($"season needs two dates, e.g. 'season 12-01 12-31' (got '{spec}')");
            var a = parts[0].Replace("-", "");
            var b = parts[1].Replace("-", "");
            var t = today.ToString("MMdd");
            return string.CompareOrdinal(a, b) <= 0
                ? string.CompareOrdinal(a, t) <= 0 && string.CompareOrdinal(t, b) <= 0
                : string.CompareOrdinal(t, a) >= 0 || string.CompareOrdinal(t, b) <= 0;
        }
    }
}
