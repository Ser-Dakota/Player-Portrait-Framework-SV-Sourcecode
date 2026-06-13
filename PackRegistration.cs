#nullable enable
namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Registration record for a player portrait pack, served via the
    /// <c>dakot.PlayerPortraitsFramework/Packs</c> dictionary asset (keyed by pack ID).
    ///
    /// Milestone 2 (approach C, own-draw): the framework draws the portrait itself as a
    /// plain overlay, so the pack now declares the <see cref="Portrait"/> source rect to
    /// draw from its texture. The texture itself is located by convention
    /// (<c>Custom/&lt;packId&gt;/PlayerPortrait</c>). No DDFC image entry anywhere.
    /// </summary>
    public class PackRegistration
    {
        public string? PackName { get; set; }
        public string? Author   { get; set; }

        /// <summary>
        /// The static source rectangle (in pixels) to draw from the pack's texture. Required for a
        /// static pack; IGNORED when <see cref="Animation"/> is set and valid.
        /// </summary>
        public PortraitRect? Portrait { get; set; }

        /// <summary>
        /// Optional animation. <c>null</c> (or invalid) = static portrait (draw <see cref="Portrait"/>,
        /// unchanged behavior). When set and valid, the framework walks frames from the sheet origin
        /// and ignores <see cref="Portrait"/> — the two are mutually exclusive, Animation wins.
        /// </summary>
        public AnimationSettings? Animation { get; set; }

        // ── Author-declared defaults (Milestone 9) — the out-of-box look the author intends. ──
        // All optional; omitted = framework baseline (scale 100%, offset 0, box 100%, name shown).
        // Scales are PERCENT ints (100 = unchanged) — same unit as the player's GMCM scale, so there
        // is one mental model. The player's GMCM settings STACK on these: offsets ADD, scales MULTIPLY.

        /// <summary>Default player-portrait scale, as a percent (100 = framework baseline).</summary>
        public int? DefaultPlayerScale   { get; set; }
        /// <summary>Default player-portrait horizontal nudge, in pixels.</summary>
        public int?   DefaultPlayerOffsetX { get; set; }
        /// <summary>Default player-portrait vertical nudge, in pixels.</summary>
        public int?   DefaultPlayerOffsetY { get; set; }

        /// <summary>Default NPC-portrait scale, as a percent (100 = framework baseline).</summary>
        public int? DefaultNpcScale   { get; set; }
        /// <summary>Default NPC-portrait horizontal nudge, in pixels.</summary>
        public int?   DefaultNpcOffsetX { get; set; }
        /// <summary>Default NPC-portrait vertical nudge, in pixels.</summary>
        public int?   DefaultNpcOffsetY { get; set; }

        /// <summary>Default box HEIGHT, as a percent (100 = framework baseline); width is unaffected.</summary>
        public int? DefaultBoxHeight { get; set; }

        /// <summary>Default for hiding the NPC name plate (false = shown).</summary>
        public bool? DefaultNameHidden { get; set; }
    }

    /// <summary>A source rectangle into a pack's portrait texture.</summary>
    public class PortraitRect
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    /// <summary>
    /// Sprite-sheet animation settings. Field names mirror SPO's animation system exactly so a pack
    /// author carries one mental model across both mods. The pack's <c>Load</c> entry points to the
    /// full sprite-sheet PNG (the strip / grid); frames are walked from its origin (0,0).
    /// </summary>
    public class AnimationSettings
    {
        /// <summary>Width of one frame, in pixels. Also drives the draw scale (NOT the whole strip width).</summary>
        public int FrameWidth { get; set; }
        /// <summary>Height of one frame, in pixels. Also drives the draw scale (NOT the whole strip height).</summary>
        public int FrameHeight { get; set; }
        /// <summary>Total number of frames to cycle through.</summary>
        public int FrameCount { get; set; }
        /// <summary>Columns in the sheet grid. <c>0</c> = a single horizontal strip (one row).</summary>
        public int Columns { get; set; } = 0;
        /// <summary>Uniform per-frame duration in milliseconds. Defaulted to 100 at draw time when unset.</summary>
        public int? FrameDurationMs { get; set; }
        /// <summary>Per-frame durations in ms (length must equal <see cref="FrameCount"/>); overrides <see cref="FrameDurationMs"/>.</summary>
        public int[]? FrameDurations { get; set; }

        /// <summary>
        /// True when the settings describe a usable animation. Invalid settings (any of count/width/
        /// height &lt;= 0) are treated as static by the framework so there is never a divide-by-zero.
        /// </summary>
        public bool IsValid => FrameCount > 0 && FrameWidth > 0 && FrameHeight > 0;
    }
}
