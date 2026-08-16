# Changelog

Short notes of what changed. Newest first.

Code commits stay detailed. This file is the at-a-glance list.

## Unreleased

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
