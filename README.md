# StratBoardImport

[![AI-DECLARATION: copilot](https://img.shields.io/badge/䷼%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](./AI-DECLARATION.md)

Dalamud plugin for Final Fantasy XIV. Display name: **Strategy Board Plus**. Internal name stays `StratBoardImport` so Dalamud updates and config keep working.

Imports long Strategy Board share codes (`[stgy:...]`) that the vanilla import field truncates, and keeps extra boards in a plugin library beyond the game’s 50-slot Saved List.

In-game command: `/sbi`

See **[CHANGELOG.md](CHANGELOG.md)** for what changed in each version.

## Versioning

Plugin versions are **`Y.X.A.B`**.

| Part | Bump when |
|------|-----------|
| **Y** | Breaking change or a new Dalamud API |
| **X** | A real user-facing feature (Library tab, native import, …) |
| **A** | Bugfix, polish, or debug tools |
| **B** | Same bits rebuilt (usually `0`) |

Example: **1.6.1.0** is Dalamud API 1, feature 6 (Library), first bugfix of that feature.

## Branches and releases

| Branch | Role |
|--------|------|
| **`stable`** | Last version we called good. Protect this on GitHub. Installers that use `releases/latest` track this. |
| **`dev`** | Development and betas. All new work and bug hunts land here. |

Do not use **`main`**. Leave it frozen. On GitHub, set the default branch to **`dev`**.

After a bugfix on `dev` is proven in game, merge it into **`stable`**. Unfinished work stays on `dev` and ships as **beta**.

| Channel | Git tag | GitHub release | Dalamud custom repo URL |
|---------|---------|----------------|-------------------------|
| **Stable** | `v1.6.1.0` | Latest (not a pre-release) | `https://github.com/Dr0nhp/StratBoardImport/releases/latest/download/pluginmaster.json` |
| **Beta** | `v1.6.2.0-beta` | Pre-release, also copied to tag `beta` | `https://github.com/Dr0nhp/StratBoardImport/releases/download/beta/pluginmaster.json` |

Beta tags must contain `-beta` so they never replace GitHub’s “latest” stable release.

## Why this plugin exists

FFXIV Strategy Boards can be shared as `[stgy:...]` text codes. A single page is usually fine. A **folder with several pages** produces a much longer string.

The game’s share-code field has a length limit. Paste a long folder code and the client **cuts it off**. The import then fails, or only the first page comes through.

This plugin writes boards **straight into the Saved List**, so that length limit does not apply. Several pages go into a named folder (10 boards per folder; extras into `Name (2)`, `Name (3)`, …).

The **Library** tab shows the Saved List as a tree. Check boards to delete them in game, copy them into the plugin (unlimited attic), send them back, or export JSON for backup and websites.

The game also has **no bulk delete**. After a bad import, or when you want a clean slate, the plugin can wipe every saved board and folder in two clicks. The Shared List is left alone.

## Share code resources

These sites publish or generate `[stgy:...]` codes. Folder bundles from here are often too long for the vanilla import field — that is what this plugin is for.

```text
strategy boards
├── collections (ready-made fights / folders)
│   ├── board.wtfdig.info     view, vote, and bundle share codes
│   ├── wtfdig.info           fight pages that link out to boards
│   ├── ffxivstrats.io        community strategies and collections
│   ├── evyx.gg               video guides with in-game board collections
│   └── aetherdraw.me         live whiteboard + searchable plan hub
├── editors & viewers
│   ├── xivstrat.app          draw boards and export in-game codes
│   ├── stgy.m4e.dev          viewer / editor for [stgy:] strings
│   └── ennea viewer          paste a code and preview it in the browser
└── convert from older planners
    ├── raidplan.io           classic raid diagrams (not native stgy)
    └── board.wtfdig.info     import: raidplan.io → share codes
```

- [board.wtfdig.info](https://board.wtfdig.info/) — view, bundle, and convert Strategy Board codes
- [WTFDIG](https://wtfdig.info/) — current savage / ultimate / extreme helpers and board links
- [FFXIVstrats](https://ffxivstrats.io/) — community strategy and collection pages
- [Evyx](https://evyx.gg/) — fight guides with copyable Strategy Board collections
- [AetherDraw](https://aetherdraw.me/) — collaborative planner with a public plan search
- [XIVStrat / XIVPlan](https://xivstrat.app/) — web editor with export to in-game share codes
- [stgy-tools](https://stgy.m4e.dev/) — browser viewer and editor for `[stgy:]` strings
- [Ennea Strategy Board Viewer](https://ennea.github.io/ffxiv-strategy-board-viewer/) — paste a code to preview
- [raidplan.io](https://raidplan.io/) — older raid planner; convert via WTFDIG’s import tool

## Install from GitHub

Dalamud cannot install from `https://github.com/USER/REPO`. Add a **custom plugin repository** URL.

**Stable** (default — last good build):

```text
https://github.com/Dr0nhp/StratBoardImport/releases/latest/download/pluginmaster.json
```

**Beta** (development builds; may be unfinished):

```text
https://github.com/Dr0nhp/StratBoardImport/releases/download/beta/pluginmaster.json
```

Use one of those URLs, not both, unless you know you want two entries.

1. In game, open `/xlsettings`
2. Go to **Experimental**
3. Under **Custom Plugin Repositories**, paste the URL and confirm with **+**
4. Save
5. Open `/xlplugins`, search for **Strategy Board Plus**, and install

The GitHub repository must be **public**. Dalamud cannot download a zip from a private repo.

`pluginmaster.json` and `latest.zip` are attached automatically when a `v*` tag is pushed.

## Create a new release

Stable — merge proven `dev` into `stable`, then tag without `-beta`:

```powershell
git checkout stable
git merge dev
git push origin stable
git tag v1.6.2.0
git push origin v1.6.2.0
```

Beta — tag from `dev`:

```powershell
git checkout dev
git tag v1.6.2.0-beta
git push origin v1.6.2.0-beta
```

GitHub Actions builds the plugin. Stable tags update `releases/latest`. Beta tags update the moving `beta` release.

## Local / dev install

Requires .NET SDK 10 and Dalamud API 15.

```powershell
dotnet build StratBoardImport.sln -c Release
```

Then `/xlsettings` → **Experimental** → **Dev Plugin Locations**, and add:

```text
StratBoardImport/bin/x64/Release/StratBoardImport.dll
```

## Usage

1. `/sbi` opens the plugin window.
2. Paste a share code (or use **From clipboard**).
3. Click **Import**. Boards are written into the Saved List (Tofu). If Strategy Board is already open, it reloads on its own.
4. Several `[stgy:]` pages: set a folder name and **Import all boards into a folder**.

- **Import raw input** still fills the in-game share-code field (for the vanilla dialog).

### Folder import (several `[stgy:]` codes)

1. Paste all page codes and click **Check**.
2. Set a folder name (default: first board name).
3. Click **Import all boards into a folder**. The plugin creates the folder if needed and writes every page into the Saved List.

Game limits: **50** boards in the Saved List in total. A folder holds **10** boards; extra pages go into `Name (2)`, `Name (3)`, and so on. Shared List: **20** temporary boards.

**Delete all saved boards** (click twice) clears the Saved List, including folders. The Shared List is not touched.

If direct import is unavailable, the older share-code window flow is used. Language and chat messages can be set under **Settings** (`auto` follows the game client).

### Library

1. Open the **Library** tab.
2. Check folders or boards in **Saved List**.
3. **Save to plugin** copies them onto disk (Dalamud config folder). The game list is unchanged.
4. **Delete in game** removes checked Saved List rows. A named folder is only deleted if you check the folder (or every board in it).
5. Check items under **Plugin library**, then **Send to game** (still 50 / 10 limits), **Remove from library**, or **Export JSON** to the clipboard.
6. **Import JSON** reads a pack from the clipboard into the plugin. It does not write to the game until you send it.

## Translations

In-game UI, chat, and errors are key-value JSON in `StratBoardImport/Localization/Translations/` (`en-UK`, `de-DE`, `fr-FR`, `ja-JP`). Keys are constants in `L.cs`. Add a new file to add a language.
