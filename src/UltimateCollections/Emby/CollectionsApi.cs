// CollectionsApi.cs
//
// The two REST routes behind the settings page's "Danger zone": count the collections, and delete every collection
// in Emby (not only ours; titles and files are not touched). The real work and the backup are in
// Core/CollectionCleaner.
//
// NOTE: both routes are admin only ([Authenticated(Roles = "Admin")]), and the server checks the confirm word again
// even though the page only unlocks the button for it. Never relax either: this deletes everything.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using UltimateCollections.Core;

namespace UltimateCollections.Emby
{
    [Route("/UltimateCollections/CollectionsSummary", "GET", Summary = "How many collections exist, and how many this plugin made")]
    [Authenticated(Roles = "Admin")]
    public class CollectionsSummaryRequest : IReturn<CollectionsSummary>
    {
    }

    public class CollectionsSummary
    {
        public int Total { get; set; }
        public int MadeByPlugin { get; set; }
    }

    [Route("/UltimateCollections/DeleteAllCollections", "POST", Summary = "Delete every collection in Emby (titles and files are not touched)")]
    [Authenticated(Roles = "Admin")]
    public class DeleteAllCollectionsRequest : IReturn<CleanupResult>
    {
        // Must be "DELETE" (CollectionCleaner.ConfirmWord).
        public string Confirm { get; set; }
    }

    public class CollectionsApi : IService
    {
        readonly ILibraryManager _library;
        readonly ICollectionManager _collections;
        readonly ILogger _logger;
        readonly IJsonSerializer _jsonSerializer;

        public CollectionsApi(ILibraryManager library, ICollectionManager collections, ILogManager logManager, IJsonSerializer jsonSerializer)
        {
            _library = library;
            _collections = collections;
            _logger = logManager.GetLogger("UltimateCollections");
            _jsonSerializer = jsonSerializer;
        }

        static string DataDir => Plugin.Instance.DataFolderPath;

        // The count shown before deleting. "Made by this plugin" = the ids in our state.json.
        public object Get(CollectionsSummaryRequest request)
        {
            var json = new EmbyJson(_jsonSerializer);
            var state = SyncCollectionsTask.LoadJson<Dictionary<string, long>>(json, Path.Combine(DataDir, "state.json"));
            var all = new EmbyLibrary(_library, _collections).GetAllCollections();
            var mine = new HashSet<long>(state.Values);
            return new CollectionsSummary { Total = all.Count, MadeByPlugin = all.Count(c => mine.Contains(c.Id)) };
        }

        public object Post(DeleteAllCollectionsRequest request)
        {
            if (request.Confirm != CollectionCleaner.ConfirmWord)
                throw new ArgumentException($"Not confirmed: type {CollectionCleaner.ConfirmWord} to delete all collections.");
            // Do not wait for a running sync: refuse straight away, the page tells the user to try again later.
            if (!Plugin.WorkLock.Wait(0))
                throw new InvalidOperationException("A collection sync is running right now. Try again when it has finished.");
            try
            {
                Directory.CreateDirectory(DataDir);
                var json = new EmbyJson(_jsonSerializer);
                var statePath = Path.Combine(DataDir, "state.json");
                var state = SyncCollectionsTask.LoadJson<Dictionary<string, long>>(json, statePath);
                using (var log = new SyncLog(_logger, Path.Combine(DataDir, "logs")))
                {
                    var result = CollectionCleaner.DeleteAll(new EmbyLibrary(_library, _collections), state,
                        Path.Combine(DataDir, "backups"), json, log, request.Confirm);
                    // DeleteAll drops our own deleted collections from state, so they are simply created again on
                    // the next sync.
                    SyncCollectionsTask.SaveJson(json, statePath, state);
                    return result;
                }
            }
            finally
            {
                Plugin.WorkLock.Release();
            }
        }
    }
}
