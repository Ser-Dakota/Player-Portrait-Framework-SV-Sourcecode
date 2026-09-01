using System;
using DialogueDisplayFramework.Data;
using DialogueDisplayFramework.Framework;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Places the NPC portrait above-right of the dialogue box (TopRight). The math is lifted
    /// as-is from DDFCScaler's TopRight case, but hardcoded for the baseline: no per-NPC
    /// orientation lookup and no size multipliers — it applies whenever a pack is active and the
    /// box is a portrait (non-question) dialogue. The player portrait is NOT a DDFC portrait
    /// (the framework own-draws it), so it never passes through here.
    /// </summary>
    [HarmonyPatch(typeof(DialogueBoxRenderer), nameof(DialogueBoxRenderer.GetDataVector))]
    public static class GetDataVectorPatch
    {
        public static bool Prefix(DialogueBox box, BaseData data, ref Vector2 __result)
        {
            try
            {
                if (!ModEntry.HasActivePack)                  return true; // no pack → leave DDFC alone
                if (data is not PortraitData portrait)        return true; // only the NPC portrait
                if (box is null || !box.isPortraitBox() || box.isQuestion) return true;

                int sourceSize = Math.Min(portrait.W, portrait.H);
                if (sourceSize <= 0)
                    sourceSize = 1024;

                // Pin the bottom-RIGHT corner to the box's top-RIGHT corner, using the framework's
                // own box rect (NOT box.width, which may still carry DDFC's stale 1200 default).
                // Bottom sits on BorderTop (the frame edge) so it tucks under the frame.
                var (_, boxRight, contentTop, borderTop) = ModEntry.GetBoxRect();
                int   basePortraitHeight = (int)(contentTop * ModEntry.PortraitHeightFactor);
                int   portraitHeight     = (int)(basePortraitHeight * ModEntry.EffectiveNpcScale()); // author × player
                if (portraitHeight <= 0)
                    portraitHeight = 1;
                int   portraitWidth      = portraitHeight;
                float scale              = (float)portraitHeight / sourceSize;

                portrait.W          = sourceSize;
                portrait.H          = sourceSize;
                portrait.Scale      = scale;
                portrait.X          = -1; // keep the NPC's emotion-based source rect
                portrait.Y          = -1;
                portrait.Right      = false;
                portrait.Bottom     = false;
                portrait.LayerDepth = 0.1f;

                // Offsets ADD on top of the pinned position (author default + player nudge).
                int portraitX = (boxRight - portraitWidth) + ModEntry.EffectiveNpcOffsetX();   // right edge flush + nudge
                int portraitY = (borderTop - portraitHeight) + ModEntry.EffectiveNpcOffsetY(); // bottom flush on frame top + nudge

                __result = new Vector2(portraitX, portraitY);
                return false;
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"GetDataVector (NPC portrait) patch failed: {ex}", LogLevel.Error);
                return true;
            }
        }
    }
}
