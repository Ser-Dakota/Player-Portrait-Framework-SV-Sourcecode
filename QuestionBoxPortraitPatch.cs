using System;
using DialogueDisplayFramework.Framework;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley.Menus;

namespace PlayerPortraitsFramework
{
    /// <summary>
    /// Draws BOTH portraits during an NPC question box, which neither the game nor DDFC does.
    ///
    /// <para>Why a whole new hook rather than relaxing a guard: vanilla
    /// <see cref="DialogueBox.draw"/> gates the entire portrait path on
    /// <c>if (isPortraitBox() &amp;&amp; !isQuestion)</c>, so <c>drawPortrait</c> is never called for a
    /// question. DDFC hooks portraits by PREFIXING <c>drawPortrait</c>, so its renderer never runs
    /// either — and the framework's own postfix, which rides DDFC's renderer, is therefore dead code
    /// during questions. Relaxing the <c>isQuestion</c> guards alone changes nothing; something has to
    /// draw at a point the game actually reaches. That point is here, a postfix on the box's draw.</para>
    ///
    /// <para>Ordering caveat (acceptable for this pass): vanilla's <c>draw</c> ends with
    /// <c>drawMouse</c>, so portraits drawn from this postfix land ON TOP of the cursor. Both
    /// portraits sit above the box while the cursor sits over the response list, so overlap is rare.
    /// Moving to a <c>drawBox</c> postfix would fix the ordering but has to distinguish the question
    /// box's own call from the transition and above-dialogue-image calls.</para>
    ///
    /// <para>Geometry is deliberately UNCHANGED this pass: portraits keep pinning to
    /// <c>ModEntry.GetBoxRect()</c>, which is viewport-derived and never reads the live box. The
    /// question box grows UPWARD as options are added, so on a long question it will grow into the
    /// portraits. That is EXPECTED here — the pinning strategy is being judged on screen first.</para>
    /// </summary>
    [HarmonyPatch(typeof(DialogueBox), nameof(DialogueBox.draw), new Type[] { typeof(SpriteBatch) })]
    public static class QuestionBoxPortraitPatch
    {
        /// <summary>
        /// One-shot latch: if calling into DDFC's renderer throws, log once and stop trying. The draw
        /// runs every frame, so an unlatched failure would bury the SMAPI console for the whole
        /// question. The player portrait keeps drawing regardless.
        /// </summary>
        private static bool _npcDrawFailed;

        /// <summary>Clears the NPC-draw failure latch (called when the active pack is re-resolved).</summary>
        internal static void ResetFailureLatch() => _npcDrawFailed = false;

        public static void Postfix(DialogueBox __instance, SpriteBatch b)
        {
            try
            {
                if (!ModEntry.HasActivePack)
                    return;
                if (__instance is null || !__instance.isQuestion)
                    return; // normal dialogue is served by DrawPlayerPortraitPatch, via DDFC's renderer
                if (__instance.transitioning)
                    return; // box is still animating open; vanilla draws only the frame

                // KEEP THIS: the generic DialogueBox(string, Response[], width) constructor — shop
                // confirms and similar — leaves characterDialogue null, and both DDFC's DrawPortrait
                // and our own draw dereference it. isPortraitBox() is false there, so this is the
                // guard that prevents a null-deref. Only the isQuestion half was relaxed.
                if (!__instance.isPortraitBox())
                    return;

                QuestionBoxDiagnostics.NoteQuestion(__instance);

                DrawNpcPortrait(b, __instance);

                // Player portrait: the exact routine the normal path uses, so emotion resolution,
                // the animation cursor and all the pinning maths stay in one place. characterDialogue
                // survives into the question box, so the NPC's current expression is simply held.
                DrawPlayerPortraitPatch.DrawPlayerPortrait(b, __instance);
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Question-box portrait draw failed: {ex}", LogLevel.Error);
            }
        }

        /// <summary>
        /// Draws the NPC portrait by invoking DDFC's renderer directly. <c>DrawPortrait</c> is public
        /// static and calls <c>GetDataVector</c> internally, so the framework's existing NPC
        /// positioning prefix applies automatically; it also reads <c>getPortraitIndex()</c> live, so
        /// the expression matches whatever the NPC was showing when the question appeared.
        /// <para><c>ActiveData</c> is assigned only inside DDFC's UpdateDialogueBoxSize and is never
        /// nulled, so the speaker's resolved entry survives into the question box.</para>
        /// </summary>
        private static void DrawNpcPortrait(SpriteBatch b, DialogueBox dialogueBox)
        {
            if (_npcDrawFailed)
                return;

            try
            {
                var active = DialogueBoxGeometry.GetActiveData();
                if (active?.Portrait is { } portrait)
                    DialogueBoxRenderer.DrawPortrait(b, dialogueBox, portrait);
            }
            catch (Exception ex)
            {
                _npcDrawFailed = true;
                ModEntry.SMonitor.Log(
                    "NPC portrait could not be drawn during a question box; suppressing further attempts "
                    + $"until the active pack changes. The player portrait is unaffected. {ex}",
                    LogLevel.Warn);
            }
        }
    }
}
