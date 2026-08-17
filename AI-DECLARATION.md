---
version: "0.1.2"
level: copilot
processes:
  design: pair
  implementation: copilot
  testing: pair
  documentation: copilot
  review: assist
components:
  StratBoardImport/: copilot
  StratBoardImport/Localization/Translations/en-UK.json: pair
  StratBoardImport/Localization/Translations/de-DE.json: pair
  StratBoardImport/Localization/Translations/fr-FR.json: copilot
  StratBoardImport/Localization/Translations/ja-JP.json: copilot
---

This format is based on [AI-DECLARATION.md](https://ai-declaration.md/en/0.1.2/).

## Notes

- Cursor (Grok) implemented most of the C#. I defined behaviour (import, Saved List limits, Library tab, JSON packs, versioning), tested in FFXIV, and iterated with developer logs until folder import and delete matched the game.
- Overall level is **copilot**, not **auto**: I directed the work and can explain the Tofu create/copy/delete path and the library storage. The long Saved List / folder-index debug loop was closer to **pair**.
- English and German UI strings were reviewed by me. French and Japanese are AI-drafted placeholders.
- No AI-generated plugin icon.
- This also covers Dalamud’s [AI usage policy](https://dalamud.dev/plugin-publishing/ai-policy/) if the plugin is submitted to the official repository.
