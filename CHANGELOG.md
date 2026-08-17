# Changelog

Short notes of what changed. Newest first.

Code commits stay detailed. This file is the at-a-glance list.

## Unreleased

- Change - Dalamud installer uses the SB+ drawing as plugin icon and cover image
- Docs - Versioning (`Y.X.A.B`), `stable` / `dev` branches, and a separate beta install URL
- Docs - `main` is frozen; development and betas use `dev`

## 1.6.1.0

- Bugfix - Library tree groups boards by folder identity, not UI row, so M9S / M9S (2) show the right pages
- Bugfix - Folder import copies into the named folder that still has space when another folder is first in the list
- Bugfix - Delete in game finds the mixed-list row by folder index after the list shrinks
- Bugfix - Library tree hover no longer covers the checkbox

## 1.6.0.0

- Feature - Library tab: checkbox tree of the Saved List and a plugin board attic
- Feature - Save in-game boards to the plugin, send them back, delete checked rows in game
- Feature - Export / import JSON packs for backup and websites
- Change - Display name is Strategy Board Plus (internal name unchanged so updates keep working)

## 1.5.2.0

- Bugfix - Folder import removes the root duplicate by deleting the mixed-list board row, not the copy inside the folder

## 1.5.1.0

- Bugfix - Folder import copies into the named folder; CreateBoard no longer spawns an empty folder per page

## 1.5.0.0

- Feature - Settings include a developer log you can copy after an import
- Bugfix - Folder import writes boards into the named folder instead of copying and leaving root duplicates

## 1.4.1.0

- Bugfix - Folder import no longer deletes the folder (and its boards) after each page
- Bugfix - Status text follows a language change
- Change - Hover help wraps; parsed-code rows no longer show the raw share code

## 1.4.0.0

- Bugfix - Folder import no longer leaves a second copy of each board in the Saved List root
- Change - Plugin window uses Import / Settings tabs, one primary import button, and hover help

## 1.3.1.0

- Bugfix - Strategy Board reloads itself after import or delete if the window is already open
- Change - Settings keep language and chat messages (auto-confirm, callback ID, and addon scan removed)
- Docs - README covers Saved List wipe

## 1.3.0.0

- Bugfix - Folder import no longer duplicates boards in the Saved List root after a folder fills up
- Change - A folder still holds 10 boards; extra pages go into `Name (2)`, `Name (3)`, …
- Feature - Button to delete all Saved List boards and folders (click twice to confirm)

## 1.2.0.0

- Feature - Import writes boards into the Saved List directly (no share-code window per page)
- Bugfix - Auto-confirm now sends the share code with the OK callback

## 1.1.2.0

- Change - Settings now include a short explanation under each option

## 1.1.1.0

- Feature - Option to hide plugin messages in chat

## 1.1.0.0

- Feature - In-game text is now in translation files (`de-DE`, `en-UK`, `fr-FR`, `ja-JP`)
- Bugfix - Folder import waited forever because the share-code window was not recognised
- Change - Saved List cap is total boards; folders have no separate 10-board limit

## 1.0.3.0

- Change - All in-game plugin text is English

## 1.0.2.0

- Change - Plugin installer text is English (was German)
- Docs - Added this changelog
- Docs - Added evyx.gg to the share-code resource list

## 1.0.1.0

- Bugfix - Parser was broken; valid in-game `[stgy:]` codes failed the checksum
- Feature - Import several share codes one after another (folder import)
- Change - Import no longer stops at 10 boards (Saved List holds 50; a folder still holds 10)
- Docs - Folder import steps and game limits

## 1.0.0.0

- Feature - First release: write long share codes into the in-game import field
- Feature - Install from a Dalamud custom plugin repo (GitHub Releases)
