#nullable enable
using System.Collections.Generic;
using System.Globalization;
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
    }
}
