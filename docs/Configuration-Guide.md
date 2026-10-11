# Ultimate Collections: Configuration Guide

> **About this guide.** Ultimate Collections was created by **jdwaymire (WaymireNET)** ([original project](https://github.com/jdwaymire/UltimateCollections)). This guide was written for the **HHRSDev fork**, which adds a few features on top of the original. It describes the original plugin's configuration **and** the fork's additions together, so you can use one document. Everything the fork added is marked **[Fork addition]**; everything else works the same in the original plugin.

> **The fork's additions** are the `match` key, meaning AND / OR / NOT expressions and the Emby metadata selectors (sections 5, 6 and 10, most of the cookbook, and the `match` parts of sections 3, 11, 13 and 14), and `tmdb_actor` together with `tv yes` (section 4.1).

This guide covers everything you can write in the **Collections** box on the plugin's settings page (Dashboard >
Plugins > Ultimate Collections).

If you are new to the plugin, read [Installation and first run](../README.md#installation) in the main README first,
then come back here.

---

## Contents

1. [The shape of the settings text](#1-the-shape-of-the-settings-text)
2. [The global section](#2-the-global-section)
3. [Two ways to fill a collection](#3-two-ways-to-fill-a-collection)
4. [Simple sources](#4-simple-sources)
5. [Boolean collections: the match key](#5-boolean-collections-the-match-key)
6. [Selector reference](#6-selector-reference)
7. [Settings that work with both styles](#7-settings-that-work-with-both-styles)
8. [Poster and backdrop settings](#8-poster-and-backdrop-settings)
9. [Cookbook](#9-cookbook)
10. [Converting a simple collection to match](#10-converting-a-simple-collection-to-match)
11. [Downloads, saved lists and API cost](#11-downloads-saved-lists-and-api-cost)
12. [Testing a collection safely](#12-testing-a-collection-safely)
13. [Errors and warnings explained](#13-errors-and-warnings-explained)
14. [Quick reference card](#14-quick-reference-card)

---

## 1. The shape of the settings text

The settings text is plain text made of **sections**, and each section holds **`key value` lines**.

```ini
# A line starting with # is a comment.
[global]                          # settings for every collection
min_items  2

[collection Time Travel]          # one section per collection; the name is what Emby shows
tmdb_keyword  time travel         # key, at least one space, value
```

The rules:

- A section starts with `[global]` or `[collection Name Of The Collection]`. The name may contain spaces and punctuation (`[collection Valentine's Day]` is fine), and **every collection name must be unique**.
- Every other line is `key value`. Keys are lower case. Any number of spaces may separate key and value, so you can line them up.
- `#` starts a comment, either at the start of a line or **after a space** at the end of one. That is why colours are written `r,g,b` and never `#rrggbb`, and why a value can never contain ` #`.
- A key repeated within a collection means **ANY of them**. Two `tmdb_keyword` lines mean either keyword, and two `match` lines mean either expression.
- Anything the plugin cannot make sense of stops the run with an error naming the line or the collection, and **nothing is changed**. The plugin would rather do nothing than fill a collection with the wrong titles.

---

## 2. The global section

`[global]` is optional. Its settings apply to every collection, and some can be overridden per collection.

| Key | Default | Meaning |
|---|---|---|
| `min_items` | `2` | A collection with fewer matching titles is not created, and is removed if it shrinks below this. |
| `max_pages` | `100` | TMDb result pages (20 titles each) read per TMDb source. 100 pages is the top 2,000 titles. |
| `list_refresh_days` | `7` | Downloaded lists are reused for this many days before being downloaded again. |
| `posters` | `yes` | `posters no` turns off generated posters and backdrops for every collection. |

```ini
[global]
min_items          3
max_pages          50
list_refresh_days  14
```

---

## 3. Two ways to fill a collection

Every collection needs at least one **source**. There are two styles, and you choose per collection.

**Simple sources** are one key per line (`tmdb_keyword`, `tmdb_actor`, `release_years`, `mdblist`, `title`). They
combine by fixed rules (section 4). They are quick to write and cover most themes.

**[Fork addition]** **`match` expressions** are one or more `match` lines holding a boolean expression of selectors. You decide exactly
how things combine, and you can use Emby's own metadata (tags, genres, studios, people, years, ratings, parental
rating) as well as TMDb and MDBList.

Use simple sources when "everything with this keyword" or "everything on this list" is what you want. Use `match`
when you need **AND between different kinds of things** (an actor and a genre), **OR across kinds** (a TMDb keyword
or an Emby tag), **NOT** (anything except horror), or **anything from Emby's own metadata**.

A collection uses **one style or the other**, never both. A collection with a `match` line cannot also have
`tmdb_keyword`, `tmdb_actor`, `mdblist`, `release_years`, `title`, `exclude_genre` or `exclude_keyword` lines. Every
one of those has a selector that means the same thing inside the expression (see
[section 10](#10-converting-a-simple-collection-to-match)).

---

## 4. Simple sources

| Key | Example | Meaning |
|---|---|---|
| `tmdb_keyword` | `tmdb_keyword time travel` | Movies TMDb has tagged with this keyword (exact name, or the numeric id). |
| `tmdb_actor` | `tmdb_actor Tom Hanks` | Movies with this actor in the cast, according to TMDb. **[Fork addition]** With `tv yes`, also the shows they acted in (section 4.1). |
| `release_years` | `release_years 1980 1989` | All TMDb movies released in these years. Use with `min_rating` / `min_votes`. |
| `mdblist` | `mdblist k0meta/christmas-extravaganza` | A public list from mdblist.com (the `user/list` part of its address). |
| `title` | `title Star Trek` | Library titles whose name contains this text. No internet needed. |

Filters for simple sources:

| Key | Example | Meaning |
|---|---|---|
| `min_rating` | `min_rating 7.0` | TMDb average rating at least this (0-10). TMDb sources only. |
| `min_votes` | `min_votes 2000` | At least this many TMDb votes, a good way to keep only well-known titles. TMDb sources only. |
| `exclude_genre` | `exclude_genre Horror` | Leave out this genre, checked by TMDb and again against Emby's genres. Repeatable. |
| `exclude_keyword` | `exclude_keyword christmas` | Leave out titles with this TMDb keyword. Repeatable. TMDb sources only. |

How simple sources combine:

- **The same key several times means ANY.** Two `tmdb_keyword` lines give titles with either keyword.
- **Different TMDb keys narrow each other (AND).** `tmdb_keyword`, `tmdb_actor`, `release_years`, `min_rating` and `min_votes` go to TMDb as one search. `tmdb_keyword christmas` plus `release_years 1980 1989` means Christmas movies from the 80's.
- **`mdblist` and `title` add on top (OR).** Their titles join whatever the TMDb part found.
- Only the last `release_years` line counts.

These rules are fixed. If they are not what you want, use `match`.

### 4.1 Actors and TV shows

**[Fork addition]** In the original plugin `tmdb_actor` is movies only, and `tv yes` with it is an error.

`tmdb_actor` with `tv yes` includes the actor's TV shows as well as their movies:

```ini
[collection Sydney Sweeney]
tmdb_actor    Sydney Sweeney
tv            yes
poster_title  SYDNEY|SWEENEY
poster_accent 170,210,70
poster_tint   35,50,15
```

How the shows are found: TMDb's search can filter movies by actor but not shows, so the plugin reads the person's
**TMDb TV credits** instead (one extra request per actor). From those credits:

- appearances **as themselves** (a role of Self, Himself or Herself: talk shows, award shows, hosting) and talk, news and reality shows are left out, so the collection holds the shows they **acted** in;
- `release_years` is checked against each show's first air year, `min_rating` and `min_votes` against its TMDb rating and vote count, and `exclude_genre` against its TMDb TV genres (movie-only genres such as Horror are skipped there, but Emby's genres are still checked);
- a `tmdb_keyword` in the same collection keeps only the shows that also have that keyword (AND, as for movies);
- an `exclude_keyword` removes the shows that have that keyword.

As always, only the shows you own end up in the collection, matched by their TMDb id.

---

## 5. Boolean collections: the match key

**[Fork addition]** This whole section describes the fork's `match` key.

### 5.1 The idea

```ini
[collection Hanks at War]
match  actor:"Tom Hanks" AND (genre:War OR tmdb_keyword:"world war ii")
```

A `match` expression is built from **selectors** joined by **operators**.

- A **selector** is `field:value`, for example `tag:Christmas`, `actor:"Tom Hanks"` or `year:1980..1989`. Each selector finds a **set of titles in your library**.
- **`AND`** keeps the titles that are in both sets.
- **`OR`** takes the titles that are in either set.
- **`NOT`** takes every title in the library **except** those in the set.
- **Parentheses** `( )` group parts of the expression.

Because every selector becomes a set of your own titles, any selector can be combined with any other: a TMDb keyword
with an Emby tag, an MDBList list with a director, a year range with a parental rating.

### 5.2 Writing values

- A value **without spaces** is written as is: `genre:Western`, `rated:PG-13`, `mdblist:k0meta/christmas-extravaganza`.
- A value **with spaces or parentheses** goes in **double quotes**: `tag:"Family Night"`, `tmdb_keyword:"time travel"`. Forget the quotes and the plugin stops with an error saying so.
- There is **no space** between the field, the colon and the value: `tag:Christmas`, not `tag: Christmas`.
- Text values are **not case sensitive**: `tag:christmas` finds titles tagged `Christmas`.
- A value cannot contain a double quote, and cannot contain ` #` (that starts a comment).

### 5.3 Operators and precedence

- `AND`, `OR` and `NOT` may be written in any case (`and`, `Or`, `NOT`).
- Two selectors next to each other with no operator between them is an error. There is no implicit AND, so you always see what you meant.
- **NOT binds tightest, then AND, then OR.** So `tag:a OR tag:b AND tag:c` means `tag:a OR (tag:b AND tag:c)`, and `NOT genre:Horror AND tag:Christmas` means `(NOT genre:Horror) AND tag:Christmas`.
- When in doubt, add parentheses. They cost nothing and make the intent obvious.

### 5.4 Several match lines

Several `match` lines in one collection mean **ANY of them**, exactly as if joined with `OR`. That is the easy way to
write long expressions:

```ini
[collection Christmas Plus]
match  tag:Christmas AND NOT genre:Horror
match  tmdb_keyword:christmas AND year:1980..1989
match  mdblist:k0meta/christmas-extravaganza AND rating:7
```

That is the same as `(line 1) OR (line 2) OR (line 3)`.

### 5.5 What NOT means

`NOT x` is **every title in scope** minus the titles of `x`. The scope is all your movies, plus all your series when
the collection has `tv yes`. So:

- `NOT genre:Horror` on its own is every non-horror movie you own (probably not what you want).
- `tag:Christmas AND NOT genre:Horror` is the useful form: narrow first, then exclude.

### 5.6 Movies and series

Without `tv yes` a `match` collection looks at **movies only**. With `tv yes` it looks at movies **and series**, for
every selector. Use `type:movie` or `type:series` inside the expression to pick one kind for part of it:

```ini
[collection Star Trek Shows and Top Movies]
match  title:"Star Trek" AND (type:series OR rating:7)
tv     yes
```

---

## 6. Selector reference

**[Fork addition]** This whole section describes the fork's `match` key.

### 6.1 Emby metadata selectors

These read what Emby shows on each title's page. They need **no internet and cost no API requests**, and they are
evaluated fresh on every run. Edit a tag in Emby today, and the collection follows tonight.

| Selector | Example | Matches | Notes |
|---|---|---|---|
| `tag:` | `tag:"Family Night"` | Titles with this tag. | Exact tag, any case. Tags are yours to set, so this is the hand-picking selector. |
| `genre:` | `genre:Western` | Titles with this genre. | Emby's genres, which come from your metadata providers. |
| `studio:` | `studio:Pixar` | Titles from this studio. | As listed under Studios on the title's page. |
| `rated:` | `rated:PG-13` | Titles with this parental rating. | Exactly as Emby shows it: `PG-13`, `R`, `TV-MA`, `TV-14`, ... |
| `title:` | `title:"Star Trek"` | Titles whose name contains this text. | Part of the name is enough. `title:Alien` also finds Aliens. |
| `actor:` | `actor:"Tom Hanks"` | Titles with this person in the cast. | Includes guest stars, so it works for series. |
| `director:` | `director:"Christopher Nolan"` | Titles this person directed. | |
| `writer:` | `writer:"Aaron Sorkin"` | Titles this person wrote. | |
| `producer:` | `producer:"Kathleen Kennedy"` | Titles this person produced. | Only if your metadata lists producers. |
| `person:` | `person:"Clint Eastwood"` | This person in any role. | Actor, director, writer, producer, composer, ... |
| `year:` | `year:1985` | Production year. | See the number forms below. A bare number is that exact year. |
| `rating:` | `rating:7.5` | Community rating, 0-10. | A bare number means **at least**. |
| `critic:` | `critic:80` | Critic rating, 0-100. | A bare number means **at least**. |
| `type:` | `type:series` | `movie` or `series`. | `show` and `tv` also mean series. Only useful with `tv yes`. |

**Number forms** for `year:`, `rating:` and `critic:`:

| Form | Example | Means |
|---|---|---|
| bare number | `year:1985` | That exact year. For `rating:` and `critic:`: at least this. |
| range | `year:1980..1989` | From the first to the second, both included. |
| at least | `rating:>=7` | 7 or more. |
| at most | `year:<=1979` | 1979 or earlier. |
| more than | `critic:>90` | Strictly more than 90. |
| less than | `rating:<5` | Strictly less than 5. |
| exactly | `rating:=10` | Exactly this. |

A title with **no value** for the field (no year, no rating) never matches a number selector, including under `<`.

**People names** must be the name on the person's page in Emby (case does not matter). If Emby lists "Robert Downey
Jr.", then `actor:"Robert Downey Jr"` without the dot finds nothing, and the log warns you.

### 6.2 Online selectors

These download a list, so they need the internet (and, for TMDb, your token). The download is **saved and reused**
(see [section 11](#11-downloads-saved-lists-and-api-cost)).

| Selector | Example | Matches | Notes |
|---|---|---|---|
| `tmdb_keyword:` | `tmdb_keyword:"time travel"` | Titles TMDb tagged with this keyword. | Exact keyword name or numeric id. With `tv yes` also shows. |
| `tmdb_actor:` | `tmdb_actor:"Tom Hanks"` | Movies with this actor according to TMDb; with `tv yes` also the shows they acted in. | Shows come from TMDb TV credits (section 4.1). Name or numeric person id. |
| `mdblist:` | `mdblist:k0meta/christmas-extravaganza` | Titles on this public MDBList list. | Shows only with `tv yes`. |

`min_rating`, `min_votes` and `max_pages` in the collection are passed to TMDb for `tmdb_keyword:` and
`tmdb_actor:`, exactly as they are for simple sources.

**`actor:` or `tmdb_actor:`?** `actor:` uses Emby's own cast lists. It is free and instant, and sees exactly what
Emby sees. For series, though, Emby usually keeps only the main cast on the series itself (guest roles are on the
episodes), so `actor:` finds the shows where the person is a regular. `tmdb_actor:` asks TMDb and costs a download,
but with `tv yes` it reads the person's full TMDb TV credits, guest and recurring roles included. Use `actor:` for
speed and for anything Emby-specific, `tmdb_actor:` when you want every show they acted in, or combine them:
`actor:"Sydney Sweeney" OR tmdb_actor:"Sydney Sweeney"`.

**`year:` or a TMDb year search?** `year:1980..1989` is free and reads your library. There is no TMDb year selector
in `match`, and none is needed: narrow with `year:` instead.

---

## 7. Settings that work with both styles

| Key | Example | Meaning |
|---|---|---|
| `tv` | `tv yes` | Include series as well as movies. Default: movies only. |
| `season` | `season 10-01 10-31` | The collection exists only between these dates (month-day, both included), every year. May cross New Year: `season 12-26 01-04`. Outside the window it is deleted. |
| `min_items` | `min_items 5` | Overrides the global `min_items` for this collection. |
| `max_pages` | `max_pages 25` | Overrides the global `max_pages` for this collection's TMDb sources. |
| `min_rating` | `min_rating 7.0` | TMDb filter for TMDb sources (and `tmdb_keyword:` / `tmdb_actor:` selectors). |
| `min_votes` | `min_votes 500` | TMDb filter, same sources as `min_rating`. |

Note the difference between `min_rating 7` (a TMDb filter, applied while downloading, TMDb sources only) and the
selector `rating:7` (Emby's community rating, applied to every title in your library).

---

## 8. Poster and backdrop settings

Every collection gets a generated poster (a 3x3 mosaic of its best-rated titles with the collection's name) and a
backdrop. These keys change the look; changing them never causes a download.

| Key | Example | Meaning |
|---|---|---|
| `poster_title` | see below | The title text. A vertical bar starts a new line. The last line is white, earlier lines use the accent colour. |
| `poster_accent` | `poster_accent 90,220,255` | Accent colour as red,green,blue (0-255): bars and upper title lines. |
| `poster_tint` | `poster_tint 10,30,70` | The colour the mosaic is tinted with. Dark works best. |
| `poster_sepia` | `poster_sepia yes` | Old-photo look. |
| `posters` | `posters no` | No generated artwork for this collection. |

The line break in `poster_title` is the **vertical bar**, not a backslash:

```ini
poster_title  STAND-UP|COMEDY
```

The **Poster style...** button on the settings page lets you pick colours with a live preview and writes these lines
for you. A `match` collection that is not built yet (new, or out of season) is previewed with the best-rated movies
in your library; once it exists in Emby, the preview uses its own titles.

---

## 9. Cookbook

Each recipe is a complete collection you can paste and adapt. **[Fork addition]** Every recipe here uses a fork addition (`match`,
or `tmdb_actor` with `tv yes`); the original plugin's own recipes are in the main [README](../README.md#recipes-examples-explained).

### 9.1 A keyword or a tag

You have a TMDb keyword for a theme, plus your own Emby tag for titles TMDb misses.

```ini
[collection Stand-Up Comedy]
match         tmdb_keyword:"stand-up comedy" OR tag:"stand-up comedy"
poster_title  STAND-UP|COMEDY
poster_accent 150,205,255
poster_tint   20,45,80
```

### 9.2 Hand-picked by tag

Tag the titles in Emby (Edit metadata > Tags), and the collection follows every night.

```ini
[collection Friday Movie Night]
match  tag:"movie night"
```

### 9.3 An actor in a genre

```ini
[collection Hanks at War]
match  actor:"Tom Hanks" AND (genre:War OR tmdb_keyword:"world war ii")
```

The actor and the genre come from Emby for free, and only the keyword is downloaded.

### 9.4 A theme without the scary ones

```ini
[collection Family Christmas]
match   tag:Christmas AND NOT (genre:Horror OR rated:R)
season  12-01 12-31
```

### 9.5 Two directors

```ini
[collection Nolan and Villeneuve]
match  director:"Christopher Nolan" OR director:"Denis Villeneuve"
```

### 9.6 A studio's best of an era

```ini
[collection Classic Pixar]
match  studio:Pixar AND year:<=2010 AND rating:7.5
```

### 9.7 Best of a decade, from your own library

There is no download at all: Emby's own years and ratings are used.

```ini
[collection Top 80's]
match  year:1980..1989 AND rating:7.5
```

Compare with the simple-source version (`release_years 1980 1989` plus `min_rating` / `min_votes`), which uses TMDb's
ratings and vote counts. Use that one if you want "well known" (`min_votes`) rather than just "well rated".

### 9.8 A franchise across movies and series, with a quality bar for movies only

```ini
[collection Star Trek]
match  title:"Star Trek" AND (type:series OR rating:6.5)
tv     yes
```

### 9.9 A curated list, cleaned up

```ini
[collection Christmas]
match   mdblist:k0meta/christmas-extravaganza AND NOT genre:Horror
match   tag:Christmas
season  12-01 12-31
```

Everything on the community list except horror, plus anything you tagged yourself.

### 9.10 Family-friendly animation

```ini
[collection Kids Animation]
match  genre:Animation AND (rated:G OR rated:PG OR rated:TV-Y OR rated:TV-Y7 OR rated:TV-G)
tv     yes
```

### 9.11 An actor's movies and shows

```ini
[collection Sydney Sweeney]
tmdb_actor  Sydney Sweeney
tv          yes
```

Every movie and show she acted in that you own. The simple key is all you need here; see section 4.1 for how the
shows are found. The `match` form does the same and can be extended:

```ini
[collection Sydney Sweeney, no horror]
match  tmdb_actor:"Sydney Sweeney" AND NOT genre:Horror
tv     yes
```

### 9.12 A keyword with a TMDb quality bar

```ini
[collection Great Heists]
match      tmdb_keyword:heist AND NOT genre:Documentary
min_votes  1000
```

`min_votes` is passed to TMDb for the keyword, so only well-known heist movies are listed.

### 9.13 Critics' picks

```ini
[collection Critics' Picks]
match      critic:90 AND year:>=2000
min_items  5
```

---

## 10. Converting a simple collection to match

**[Fork addition]** This whole section describes the fork's `match` key.

Every simple-source key has a selector that means the same thing:

| Simple source | Inside match |
|---|---|
| `tmdb_keyword time travel` | `tmdb_keyword:"time travel"` |
| `tmdb_actor Tom Hanks` | `tmdb_actor:"Tom Hanks"` (or `actor:"Tom Hanks"` from Emby) |
| `mdblist user/list` | `mdblist:user/list` |
| `title Star Trek` | `title:"Star Trek"` |
| `release_years 1980 1989` | `year:1980..1989` (Emby's years, no download) |
| `exclude_genre Horror` | `AND NOT genre:Horror` |
| `exclude_keyword christmas` | `AND NOT tmdb_keyword:christmas` |
| two `tmdb_keyword` lines | `tmdb_keyword:a OR tmdb_keyword:b` |

Example, before:

```ini
[collection Winter Time]
tmdb_keyword    winter
tmdb_keyword    snow
exclude_genre   Horror
exclude_keyword christmas
season          12-01 02-29
```

After, the same collection plus titles you tagged `winter`:

```ini
[collection Winter Time]
match   (tmdb_keyword:winter OR tmdb_keyword:snow OR tag:winter) AND NOT (genre:Horror OR tmdb_keyword:christmas)
season  12-01 02-29
```

Two small differences to know about:

- A simple collection sends everything to TMDb as **one** search. In `match`, each online selector is downloaded **on its own** and combined in your library. The result is the same for keywords; it can differ slightly for `exclude_genre`, which in a simple collection is applied by TMDb's genres **and** Emby's, while `NOT genre:` uses Emby's genres only.
- The first run after converting downloads each online selector once, into its own saved list. The collection's old saved list is removed on the same run.

---

## 11. Downloads, saved lists and API cost

- **[Fork addition]** **Emby selectors** (`tag:`, `genre:`, `actor:`, `year:`, ...) never download anything. They are checked against your library on every run.
- **[Fork addition]** **Online selectors** are downloaded once and saved in the plugin's data folder under `lists/match/`, one file per selector. A saved list is reused until it is `list_refresh_days` old (default 7), or until you tick **Download all lists again** on the settings page.
- A saved list belongs to the selector **and** the settings that change what is downloaded: `tv`, `max_pages`, `min_rating`, `min_votes`. Two collections that use `tmdb_keyword:christmas` with the same settings **share one download**. Change one of those settings and that selector is downloaded again.
- Changing the expression around a selector (adding `AND NOT genre:Horror`, say) costs **no** download: only selectors that are new or whose settings changed are fetched.
- Saved lists that no collection uses any more are deleted after a full run (not when **Only these collections** is set, since then the other collections only look missing).
- Simple-source collections keep their saved lists in `lists/` exactly as before.

---

## 12. Testing a collection safely

1. Tick **Dry run** and **Log the matching titles**.
2. Put the collection's name in **Only these collections**, so the run touches nothing else.
3. Save, then run **Dashboard > Scheduled Tasks > Ultimate Collections > Sync collections**.
4. Read the log (`logs/YYYY-MM-DD.log` in the plugin's data folder; example below).
5. When the titles are right, untick **Dry run**, clear **Only these collections**, Save, and run again.

**[Fork addition]** For a `match` collection the log shows what each selector found, then the result. (Each title in the real log is
followed by its Emby, TMDb and IMDb ids; they are left out here.)

```
[Hanks at War] actor:"Tom Hanks": 14 in the library
[Hanks at War] genre:War: 63 in the library
[Hanks at War] tmdb_keyword:"world war ii": 41 in the library
[Hanks at War] matches: Greyhound (2020); Saving Private Ryan (1998)
[Hanks at War] [dry-run] create with 2 items (list has 118, match: tmdb_keyword:"world war ii" downloaded (new))
```

The per-selector counts are the quickest way to debug: a selector at 0 is usually a spelling difference from what
Emby shows.

---

## 13. Errors and warnings explained

Errors in the settings stop the whole run before anything changes. Errors in one collection leave **that**
collection unchanged and let the others sync.

Messages that begin `line N: match:`, the `year:` and `type:` messages, the "has 'match' and also" message, and the
"matches nothing in the library" and "returned nothing" messages come from the fork's `match` key. The others are
the original plugin's.

| Message | Cause and fix |
|---|---|
| `line N: match: unknown selector 'x:'` | A misspelt field. The message lists the valid ones. |
| `line N: match: unexpected word 'travel' ... needs quotes` | A value with a space is missing its quotes: `tmdb_keyword:"time travel"`. |
| `line N: match: unexpected 'tag:b' ... (missing AND / OR?)` | Two selectors with no operator between them. |
| `line N: match: '(' at column C is never closed` | A missing `)`. |
| `line N: match: unexpected ')' ... (no matching '(')` | One `)` too many. |
| `line N: match: the expression ends too early` | The line ends after `AND`, `OR`, `NOT` or `(`. |
| `line N: match: the quote after 'x:' ... is never closed` | A missing closing `"`. |
| `year: needs a number` | A number selector got text: write `year:1980..1989`, `rating:>=7`. |
| `type: is movie or series` | `type:` takes `movie` or `series` (or `show` / `tv`). |
| `collection 'X' has 'match' and also tmdb_keyword` | Mixed styles. Move the simple source into the expression (section 10). |
| `collection 'X' has no source` | A collection with neither a simple source nor a `match` line. |
| `[X] tag:"..." matches nothing in the library (check the spelling)` | A warning only. The value differs from what Emby shows, or no title has it yet. |
| `[X] ERROR, collection left unchanged: mdblist:... returned nothing` | An online list came back empty: probably removed, renamed or unreachable. The collection is kept as it was. |
| `[X] ERROR: sources returned nothing, collection left unchanged` | Every selector matched nothing at all, which looks broken rather than empty, so the collection is kept. |
| `[X] only N of the M listed items are in the library (< min_items K)` | The expression matched too few titles; the collection is not created, or is removed. |
| `TMDb keyword 'x' not found (close: ...)` | The keyword name must match TMDb exactly; pick one of the suggested names or use the numeric id. |

---

## 14. Quick reference card

```ini
[global]
min_items          2          # collections smaller than this are not made
max_pages          100        # TMDb pages (20 titles) per TMDb source
list_refresh_days  7          # re-download lists after this many days

[collection Name]
# --- either simple sources (repeat = ANY) ---
tmdb_keyword    <keyword>
tmdb_actor      <name>
release_years   <from> <to>
mdblist         <user>/<list>
title           <text>
exclude_genre   <genre>
exclude_keyword <keyword>
# --- or match expressions (repeat = ANY)  [fork addition] ---
match   <selector> AND / OR / NOT <selector> ... ( ) for grouping
#   Emby:   tag: genre: studio: rated: title: actor: director: writer: producer: person:
#           year:1985  year:1980..1989  year:>=2000  rating:7  rating:<5  critic:80  type:movie|series
#   Online: tmdb_keyword: tmdb_actor: mdblist:
#   Quote values with spaces: tag:"Family Night"
# --- both styles ---
tv          yes
season      MM-DD MM-DD
min_items   <n>
max_pages   <n>
min_rating  <0-10>            # TMDb filter for TMDb sources
min_votes   <n>               # TMDb filter for TMDb sources
# --- poster ---
poster_title   LINE1|LINE2
poster_accent  r,g,b
poster_tint    r,g,b
poster_sepia   yes
posters        no
```
