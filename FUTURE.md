# Strategy Board Plus — future work

Folder import is stable. The Library tab in **1.6.0.0** is the first product slice.

## Project

- **Repo / internal name:** `StratBoardImport` (keep this so Dalamud updates and config still work)
- **Display name:** Strategy Board Plus
- **Command:** `/sbi` until we decide otherwise
- **Game:** FFXIV Strategy Boards via Dalamud

## Versioning (`Y.X.A.B`)

| Part | Bump when |
|------|-----------|
| **X** | A real user-facing feature |
| **A** | Bugfix, polish, or debug tools |
| **B** | Same bits rebuilt (usually `0`) |
| **Y** | Breaking / Dalamud API change |

## Shipped in 1.6.0.0

- Library tab with checkbox tree (Saved List + plugin packs)
- Delete checked boards in game (named folders only if the folder is checked)
- Save to plugin / send to game / remove from library
- JSON export and import on the clipboard
- Display name Strategy Board Plus

## Still open

- Plugin icon (square, readable at Dalamud installer size)
- One global library vs per-character
- Extra command `/sbp`, or only `/sbi`
- Wipe-all Saved List on Library as well as Import
- Library search / filter
- Import JSON from a URL for raid groups

## Storage

Dalamud config directory on disk. Settings stay in the plugin config JSON. Board packs live in `library.json` next to it.

## JSON pack format

```json
{
  "format": "strategy-board-plus-pack",
  "version": 1,
  "name": "M9S memes",
  "description": "",
  "folders": [
    {
      "name": "M9S",
      "boards": [
        {
          "name": "Vamp Stomp 1 - CE",
          "code": "[stgy:...]"
        }
      ]
    }
  ]
}
```

Root boards in a pack use a top-level `boards` array.

## When implementing a slice

One concern per change. Feature bump only for user-facing work. No feature bump for debug or polish.
