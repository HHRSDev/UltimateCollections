// ListCache.cs
//
// Keeps a copy of every downloaded list on disk (one JSON file per collection in the plugin's lists/ folder) so we
// are not hitting TMDb and MDBList every night. The nightly run matches the saved list against the library, which
// costs nothing; a list is only downloaded again when it is a week old, when the collection's settings change, or
// when somebody ticks "Download all lists again".
//
// NOTE: the fingerprint and the file layout are a promise. If either one changes, every saved list looks stale and
// the next run downloads all of them again (several hundred TMDb requests). An earlier version got bitten by this
// twice, so think twice before touching Fingerprint() or SavedList.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UltimateCollections.Core
{
    // What one saved list file looks like. The property names are lower case on purpose: they are the JSON names,
    // and Emby's serializer has no attribute we can rely on to rename them.
#pragma warning disable IDE1006
    public sealed class SavedList
    {
        public string name { get; set; }
        public string sig { get; set; }         // fingerprint of the settings it was downloaded with
        public long fetched { get; set; }       // unix time of the download
        public int listed { get; set; }         // how many titles the source had (for the log)
        public List<long> tmdb { get; set; }
        public List<string> imdb { get; set; }
        public List<long> tv_tmdb { get; set; }
        public List<string> tv_imdb { get; set; }
    }
#pragma warning restore IDE1006

    public class ListCache
    {
        readonly string _dir;
        readonly IJson _json;
        readonly ISyncLog _log;

        public ListCache(string dir, IJson json, ISyncLog log)
        {
            _dir = dir;
            _json = json;
            _log = log;
        }

        // Turns a collection name into something safe for a file name ("Valentine's Day" -> "Valentine_s_Day").
        public static string FileName(string name) => Regex.Replace(name, "[^A-Za-z0-9._-]+", "_");

        // A short hash of the settings that decide WHAT gets downloaded (sources, exclusions, tv, max_pages, ratings).
        // Settings that are not set are skipped entirely, which means adding a brand new setting to the code later
        // will not change the fingerprint of collections that do not use it. Poster settings are not in here, so
        // changing a colour never costs a download.
        public static string Fingerprint(CollectionDefinition col)
        {
            var sb = new StringBuilder();
            foreach (var k in Keys.Signature.OrderBy(x => x, StringComparer.Ordinal))
            {
                if (col.Lists.TryGetValue(k, out var list))
                    sb.Append(k).Append('=').Append(string.Join("\u001f", list)).Append('\n');
                else if (col.Values.TryGetValue(k, out var v))
                    sb.Append(k).Append(':').Append(v).Append('\n');
            }
            using (var sha = SHA1.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())).Select(b => b.ToString("x2"))).Substring(0, 12);
        }

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        static SourceList ToSourceList(SavedList s)
        {
            var r = new SourceList { Listed = s.listed };
            r.Tmdb.UnionWith(s.tmdb ?? new List<long>());
            r.Imdb.UnionWith(s.imdb ?? new List<string>());
            r.TvTmdb.UnionWith(s.tv_tmdb ?? new List<long>());
            r.TvImdb.UnionWith(s.tv_imdb ?? new List<string>());
            return r;
        }

        string PathFor(string name) => Path.Combine(_dir, FileName(name) + ".json");

        // The saved list of a collection as it is on disk, or null. Never downloads; the settings page's poster
        // preview uses it for collections that are not built yet (out of season, say).
        public SavedList Peek(string name) => Load(PathFor(name));

        // ---- reading and writing the files ----

        // A missing file is normal (new collection). A broken one is not worth stopping for: we say so in the log
        // and download the list again.
        SavedList Load(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return _json.Deserialize<SavedList>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                _log.Warn($"saved list {Path.GetFileName(path)} unreadable ({e.Message}), downloading again");
                return null;
            }
        }

        // Writes to a .tmp file first and then swaps it in, so a crash or a full disk halfway through never leaves
        // a half-written list behind. The ids are sorted only to make the files easy to compare by eye.
        void Save(string path, CollectionDefinition col, string sig, SourceList r)
        {
            Directory.CreateDirectory(_dir);
            var data = new SavedList
            {
                name = col.Name, sig = sig, fetched = Now(), listed = r.Listed,
                tmdb = r.Tmdb.OrderBy(x => x).ToList(), imdb = r.Imdb.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                tv_tmdb = r.TvTmdb.OrderBy(x => x).ToList(), tv_imdb = r.TvImdb.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            };
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, _json.Serialize(data));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }

        // ---- the main entry point ----

        // Hands back the collection's list plus a few words for the log about where it came from.
        //   1. saved copy, same settings, not too old, not forced  -> use it, no download
        //   2. otherwise download it and save it
        //   3. download failed but we still have a copy made with the same settings -> use the old copy, warn
        // An empty download is never saved; a source that suddenly returns nothing is more likely broken than empty.
        public async Task<(SourceList list, string source)> GetAsync(CollectionDefinition col, Func<Task<SourceList>> download,
            double refreshDays, bool force, CancellationToken ct)
        {
            var sig = Fingerprint(col);
            var path = PathFor(col.Name);
            var old = Load(path);
            var age = old == null ? 0 : (Now() - old.fetched) / 86400.0;
            if (old != null && old.sig == sig && !force && age < refreshDays)
                return (ToSourceList(old), $"saved list, {age:0.0} days old");
            var why = old == null ? "new" : old.sig != sig ? "sources changed" : force ? "forced" : "weekly refresh";
            SourceList r;
            try
            {
                r = await download().ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException) && old != null && old.sig == sig)
            {
                // a copy made with DIFFERENT settings would be the wrong list, so only a matching one is used
                _log.Warn($"[{col.Name}] list download failed ({e.Message}), using the saved list from {age:0.0} days ago");
                return (ToSourceList(old), "saved list (download failed)");
            }
            if (r.Listed > 0)
                Save(path, col, sig, r);
            return (r, $"downloaded ({why})");
        }

        // Clean-up after a full run: drop the saved lists of collections that are gone from the settings.
        // Not called on an "Only these collections" run, because then the other collections only look missing.
        public void Prune(IEnumerable<string> keepNames)
        {
            if (!Directory.Exists(_dir))
                return;
            var keep = new HashSet<string>(keepNames.Select(n => FileName(n) + ".json"));
            foreach (var f in Directory.GetFiles(_dir, "*.json"))
                if (!keep.Contains(Path.GetFileName(f)))
                    File.Delete(f);
        }
    }
}
