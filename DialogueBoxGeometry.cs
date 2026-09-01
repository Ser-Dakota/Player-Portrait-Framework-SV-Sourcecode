using System;
using DialogueDisplayFramework.Data;
using DialogueDisplayFramework.Framework;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Dictates the dialogue box footprint (TopRight), cuts the friendship jewel, and copies
    /// TopRight's (intentionally rough) Name placement. Values lifted as-is from DDFCScaler's
    /// TopRight branch, hardcoded for the baseline (100% box size, no per-NPC opt-in).
    ///
    /// DDFC's <c>DialogueBoxPatches</c> is internal, so its private <c>UpdateDialogueBoxSize</c>
    /// (where we hook) and its <c>ActiveData</c> (the live resolved entry) are reached via
    /// reflection — same approach DDFCScaler used.
    /// </summary>
    public static class DialogueBoxGeometry
    {
        private static IMonitor Monitor = null!;

        public static void ApplyManualPatches(Harmony harmony, IMonitor monitor)
        {
            Monitor = monitor;

            var patchesType = AccessTools.TypeByName("DialogueDisplayFramework.Framework.DialogueBoxPatches");
            var method      = patchesType is null ? null : AccessTools.Method(patchesType, "UpdateDialogueBoxSize");
            if (method is null)
            {
                monitor.Log("DDFC's UpdateDialogueBoxSize was not found — box repositioning is inactive.", LogLevel.Warn);
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(DialogueBoxGeometry), nameof(BoxResize_Postfix)));
        }

        public static void BoxResize_Postfix(DialogueBox dialogueBox)
        {
            try
            {
                if (!ModEntry.HasActivePack)
                    return; // no pack → leave DDFC's box alone
                if (dialogueBox is null || !dialogueBox.isPortraitBox() || dialogueBox.isQuestion)
                    return;

                var (vpW, _) = ModEntry.GetViewport();

                // Box footprint from the SINGLE source — GetScaledBox already applies viewport scaling
                // (M8) AND box scale (M9), so the box we stamp onto DDFC and the rect the portraits pin
                // to always agree. Centered X is computed there, so widening never drifts off-center.
                var (boxX, boxY, boxWidth, boxHeight) = ModEntry.GetScaledBox();

                dialogueBox.x                 = boxX;
                dialogueBox.xPositionOnScreen = boxX;
                dialogueBox.y                 = boxY;
                dialogueBox.yPositionOnScreen = boxY;
                dialogueBox.width             = boxWidth;
                dialogueBox.height            = boxHeight;

                // Operate on the live resolved entry: widen the dialogue text, cut the jewel,
                // copy TopRight's (rough) Name placement.
                var active = GetActiveData();
                if (active != null)
                {
                    // Widen the text so it fills the new box width instead of puddling left.
                    // Some NPC entries omit Dialogue (DDFC then uses its default 716-wide); create
                    // one so the width applies broadly.
                    active.Dialogue ??= new DialogueStringData { XOffset = 8, YOffset = 8 };
                    // Wrap width is tied to the box WIDTH, which the box-height control does NOT touch
                    // (M9.1) — the box only grows taller, so text gains vertical room (more lines) but
                    // never reflows narrower/wider. Hence no EffectiveBoxHeight factor here.
                    active.Dialogue.Width = ModEntry.DialogueWidth(vpW);

                    if (active.Jewel != null)
                        active.Jewel.Disabled = true; // jewel removed from the design

                    if (active.Name != null)
                    {
                        // Name plate hide toggle (M9): author default OR player toggle.
                        active.Name.Disabled = ModEntry.EffectiveNameHidden();

                        // Name plate near the box's top-right corner, over the NPC (the speaker) — fixes
                        // speaker-association (it previously sat over the player). Confirmed in-game: the
                        // speaker name is LEFT-anchored and grows rightward, so we anchor relative to the
                        // box's RIGHT edge (Right = true) and draw LEFT-aligned; the left edge then lands
                        // at (boxRight + NameXOffset) and longer names extend rightward from there.
                        active.Name.Right     = true;
                        active.Name.Bottom    = false;
                        active.Name.Alignment = SpriteText.ScrollTextAlignment.Left;
                        active.Name.XOffset   = ModEntry.NameXOffset; // left anchor, this far left of the box right edge
                        active.Name.YOffset   = ModEntry.NameYOffset; // above the box top edge
                    }
                }

                // Force DDFC to re-apply box position next frame (desktop). Mirrors DDFCScaler.
                DialogueBoxInterface.AppliedBoxPosition = null;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Box resize postfix failed: {ex}", LogLevel.Error);
            }
        }

        /// <summary>Reflects out DDFC's static <c>DialogueBoxPatches.ActiveData</c> (internal type).</summary>
        private static DialogueDisplayData? GetActiveData()
        {
            var patchesType = AccessTools.TypeByName("DialogueDisplayFramework.Framework.DialogueBoxPatches");
            if (patchesType is null)
                return null;
            return AccessTools.Property(patchesType, "ActiveData")?.GetValue(null) as DialogueDisplayData;
        }
    }
}
