# Player Portraits Framework

A Stardew Valley framework mod that draws a **player portrait** in the dialogue box, opposite the NPC you're talking to — turning ordinary conversations into a two-portrait, visual-novel-style layout.

This is a **framework**. On its own it does nothing visible — it needs a **player portrait pack** installed to supply the artwork. Think of it like Content Patcher: the engine is here, the content comes separately.

---

## What it does

- Draws a player portrait pinned to the dialogue box, mirrored opposite the NPC's portrait.
- Lets the NPC portrait, name, hearts, and dialogue box keep rendering through Dialogue Display Framework Continued (DDFC) — the framework only adds the player portrait and controls layout geometry.
- Scales the whole layout to the player's resolution.
- Supports **animated** player portraits (sprite-sheet strips).
- Matches the player's **expression to the NPC's**, by reading the NPC's live portrait index while the dialogue is on screen — so it works with any NPC and any NPC-portrait mod, with no per-character setup.
- Keeps both portraits on screen through **player-choice boxes**, so the conversation stays visual at the moment you're picking a reply. Can be switched off.
- Adds an in-game config menu (via Generic Mod Config Menu) for pack selection, portrait scale/offset, box height, portraits during choices, and a name-plate toggle.

### Pack modes

A pack picks exactly one of three shapes:

| Mode | Shape | Use it for |
|---|---|---|
| **Simple** | One image (static or animated). | A single player face, no emotion awareness. This is the original v1 behaviour — existing packs keep working unchanged. |
| **Emotion, static** | One sheet carrying every emotion slot in a grid. | Emotion matching without animation. The sheet is the costume unit: one Content Patcher condition swaps all the emotions at once (a winter outfit, a higher heart level). |
| **Emotion, animated** | One file per emotion, each with its own animation. | Emotion matching where each expression animates at its own length — a 4-frame blink for neutral, a 2-frame bounce for happy. |

Content Patcher decides *which art is loaded* (any `When` condition you like); the framework decides *which slot is on screen*. The two layer rather than compete.

---

## Requirements

- [SMAPI](https://www.nexusmods.com/stardewvalley/mods/2400)
- [Content Patcher](https://www.nexusmods.com/stardewvalley/mods/1915)
- [Dialogue Display Framework Continued (DDFC)](https://www.nexusmods.com/stardewvalley/mods/11661)
- A **player portrait pack** (the actual artwork — install at least one)
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) — *optional*, for the in-game settings menu

---

## Installation (players)

1. Install SMAPI, Content Patcher, and DDFC.
2. Download the latest release of Player Portraits Framework from the [Releases](../../releases) page and unzip it into your `Mods` folder.
3. Install a player portrait pack the same way.
4. Launch the game through SMAPI. Open any dialogue — you should see a player portrait appear opposite the NPC.
5. (Optional) With GMCM installed, adjust settings from the in-game options menu.

---

## For pack authors

Player portrait packs are normal Content Patcher content packs — no C# required. They load a portrait texture and register it with the framework. See the **Pack Author Guide** included in this repository for the full registration schema, animation format, the three pack modes, and author-default fields.

---

## Compatibility

- **Desktop only** for now. Android is not currently supported.
- **Not compatible with the standalone DDFCScaler mod** — both control dialogue box and portrait geometry, so they will fight. Disable DDFCScaler if it's installed. (The framework logs a warning on startup if it detects DDFCScaler loaded.)
- Other DDFC-based portrait mods (HD NPC portraits, etc.) work fine — the framework only adds the player portrait and does not touch NPC portrait artwork.

---

## Credits

- **SerDakota** — design, content, testing
- Built on [Dialogue Display Framework Continued](https://www.nexusmods.com/stardewvalley/mods/11661) by Mangupix
