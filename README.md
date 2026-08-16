# StratBoardImport

Dalamud plugin for Final Fantasy XIV. Imports long Strategy Board share codes (`[stgy:...]`) that the vanilla import field truncates.

In-game command: `/sbi`

See **[CHANGELOG.md](CHANGELOG.md)** for what changed in each version.

## Why this plugin exists

FFXIV Strategy Boards can be shared as text codes. A single page is usually fine. A **folder with several pages** produces a much longer import string.

The game’s share-code field has a length limit. Paste a long folder code and the client **cuts it off**. The import then fails, or only the first page comes through.

This plugin takes the full import string and writes it into the in-game field, so the code is not truncated. You get the complete board or folder instead of a broken partial import.

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

Dalamud cannot install from `https://github.com/USER/REPO`. Add this **custom plugin repository** URL instead:

```text
https://github.com/Dr0nhp/StratBoardImport/releases/latest/download/pluginmaster.json
```

1. In game, open `/xlsettings`
2. Go to **Experimental**
3. Under **Custom Plugin Repositories**, paste the URL and confirm with **+**
4. Save
5. Open `/xlplugins`, search for **Strategy Board Import**, and install

The GitHub repository must be **public**. Dalamud cannot download a zip from a private repo.

`pluginmaster.json` and `latest.zip` are attached automatically when a `v*` tag is pushed (for example `v1.0.0.0`).

## Create a new release

```powershell
git tag v1.0.2.0
git push origin v1.0.2.0
```

GitHub Actions builds the plugin and attaches `latest.zip` plus `pluginmaster.json` to the release.

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
3. In game: **Strategy Board → New Strategy → Share Code**.
4. Click **Import** in the plugin.
5. Confirm with OK / Apply in the game window.

- One long folder code: **Import raw input**
- Several `[stgy:]` codes: use **Import all boards into folder**

### Folder import (several `[stgy:]` codes)

The game has no public API to create folders or assign boards. The plugin assists the in-game flow instead:

1. Paste all page codes and click **Check**.
2. Set a folder name (default: first board name). Starting the job copies that name to the clipboard.
3. In game, create or open that folder.
4. Open **New Strategy → Share Code**. The plugin fills the next code and confirms it.
5. Repeat step 4 for each remaining page.

Game limits: **50** boards in the Saved List, **10** per folder, **20** temporary in the Shared List. Imports larger than 10 still run; split them across folders or leave extras in the Saved List. If boards land in the main list, drag them into a folder.

If the plugin cannot find the input field, leave the share-code window open and enable the addon scan under **Settings**.
