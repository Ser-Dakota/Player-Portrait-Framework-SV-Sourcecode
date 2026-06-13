using System;
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
    /// box border: whatever the NPC does (tuck behind the frame), the player does too. This replaces
    /// the M4/M5 RenderingActiveMenu own-draw, which sat on a different layer than the NPC.
    ///
    /// Pinned (mirror of the NPC): bottom-LEFT corner flush to the box's top-LEFT corner, with the
    /// bottom on the frame's BorderTop (a bead above the content top) so it tucks under the frame.
    /// </summary>
    [HarmonyPatch(typeof(DialogueBoxRenderer), nameof(DialogueBoxRenderer.DrawPortrait))]
    public static class DrawPlayerPortraitPatch
    {
        // ── Animation runtime state (V2) ──────────────────────────────────────────────────
        // One active pack → one player portrait → one animation cursor. Advanced each draw below,
        // reset to frame 0 on dialogue box open/close (ModEntry.OnMenuChanged → ResetAnimation).
        private static int    _animFrame;
        private static double _animElapsed;

        /// <summary>
        /// Reset the animation to frame 0. Called on portrait dialogue box OPEN and CLOSE — PPF's
        /// analog of SPO's "active item changed" — so every conversation starts at frame 0. NOT called
        /// on page-advance (same DialogueBox instance), so animation plays continuously across pages.
        /// </summary>
        internal static void ResetAnimation()
        {
            _animFrame   = 0;
            _animElapsed = 0;
        }

        public static void Postfix(SpriteBatch b, DialogueBox dialogueBox)
        {
            try
            {
                if (ModEntry.ActiveTexturePath is null)
                    return;
                if (dialogueBox is null || !dialogueBox.isPortraitBox() || dialogueBox.isQuestion)
                    return;

                var texture = ModEntry.TryGetActiveTexture();
                if (texture is null)
                    return;

                // Pick the source frame + its dimensions. ActiveAnimation is non-null ONLY when valid
                // (vetted in RefreshActivePack), so here it implies count/width/height > 0.
                //   • Animated → walk the sheet, IGNORE the Portrait rect. CRITICAL: size from
                //     FrameWidth/FrameHeight, never texture.Width (that's the whole strip → squash).
                //   • Static   → the existing Portrait rect, unchanged behavior.
                var anim = ModEntry.ActiveAnimation;
                Rectangle source;
                int srcW, srcH;

                if (anim != null)
                {
                    source = AdvanceAndGetFrame(anim);
                    srcW   = anim.FrameWidth;
                    srcH   = anim.FrameHeight;
                }
                else if (ModEntry.ActiveRect is { } rect)
                {
                    source = new Rectangle(rect.X, rect.Y, rect.W, rect.H);
                    srcW   = rect.W;
                    srcH   = rect.H;
                }
                else
                {
                    return; // neither animation nor rect → nothing to draw (RefreshActivePack guards this)
                }

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
                int playerX = boxLeft + ModEntry.EffectivePlayerOffsetX();                     // flush to box left edge + nudge
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
        /// Advances the animation cursor by this draw's elapsed time and returns the current frame's
        /// source rectangle, walked SPO-style from the sheet origin. <paramref name="anim"/> is assumed
        /// valid (count/width/height &gt; 0), so there is no divide-by-zero.
        /// </summary>
        private static Rectangle AdvanceAndGetFrame(AnimationSettings anim)
        {
            _animElapsed += Game1.currentGameTime?.ElapsedGameTime.TotalMilliseconds ?? 0;

            // This frame's dwell time: a valid, in-bounds per-frame array overrides the uniform value;
            // the uniform value defaults to 100ms. A duration of 0 FREEZES on this frame (no spinning).
            int duration =
                (anim.FrameDurations is { } durs && durs.Length == anim.FrameCount && _animFrame < durs.Length)
                    ? durs[_animFrame]
                    : (anim.FrameDurationMs ?? 100);

            if (duration > 0 && _animElapsed >= duration)
            {
                _animFrame   = (_animFrame + 1) % anim.FrameCount;
                _animElapsed = 0;
            }

            int cols = anim.Columns > 0 ? anim.Columns : anim.FrameCount; // 0 = single horizontal strip
            int row  = _animFrame / cols;
            int col  = _animFrame % cols;
            return new Rectangle(col * anim.FrameWidth, row * anim.FrameHeight, anim.FrameWidth, anim.FrameHeight);
        }
    }
}
