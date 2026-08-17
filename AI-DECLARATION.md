---
version: "0.1.2"
level: copilot
---

[![AI-DECLARATION: copilot](https://img.shields.io/badge/䷼%20AI--DECLARATION-copilot-fee2e2?labelColor=fee2e2)](https://ai-declaration.md)

This format is based on [AI-DECLARATION.md](https://ai-declaration.md/en/0.1.2/).

## Processes

| Process | Level |
| --- | --- |
| design | pair |
| implementation | copilot |
| testing | pair |
| documentation | copilot |
| review | assist |

## Components

| Path | Level |
| --- | --- |
| `StratBoardImport/` | copilot |
| `StratBoardImport/Localization/Translations/en-UK.json` | pair |
| `StratBoardImport/Localization/Translations/de-DE.json` | pair |
| `StratBoardImport/Localization/Translations/fr-FR.json` | copilot |
| `StratBoardImport/Localization/Translations/ja-JP.json` | copilot |

## Notes

- Cursor wrote most of the C#. I set the behaviour (import, Saved List limits, Library tab, JSON packs, versioning), tested in FFXIV, and used developer logs until folder import and delete matched the game.
- Overall level is **copilot**, not **auto**. I directed the work and can explain the Tofu create/copy/delete path and the library storage. The Saved List / folder-index debug loop was closer to **pair**.
- English and German UI strings were reviewed by me. French and Japanese are AI-drafted placeholders.
- No AI-generated plugin icon.
- This also covers Dalamud’s [AI usage policy](https://dalamud.dev/plugin-publishing/ai-policy/) if the plugin is submitted to the official repository.
