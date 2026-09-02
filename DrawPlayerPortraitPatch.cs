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
            if (!ModEntry.HasActivePack)
                return;
            if (dialogueBox is null || !dialogueBox.isPortraitBox())
                return;

            // No isQuestion check here any more: this postfix rides DDFC's renderer, which vanilla
            // never reaches during a question box (draw gates drawPortrait on !isQuestion), so the
            // condition was unreachable. Question boxes are served by QuestionBoxPortraitPatch, which
            // calls DrawPlayerPortrait below directly.
            QuestionBoxDiagnostics.NoteNormalBox(dialogueBox);
            DrawPlayerPortrait(b, dialogueBox);
        }

        /// <summary>
        /// Draws the player portrait for this box. Shared by the normal-dialogue postfix above and by
        /// <see cref="QuestionBoxPortraitPatch"/>, so emotion resolution, the animation cursor and the
        /// pinning maths live in exactly one place regardless of which draw path got us here.
        /// <para>Callers are responsible for the <c>isPortraitBox()</c> guard — this method
        /// dereferences <c>characterDialogue</c>.</para>
        /// </summary>
        internal static void DrawPlayerPortrait(SpriteBatch b, DialogueBox dialogueBox)
        {
            try
            {
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
