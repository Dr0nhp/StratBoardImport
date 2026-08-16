# Strategy Board Import

Dalamud-Plugin für Final Fantasy XIV. Importiert **lange Strategy-Board-Share-Codes** (`[stgy:...]`), die das normale Importfeld abschneidet.

Befehl im Spiel: `/sbi`

## Installation über GitHub

Den normalen Repository-Link (`https://github.com/Dr0nhp/solid-palm-tree`) kann Dalamud nicht laden. Stattdessen diese URL als **Custom Plugin Repository** eintragen:

```text
https://github.com/Dr0nhp/solid-palm-tree/releases/latest/download/pluginmaster.json
```

1. Im Spiel `/xlsettings` öffnen
2. Tab **Experimental**
3. Unter **Custom Plugin Repositories** die URL einfügen und mit **+** bestätigen
4. Speichern
5. `/xlplugins` öffnen, nach **Strategy Board Import** suchen und installieren

Die Datei `pluginmaster.json` und `latest.zip` entstehen automatisch, sobald ein GitHub-Release mit Tag `v*` existiert (zum Beispiel `v1.0.0.0`).

## Neues Release erzeugen

```powershell
git tag v1.0.0.1
git push origin v1.0.0.1
```

GitHub Actions baut das Plugin und hängt `latest.zip` plus `pluginmaster.json` an das Release.

## Installation als Dev Plugin (lokal)

```powershell
dotnet build StratBoardImport.sln -c Release
```

Danach `/xlsettings` → **Experimental** → **Dev Plugin Locations** und diese DLL eintragen:

```text
StratBoardImport/bin/x64/Release/StratBoardImport.dll
```

## Bedienung

1. `/sbi` öffnet das Plugin-Fenster.
2. Share-Code einfügen (oder **Aus Zwischenablage**).
3. Im Spiel: **Strategy Board → Neue Strategie → Share-Code**.
4. Im Plugin **Importieren** klicken.
5. Im Spiel auf OK / Übernehmen.

- Ein langer Ordner-Code: **Roheingabe importieren**
- Mehrere `[stgy:]`-Codes: Seiten einzeln importieren, Dialog dazwischen neu öffnen

Falls das Eingabefeld nicht gefunden wird: Share-Code-Fenster offen lassen und unter **Einstellungen** den Addon-Scan aktivieren.
