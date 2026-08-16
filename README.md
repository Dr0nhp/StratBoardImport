# StratBoardImport

Dalamud plugin for Final Fantasy XIV. Imports long Strategy Board share codes (`[stgy:...]`) that the vanilla import field truncates.

In-game command: `/sbi`

## Repository name

Use **`StratBoardImport`**.

It matches the plugin’s internal name, is easy to search, and reads clearly as an FFXIV/Dalamud project. `solid-palm-tree` is only GitHub’s placeholder name.

Rename on GitHub: **Settings → General → Repository name → `StratBoardImport`**. After that, the install URL becomes:

```text
https://github.com/Dr0nhp/StratBoardImport/releases/latest/download/pluginmaster.json
```

Until you rename it, use the current URL below.

## Install from GitHub

Dalamud cannot install from `https://github.com/USER/REPO`. Add this **custom plugin repository** URL instead:

```text
https://github.com/Dr0nhp/solid-palm-tree/releases/latest/download/pluginmaster.json
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
git tag v1.0.0.1
git push origin v1.0.0.1
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
- Several `[stgy:]` codes: import each page separately and reopen the share-code dialog in between

If the plugin cannot find the input field, leave the share-code window open and enable the addon scan under **Settings**.
