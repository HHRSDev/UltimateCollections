// CollectionCleaner.cs
//
// "Delete All Collections" from the settings page's danger zone: removes every collection (BoxSet) in Emby, not only
// the plugin's. Titles and files are not touched.
//
// NOTE: this is the one place where the plugin touches collections it did not make, so it is fenced in: the word
// DELETE must be typed (checked here as well as on the page), and a backup of every collection and its members is
// written first. If the backup cannot be written, nothing is deleted.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UltimateCollections.Core
{
    // ---- backup file layout ----

    // Property names are lower case on purpose: they are the JSON names in the backup file.
#pragma warning disable IDE1006
    // One deleted collection in the backup file.
    public sealed class BackupCollection
    {
        public long id { get; set; }
        public string name { get; set; }
        public bool made_by_plugin { get; set; }
        public List<BackupMember> members { get; set; }
    }

    public sealed class BackupMember
    {
        public long id { get; set; }
        public string name { get; set; }
        public int? year { get; set; }
        public string tmdb { get; set; }
        public string imdb { get; set; }
    }

    public sealed class CleanupResult
    {
        public int Found { get; set; }
        public int Deleted { get; set; }
        public int Failed { get; set; }
        public string BackupFile { get; set; }
    }
#pragma warning restore IDE1006

    // ---- the delete ----

    public static class CollectionCleaner
    {
        // The word the user has to type; checked here as well as on the page.
        public const string ConfirmWord = "DELETE";

        // Backup first, then delete one by one. The backup write is not in a try: if it throws, we never get to the
        // delete loop. One failing delete is logged and counted and does not stop the rest.
        public static CleanupResult DeleteAll(IEmbyLibrary emby, Dictionary<string, long> pluginState, string backupDir, IJson json,
            ISyncLog log, string confirm)
        {
            if (confirm != ConfirmWord)
                throw new InvalidOperationException($"not confirmed: type {ConfirmWord} to delete all collections");
            var all = emby.GetAllCollections();
            var mine = new HashSet<long>(pluginState.Values);
            var backup = all.Select(c => new BackupCollection
            {
                id = c.Id,
                name = c.Name,
                made_by_plugin = mine.Contains(c.Id),
                members = emby.GetMemberItems(c.Id).Select(m => new BackupMember
                    { id = m.Id, name = m.Name, year = m.Year, tmdb = m.TmdbId, imdb = m.ImdbId }).ToList(),
            }).ToList();

            Directory.CreateDirectory(backupDir);
            var file = Path.Combine(backupDir, $"collections-{DateTime.Now:yyyy-MM-dd-HHmmss}.json");
            File.WriteAllText(file, json.Serialize(backup));
            log.Info($"delete all collections: {all.Count} found, backup written: {file}");

            var res = new CleanupResult { Found = all.Count, BackupFile = file };
            foreach (var c in all)
            {
                try
                {
                    emby.Delete(c.Id);
                    res.Deleted++;
                }
                catch (Exception e)
                {
                    res.Failed++;
                    log.Error($"delete all collections: '{c.Name}' ({c.Id}) could not be deleted: {e.Message}");
                }
            }
            // The plugin's own collections are gone too: forget them, so the next sync creates them again instead of
            // looking for ids that no longer exist. Only ids that are really gone are dropped (a failed delete stays).
            foreach (var name in pluginState.Where(kv => !emby.CollectionExists(kv.Value)).Select(kv => kv.Key).ToList())
                pluginState.Remove(name);
            log.Info($"delete all collections: {res.Deleted} deleted, {res.Failed} failed");
            return res;
        }
    }
}
