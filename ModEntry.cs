using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        /// <summary>The pack-registration dictionary asset. Packs <c>EditData</c> their entry into this.</summary>
        public const string PacksAssetName = "dakot.PlayerPortraitsFramework/Packs";

        private const string DdfcUniqueId = "Mangupix.DialogueDisplayFrameworkContinued";

        // ── Milestone 8 box geometry — viewport-relative fractions ───────────────────────────
        // The M4 look was tuned to a one-liner on a 1920×1080 screen, so those pixel values are
        // really 1080p ratios. Express them as fractions of the live viewport so the SAME
        // proportions hold at any resolution (the look is locked — this only makes it scale).
        // The scaled accessors use Math.Round so that at exactly 1920×1080 each one reproduces its
        // original pixel value (1700 / 200 / 64 / 1660) — nothing about the 1080p layout changes.
        // Font does NOT shrink, so a wordy NPC on a short box may still spill; the wide width buys
        // lines back. Twiddle the fractions, not the call sites.
        internal const double BoxWidthFrac    = 1700.0 / 1920.0; // ≈ 0.885 of viewport width
        internal const double BoxHeightFrac   = 200.0  / 1080.0; // ≈ 0.185 of viewport height
        internal const double BoxMarginFrac   = 64.0   / 1080.0; // ≈ 0.0593 of viewport height (bottom gap)
        internal const double DialoguePadFrac = 40.0   / 1920.0; // ≈ 0.0208 of viewport width (text inset from box width)

        /// <summary>Scaled box width for the given viewport width (≈0.885·vpW; 1700 at 1920).</summary>
        internal static int BoxWidth(int vpW)  => (int)Math.Round(vpW * BoxWidthFrac);
        /// <summary>Scaled box height for the given viewport height (≈0.185·vpH; 200 at 1080).</summary>
        internal static int BoxHeight(int vpH) => (int)Math.Round(vpH * BoxHeightFrac);
        /// <summary>Scaled bottom margin for the given viewport height (≈0.0593·vpH; 64 at 1080).</summary>
        internal static int BoxMargin(int vpH) => (int)Math.Round(vpH * BoxMarginFrac);
        /// <summary>Scaled dialogue-text width: the scaled box width minus a proportional inset (1660 at 1920).</summary>
        internal static int DialogueWidth(int vpW) => BoxWidth(vpW) - (int)Math.Round(vpW * DialoguePadFrac);

        // ── Portrait placement constants ──
        internal const float PortraitHeightFactor = 0.80f; // portrait height = space-above-box * this
        internal const int   BorderThickness      = 16;    // vanilla dialogue frame bead above the content top

        // ── Name plate placement (anchored near the box's right edge, over the NPC). Twiddle me. ──
        // Confirmed in-game (M7): the speaker name's LEFT edge is the fixed anchor and longer names
        // grow RIGHTWARD. With Name.Right = true the anchor is the box's RIGHT edge, so the left edge
        // lands at (boxRight + XOffset); more-negative XOffset moves that anchor further left, giving
        // long names more rightward runway before they reach the box's right side. The risk is the
        // RIGHT reach on the LONGEST name (Caroline / Demetrius / Sebastian), not the left.
        // -150 measured from a screenshot: at -100 the longest name (Sebastian) scroll overran the
        // content right edge (boxRight=1809px) by 45px into the border/black; -150 lands its right
        // edge ~5px inside the content, just touching the brown border. Shorter names land further left.
        internal const int NameXOffset = -150; // left anchor, this far left of the box's right edge
        internal const int NameYOffset = -60;  // above the box's top edge (like vanilla name scrolls)

        internal static IMonitor   SMonitor = null!;
        internal static IModHelper SHelper  = null!;

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

        // ── Player config + GMCM (Milestone 9) ───────────────────────────────────────────────
        internal static ModConfig Config = new();               // player prefs; overwritten from disk in Entry
        private  static IGenericModConfigMenuApi? _gmcm;        // null when GMCM isn't installed
        private  static bool _gmcmRegistered;                   // so we can Unregister before re-registering

        // The ACTIVE pack's author-declared defaults, captured in RefreshActivePack with framework
        // baselines filled in (scale ×1.0, offset 0, name shown). The player's GMCM prefs stack on
        // these: offsets ADD, scales MULTIPLY. The patches read the Effective* helpers below.
        private static float ActivePlayerScale   = 1f;
        private static int   ActivePlayerOffsetX;
        private static int   ActivePlayerOffsetY;
        private static float ActiveNpcScale      = 1f;
        private static int   ActiveNpcOffsetX;
        private static int   ActiveNpcOffsetY;
        private static float ActiveBoxHeight     = 1f;
        private static bool  ActiveNameHidden;

        // Stacking: scale MULTIPLIES (author default × player percent), offsets ADD (author + player).
        internal static float EffectivePlayerScale()   => ActivePlayerScale * (Config.PlayerScalePct / 100f);
        internal static int   EffectivePlayerOffsetX() => ActivePlayerOffsetX + Config.PlayerOffsetX;
        internal static int   EffectivePlayerOffsetY() => ActivePlayerOffsetY + Config.PlayerOffsetY;
        internal static float EffectiveNpcScale()      => ActiveNpcScale * (Config.NpcScalePct / 100f);
        internal static int   EffectiveNpcOffsetX()    => ActiveNpcOffsetX + Config.NpcOffsetX;
        internal static int   EffectiveNpcOffsetY()    => ActiveNpcOffsetY + Config.NpcOffsetY;
        internal static float EffectiveBoxHeight()     => ActiveBoxHeight * (Config.BoxHeightPct / 100f);
        // Bool can't add/multiply: hide if EITHER the author default OR the player toggle says hide.
        internal static bool  EffectiveNameHidden()    => ActiveNameHidden || Config.NameHidden;

        public override void Entry(IModHelper helper)
        {
            SMonitor = Monitor;
            SHelper  = helper;
            Config   = helper.ReadConfig<ModConfig>();

            helper.Events.Content.AssetRequested    += OnAssetRequested;
            helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
            helper.Events.GameLoop.GameLaunched     += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded       += OnSaveLoaded;
            helper.Events.Display.MenuChanged       += OnMenuChanged;

            // Geometry + draw, all via Harmony:
            //  • GetDataVectorPatch       → NPC portrait corner-pin (TopRight).
            //  • DrawPlayerPortraitPatch  → player portrait, drawn at the SAME pipeline stage as the
            //    NPC portrait so the two share an identical layer relation to the box border.
            //  • DialogueBoxGeometry      → box resize + jewel cut + name (internal DDFC method).
            var harmony = new Harmony(ModManifest.UniqueID);
            harmony.PatchAll(typeof(ModEntry).Assembly);
            DialogueBoxGeometry.ApplyManualPatches(harmony, Monitor);
        }

        // ── Pack registration asset ────────────────────────────────────────────────────
        // Serve an empty dictionary by default; packs register into it with Content Patcher EditData.
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(PacksAssetName))
                e.LoadFrom(() => new Dictionary<string, PackRegistration>(), AssetLoadPriority.Low);
        }

        private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
        {
            if (e.NamesWithoutLocale.Any(n => n.IsEquivalentTo(PacksAssetName)))
            {
                RefreshActivePack();
                ConfigureGmcm(); // packs changed → rebuild the dropdown (allowedValues is baked at registration)
            }

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
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            if (!Helper.ModRegistry.IsLoaded(DdfcUniqueId))
                Monitor.Log("Dialogue Display Framework Continued is not installed — the two-portrait layout will not work.", LogLevel.Warn);

            // DDFCScaler also patches the box / NPC-portrait geometry; running both fights over the
            // same layout. Warn (don't block) — the user can disable one.
            if (Helper.ModRegistry.IsLoaded("dakot.DDFCScaler"))
                Monitor.Log("DDFCScaler is installed — it also controls dialogue box / portrait geometry and will conflict with this framework. Disable DDFCScaler to avoid a broken layout.", LogLevel.Warn);

            _gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (_gmcm is null)
                Monitor.Log("Generic Mod Config Menu is not installed — the in-game config menu is unavailable (config.json still applies).", LogLevel.Info);

            RefreshActivePack();
            ConfigureGmcm(); // build the menu after packs are known (the pack dropdown lists them)
        }

        /// <summary>
        /// The single funnel for the layout viewport. EVERY dimension read in the layout code goes
        /// through here — never <c>Game1.uiViewport</c> directly — so the box, both portraits, and the
        /// DDFC box-stamp all measure against the same surface.
        /// <para>Desktop only for now: returns <c>Game1.uiViewport</c>. Android is intentionally CUT,
        /// but its known future shape (DDFCScaler's pattern: a platform check that swaps in the Android
        /// surface, plus a separate draw hook) bolts on by editing THIS one method — not by hunting
        /// down scattered viewport reads. Build the door, don't walk through it.</para>
        /// </summary>
        internal static (int vpW, int vpH) GetViewport()
        {
            // Android branch goes HERE when it's un-cut (platform check + surface swap). Desktop today.
            return (Game1.uiViewport.Width, Game1.uiViewport.Height);
        }

        /// <summary>
        /// The framework's authoritative box footprint: top-left corner + dimensions. The ONE place the
        /// box-height control (<see cref="EffectiveBoxHeight"/>) is applied — the box resize stamp and
        /// <see cref="GetBoxRect"/> both derive from this, so the DDFC box and the portrait pins always
        /// agree. The box-HEIGHT control (<see cref="EffectiveBoxHeight"/>) affects HEIGHT ONLY (M9.1):
        /// the WIDTH stays locked at its viewport-relative value, so the full-width layout never changes.
        /// A taller box grows UPWARD — the bottom margin does NOT scale, so it stays pinned to the same
        /// gap above the screen bottom — and stays horizontally centered.
        /// </summary>
        internal static (int X, int Y, int W, int H) GetScaledBox()
        {
            var (vpW, vpH) = GetViewport();
            float h = EffectiveBoxHeight();

            int boxWidth  = BoxWidth(vpW);                       // width is NOT affected by the box-height control
            int boxHeight = (int)Math.Round(BoxHeight(vpH) * h); // height scales (more/fewer text lines)
            int boxMargin = BoxMargin(vpH);                      // fixed gap → box stays bottom-anchored as it scales

            int x = (vpW - boxWidth) / 2;                   // centered horizontally (width fixed → never drifts)
            int y = vpH - boxMargin - boxHeight;            // bottom-anchored box top (== content top)
            return (x, y, boxWidth, boxHeight);
        }

        /// <summary>
        /// The framework's authoritative dialogue box rectangle, computed from its own constants —
        /// NOT read from DDFC's box state (which can carry a stale/default width). Both portraits and
        /// the box resize derive from this, so they always agree.
        /// <para><c>ContentTop</c> is the box's content top edge (where DDFC anchors the box). The
        /// vanilla frame draws a bead ~<see cref="BorderThickness"/>px above that; <c>BorderTop</c> is
        /// that frame top. Portraits pin their bottoms to <c>BorderTop</c> so they sit flush on the
        /// visible frame edge and tuck behind it.</para>
        /// </summary>
        internal static (int Left, int Right, int ContentTop, int BorderTop) GetBoxRect()
        {
            var (x, y, w, _) = GetScaledBox();
            return (x, x + w, y, y - BorderThickness);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e) => RefreshActivePack();

        /// <summary>
        /// Reset the player-portrait animation to frame 0 whenever a portrait dialogue box opens or
        /// closes — PPF's analog of SPO's "active item changed". A page-advance within a conversation
        /// reuses the SAME <see cref="DialogueBox"/> instance (no MenuChanged), so animation plays
        /// continuously across pages and only resets per open/close.
        /// </summary>
        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            bool opened = e.NewMenu is DialogueBox;
            bool closed = e.OldMenu is DialogueBox;
            if (opened || closed)
                DrawPlayerPortraitPatch.ResetAnimation();
        }

        private Dictionary<string, PackRegistration> GetPacks() =>
            Helper.GameContent.Load<Dictionary<string, PackRegistration>>(PacksAssetName);

        /// <summary>
        /// The active pack ID: the player's GMCM selection when it names an installed pack, otherwise
        /// the "Set All" default — first alphabetically by UniqueID (also the lone pack when only one
        /// is installed). Caller guarantees <paramref name="packs"/> is non-empty.
        /// </summary>
        private string ResolveSelectedPackId(Dictionary<string, PackRegistration> packs)
        {
            if (Config.SelectedPack != "Auto" && packs.ContainsKey(Config.SelectedPack))
                return Config.SelectedPack;
            return packs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).First();
        }

        // ── GMCM (Milestone 9) ───────────────────────────────────────────────────────────
        /// <summary>
        /// (Re)builds the GMCM page. Re-registers from scratch because the pack dropdown's
        /// allowedValues are baked at registration, so a newly-loaded pack only appears after a
        /// rebuild. Saving writes config.json and re-resolves the active pack so changes apply live
        /// (no restart). No-op when GMCM isn't installed.
        /// </summary>
        private void ConfigureGmcm()
        {
            if (_gmcm is null)
                return;

            if (_gmcmRegistered)
                _gmcm.Unregister(ModManifest);
            _gmcmRegistered = true;

            Dictionary<string, PackRegistration> packs;
            try { packs = GetPacks(); }
            catch { packs = new(); }

            _gmcm.Register(
                mod: ModManifest,
                reset: () =>
                {
                    Config = new ModConfig();
                    RefreshActivePack();
                    DrawPlayerPortraitPatch.ResetAnimation();
                },
                save: () =>
                {
                    Helper.WriteConfig(Config);
                    RefreshActivePack();                       // re-resolve selection + author defaults
                    DrawPlayerPortraitPatch.ResetAnimation();  // selection may have swapped the sheet
                });

            const int OffMin = -400, OffMax = 400, OffStep = 5;
            const int ScaleMin = 25, ScaleMax = 200, ScaleStep = 5;

            // ── Player portrait ──────────────────────────────────────────────
            _gmcm.AddSectionTitle(ModManifest, () => "Player Portrait");

            var packOptions = new[] { "Auto" }
                .Concat(packs.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            _gmcm.AddTextOption(
                mod: ModManifest,
                name: () => "Portrait Pack",
                tooltip: () => "Which installed pack supplies the player portrait.",
                getValue: () => (Config.SelectedPack == "Auto" || packs.ContainsKey(Config.SelectedPack)) ? Config.SelectedPack : "Auto",
                setValue: v => Config.SelectedPack = v,
                allowedValues: packOptions,
                formatAllowedValue: v => v == "Auto"
                    ? "Automatic (first pack)"
                    : (packs.TryGetValue(v, out var p) ? (p?.PackName ?? v) : v));

            _gmcm.AddNumberOption(ModManifest, () => Config.PlayerScalePct, v => Config.PlayerScalePct = v,
                () => "Player Scale", () => "Scale the player portrait (percent of the author's default).",
                min: ScaleMin, max: ScaleMax, interval: ScaleStep, formatValue: v => $"{v}%");
            _gmcm.AddNumberOption(ModManifest, () => Config.PlayerOffsetX, v => Config.PlayerOffsetX = v,
                () => "Player Offset X", () => "Nudge the player portrait horizontally, in pixels (adds to the author default).",
                min: OffMin, max: OffMax, interval: OffStep);
            _gmcm.AddNumberOption(ModManifest, () => Config.PlayerOffsetY, v => Config.PlayerOffsetY = v,
                () => "Player Offset Y", () => "Nudge the player portrait vertically, in pixels (adds to the author default).",
                min: OffMin, max: OffMax, interval: OffStep);

            // ── NPC portrait ─────────────────────────────────────────────────
            _gmcm.AddSectionTitle(ModManifest, () => "NPC Portrait");
            _gmcm.AddNumberOption(ModManifest, () => Config.NpcScalePct, v => Config.NpcScalePct = v,
                () => "NPC Scale", () => "Scale the NPC portrait (percent of the author's default).",
                min: ScaleMin, max: ScaleMax, interval: ScaleStep, formatValue: v => $"{v}%");
            _gmcm.AddNumberOption(ModManifest, () => Config.NpcOffsetX, v => Config.NpcOffsetX = v,
                () => "NPC Offset X", () => "Nudge the NPC portrait horizontally, in pixels (adds to the author default).",
                min: OffMin, max: OffMax, interval: OffStep);
            _gmcm.AddNumberOption(ModManifest, () => Config.NpcOffsetY, v => Config.NpcOffsetY = v,
                () => "NPC Offset Y", () => "Nudge the NPC portrait vertically, in pixels (adds to the author default).",
                min: OffMin, max: OffMax, interval: OffStep);

            // ── Dialogue box ─────────────────────────────────────────────────
            _gmcm.AddSectionTitle(ModManifest, () => "Dialogue Box");
            _gmcm.AddNumberOption(ModManifest, () => Config.BoxHeightPct, v => Config.BoxHeightPct = v,
                () => "Box Height",
                () => "Make the dialogue box TALLER or shorter (the width stays the same). A taller box "
                    + "fits more lines of text. This scales the SPACE, not the font — for bigger TEXT, "
                    + "use the game's UI Scale option.",
                min: 50, max: ScaleMax, interval: ScaleStep, formatValue: v => $"{v}%");

            // ── Name plate ───────────────────────────────────────────────────
            _gmcm.AddSectionTitle(ModManifest, () => "Name Plate");
            _gmcm.AddBoolOption(ModManifest, () => Config.NameHidden, v => Config.NameHidden = v,
                () => "Hide NPC Name", () => "Hide the speaker's name plate.");
        }

        // ── Detect + pick ──────────────────────────────────────────────────────────────
        private void RefreshActivePack()
        {
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

            // Reset author defaults to framework baselines until a pack is resolved below.
            ActivePlayerScale = 1f; ActivePlayerOffsetX = 0; ActivePlayerOffsetY = 0;
            ActiveNpcScale    = 1f; ActiveNpcOffsetX    = 0; ActiveNpcOffsetY    = 0;
            ActiveBoxHeight   = 1f; ActiveNameHidden    = false;

            Dictionary<string, PackRegistration> packs;
            try
            {
                packs = GetPacks();
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to read packs asset: {ex.Message}", LogLevel.Warn);
                return;
            }

            if (packs.Count == 0)
            {
                Monitor.Log("No player portrait packs detected.", LogLevel.Info);
                return;
            }

            Monitor.Log($"Detected {packs.Count} player portrait pack(s):", LogLevel.Info);
            foreach (var (id, reg) in packs.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                Monitor.Log($"  • {id}  —  \"{reg?.PackName ?? "(no name)"}\" by {reg?.Author ?? "(unknown)"}", LogLevel.Info);

            // Selection (M9): the player's GMCM choice if it names an installed pack; otherwise "Set
            // All" default — the first pack alphabetically by UniqueID (deterministic; also the lone
            // pack when only one is installed).
            string activeId = ResolveSelectedPackId(packs);
            var active = packs[activeId];

            // Capture this pack's author-declared defaults (framework baseline where omitted). The
            // player's GMCM prefs stack on these at draw time via the Effective* helpers.
            // Author scales are PERCENT ints (100 = baseline); convert to internal float multipliers.
            ActivePlayerScale   = (active?.DefaultPlayerScale ?? 100) / 100f;
            ActivePlayerOffsetX = active?.DefaultPlayerOffsetX ?? 0;
            ActivePlayerOffsetY = active?.DefaultPlayerOffsetY ?? 0;
            ActiveNpcScale      = (active?.DefaultNpcScale ?? 100) / 100f;
            ActiveNpcOffsetX    = active?.DefaultNpcOffsetX     ?? 0;
            ActiveNpcOffsetY    = active?.DefaultNpcOffsetY     ?? 0;
            ActiveBoxHeight     = (active?.DefaultBoxHeight ?? 100) / 100f;
            ActiveNameHidden    = active?.DefaultNameHidden     ?? false;

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
        }

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
    }
}
