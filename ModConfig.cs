#nullable enable
namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Player-facing config (config.json), surfaced through GMCM (Milestone 9). These are the
    /// player's GLOBAL preferences; they STACK on top of the active pack's author-declared defaults:
    /// offsets ADD, scales MULTIPLY (see <c>ModEntry.Effective*</c>). All defaults here are the
    /// neutral "no change" values, so a fresh config reproduces the pre-M9 look exactly.
    /// </summary>
    public class ModConfig
    {
        /// <summary>Selected player portrait pack UniqueID, or <c>"Auto"</c> = first pack alphabetically.</summary>
        public string SelectedPack { get; set; } = "Auto";

        // ── Player portrait (scale multiplies the author default; offsets add to it) ──
        public int PlayerScalePct { get; set; } = 100; // 100 = ×1.0
        public int PlayerOffsetX  { get; set; } = 0;
        public int PlayerOffsetY  { get; set; } = 0;

        // ── NPC portrait ──
        public int NpcScalePct { get; set; } = 100;
        public int NpcOffsetX  { get; set; } = 0;
        public int NpcOffsetY  { get; set; } = 0;

        // ── Box (HEIGHT only — taller/shorter box; width is locked to the viewport-relative value) ──
        public int BoxHeightPct { get; set; } = 100;

        // ── Name plate ──
        public bool NameHidden { get; set; } = false;
    }
}
