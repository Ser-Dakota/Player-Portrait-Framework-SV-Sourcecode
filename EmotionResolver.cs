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
