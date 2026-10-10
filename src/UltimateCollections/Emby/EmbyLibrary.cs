// EmbyLibrary.cs
//
// IEmbyLibrary on Emby's internal services: reading movies, series and collections, and creating, filling and
// deleting collections. This is the only file that touches Emby's internal APIs for collections, so an Emby update
// that breaks the plugin should break here. Membership uses internal long ids directly, no REST and no API key.
//
// NOTE: DtoOptions(false) loads only the fields we list. Without ItemFields.Id every item came back with InternalId 0
// and all matches collapsed into one wrong title (first dry run on a real server, 2026-10-09). Query() now refuses to hand
// such items to the sync at all.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using UltimateCollections.Core;

namespace UltimateCollections.Emby
{
    public class EmbyLibrary : IEmbyLibrary
    {
        readonly ILibraryManager _library;
        readonly ICollectionManager _collections;

        public EmbyLibrary(ILibraryManager library, ICollectionManager collections)
        {
            _library = library;
            _collections = collections;
        }

        // Id and Name must be listed too: with DtoOptions(false) Emby loads only the listed fields, and without
        // ItemFields.Id every item comes back with InternalId 0 (seen on 4.10.1, 2026-10-09).
        static readonly ItemFields[] Fields =
            { ItemFields.Id, ItemFields.Name, ItemFields.ProviderIds, ItemFields.Genres, ItemFields.ProductionYear, ItemFields.CommunityRating };

        // ---- reading ----

        List<LibraryItem> Query(string type) =>
            Query(new InternalItemsQuery { IncludeItemTypes = new[] { type }, Recursive = true, IsVirtualItem = false }, type);

        // Every item list goes through here: it sets the field list and turns Emby's items into our LibraryItem.
        // The IMDb id is looked up under both "Imdb" and "IMDB".
        List<LibraryItem> Query(InternalItemsQuery query, string what)
        {
            query.DtoOptions = new DtoOptions(false) { Fields = Fields };
            var items = _library.GetItemList(query);
            // Never let a half-loaded item reach the sync: id 0 would be added to / removed from collections.
            // Throwing here stops the run before anything is changed.
            if (items.Any(i => i.InternalId == 0 || string.IsNullOrEmpty(i.Name)))
                throw new InvalidOperationException($"Emby returned {what} items without id or name; nothing changed");
            return items.Select(i => new LibraryItem
            {
                Id = i.InternalId,
                Name = i.Name,
                Year = i.ProductionYear,
                TmdbId = ProviderId(i, "Tmdb"),
                ImdbId = ProviderId(i, "Imdb") ?? ProviderId(i, "IMDB"),
                Genres = i.Genres ?? Array.Empty<string>(),
                IsSeries = i is Series,
                Rating = i.CommunityRating,
            }).ToList();
        }

        static string ProviderId(BaseItem item, string name) =>
            item.ProviderIds != null && item.ProviderIds.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : null;

        public List<LibraryItem> GetMovies() => Query("Movie");

        public List<LibraryItem> GetSeries() => Query("Series");

        // Name -> id of every collection in Emby, whoever made it. Used for the ownership check: a collection with
        // our name that is not in state.json belongs to someone else and is left alone.
        public Dictionary<string, long> GetCollections()
        {
            var result = new Dictionary<string, long>();
            foreach (var b in _library.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { "BoxSet" }, Recursive = true }))
                result[b.Name] = b.InternalId;      // a duplicate name keeps one id; only the name matters for the ownership check
            return result;
        }

        public List<LibraryItem> GetAllCollections() =>
            Query(new InternalItemsQuery { IncludeItemTypes = new[] { "BoxSet" }, Recursive = true }, "BoxSet");

        public List<LibraryItem> GetMemberItems(long collectionId) =>
            Query(new InternalItemsQuery { CollectionIds = new[] { collectionId } }, "collection member");

        BoxSet Get(long id) => _library.GetItemById(id) as BoxSet;

        public bool CollectionExists(long id) => Get(id) != null;

        // Just the member ids, which is all the sync needs to work out what to add and remove.
        public HashSet<long> GetMembers(long collectionId) =>
            new HashSet<long>(_library.GetInternalItemIds(new InternalItemsQuery { CollectionIds = new[] { collectionId } }));

        // ---- changing ----

        public async Task<long> CreateCollectionAsync(string name, long[] itemIds)
        {
            var box = await _collections.CreateCollection(new CollectionCreationOptions { Name = name, ItemIdList = itemIds })
                .ConfigureAwait(false);
            return box.InternalId;
        }

        public Task AddAsync(long collectionId, long[] itemIds) => _collections.AddToCollection(collectionId, itemIds);

        // RemoveFromCollection wants the BoxSet itself, not its id, so it has to be looked up first.
        public void Remove(long collectionId, long[] itemIds)
        {
            var box = Get(collectionId) ?? throw new InvalidOperationException($"collection {collectionId} not found");
            _collections.RemoveFromCollection(box, itemIds);
        }

        // A collection that is already gone (deleted by hand in Emby) is not an error. DeleteFileLocation takes the
        // collection's own metadata folder with it (/var/lib/emby/metadata/collections/<name>/, where its poster.jpg
        // and fanart.jpg live); checked 2026-10-09. Titles are never touched.
        public void Delete(long collectionId)
        {
            var box = Get(collectionId);
            if (box != null)
                _library.DeleteItem(box, new DeleteOptions { DeleteFileLocation = true });
        }
    }
}
