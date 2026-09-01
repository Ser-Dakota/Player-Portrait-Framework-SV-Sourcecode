# Player Portraits Framework V2 — NPC Emotion Matching — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the player portrait match the NPC's live expression by reading the NPC's resolved portrait index at draw time and drawing the corresponding player emotion slot.

**Architecture:** A pack declares exactly one of three modes. Mode 1 (Simple) is V1 verbatim. Mode 2 (Emotion, static) draws cell N out of one grid sheet. Mode 3 (Emotion, animated) draws one file per emotion, each with its own animation. Content Patcher decides *which art is loaded* (via `When` conditions on the Load targets); the framework only decides *which slot is on screen right now*. All emotion math is extracted into two pure, game-independent classes (`EmotionResolver`, `AnimationCursor`) so the acceptance-critical logic is unit-testable without the game running; the Harmony patches stay thin wiring.

**Tech Stack:** C# / .NET 6 (`net6.0`), SMAPI 4.x, HarmonyLib, Content Patcher 2.x, Dialogue Display Framework Continued (DDFC), MonoGame (`Microsoft.Xna.Framework`). Tests: xUnit on `net10.0` (test-only project, never shipped).

## Global Constraints

- **Mode 1 packs must behave identically to V1.** No change to `Portrait` / `Animation` semantics, the `Custom/<packId>/PlayerPortrait` asset target, or the existing draw math. This is the single highest-priority constraint — a V1 pack that renders differently after this work is a failed implementation.
- **A pack is exactly one mode.** Modes are mutually exclusive. Mixed static+animated within one pack is explicitly out of scope.
- **Emotion index source:** the NPC's *resolved integer* portrait index, read live at draw time via `dialogueBox.characterDialogue.getPortraitIndex()`. Never parse `$h` / `$s` dialogue tokens — those are vanilla dialogue syntax the game has already converted to an integer before the framework sees it.
- **Default emotion mapping is 1:1** — NPC index N → player slot N. `EmotionMap` declares ONLY the exceptions; unlisted indices stay 1:1.
- **Out-of-range / missing slot → CLAMP TO 0**, with no per-frame error spam. This is array safety, not artistic intent.
- **Aspect ratio comes from one frame / one slot** (`FrameWidth`/`FrameHeight`, or `SlotWidth`/`SlotHeight`) — NEVER from the whole sheet or strip. Using `texture.Width` squashes the portrait. This was the critical V1 gotcha; it applies identically to both new modes.
- **Asset targets (exact strings):**
  - Mode 1: `Custom/<packId>/PlayerPortrait`
  - Mode 2: `Custom/<packId>/PlayerPortrait/Sheet`
  - Mode 3: `Custom/<packId>/PlayerPortrait/Emotion/<slotIndex>`
- **Mode 2 sheet default is 2 columns** (matches vanilla portrait sheets); `Columns` is overridable.
- **Mode 3 animation restarts at frame 0 on emotion change** — reset both the frame index and the elapsed accumulator. The V1 resets (dialogue box open, dialogue box close) still apply.
- **`FrameDurations`** (per-frame ms array, length must equal `FrameCount`) overrides `FrameDurationMs`; `FrameDurationMs` defaults to 100; a duration of `0` freezes on that frame. Same as V1.
- **JSON dictionary keys are strings.** `EmotionMap` and `EmotionAnimations` come out of Content Patcher `EditData` as `Dictionary<string, …>` keyed by the *stringified* index (`"0"`, `"1"`). Always convert with `CultureInfo.InvariantCulture`.
- Nullable reference types are enabled (`<Nullable>enable</Nullable>`). Match the existing comment density and `// ── Section ──` header style of the surrounding code.

---

## Prerequisite: restore the DDFC build reference

**This blocks every task that runs `dotnet build` on the main project.**

`PlayerPortraitsFramework.csproj` references
`$(GamePath)\Mods\DialogueDisplayFramework\DialogueDisplayFramework.dll`, and that file
does not currently exist — the game's `Mods` folder
(`C:\Users\dakot\Documents\Applications\Steam\steamapps\common\Stardew Valley\Mods`)
contains only CJBCheatsMenu, ConsoleCommands, ContentPatcher, GenericModConfigMenu,
LookupAnything, NoclipMode, and SaveBackup. DDFC is not installed and no copy exists
anywhere under `Documents` or the Vortex staging folder.

A `dotnet build` on the current `main` was run to confirm the scope of this: it fails with
`MSB3245: Could not resolve this reference. Could not locate the assembly
"DialogueDisplayFramework"` followed by `CS0246` for `DialogueBoxRenderer`,
`DialogueDisplayData` and `BaseData`. **Every other reference resolves** — no SMAPI, Stardew,
Harmony or MonoGame errors appear, so ModBuildConfig locates the game correctly at its
non-standard path and DDFC is the *only* missing piece.

Before starting Task 5, install Dialogue Display Framework Continued
(`Mangupix.DialogueDisplayFrameworkContinued`) into the game's `Mods` folder so the
`HintPath` resolves.

Tasks 1–4 are pure logic in a standalone test project and **build and test fine without
DDFC** — start there regardless.

---

## File Structure

| File | Responsibility |
|---|---|
| `PackRegistration.cs` *(modify)* | Pack manifest POCOs. Gains `EmotionSheet`, `EmotionAnimations`, `EmotionMap` + the new `EmotionSheetSettings` class. Stays free of game/XNA types so it can be linked into the test project. |
| `EmotionResolver.cs` *(create)* | Pure logic: which mode a pack is, NPC index → player slot remap, and mode-2 sheet cell rect math with bounds clamping. No SMAPI/XNA/Stardew references. |
| `AnimationCursor.cs` *(create)* | Pure animation cursor: frame index + elapsed accumulator, advanced by an injected `elapsedMs`, restarting at frame 0 when the slot changes. No SMAPI/XNA/Stardew references. |
| `ModEntry.cs` *(modify)* | Adds V2 active-pack state (mode, sheet, per-emotion animations, emotion map, pack id), a multi-asset texture cache with a failure memo, and `HasActivePack`. |
| `DrawPlayerPortraitPatch.cs` *(modify)* | Reads the live NPC portrait index, picks the texture + source rect for the active mode, drives the `AnimationCursor`, draws. Warn-once fallback logging. |
| `GetDataVectorPatch.cs` *(modify)* | Gate changes from `ActiveTexturePath is null` to `!HasActivePack` (mode 3 has no single texture path). |
| `DialogueBoxGeometry.cs` *(modify)* | Same gate change. |
| `tests/PlayerPortraitsFramework.Tests/` *(create)* | xUnit project linking the three pure source files. |
| `docs/superpowers/plans/new-ppf-test-art.ps1` *(exists)* | Generates the numbered, colour-coded acceptance-test PNGs for Task 7. Already written and verified to run on this machine. |
| `PlayerPortraitsFramework_Pack_Author_Guide.txt` *(modify)* | Documents the three modes. |
| `manifest.json`, `README.md` *(modify)* | Version + description bump. |

---

### Task 1: Pack data shape + mode resolution (with test project)

**Files:**
- Create: `tests/PlayerPortraitsFramework.Tests/PlayerPortraitsFramework.Tests.csproj`
- Create: `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs`
- Create: `EmotionResolver.cs`
- Modify: `PackRegistration.cs` (append new properties to `PackRegistration`, append `EmotionSheetSettings` class)
- Modify: `PlayerPortraitsFramework.csproj` (exclude `tests/**` from the main project's compile glob)

**Interfaces:**
- Consumes: existing `PackRegistration`, `PortraitRect`, `AnimationSettings` from `PackRegistration.cs`.
- Produces:
  - `enum PackMode { None, Simple, EmotionStatic, EmotionAnimated }`
  - `static PackMode EmotionResolver.ResolveMode(PackRegistration? pack)`
  - `class EmotionSheetSettings { int SlotWidth; int SlotHeight; int Columns = 2; bool IsValid { get; } }`
  - `PackRegistration.EmotionSheet` (`EmotionSheetSettings?`), `PackRegistration.EmotionAnimations` (`Dictionary<string, AnimationSettings>?`), `PackRegistration.EmotionMap` (`Dictionary<string, int>?`)

- [ ] **Step 1: Create the test project**

Run from the repo root (`Player Portrait Framework Sourcecode`):

```bash
dotnet new xunit -o "tests/PlayerPortraitsFramework.Tests" -n PlayerPortraitsFramework.Tests
rm "tests/PlayerPortraitsFramework.Tests/UnitTest1.cs"
```

Then overwrite `tests/PlayerPortraitsFramework.Tests/PlayerPortraitsFramework.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <RootNamespace>PlayerPortraitsFramework.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <!--
      The framework's PURE logic is linked in, not project-referenced: the main project
      needs the Stardew/SMAPI/DDFC assemblies to build, and these three files deliberately
      have zero game dependencies precisely so they can be tested without the game.
    -->
    <Compile Include="..\..\PackRegistration.cs" Link="PackRegistration.cs" />
    <Compile Include="..\..\EmotionResolver.cs" Link="EmotionResolver.cs" />
    <Compile Include="..\..\AnimationCursor.cs" Link="AnimationCursor.cs" />
  </ItemGroup>

</Project>
```

> `AnimationCursor.cs` does not exist until Task 4. Create it now as an empty placeholder so
> the project restores — `printf '' > AnimationCursor.cs` from the repo root. Task 4 fills it in.

- [ ] **Step 2: Keep the test folder out of the main project**

In `PlayerPortraitsFramework.csproj`, add this `ItemGroup` after the existing ones. Without it the
SDK's default glob would compile the test sources into the mod DLL.

```xml
  <ItemGroup>
    <!-- The xUnit project under tests/ is built separately; keep it out of the mod assembly. -->
    <Compile Remove="tests/**" />
    <None Remove="tests/**" />
  </ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs`:

```csharp
using PlayerPortraitsFramework;
using Xunit;

namespace PlayerPortraitsFramework.Tests;

public class ResolveModeTests
{
    [Fact]
    public void NullPack_IsNone()
    {
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(null));
    }

    [Fact]
    public void EmptyPack_IsNone()
    {
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(new PackRegistration()));
    }

    [Fact]
    public void PortraitRectOnly_IsSimple()
    {
        var pack = new PackRegistration { Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 } };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void ValidAnimationOnly_IsSimple()
    {
        var pack = new PackRegistration
        {
            Animation = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 4 }
        };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void InvalidAnimationWithNoPortrait_IsNone()
    {
        var pack = new PackRegistration
        {
            Animation = new AnimationSettings { FrameWidth = 0, FrameHeight = 0, FrameCount = 0 }
        };
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void ValidEmotionSheet_IsEmotionStatic()
    {
        var pack = new PackRegistration
        {
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 1024, SlotHeight = 1024 }
        };
        Assert.Equal(PackMode.EmotionStatic, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionSheetWithZeroDimensions_IsNone()
    {
        var pack = new PackRegistration
        {
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 0, SlotHeight = 1024 }
        };
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimations_IsEmotionAnimated()
    {
        var pack = new PackRegistration
        {
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 4 }
            }
        };
        Assert.Equal(PackMode.EmotionAnimated, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimationsWithNoValidEntries_FallsThrough()
    {
        var pack = new PackRegistration
        {
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 0, FrameHeight = 0, FrameCount = 0 }
            },
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 }
        };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimated_WinsOverSheetAndSimple()
    {
        // Modes are mutually exclusive by contract; if an author declares several anyway,
        // resolution must be deterministic: animated > sheet > simple.
        var pack = new PackRegistration
        {
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 },
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 512, SlotHeight = 512 },
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 2 }
            }
        };
        Assert.Equal(PackMode.EmotionAnimated, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionStatic_WinsOverSimple()
    {
        var pack = new PackRegistration
        {
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 },
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 512, SlotHeight = 512 }
        };
        Assert.Equal(PackMode.EmotionStatic, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionSheet_DefaultsToTwoColumns()
    {
        Assert.Equal(2, new EmotionSheetSettings().Columns);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run from the repo root:

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: FAIL — compile errors, `The type or namespace name 'PackMode' could not be found` and
`'PackRegistration' does not contain a definition for 'EmotionSheet'`.

- [ ] **Step 5: Add the V2 pack fields**

In `PackRegistration.cs`, add `using System.Collections.Generic;` at the top of the file (above
`namespace`), then insert these properties into `PackRegistration` immediately after the existing
`Animation` property and before the `// ── Author-declared defaults (Milestone 9) ──` block:

```csharp
        // ── V2: emotion matching ─────────────────────────────────────────────────────────
        // A pack is exactly ONE mode. Mode 1 (Simple) = Portrait/Animation above, unchanged.
        // Mode 2 (EmotionStatic) = one EmotionSheet grid. Mode 3 (EmotionAnimated) = one file
        // per emotion via EmotionAnimations. Content Patcher decides WHICH art is loaded (its
        // When conditions swap the sheet or an individual slot); the framework only decides
        // WHICH SLOT is on screen, from the NPC's live portrait index.

        /// <summary>
        /// Mode 2. ONE sheet carrying every emotion slot, sliced into cells. The sheet is the
        /// costume unit: a CP condition swaps the WHOLE sheet (season/outfit), not per-emotion files.
        /// Loaded to <c>Custom/&lt;packId&gt;/PlayerPortrait/Sheet</c>.
        /// </summary>
        public EmotionSheetSettings? EmotionSheet { get; set; }

        /// <summary>
        /// Mode 3. ONE FILE PER EMOTION, keyed by slot index as a STRING ("0", "1", …) because
        /// that is how the value arrives from Content Patcher's EditData. Not a grid: animations
        /// have different lengths per emotion, and a grid would force padding to the longest.
        /// Each slot is loaded to <c>Custom/&lt;packId&gt;/PlayerPortrait/Emotion/&lt;slotIndex&gt;</c>.
        /// </summary>
        public Dictionary<string, AnimationSettings>? EmotionAnimations { get; set; }

        /// <summary>
        /// Optional remap for modes 2 and 3, declaring ONLY the exceptions — unlisted indices stay
        /// 1:1 (NPC index N → player slot N). Lets an author reuse one drawn face across several
        /// NPC moods. Keys are the NPC portrait index as a string; values are the player slot.
        /// </summary>
        public Dictionary<string, int>? EmotionMap { get; set; }
```

Then append this class at the end of the namespace, after `AnimationSettings`:

```csharp
    /// <summary>
    /// Mode 2 sheet geometry: one image carrying every emotion slot in a grid. The framework
    /// computes cell N's rect from these, so the author never writes per-emotion rects.
    /// </summary>
    public class EmotionSheetSettings
    {
        /// <summary>Width of one emotion slot, in pixels. Also drives the draw scale (NOT the sheet width).</summary>
        public int SlotWidth { get; set; }
        /// <summary>Height of one emotion slot, in pixels. Also drives the draw scale (NOT the sheet height).</summary>
        public int SlotHeight { get; set; }
        /// <summary>Columns in the grid. Defaults to 2, matching vanilla portrait sheets.</summary>
        public int Columns { get; set; } = 2;

        /// <summary>
        /// True when the settings describe a usable sheet. Invalid settings (slot width or height
        /// &lt;= 0) are rejected at mode resolution so there is never a zero-size source rect.
        /// </summary>
        public bool IsValid => SlotWidth > 0 && SlotHeight > 0;
    }
```

- [ ] **Step 6: Write the mode resolver**

Create `EmotionResolver.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace PlayerPortraitsFramework
{
    /// <summary>Which of the three mutually exclusive shapes a pack declares.</summary>
    public enum PackMode
    {
        /// <summary>Nothing usable declared — the pack is detected but draws nothing.</summary>
        None,
        /// <summary>Mode 1 (= V1): one image, no emotion awareness. Portrait rect or Animation.</summary>
        Simple,
        /// <summary>Mode 2: one grid sheet carrying every emotion slot.</summary>
        EmotionStatic,
        /// <summary>Mode 3: one animated file per emotion.</summary>
        EmotionAnimated
    }

    /// <summary>
    /// The framework's emotion math, deliberately free of SMAPI / XNA / Stardew types so it can be
    /// unit-tested without the game running. Everything here is a pure function of its arguments.
    /// </summary>
    public static class EmotionResolver
    {
        /// <summary>
        /// Which mode a pack is. Modes are mutually exclusive by contract, but an author can still
        /// declare several by mistake, so resolution is deterministic and documented:
        /// EmotionAnimated &gt; EmotionStatic &gt; Simple. Invalid declarations (zero dimensions,
        /// zero frames) fall through to the next candidate rather than producing a broken draw.
        /// </summary>
        public static PackMode ResolveMode(PackRegistration? pack)
        {
            if (pack is null)
                return PackMode.None;

            if (pack.EmotionAnimations is { Count: > 0 } anims && anims.Values.Any(a => a is { IsValid: true }))
                return PackMode.EmotionAnimated;

            if (pack.EmotionSheet is { IsValid: true })
                return PackMode.EmotionStatic;

            if (pack.Animation is { IsValid: true } || pack.Portrait is not null)
                return PackMode.Simple;

            return PackMode.None;
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: PASS — 12 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add PackRegistration.cs EmotionResolver.cs AnimationCursor.cs PlayerPortraitsFramework.csproj tests
git commit -m "feat: add V2 pack fields and pack-mode resolution with test project"
```

---

### Task 2: NPC index → player slot remap

**Files:**
- Modify: `EmotionResolver.cs` (add `ResolveSlot`)
- Test: `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs` (append a class)

**Interfaces:**
- Consumes: `EmotionResolver` from Task 1.
- Produces: `static int EmotionResolver.ResolveSlot(int npcPortraitIndex, Dictionary<string, int>? emotionMap)`

- [ ] **Step 1: Write the failing tests**

Append to `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs`:

```csharp
public class ResolveSlotTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(37, 37)]
    public void NoMap_IsOneToOne(int npcIndex, int expected)
    {
        Assert.Equal(expected, EmotionResolver.ResolveSlot(npcIndex, null));
    }

    [Fact]
    public void EmptyMap_IsOneToOne()
    {
        Assert.Equal(3, EmotionResolver.ResolveSlot(3, new Dictionary<string, int>()));
    }

    [Fact]
    public void ListedIndex_IsRemapped()
    {
        // The spec's example: {"1": 4, "2": 0, "3": 0}
        var map = new Dictionary<string, int> { ["1"] = 4, ["2"] = 0, ["3"] = 0 };
        Assert.Equal(4, EmotionResolver.ResolveSlot(1, map));
        Assert.Equal(0, EmotionResolver.ResolveSlot(2, map));
        Assert.Equal(0, EmotionResolver.ResolveSlot(3, map));
    }

    [Fact]
    public void UnlistedIndex_StaysOneToOne()
    {
        var map = new Dictionary<string, int> { ["1"] = 4, ["2"] = 0, ["3"] = 0 };
        Assert.Equal(0, EmotionResolver.ResolveSlot(0, map));
        Assert.Equal(4, EmotionResolver.ResolveSlot(4, map));
        Assert.Equal(9, EmotionResolver.ResolveSlot(9, map));
    }

    [Fact]
    public void NegativeNpcIndex_ClampsToZero()
    {
        Assert.Equal(0, EmotionResolver.ResolveSlot(-1, null));
    }

    [Fact]
    public void NegativeNpcIndex_ClampsBeforeLookup()
    {
        // -1 becomes 0 first, so it picks up slot 0's remap rather than missing the map entirely.
        var map = new Dictionary<string, int> { ["0"] = 7 };
        Assert.Equal(7, EmotionResolver.ResolveSlot(-1, map));
    }

    [Fact]
    public void NegativeMappedValue_ClampsToZero()
    {
        var map = new Dictionary<string, int> { ["2"] = -5 };
        Assert.Equal(0, EmotionResolver.ResolveSlot(2, map));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: FAIL — `'EmotionResolver' does not contain a definition for 'ResolveSlot'`.

- [ ] **Step 3: Implement `ResolveSlot`**

Add `using System.Globalization;` to the `using` block in `EmotionResolver.cs`, then add this
method to `EmotionResolver` after `ResolveMode`:

```csharp
        /// <summary>
        /// The NPC's live portrait index → the player slot to draw. Default is 1:1; the optional
        /// map declares ONLY the exceptions. Negatives clamp to 0 at both ends (before the lookup,
        /// so a negative index still picks up slot 0's remap, and after, so a bad map value can
        /// never produce a negative slot).
        /// </summary>
        /// <param name="npcPortraitIndex">The integer the game already resolved for the NPC's expression.</param>
        /// <param name="emotionMap">Exception map keyed by the NPC index as a string; null = pure 1:1.</param>
        public static int ResolveSlot(int npcPortraitIndex, Dictionary<string, int>? emotionMap)
        {
            if (npcPortraitIndex < 0)
                npcPortraitIndex = 0;

            // Keys arrive from Content Patcher JSON as strings, so the lookup is string-keyed.
            // InvariantCulture: digit formatting must never follow the player's locale.
            if (emotionMap != null
                && emotionMap.TryGetValue(npcPortraitIndex.ToString(CultureInfo.InvariantCulture), out int mapped))
                return mapped < 0 ? 0 : mapped;

            return npcPortraitIndex;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: PASS — 22 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add EmotionResolver.cs tests
git commit -m "feat: resolve NPC portrait index to player emotion slot with optional remap"
```

---

### Task 3: Mode 2 sheet cell math

**Files:**
- Modify: `EmotionResolver.cs` (add `TryGetSheetCell`)
- Test: `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs` (append a class)

**Interfaces:**
- Consumes: `EmotionSheetSettings` (Task 1), `EmotionResolver` (Task 1).
- Produces: `static bool EmotionResolver.TryGetSheetCell(int slot, EmotionSheetSettings? sheet, int textureWidth, int textureHeight, out int x, out int y)` — returns `false` and sets `x`/`y` to `0` when the cell would fall outside the sheet, which IS the clamp-to-0 behavior (slot 0 is always at 0,0).

- [ ] **Step 1: Write the failing tests**

Append to `tests/PlayerPortraitsFramework.Tests/EmotionResolverTests.cs`:

```csharp
public class SheetCellTests
{
    // A 2-column sheet of 1024px slots, 3 rows tall → slots 0..5.
    private static EmotionSheetSettings Sheet(int columns = 2) =>
        new() { SlotWidth = 1024, SlotHeight = 1024, Columns = columns };

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1024, 0)]
    [InlineData(2, 0, 1024)]
    [InlineData(3, 1024, 1024)]
    [InlineData(4, 0, 2048)]
    [InlineData(5, 1024, 2048)]
    public void TwoColumnGrid_WalksRowsThenColumns(int slot, int expectedX, int expectedY)
    {
        bool ok = EmotionResolver.TryGetSheetCell(slot, Sheet(), 2048, 3072, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void ColumnsOverride_IsHonoured()
    {
        // 3 columns: slot 3 wraps to row 1, column 0.
        bool ok = EmotionResolver.TryGetSheetCell(3, Sheet(columns: 3), 3072, 2048, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(0, x);
        Assert.Equal(1024, y);
    }

    [Fact]
    public void ZeroOrNegativeColumns_DefaultsToTwo()
    {
        bool ok = EmotionResolver.TryGetSheetCell(2, Sheet(columns: 0), 2048, 2048, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(0, x);
        Assert.Equal(1024, y);
    }

    [Fact]
    public void SlotBelowTheSheet_FallsBackToZero()
    {
        // Sheet is only 2 rows (4 slots); slot 4 would start at y=2048, past the bottom.
        bool ok = EmotionResolver.TryGetSheetCell(4, Sheet(), 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlotPastTheRightEdge_FallsBackToZero()
    {
        // Sheet is only 1 column wide but declares 2 → slot 1 would start at x=1024, past the edge.
        bool ok = EmotionResolver.TryGetSheetCell(1, Sheet(), 1024, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void NegativeSlot_FallsBackToZero()
    {
        bool ok = EmotionResolver.TryGetSheetCell(-3, Sheet(), 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void NullSheet_FallsBackToZero()
    {
        bool ok = EmotionResolver.TryGetSheetCell(0, null, 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void InvalidSheetDimensions_FallBackToZero()
    {
        var bad = new EmotionSheetSettings { SlotWidth = 0, SlotHeight = 1024 };
        bool ok = EmotionResolver.TryGetSheetCell(0, bad, 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlotZero_OnASheetTooSmallForOneCell_FallsBackToZero()
    {
        // Degenerate art: even slot 0 does not fit. Still returns 0,0 — the caller draws a clipped
        // cell rather than crashing, and the warn-once log tells the author.
        bool ok = EmotionResolver.TryGetSheetCell(0, Sheet(), 512, 512, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: FAIL — `'EmotionResolver' does not contain a definition for 'TryGetSheetCell'`.

- [ ] **Step 3: Implement `TryGetSheetCell`**

Add to `EmotionResolver` after `ResolveSlot`:

```csharp
        /// <summary>
        /// Mode 2: the top-left corner of emotion slot <paramref name="slot"/> within the sheet.
        /// Returns <c>false</c> — with <paramref name="x"/>/<paramref name="y"/> set to 0,0 — when the
        /// cell would fall outside the loaded texture. That IS the clamp-to-slot-0 rule: slot 0 always
        /// lives at the origin, so the caller can draw the returned coordinates either way and use the
        /// <c>false</c> purely to decide whether to log a (warn-once) fallback.
        /// </summary>
        public static bool TryGetSheetCell(int slot, EmotionSheetSettings? sheet, int textureWidth, int textureHeight, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (slot < 0 || sheet is not { IsValid: true })
                return false;

            int columns = sheet.Columns > 0 ? sheet.Columns : 2; // 2 = vanilla portrait sheet layout
            int cellX = (slot % columns) * sheet.SlotWidth;
            int cellY = (slot / columns) * sheet.SlotHeight;

            // Bounds check against the ACTUAL loaded texture, not a declared slot count: the author
            // never states how many slots exist, so the sheet's real size is the only truth available.
            if (cellX + sheet.SlotWidth > textureWidth || cellY + sheet.SlotHeight > textureHeight)
                return false;

            x = cellX;
            y = cellY;
            return true;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: PASS — 36 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add EmotionResolver.cs tests
git commit -m "feat: compute emotion sheet cell rects with clamp-to-slot-0 bounds check"
```

---

### Task 4: Animation cursor with emotion-change restart

**Files:**
- Modify: `AnimationCursor.cs` (created empty in Task 1)
- Test: `tests/PlayerPortraitsFramework.Tests/AnimationCursorTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `class AnimationCursor` with `int Frame { get; }`, `double Elapsed { get; }`, `int Slot { get; }`, `void Reset()`, and
  `void Advance(int slot, double elapsedMs, int frameCount, int? uniformDurationMs, int[]? perFrameDurations)`.

This replaces `DrawPlayerPortraitPatch`'s `_animFrame` / `_animElapsed` statics and its inline timing
logic. Extracting it makes the acceptance-critical "restart at frame 0 on emotion change" rule
testable without the game, by injecting `elapsedMs` instead of reading `Game1.currentGameTime`.

- [ ] **Step 1: Write the failing tests**

Create `tests/PlayerPortraitsFramework.Tests/AnimationCursorTests.cs`:

```csharp
using PlayerPortraitsFramework;
using Xunit;

namespace PlayerPortraitsFramework.Tests;

public class AnimationCursorTests
{
    private const int Slot0 = 0;
    private const int Slot1 = 1;

    [Fact]
    public void FreshCursor_StartsAtFrameZeroWithNoSlot()
    {
        var cursor = new AnimationCursor();
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
        Assert.Equal(-1, cursor.Slot);
    }

    [Fact]
    public void BelowDuration_HoldsTheFrame()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 50, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(50, cursor.Elapsed);
    }

    [Fact]
    public void ReachingDuration_AdvancesAndResetsElapsed()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
    }

    [Fact]
    public void PastTheLastFrame_WrapsToZero()
    {
        var cursor = new AnimationCursor();
        for (int i = 0; i < 4; i++)
            cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void UnsetDuration_DefaultsTo100Ms()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 99, frameCount: 2, uniformDurationMs: null, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        cursor.Advance(Slot0, 1, frameCount: 2, uniformDurationMs: null, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
    }

    [Fact]
    public void PerFrameDurations_OverrideTheUniformValue()
    {
        // Blink pattern: frame 0 dwells 1800ms, the rest 100ms each.
        var durations = new[] { 1800, 100, 100, 100 };
        var cursor = new AnimationCursor();

        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(0, cursor.Frame); // 200 < 1800, still on the long frame

        cursor.Advance(Slot0, 1600, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(1, cursor.Frame);

        cursor.Advance(Slot0, 100, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(2, cursor.Frame);
    }

    [Fact]
    public void PerFrameDurationsOfWrongLength_AreIgnored()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: new[] { 1800, 100 });
        Assert.Equal(1, cursor.Frame); // fell back to the uniform 200ms
    }

    [Fact]
    public void ZeroDuration_FreezesOnTheFrame()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 10_000, frameCount: 4, uniformDurationMs: 0, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void SlotChange_RestartsAtFrameZeroAndClearsElapsed()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(2, cursor.Frame);

        // Emotion changed mid-conversation → the new expression plays its own beat from the top.
        cursor.Advance(Slot1, 16, frameCount: 2, uniformDurationMs: 300, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(Slot1, cursor.Slot);
        Assert.Equal(16, cursor.Elapsed); // this tick's time starts frame 0's dwell, not the old frame's
    }

    [Fact]
    public void SameSlot_DoesNotRestart()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Advance(Slot0, 100, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
        Assert.Equal(100, cursor.Elapsed);
    }

    [Fact]
    public void Reset_ReturnsToFrameZeroAndForgetsTheSlot()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot1, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Reset();

        Assert.Equal(0, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
        Assert.Equal(-1, cursor.Slot);

        // After a reset the SAME slot must restart too (dialogue box reopened on the same emotion).
        cursor.Advance(Slot1, 16, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void ShrinkingFrameCount_DoesNotLeaveAStaleFrameIndex()
    {
        // A CP condition can swap a slot's art for a shorter variant while the cursor is past its end.
        var cursor = new AnimationCursor();
        for (int i = 0; i < 3; i++)
            cursor.Advance(Slot0, 200, frameCount: 6, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(3, cursor.Frame);

        cursor.Advance(Slot0, 10, frameCount: 2, uniformDurationMs: 200, perFrameDurations: null);
        Assert.True(cursor.Frame < 2);
    }

    [Fact]
    public void ZeroFrameCount_IsSafe()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 0, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: FAIL — `The type or namespace name 'AnimationCursor' could not be found`.

- [ ] **Step 3: Implement `AnimationCursor`**

Replace the empty `AnimationCursor.cs` with:

```csharp
#nullable enable
namespace PlayerPortraitsFramework
{
    /// <summary>
    /// The player portrait's animation playhead: which frame is showing and how long it has been
    /// showing for. Elapsed time is INJECTED rather than read from <c>Game1.currentGameTime</c>, which
    /// keeps this class free of game types and makes the emotion-change restart rule unit-testable.
    ///
    /// <para>Reset points: the dialogue box opening, the dialogue box closing (both via
    /// <see cref="Reset"/>), and — new in V2 — the emotion slot changing mid-conversation, which
    /// <see cref="Advance"/> detects on its own so each expression plays its own beat from the top.</para>
    /// </summary>
    public sealed class AnimationCursor
    {
        /// <summary>Sentinel meaning "no slot has played yet", so the first <see cref="Advance"/> always restarts.</summary>
        private const int NoSlot = -1;

        /// <summary>The frame index currently showing.</summary>
        public int Frame { get; private set; }
        /// <summary>Milliseconds this frame has been showing.</summary>
        public double Elapsed { get; private set; }
        /// <summary>The emotion slot currently playing, or <c>-1</c> before the first advance / after a reset.</summary>
        public int Slot { get; private set; } = NoSlot;

        /// <summary>
        /// Return to frame 0 and forget the current slot. Called on dialogue box OPEN and CLOSE, so
        /// every conversation starts at frame 0 — including one that reopens on the same emotion.
        /// </summary>
        public void Reset()
        {
            Frame   = 0;
            Elapsed = 0;
            Slot    = NoSlot;
        }

        /// <summary>
        /// Advance the playhead by one draw's worth of time.
        /// </summary>
        /// <param name="slot">The emotion slot being drawn this frame. A change restarts at frame 0.</param>
        /// <param name="elapsedMs">Milliseconds since the previous draw.</param>
        /// <param name="frameCount">Frames in the current slot's animation.</param>
        /// <param name="uniformDurationMs">Per-frame dwell in ms; <c>null</c> defaults to 100.</param>
        /// <param name="perFrameDurations">Per-frame dwell array; overrides <paramref name="uniformDurationMs"/> when its length equals <paramref name="frameCount"/>.</param>
        public void Advance(int slot, double elapsedMs, int frameCount, int? uniformDurationMs, int[]? perFrameDurations)
        {
            if (frameCount <= 0)
            {
                // Nothing to walk. Park at frame 0 rather than dividing by zero.
                Frame   = 0;
                Elapsed = 0;
                Slot    = slot;
                return;
            }

            // Emotion changed mid-conversation → the new expression plays its own beat from the top.
            // BOTH the frame index and the accumulator reset, so the new frame 0 gets its full dwell.
            if (slot != Slot)
            {
                Slot    = slot;
                Frame   = 0;
                Elapsed = 0;
            }

            // A CP condition can swap in a SHORTER variant of the same slot while we sit past its end.
            if (Frame >= frameCount)
                Frame = 0;

            Elapsed += elapsedMs;

            // This frame's dwell: a valid, in-bounds per-frame array overrides the uniform value; the
            // uniform value defaults to 100ms. A duration of 0 FREEZES on this frame (no spinning).
            int duration =
                (perFrameDurations is { } durations && durations.Length == frameCount && Frame < durations.Length)
                    ? durations[Frame]
                    : (uniformDurationMs ?? 100);

            if (duration > 0 && Elapsed >= duration)
            {
                Frame   = (Frame + 1) % frameCount;
                Elapsed = 0;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: PASS — 49 passed, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add AnimationCursor.cs tests
git commit -m "feat: add animation cursor that restarts at frame 0 on emotion change"
```

---

### Task 5: Wire V2 pack state into ModEntry

**Files:**
- Modify: `ModEntry.cs`
- Modify: `GetDataVectorPatch.cs:25`
- Modify: `DialogueBoxGeometry.cs:44`

**Interfaces:**
- Consumes: `PackMode`, `EmotionResolver.ResolveMode` (Task 1); `EmotionSheetSettings`, `PackRegistration.EmotionSheet` / `.EmotionAnimations` / `.EmotionMap` (Task 1).
- Produces, all `internal static` on `ModEntry` for the Harmony patches to read:
  - `PackMode ActiveMode`
  - `bool HasActivePack` (property → `ActiveMode != PackMode.None`)
  - `string? ActivePackId`
  - `EmotionSheetSettings? ActiveSheet`
  - `Dictionary<string, AnimationSettings>? ActiveEmotionAnimations`
  - `Dictionary<string, int>? ActiveEmotionMap`
  - `string EmotionTexturePath(string packId, int slot)`
  - `Texture2D? TryGetTexture(string assetName)`
  - `ActiveTexturePath` keeps its existing meaning for modes 1 and 2 (it is `null` in mode 3, which has one texture per slot).

**This task requires DDFC installed** — see the Prerequisite section. There is no unit test here:
this is glue over SMAPI's content pipeline and Harmony statics, and verification is a clean build
plus the in-game acceptance checks in Task 7.

- [ ] **Step 1: Replace the single-texture cache with a multi-asset one**

In `ModEntry.cs`, delete the `ActiveTexture` field (line 67) and replace the whole
`TryGetActiveTexture` method (lines 415-437) with the block below. Add
`using Microsoft.Xna.Framework.Graphics;` if it is not already present (it is, at line 6).

```csharp
        // ── Texture cache (V2) ───────────────────────────────────────────────────────────
        // V1 cached ONE texture; mode 3 needs one per emotion slot, so the cache is keyed by asset
        // name. FailedAssets memoises misses: a slot a pack never provides would otherwise throw and
        // be caught 60× a second. Both are cleared on invalidation and on pack refresh.
        private static readonly Dictionary<string, Texture2D> TextureCache = new();
        private static readonly HashSet<string>               FailedAssets = new();

        /// <summary>
        /// Returns a pack texture, loading it on demand. Returns null quietly if the PNG isn't
        /// loadable (e.g. before Content Patcher applies its Load, or a slot the pack never supplies)
        /// so the caller can fall back or skip the frame. Self-healing: reloads if the cached texture
        /// was disposed.
        /// </summary>
        internal static Texture2D? TryGetTexture(string assetName)
        {
            if (TextureCache.TryGetValue(assetName, out var cached))
            {
                if (cached is { IsDisposed: false })
                    return cached;
                TextureCache.Remove(assetName);
            }

            if (FailedAssets.Contains(assetName))
                return null; // known miss — don't throw/catch every frame

            try
            {
                var texture = SHelper.GameContent.Load<Texture2D>(assetName);
                TextureCache[assetName] = texture;
                return texture;
            }
            catch
            {
                FailedAssets.Add(assetName);
                return null;
            }
        }

        /// <summary>Drops every cached texture and every memoised miss (pack change / asset invalidation).</summary>
        private static void ClearTextureCache()
        {
            TextureCache.Clear();
            FailedAssets.Clear();
        }
```

- [ ] **Step 2: Add the V2 active-pack state**

In `ModEntry.cs`, replace the "Active pack state" block (lines 63-67, now missing `ActiveTexture`)
with:

```csharp
        // Active pack state read by the Harmony patches (static so they can reach it).
        internal static string?            ActivePackId;      // the resolved pack's UniqueID, or null when no pack
        internal static PackMode           ActiveMode;        // which of the three shapes the active pack declares
        internal static string?            ActiveTexturePath; // modes 1 & 2 only: the single texture. Mode 3 is per-slot.
        internal static PortraitRect?      ActiveRect;        // mode 1: static source rect (null for a pure-animated pack)
        internal static AnimationSettings? ActiveAnimation;   // mode 1: non-null ONLY when valid → the portrait animates
        internal static EmotionSheetSettings? ActiveSheet;    // mode 2: the emotion grid's geometry
        internal static Dictionary<string, AnimationSettings>? ActiveEmotionAnimations; // mode 3: per-slot animations
        internal static Dictionary<string, int>?               ActiveEmotionMap;        // modes 2 & 3: exception remap

        /// <summary>
        /// Whether a usable pack is active. This — NOT <see cref="ActiveTexturePath"/> — is the gate
        /// every patch checks: mode 3 has no single texture path, so the old null-path check would
        /// silently disable the box geometry for emotion-animated packs.
        /// </summary>
        internal static bool HasActivePack => ActiveMode != PackMode.None;

        /// <summary>Mode 1 texture target: one image, no emotion awareness (V1, unchanged).</summary>
        internal static string SimpleTexturePath(string packId) => $"Custom/{packId}/PlayerPortrait";
        /// <summary>Mode 2 texture target: one grid sheet carrying every emotion slot.</summary>
        internal static string SheetTexturePath(string packId) => $"Custom/{packId}/PlayerPortrait/Sheet";
        /// <summary>Mode 3 texture target: one animated file per emotion slot.</summary>
        internal static string EmotionTexturePath(string packId, int slot) => $"Custom/{packId}/PlayerPortrait/Emotion/{slot}";
```

- [ ] **Step 3: Rewrite the pack-resolution tail of `RefreshActivePack`**

In `ModEntry.cs`, replace the reset block at the top of `RefreshActivePack` (lines 337-340) with:

```csharp
            ActivePackId            = null;
            ActiveMode              = PackMode.None;
            ActiveTexturePath       = null;
            ActiveRect              = null;
            ActiveAnimation         = null;
            ActiveSheet             = null;
            ActiveEmotionAnimations = null;
            ActiveEmotionMap        = null;
            ClearTextureCache();
            DrawPlayerPortraitPatch.ResetFallbackWarnings();
```

Then replace everything from the `// Static vs animated (V2).` comment (line 386) to the end of the
method (line 412) with:

```csharp
            // ── Mode selection (V2) ──────────────────────────────────────────────────────
            // Exactly one of three shapes. EmotionResolver owns the precedence rules so they are
            // unit-tested; this method only wires the chosen shape to its asset target(s).
            ActiveMode       = EmotionResolver.ResolveMode(active);
            ActiveEmotionMap = active?.EmotionMap;

            if (ActiveMode == PackMode.None)
            {
                Monitor.Log(
                    $"Active pack '{activeId}' declares nothing usable — it needs a Portrait rect, a valid "
                    + "Animation, a valid EmotionSheet, or at least one valid EmotionAnimations entry. Nothing to draw.",
                    LogLevel.Warn);
                return;
            }

            ActivePackId = activeId;

            switch (ActiveMode)
            {
                // ── Mode 1 (= V1): one image, no emotion awareness. Behaviour is unchanged. ──
                case PackMode.Simple:
                {
                    var  anim     = active!.Animation;
                    bool animated = anim is { IsValid: true };

                    if (animated && active.Portrait != null)
                        Monitor.Log($"Active pack '{activeId}' declares both Animation and Portrait; Animation wins, Portrait ignored.", LogLevel.Info);

                    ActiveTexturePath = SimpleTexturePath(activeId);
                    ActiveRect        = active.Portrait;             // may be null for a pure-animated pack
                    ActiveAnimation   = animated ? anim : null;      // non-null only when valid

                    if (animated)
                        Monitor.Log($"Active pack: {activeId}  (simple, texture: {ActiveTexturePath}, animated: {anim!.FrameCount} frames @ {anim.FrameWidth}x{anim.FrameHeight})", LogLevel.Info);
                    else
                        Monitor.Log($"Active pack: {activeId}  (simple, texture: {ActiveTexturePath}, rect: {ActiveRect!.X},{ActiveRect.Y} {ActiveRect.W}x{ActiveRect.H})", LogLevel.Info);
                    break;
                }

                // ── Mode 2: ONE sheet carrying all emotion slots. A CP condition swaps the whole
                //    sheet (season/outfit), so the framework only ever asks for one asset here. ──
                case PackMode.EmotionStatic:
                {
                    ActiveSheet       = active!.EmotionSheet;
                    ActiveTexturePath = SheetTexturePath(activeId);
                    Monitor.Log(
                        $"Active pack: {activeId}  (emotion sheet, texture: {ActiveTexturePath}, "
                        + $"slots {ActiveSheet!.SlotWidth}x{ActiveSheet.SlotHeight} in {(ActiveSheet.Columns > 0 ? ActiveSheet.Columns : 2)} columns)",
                        LogLevel.Info);
                    break;
                }

                // ── Mode 3: ONE FILE PER EMOTION. No single texture path — each slot is loaded on
                //    demand from Custom/<packId>/PlayerPortrait/Emotion/<slot>, which is also how
                //    seasonal variants work (several CP Loads to the SAME slot, different When). ──
                case PackMode.EmotionAnimated:
                {
                    ActiveEmotionAnimations = active!.EmotionAnimations;
                    ActiveTexturePath       = null;
                    Monitor.Log(
                        $"Active pack: {activeId}  (emotion animations, {ActiveEmotionAnimations!.Count} slot(s): "
                        + $"{string.Join(", ", ActiveEmotionAnimations.Keys.OrderBy(k => k, StringComparer.Ordinal))})",
                        LogLevel.Info);
                    break;
                }
            }
```

- [ ] **Step 4: Update asset invalidation**

In `ModEntry.cs`, replace the second half of `OnAssetsInvalidated` (lines 134-137, the
`// If the active pack's texture asset was invalidated` comment and its `if`) with:

```csharp
            // Any invalidation under this pack's asset prefix drops the cache: modes 2 and 3 have
            // several targets (the sheet, or one file per emotion slot), and CP re-Loads them when a
            // When condition flips (a new season, a heart level). Clearing the memoised MISSES matters
            // as much as the hits — a slot that wasn't loadable before may be now.
            if (ActivePackId != null)
            {
                string prefix = SimpleTexturePath(ActivePackId); // "Custom/<packId>/PlayerPortrait" — covers /Sheet and /Emotion/N
                if (e.NamesWithoutLocale.Any(n => n.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    ClearTextureCache();
            }
```

- [ ] **Step 5: Change the two gates from `ActiveTexturePath` to `HasActivePack`**

`GetDataVectorPatch.cs:25` — replace:

```csharp
                if (ModEntry.ActiveTexturePath is null)       return true; // no pack → leave DDFC alone
```

with:

```csharp
                if (!ModEntry.HasActivePack)                  return true; // no pack → leave DDFC alone
```

`DialogueBoxGeometry.cs:44` — replace:

```csharp
                if (ModEntry.ActiveTexturePath is null)
                    return; // no pack → leave DDFC's box alone
```

with:

```csharp
                if (!ModEntry.HasActivePack)
                    return; // no pack → leave DDFC's box alone
```

- [ ] **Step 6: Build to verify it compiles**

```bash
dotnet build
```

Expected: FAIL — `'DrawPlayerPortraitPatch' does not contain a definition for 'ResetFallbackWarnings'`
and errors about `ActiveTexture` / `TryGetActiveTexture` in `DrawPlayerPortraitPatch.cs`. Task 6
fixes those; they are the only errors expected. If you see errors mentioning
`DialogueDisplayFramework`, the DDFC reference is still missing — see the Prerequisite section.

- [ ] **Step 7: Commit**

```bash
git add ModEntry.cs GetDataVectorPatch.cs DialogueBoxGeometry.cs
git commit -m "feat: resolve V2 pack modes and cache textures per asset in ModEntry"
```

---

### Task 6: Draw the emotion-matched player portrait

**Files:**
- Modify: `DrawPlayerPortraitPatch.cs` (substantially rewritten)

**Interfaces:**
- Consumes: `AnimationCursor` (Task 4); `EmotionResolver.ResolveSlot` / `.TryGetSheetCell` (Tasks 2-3); `ModEntry.HasActivePack` / `.ActiveMode` / `.ActivePackId` / `.ActiveTexturePath` / `.ActiveRect` / `.ActiveAnimation` / `.ActiveSheet` / `.ActiveEmotionAnimations` / `.ActiveEmotionMap` / `.TryGetTexture` / `.EmotionTexturePath` (Task 5).
- Produces: `DrawPlayerPortraitPatch.ResetAnimation()` (existing name, now delegating to the cursor) and `DrawPlayerPortraitPatch.ResetFallbackWarnings()` (called by `RefreshActivePack`).

The NPC's live expression comes from `dialogueBox.characterDialogue.getPortraitIndex()` — a public
method on `StardewValley.Dialogue` that returns the integer the game has ALREADY resolved from the
`$h` / `$s` tokens. Because the project compiles against the game assembly, a successful build is
proof the API exists; there is no reflection fallback to write.

- [ ] **Step 1: Replace the file's contents**

Replace `DrawPlayerPortraitPatch.cs` entirely with:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using DialogueDisplayFramework.Framework;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Draws the player portrait as a postfix on <see cref="DialogueBoxRenderer.DrawPortrait"/> —
    /// i.e. into the SAME SpriteBatch, one draw call after DDFC draws the NPC portrait. Drawing at
    /// the identical pipeline stage guarantees both portraits share the same layer relation to the
    /// box border: whatever the NPC does (tuck behind the frame), the player does too.
    ///
    /// Pinned (mirror of the NPC): bottom-LEFT corner flush to the box's top-LEFT corner, with the
    /// bottom on the frame's BorderTop (a bead above the content top) so it tucks under the frame.
    ///
    /// <para>V2 — emotion matching. The NPC's CURRENT portrait index is read here, at draw time, and
    /// the matching player slot is drawn. The index is the integer the game has already resolved from
    /// the dialogue's $h/$s tokens, so this works for any NPC regardless of which portrait mod is
    /// installed. Content Patcher decides WHICH art is loaded (its When conditions); this decides
    /// WHICH SLOT is on screen.</para>
    /// </summary>
    [HarmonyPatch(typeof(DialogueBoxRenderer), nameof(DialogueBoxRenderer.DrawPortrait))]
    public static class DrawPlayerPortraitPatch
    {
        // ── Animation runtime state ──────────────────────────────────────────────────────
        // One active pack → one player portrait → one playhead. The cursor also owns the V2 rule
        // that a changed emotion slot restarts at frame 0 (see AnimationCursor.Advance).
        private static readonly AnimationCursor Cursor = new();

        // Slots we've already warned about falling back to 0. Without this the warning would fire
        // 60× a second for the whole conversation. Cleared whenever the active pack is re-resolved.
        private static readonly HashSet<int> WarnedSlots = new();

        /// <summary>
        /// Reset the animation to frame 0. Called on portrait dialogue box OPEN and CLOSE — PPF's
        /// analog of SPO's "active item changed" — so every conversation starts at frame 0. NOT called
        /// on page-advance (same DialogueBox instance), so animation plays continuously across pages.
        /// </summary>
        internal static void ResetAnimation() => Cursor.Reset();

        /// <summary>Forget which slots have already logged a fallback (called when the active pack changes).</summary>
        internal static void ResetFallbackWarnings() => WarnedSlots.Clear();

        public static void Postfix(SpriteBatch b, DialogueBox dialogueBox)
        {
            try
            {
                if (!ModEntry.HasActivePack)
                    return;
                if (dialogueBox is null || !dialogueBox.isPortraitBox() || dialogueBox.isQuestion)
                    return;

                // The NPC's LIVE expression: the integer the game already resolved. NOT the $h/$s
                // tokens — those are vanilla dialogue syntax converted to an index before we see it.
                int npcPortraitIndex = dialogueBox.characterDialogue?.getPortraitIndex() ?? 0;
                int slot             = EmotionResolver.ResolveSlot(npcPortraitIndex, ModEntry.ActiveEmotionMap);

                if (!TryGetSource(slot, out var texture, out var source, out int srcW, out int srcH) || texture is null)
                    return;

                // CRITICAL (same gotcha as V1): the draw scale comes from ONE frame / ONE slot, never
                // from texture.Width/Height — that's the whole sheet or strip and would squash the face.
                int sourceSize = Math.Min(srcW, srcH);
                if (sourceSize <= 0)
                    sourceSize = 1024;

                var (boxLeft, _, contentTop, borderTop) = ModEntry.GetBoxRect();
                int basePortraitHeight = (int)(contentTop * ModEntry.PortraitHeightFactor);
                int portraitHeight     = (int)(basePortraitHeight * ModEntry.EffectivePlayerScale()); // author × player
                if (portraitHeight <= 0)
                    portraitHeight = 1;
                float scale = (float)portraitHeight / sourceSize;

                // Offsets ADD on top of the pinned position (author default + player nudge).
                int playerX = boxLeft + ModEntry.EffectivePlayerOffsetX();                      // flush to box left edge + nudge
                int playerY = (borderTop - portraitHeight) + ModEntry.EffectivePlayerOffsetY(); // bottom flush on frame top + nudge

                b.Draw(
                    texture, new Vector2(playerX, playerY), source, Color.White,
                    0f, Vector2.Zero, scale, SpriteEffects.None, 0.1f); // 0.1f matches the NPC portrait's LayerDepth
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Player portrait draw (DrawPortrait postfix) failed: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Picks this frame's texture and source rect for the active pack's mode, plus the ONE-slot /
        /// ONE-frame dimensions the draw scale must be derived from. Returns false when there is
        /// nothing loadable yet (the caller skips the frame and retries next draw).
        /// </summary>
        private static bool TryGetSource(int slot, out Texture2D? texture, out Rectangle source, out int srcW, out int srcH)
        {
            texture = null;
            source  = Rectangle.Empty;
            srcW    = 0;
            srcH    = 0;

            switch (ModEntry.ActiveMode)
            {
                // ── Mode 1 (= V1): one image, no emotion awareness. Unchanged behaviour. ──
                case PackMode.Simple:
                {
                    texture = ModEntry.ActiveTexturePath is null ? null : ModEntry.TryGetTexture(ModEntry.ActiveTexturePath);
                    if (texture is null)
                        return false;

                    if (ModEntry.ActiveAnimation is { } anim)
                    {
                        // Slot 0 always: a simple pack has no emotions, so the cursor never restarts
                        // mid-conversation and behaves exactly as V1's inline counter did.
                        Cursor.Advance(0, ElapsedMs(), anim.FrameCount, anim.FrameDurationMs, anim.FrameDurations);
                        source = FrameRect(anim, Cursor.Frame);
                        srcW   = anim.FrameWidth;
                        srcH   = anim.FrameHeight;
                        return true;
                    }

                    if (ModEntry.ActiveRect is { } rect)
                    {
                        source = new Rectangle(rect.X, rect.Y, rect.W, rect.H);
                        srcW   = rect.W;
                        srcH   = rect.H;
                        return true;
                    }

                    return false; // neither animation nor rect → RefreshActivePack already guarded this
                }

                // ── Mode 2: cell N of the one emotion sheet. ──
                case PackMode.EmotionStatic:
                {
                    texture = ModEntry.ActiveTexturePath is null ? null : ModEntry.TryGetTexture(ModEntry.ActiveTexturePath);
                    if (texture is null || ModEntry.ActiveSheet is not { } sheet)
                        return false;

                    // false = the cell falls outside the sheet → x,y are already 0,0 (slot 0). Array
                    // safety, separate from artistic intent: the author's slot-0 art is whatever they drew.
                    if (!EmotionResolver.TryGetSheetCell(slot, sheet, texture.Width, texture.Height, out int cellX, out int cellY))
                        WarnFallbackOnce(slot, $"is outside the {texture.Width}x{texture.Height} emotion sheet");

                    source = new Rectangle(cellX, cellY, sheet.SlotWidth, sheet.SlotHeight);
                    srcW   = sheet.SlotWidth;
                    srcH   = sheet.SlotHeight;
                    return true;
                }

                // ── Mode 3: one file per emotion, each with its own animation length. ──
                case PackMode.EmotionAnimated:
                {
                    if (!TryLoadEmotionSlot(slot, out texture, out var anim))
                    {
                        if (slot == 0)
                            return false;                       // even slot 0 is unavailable — skip the frame

                        WarnFallbackOnce(slot, "has no loadable art or animation data");
                        if (!TryLoadEmotionSlot(0, out texture, out anim))
                            return false;
                        slot = 0;
                    }

                    // Passing the RESOLVED slot (0 after a fallback) means two NPC moods that both fall
                    // back to 0 don't retrigger the restart on every index change.
                    Cursor.Advance(slot, ElapsedMs(), anim!.FrameCount, anim.FrameDurationMs, anim.FrameDurations);
                    source = FrameRect(anim, Cursor.Frame);
                    srcW   = anim.FrameWidth;
                    srcH   = anim.FrameHeight;
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Loads one mode-3 emotion slot: its animation data (from the pack registration) and its
        /// texture (<c>Custom/&lt;packId&gt;/PlayerPortrait/Emotion/&lt;slot&gt;</c>, which Content Patcher
        /// may have supplied several conditional variants of — CP picks, we just ask).
        /// </summary>
        private static bool TryLoadEmotionSlot(int slot, out Texture2D? texture, out AnimationSettings? anim)
        {
            texture = null;
            anim    = null;

            var animations = ModEntry.ActiveEmotionAnimations;
            string? packId = ModEntry.ActivePackId;
            if (animations is null || packId is null)
                return false;

            // Keys arrive from Content Patcher JSON as strings ("0", "1", …).
            if (!animations.TryGetValue(slot.ToString(CultureInfo.InvariantCulture), out anim) || anim is not { IsValid: true })
            {
                anim = null;
                return false;
            }

            texture = ModEntry.TryGetTexture(ModEntry.EmotionTexturePath(packId, slot));
            if (texture is null)
            {
                anim = null;
                return false;
            }

            return true;
        }

        /// <summary>Milliseconds since the previous draw, as the game reports them.</summary>
        private static double ElapsedMs() => Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 0;

        /// <summary>
        /// The current frame's source rectangle, walked from the sheet origin. <paramref name="anim"/>
        /// is assumed valid (count/width/height &gt; 0), so there is no divide-by-zero. Columns 0 = a
        /// single horizontal strip, which is the usual shape for a mode-3 per-emotion file.
        /// </summary>
        private static Rectangle FrameRect(AnimationSettings anim, int frame)
        {
            int columns = anim.Columns > 0 ? anim.Columns : anim.FrameCount;
            int row     = frame / columns;
            int col     = frame % columns;
            return new Rectangle(col * anim.FrameWidth, row * anim.FrameHeight, anim.FrameWidth, anim.FrameHeight);
        }

        /// <summary>
        /// Logs a slot's fallback-to-0 ONCE per active pack. The draw runs every frame, so an
        /// unconditional log here would bury the SMAPI console for the length of the conversation.
        /// </summary>
        private static void WarnFallbackOnce(int slot, string reason)
        {
            if (!WarnedSlots.Add(slot))
                return;
            ModEntry.SMonitor.Log($"Player portrait emotion slot {slot} {reason} — falling back to slot 0.", LogLevel.Warn);
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
dotnet build
```

Expected: PASS — `Build succeeded`, 0 errors. (ModBuildConfig also copies the output into the game's
`Mods` folder.)

- [ ] **Step 3: Re-run the unit tests (nothing should have regressed)**

```bash
dotnet test "tests/PlayerPortraitsFramework.Tests"
```

Expected: PASS — 49 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add DrawPlayerPortraitPatch.cs
git commit -m "feat: draw the player emotion slot matching the NPC's live portrait index"
```

---

### Task 7: In-game acceptance packs + documentation

**Files:**
- Create: test content packs under the game's `Mods` folder (throwaway, NOT committed)
- Modify: `PlayerPortraitsFramework_Pack_Author_Guide.txt`
- Modify: `manifest.json` (version → `1.0.0`, description)
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above. Produces: no code.

- [ ] **Step 1: Generate the acceptance art**

`docs/superpowers/plans/new-ppf-test-art.ps1` (committed alongside this plan, already verified to
run on this machine) paints every cell and frame a distinct flat colour with a large white digit and
a `BASE` / `WINTER` / `E0` / `E1` tag, so the slot and frame actually on screen are unmistakable.
Run it once — it writes all five PNGs for both packs below:

```bash
powershell -ExecutionPolicy Bypass \
  -File "docs/superpowers/plans/new-ppf-test-art.ps1" \
  -OutRoot "C:/Users/dakot/Documents/Applications/Steam/steamapps/common/Stardew Valley/Mods"
```

Expected output — five lines confirming the sizes:

```
wrote ..\_PPFTestEmotionSheet\assets\sheet.png  (512x768)
wrote ..\_PPFTestEmotionSheet\assets\sheet_winter.png  (512x768)
wrote ..\_PPFTestEmotionAnim\assets\emotion0.png  (1024x256)
wrote ..\_PPFTestEmotionAnim\assets\emotion1.png  (512x256)
wrote ..\_PPFTestEmotionAnim\assets\emotion1_winter.png  (512x256)
```

- [ ] **Step 2: Build a Mode 2 acceptance pack**

Create `<game>/Mods/_PPFTestEmotionSheet/manifest.json`:

```json
{
  "Name": "PPF Test — Emotion Sheet",
  "Author": "test",
  "Version": "1.0.0",
  "UniqueID": "test.PPFEmotionSheet",
  "ContentPackFor": { "UniqueID": "Pathoschild.ContentPatcher", "MinimumVersion": "2.0.0" },
  "Dependencies": [ { "UniqueID": "dakot.PlayerPortraitsFramework", "IsRequired": true } ]
}
```

and `<game>/Mods/_PPFTestEmotionSheet/content.json`:

```json
{
  "Format": "2.0.0",
  "Changes": [
    {
      "Action": "Load",
      "Target": "Custom/test.PPFEmotionSheet/PlayerPortrait/Sheet",
      "FromFile": "assets/sheet.png"
    },
    {
      "Action": "Load",
      "Target": "Custom/test.PPFEmotionSheet/PlayerPortrait/Sheet",
      "FromFile": "assets/sheet_winter.png",
      "When": { "Season": "winter" }
    },
    {
      "Action": "EditData",
      "Target": "dakot.PlayerPortraitsFramework/Packs",
      "Entries": {
        "test.PPFEmotionSheet": {
          "PackName": "PPF Test — Emotion Sheet",
          "Author": "test",
          "EmotionSheet": { "SlotWidth": 256, "SlotHeight": 256, "Columns": 2 },
          "EmotionMap": { "3": 0 }
        }
      }
    }
  ]
}
```

Step 1 already wrote this pack's `assets/sheet.png` (512×768 — 2 columns × 3 rows of 256px cells,
slots 0-5) and `assets/sheet_winter.png` (same layout, rotated palette).

- [ ] **Step 3: Verify Mode 2 in game**

Launch the game, load a save, and talk to an NPC whose dialogue changes expression mid-conversation
(Abigail, Haley, and Sebastian all have `$h`/`$s`/`$u` lines; `LookupAnything` is installed and can
confirm an NPC's dialogue). Confirm each of these:

- The player portrait shows digit **0** while the NPC is neutral.
- When the NPC's face changes, the player's digit changes to match the NPC's index (happy → 1, sad → 2).
- Index **3** shows digit **0** (the `EmotionMap` exception), while unlisted indices stay 1:1.
- SMAPI's console shows `Active pack: test.PPFEmotionSheet  (emotion sheet, …)` at startup.
- Any out-of-range index logs `falling back to slot 0` **exactly once**, not once per frame.

Then use CJBCheatsMenu (installed) to set the season to winter and confirm the winter sheet replaces
**all** emotions at once.

- [ ] **Step 4: Build a Mode 3 acceptance pack**

Create `<game>/Mods/_PPFTestEmotionAnim/manifest.json` (same shape as Step 1, with
`"UniqueID": "test.PPFEmotionAnim"` and `"Name": "PPF Test — Emotion Animations"`), and
`content.json`:

```json
{
  "Format": "2.0.0",
  "Changes": [
    {
      "Action": "Load",
      "Target": "Custom/test.PPFEmotionAnim/PlayerPortrait/Emotion/0",
      "FromFile": "assets/emotion0.png"
    },
    {
      "Action": "Load",
      "Target": "Custom/test.PPFEmotionAnim/PlayerPortrait/Emotion/1",
      "FromFile": "assets/emotion1.png"
    },
    {
      "Action": "Load",
      "Target": "Custom/test.PPFEmotionAnim/PlayerPortrait/Emotion/1",
      "FromFile": "assets/emotion1_winter.png",
      "When": { "Season": "winter" }
    },
    {
      "Action": "EditData",
      "Target": "dakot.PlayerPortraitsFramework/Packs",
      "Entries": {
        "test.PPFEmotionAnim": {
          "PackName": "PPF Test — Emotion Animations",
          "Author": "test",
          "EmotionAnimations": {
            "0": { "FrameWidth": 256, "FrameHeight": 256, "FrameCount": 4, "FrameDurationMs": 200 },
            "1": { "FrameWidth": 256, "FrameHeight": 256, "FrameCount": 2, "FrameDurationMs": 300 }
          }
        }
      }
    }
  ]
}
```

Step 1 already wrote this pack's `assets/emotion0.png` (1024×256, a four-frame strip numbered 0-3),
`assets/emotion1.png` (512×256, two frames, different palette) and `assets/emotion1_winter.png`
(512×256, two frames, third palette).

- [ ] **Step 5: Verify Mode 3 in game**

Select this pack in GMCM (**Player Portrait → Portrait Pack**), then confirm:

- Slot 0 cycles 4 frames at 200ms; slot 1 cycles 2 frames at 300ms — **each emotion plays its own
  length**, so the two must visibly differ in speed and cycle length.
- Changing the NPC's expression mid-conversation makes the player's animation **restart at frame 0**
  (the new emotion's first frame appears immediately, never mid-cycle).
- An NPC index with no declared slot (e.g. 2 or 4) falls back to slot 0 and logs once.
- Frames are **not squashed** — the aspect comes from `FrameWidth`/`FrameHeight`, not the strip.
- With the season set to winter, slot 1 uses `emotion1_winter.png` while slot 0 is unchanged.

- [ ] **Step 6: Verify Mode 1 has not regressed**

This is the highest-priority acceptance check. Install (or re-enable) an existing V1 pack — one
declaring only `Portrait`, and one declaring only `Animation` — select each in GMCM, and confirm the
portrait renders **exactly as it did before this work**: same position, same size, same animation
timing, and no emotion switching. Confirm the dialogue box geometry (width, height, name plate
position, no friendship jewel) is unchanged, which is what the `HasActivePack` gate change in Task 5
protects.

- [ ] **Step 7: Document the three modes**

In `PlayerPortraitsFramework_Pack_Author_Guide.txt`, bump the header to `(v2.0)`, and insert a new
section between the current section 3 (Animated portraits) and section 4 (Author-declared defaults),
renumbering the later sections:

```
--------------------------------------------------------------------------
 4. Emotion matching — optional
--------------------------------------------------------------------------

The framework can match the PLAYER's expression to the NPC's. It reads the NPC's
CURRENT portrait index while the dialogue is on screen and draws your matching
slot — so it works with any NPC and any NPC-portrait mod, with no per-character
setup and nothing to write about $h / $s dialogue tokens.

Two layers, and they do not compete:
  • CONTENT PATCHER decides WHICH ART IS LOADED. Put any When condition you like
    (season, weather, hearts, time, indoors) on your Load entries. The framework
    never knows conditions exist.
  • THE FRAMEWORK decides WHICH SLOT IS ON SCREEN, from the NPC's live index.

Your pack is exactly ONE of three modes:
  Mode 1  Simple            — sections 2 and 3 above. One image. No emotion
                              awareness. Unchanged from v1; existing packs keep
                              working with no edits.
  Mode 2  Emotion, static   — one sheet carrying every emotion.
  Mode 3  Emotion, animated — one file per emotion, each with its own animation.

Do not mix modes in one pack. If you declare several anyway, the framework picks
mode 3 over mode 2 over mode 1 and ignores the rest.


 4a. Mode 2 — one sheet, all emotions
--------------------------------------------------------------------------

Load ONE sheet to:   Custom/<your-pack-UniqueID>/PlayerPortrait/Sheet

Declare the slot size so the framework can compute cell N's rectangle. Columns
defaults to 2, matching vanilla portrait sheets:

  "you.MyPlayerPortraitPack": {
    "PackName": "My Player Portrait Pack",
    "Author": "You",
    "EmotionSheet": { "SlotWidth": 1024, "SlotHeight": 1024, "Columns": 2 }
  }

Cells are numbered left-to-right, top-to-bottom: slot 0 top-left, slot 1 to its
right, slot 2 starting the second row, and so on.

THE SHEET IS THE COSTUME UNIT. To vary emotions by season / weather / hearts,
write a SECOND Load to the SAME target with a When condition — one winter sheet
carrying all the emotions, one condition. Do NOT split static emotions into
per-emotion files:

  { "Action": "Load", "Target": "Custom/you.MyPack/PlayerPortrait/Sheet",
    "FromFile": "assets/emotions.png" },
  { "Action": "Load", "Target": "Custom/you.MyPack/PlayerPortrait/Sheet",
    "FromFile": "assets/emotions_winter.png", "When": { "Season": "winter" } }


 4b. Mode 3 — one file per emotion, animated
--------------------------------------------------------------------------

Load ONE FILE PER EMOTION to:
    Custom/<your-pack-UniqueID>/PlayerPortrait/Emotion/<slotIndex>

Not a grid — animations have different lengths per emotion, and a grid would
force you to pad every emotion to the longest one and produce an absurd PNG.

Give each slot its own animation block. The fields are exactly the ones from
section 3, so there is nothing new to learn:

  "you.MyPlayerPortraitPack": {
    "PackName": "My Player Portrait Pack",
    "Author": "You",
    "EmotionAnimations": {
      "0": { "FrameWidth": 1024, "FrameHeight": 1024, "FrameCount": 4, "FrameDurationMs": 200 },
      "1": { "FrameWidth": 1024, "FrameHeight": 1024, "FrameCount": 2, "FrameDurationMs": 300 }
    }
  }

  • Slot keys are STRINGS ("0", "1", …).
  • Columns, FrameDurationMs and FrameDurations work exactly as in section 3.
  • Aspect ratio comes from FrameWidth/FrameHeight — never the whole strip.
  • Each emotion restarts at FRAME 0 when the NPC's expression changes, so every
    expression plays its own beat from the top.

Seasonal / conditional variants = several Loads to the SAME slot target with
different When conditions. Content Patcher resolves which one wins; the framework
just asks for the slot:

  { "Action": "Load", "Target": "Custom/you.MyPack/PlayerPortrait/Emotion/1",
    "FromFile": "assets/happy.png" },
  { "Action": "Load", "Target": "Custom/you.MyPack/PlayerPortrait/Emotion/1",
    "FromFile": "assets/happy_winter.png", "When": { "Season": "winter" } }


 4c. EmotionMap — reusing one face across several moods
--------------------------------------------------------------------------

By default the mapping is 1:1 — NPC index N draws your slot N. Declare ONLY the
exceptions; anything you leave out stays 1:1:

  "EmotionMap": { "1": 4, "2": 0, "3": 0 }

Here NPC index 1 draws your slot 4, indices 2 and 3 both draw slot 0, and every
other index is unchanged. Use it to reuse one drawn face across several NPC moods
instead of drawing near-duplicates.


 4d. Missing slots
--------------------------------------------------------------------------

If an NPC's index has no slot — outside your sheet, or a file you never supplied —
the framework draws SLOT 0 and logs a warning ONCE. This is array safety, not a
statement about your art: slot 0 holds whatever you put there. You do NOT need to
supply every index; give slot 0 a sensible neutral face and the rest is optional.
```

Also update the guide's intro (line 9-10) so step 1 reads "LOADS its player face texture(s) to a
conventional asset target, and", and add a line to the IMPORTANT block in section 2:

```
  • The Load target depends on your mode — see section 4 for the emotion targets.
```

- [ ] **Step 8: Bump the manifest and README**

In `manifest.json`, set:

```json
  "Version": "1.0.0",
  "Description": "Draws a player portrait alongside the NPC portrait in dialogue, sourced from separately-installed portrait packs. V2: the player's expression matches the NPC's live portrait index, via a single emotion sheet or one animated file per emotion.",
```

In `README.md`, add the three modes to the feature list, matching the file's existing tone and
heading style.

- [ ] **Step 9: Remove the throwaway test packs**

```bash
rm -rf "C:/Users/dakot/Documents/Applications/Steam/steamapps/common/Stardew Valley/Mods/_PPFTestEmotionSheet"
rm -rf "C:/Users/dakot/Documents/Applications/Steam/steamapps/common/Stardew Valley/Mods/_PPFTestEmotionAnim"
```

- [ ] **Step 10: Commit**

```bash
git add PlayerPortraitsFramework_Pack_Author_Guide.txt manifest.json README.md docs
git commit -m "docs: document the three pack modes and bump to v1.0.0"
```

---

## Acceptance Traceability

| Spec acceptance criterion | Verified by |
|---|---|
| Mode 1 packs behave identically to V1 | Task 1 `ResolveMode` tests; Task 6 Step 1 (`PackMode.Simple` branch is V1's code); Task 7 Step 6 (in-game) |
| Mode 2: NPC expression change swaps the player's sheet cell | Task 2 + Task 3 tests; Task 7 Step 3 |
| Mode 2 + CP condition on the sheet: seasonal sheet swaps all emotions at once | Task 5 Step 4 (invalidation clears the cache); Task 7 Step 3 |
| Mode 3: each emotion plays its own animation at its own length | Task 4 tests; Task 7 Step 5 |
| Mode 3: changing emotion restarts at frame 0 | Task 4 `SlotChange_RestartsAtFrameZeroAndClearsElapsed`; Task 7 Step 5 |
| Mode 3 + CP conditions on individual slot targets | Task 5 Step 4; Task 7 Step 5 |
| `EmotionMap` remaps as declared; unlisted indices 1:1 | Task 2 `ListedIndex_IsRemapped` / `UnlistedIndex_StaysOneToOne`; Task 7 Step 3 |
| Missing / out-of-range slot falls back to slot 0, no error spam | Task 3 fallback tests; Task 6 `WarnFallbackOnce` + `FailedAssets` memo; Task 7 Steps 3 and 5 |
