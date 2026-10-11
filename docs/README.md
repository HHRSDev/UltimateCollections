# Ultimate Collections: Documentation

> Ultimate Collections was created by **jdwaymire (WaymireNET)** ([original project](https://github.com/jdwaymire/UltimateCollections)). The documents in this folder were written for the **HHRSDev fork** and describe the changes it makes on top of the original.

The project's documents. Each `.md` file is the source; the `.htm` beside it is generated for reading in a browser
by `Convert-MdToHtm.ps1` (run it after editing any `.md`). `index.htm` lists every page and resource in this folder.

| Document | What it is |
|---|---|
| [Configuration Guide](Configuration-Guide.md) | Everything you can write in the Collections settings: the file format, simple sources, boolean `match` expressions, every selector, actors with their TV shows, a cookbook, conversions, testing and error messages. Start here to write collections. |
| [Fork Changes](Boolean-Match.md) | The write-up of the fork's changes, starting with the `match` key and the Emby metadata selectors: design, files changed, backwards compatibility, testing, known limits. Its addendum covers `tmdb_actor` with `tv yes` (an actor's TV shows from their TMDb TV credits). |

The main [README](../README.md) at the repository root covers installation, the settings page, posters, the nightly
sync and troubleshooting.
