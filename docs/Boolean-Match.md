# Boolean Match Expressions and Emby Metadata Selectors

The change that added the `match` key to Ultimate Collections. This page covers what it does, how it is built,
what was tested, and why existing configurations are unaffected. For how to **use** it, see the
[Configuration Guide](Configuration-Guide.md).

| | |
|---|---|
| Fork | github.com/HHRSDev/UltimateCollections (forked from jdwaymire/UltimateCollections) |
| Date | 2026-10-10 |
| Status | Built and tested on a live Emby server by the fork owner |

---

## 1. What was missing

The original plugin fills a collection from a fixed set of sources (`tmdb_keyword`, `tmdb_actor`, `release_years`,
`mdblist`, `title`) that combine by fixed rules:

- the same key repeated means ANY;
- different TMDb keys are sent to TMDb as one search, so they mean AND;
- `mdblist` and `title` are added on top (OR).

That made some everyday collections impossible to write:

- **An actor AND a genre** ("Tom Hanks war films"), since genres could only be excluded, never required.
- **OR across kinds**: a TMDb keyword **or** an Emby tag; a list **or** a director.
- **NOT** anything other than an excluded genre or keyword.
- **Emby's own metadata**: tags (the natural way to hand-pick titles), studios, directors and writers, parental ratings, Emby's own years and ratings.

---

## 2. What was added

### 2.1 The match key

A collection may now contain one or more `match` lines, each holding a boolean expression:

```ini
[collection Hanks at War]
match  actor:"Tom Hanks" AND (genre:War OR tmdb_keyword:"world war ii")
```

- Selectors are written `field:value`, with double quotes around values that contain spaces or parentheses.
- Operators are `AND`, `OR`, `NOT`, in any case, with parentheses for grouping. NOT binds tightest, then AND, then OR. There is no implicit AND: two selectors next to each other are an error.
- Several `match` lines in one collection are combined with OR, consistent with the plugin's long-standing rule that a repeated key means ANY.
- `match` replaces the simple sources for that collection: a collection with `match` may not also use `tmdb_keyword`, `tmdb_actor`, `mdblist`, `release_years`, `title`, `exclude_genre` or `exclude_keyword`. That rule keeps every collection's combining logic in one place. `tv`, `season`, `min_items`, `max_pages`, `min_rating`, `min_votes` and the poster keys work as before.

### 2.2 Selectors

**Emby metadata** (read from the library on every run, no internet):

| Selector | Source in Emby |
|---|---|
| `tag:` | Tags |
| `genre:` | Genres |
| `studio:` | Studios |
| `rated:` | Official (parental) rating |
| `title:` | Name, contains, any case |
| `actor:` | People of type Actor or GuestStar |
| `director:`, `writer:`, `producer:` | People of that type |
| `person:` | People of any type |
| `year:` | Production year |
| `rating:` | Community rating |
| `critic:` | Critic rating |
| `type:` | Movie or Series |

Number selectors accept `N`, `A..B`, `>=N`, `<=N`, `>N`, `<N` and `=N`. A bare number is an exact year for `year:`
and "at least" for `rating:` and `critic:`, matching the meaning of the existing `min_rating`.

**Online** (downloaded and saved): `tmdb_keyword:`, `tmdb_actor:`, `mdblist:`.

---

## 3. How it works

### 3.1 Every selector is a set of library items

The central decision: each selector is resolved to the **set of library item ids** it matches, and the expression is
evaluated with set operations:

| Operator | Set operation |
|---|---|
| `a AND b` | intersection |
| `a OR b` | union |
| `NOT a` | universe minus a |

The **universe** is the collection's scope: all movies, plus all series when the collection has `tv yes`.

Because everything is reduced to library ids before combining, a TMDb list, an MDBList list and an Emby tag are
interchangeable, and new selectors can be added later without touching the evaluator.

### 3.2 Parsing

`Core/MatchExpression.cs` holds a tokenizer and a recursive-descent parser:

```
expr := and ( OR and )*
and  := not ( AND not )*
not  := NOT not  /  '(' expr ')'  /  field:value
```

Selectors are validated while the settings are read: an unknown field, a bad number, a missing quote or bracket,
or a stray word all raise a `ConfigException` carrying the line number and column. The whole run stops before
anything is changed, the same as every other settings error in the plugin.

### 3.3 Resolving selectors

In `Syncer.MatchAsync`:

- **Text, number and type selectors** are tested against the `LibraryItem` list already loaded for the run. `EmbyLibrary` now also loads Tags, Studios, OfficialRating and CriticRating for each item.
- **People selectors** call `IEmbyLibrary.GetItemsWithPerson`. It finds the Person item by name (compared again without case) and asks Emby for the movies and series linked to it with `InternalItemsQuery.PersonIds` and `PersonTypes`. This is one indexed query per selector, not a scan of every item's cast.
- **Online selectors** each get their own saved list. A synthetic `CollectionDefinition` holding just that one source plus the owning collection's `tv`, `max_pages`, `min_rating` and `min_votes` is passed to the existing download code (`ListFetcher.FetchAsync`) and the existing `ListCache`. That reuses the download, retry, throttling, fingerprinting and refresh logic unchanged.
- A selector used twice in one expression is resolved once.

### 3.4 Saved lists for online selectors

- They live in a separate folder, `lists/match/`, managed by a second `ListCache` instance.
- A file's name is derived from the selector and the download-relevant settings (for example `tmdb_keyword=christmas max_pages=100 tv=yes`), so two collections using the same selector with the same settings share one download.
- `tmdb_actor:` never requests TV (TMDb's TV search has no cast filter), so it ignores `tv yes` rather than failing as the simple `tmdb_actor` key does.
- `mdblist:` does not include `max_pages`, `min_rating` or `min_votes` in its name, since they do not affect it.
- After a full run, saved lists no collection uses any more are deleted. The existing clean-up of `lists/` now skips collections that use `match`, so a collection converted to `match` loses its stale saved list.

### 3.5 Safety rules

These follow the plugin's existing principle that a source which suddenly returns nothing is more likely broken
than empty:

- An **online selector that returns nothing** raises an error for that collection, which is left unchanged.
- An **Emby selector that matches nothing** is a warning (`tag:"x" matches nothing in the library (check the spelling)`), because a tag nobody has yet is a legitimate answer.
- If **every selector of a collection matches nothing**, the existing "sources returned nothing" error applies and the collection is left unchanged.
- An expression whose selectors found titles but whose combination is empty is a real result: `min_items` applies as usual.

### 3.6 Logging

With **Log the matching titles** on, each selector's count is logged before the result, so a misspelt value shows up
as a 0 straight away:

```
[Hanks at War] actor:"Tom Hanks": 14 in the library
[Hanks at War] genre:War: 63 in the library
[Hanks at War] tmdb_keyword:"world war ii": 41 in the library
```

The summary line names the source as `match` and lists each online selector's origin (saved list or download,
with the reason).

---

## 4. Files changed

| File | Change |
|---|---|
| `src/UltimateCollections/Core/MatchExpression.cs` | New. Selectors (`MatchAtom`), the expression tree (`AtomNode`, `NotNode`, `AndOrNode`) and the parser (`MatchParser`). |
| `src/UltimateCollections/Core/CollectionsConfig.cs` | `match` registered as a repeatable source key; each line parsed with its line number; several lines joined with OR; mixing with simple sources rejected; `NeedsDownload` counts online selectors. |
| `src/UltimateCollections/Core/Syncer.cs` | `LibraryItem` gains Tags, Studios, OfficialRating, CriticRating. `IEmbyLibrary` gains `GetItemsWithPerson`. New `MatchCache` property, `MatchAsync` path, and pruning of `lists/match/`. |
| `src/UltimateCollections/Emby/EmbyLibrary.cs` | Loads the four extra fields; implements `GetItemsWithPerson`. |
| `src/UltimateCollections/Emby/SyncCollectionsTask.cs` | Creates the `lists/match/` cache. |
| `README.md` | New `match` section, selector tables, recipe 14, FAQ entries, links to these documents. |
| `examples/collections.conf` | `match` documented in the header; two commented-out examples. |
| `docs/` | This write-up, the Configuration Guide, and the docs index. |

The settings page (`Web/configPage.*`) needed no change: it passes the settings text through as is.

---

## 5. Backwards compatibility

**Existing configurations behave exactly as before, and their saved lists stay valid** (no re-download).

Why:

- The new code runs only for collections that contain a `match` line. Every other collection takes the original code path.
- The saved-list fingerprint is built from `Keys.Signature`, which was deliberately left unchanged, so every existing fingerprint is identical and no saved list is considered stale.
- `match` lists live in the `lists/match/` subfolder. The original clean-up only looks at files directly in `lists/`, so the two never touch.
- `state.json`, `artwork-state.json` and the plugin's settings format are unchanged.

How it was verified: the original code (git `HEAD` before the change) and the new code were both built, and the same
legacy configuration was fed to each. That configuration was the original `examples/collections.conf` plus every
recipe in the original README: 40 collections. A small program printed, for each collection, the parsed keys and
values, line number, TV flag, download flag and saved-list fingerprint. It then ran a full dry-run sync against a
fake library and deterministic fake downloads. **The output of the two builds was byte-identical**: all 40 parses and
fingerprints, and all 83 sync log lines, including the "sources returned nothing" error, the min-items handling and
the deletion of a collection no longer in the settings.

The one theoretical difference: the original parser silently ignored unknown keys, so a configuration that happened
to contain a line starting with `match` used to ignore it and now reads it as an expression. If that line is not a
valid expression, the run stops with an error naming the line.

---

## 6. Testing

**Unit-level (scratch harness, 28 checks, all passing).** Built against the plugin DLL with a fake `IEmbyLibrary` and
a fake download function:

- Parser: precedence (NOT over AND over OR); case-insensitive operators; error messages for unquoted spaces, unknown fields, unclosed and stray parentheses, missing operators, dangling operators, bad numbers, bad `type:` values, unclosed quotes and empty values.
- Settings: errors carry the line number; mixing `match` with simple sources is rejected; two `match` lines become an OR; simple-source collections parse as before.
- Sync: an actor AND (genre OR keyword); the same with `tv yes`, picking up a series through the TV list and a case-insensitive person name; tag AND NOT genre AND NOT rating; keyword OR tag across two `match` lines; person AND year range AND rating; NOT against the universe; an empty online list (error, collection unchanged); a misspelt tag (warning, then "sources returned nothing"); a simple-source collection alongside, unchanged.
- Saved lists: one download per distinct selector and settings; saved under `lists/match/`; reused on the next run; unused ones pruned while simple-source lists are kept; a collection converted to `match` loses its old saved list.

**Compatibility.** See section 5.

**Live.** Tested by the fork owner on their Emby server, 2026-10-10, before the clean-up change in section 3.4 (which
only affects which stale files are deleted).

---

## 7. Known limits and possible next steps

- **Poster preview.** The Poster style popup previews a `match` collection with its own titles once it exists in Emby. Before that (new, or out of season) it falls back to the best-rated movies in the library, because the preview reads saved lists by collection name and a `match` collection has none. Evaluating the expression in the preview would close this gap.
- **Person names** must match Emby's spelling apart from case. A future version could accept a person's TMDb or IMDb id.
- **More selectors** fit the same pattern: library (`library:`), collection membership, date added, runtime, resolution, audio language, played or favourite for a given user. Each is a new entry in `MatchAtom` plus, where needed, a field loaded in `EmbyLibrary`.
- **Release.** The project version is still 1.0.0. A release needs `<Version>` in `UltimateCollections.csproj` bumped (for example to 1.1.0) and a matching `v1.1.0` tag pushed; the release workflow builds and attaches the DLL.
