#nullable enable
namespace PlayerPortraitsFramework
{
    /// <summary>
    /// The player portrait's animation playhead: which frame is showing and how long it has been
    /// showing for. Elapsed time is INJECTED rather than read from <c>Game1.currentGameTime</c>, which
    /// keeps this class free of game types and makes the emotion-change restart rule unit-testable.
    ///
    /// <para>Reset points: the dialogue box opening, the dialogue box closing (both via
    /// <see cref="Reset"/>), and — new in V2 — the emotion slot changing mid-conversation, which
    /// <see cref="Advance"/> detects on its own so each expression plays its own beat from the top.</para>
    /// </summary>
    public sealed class AnimationCursor
    {
        /// <summary>Sentinel meaning "no slot has played yet", so the first <see cref="Advance"/> always restarts.</summary>
        private const int NoSlot = -1;

        /// <summary>The frame index currently showing.</summary>
        public int Frame { get; private set; }
        /// <summary>Milliseconds this frame has been showing.</summary>
        public double Elapsed { get; private set; }
        /// <summary>The emotion slot currently playing, or <c>-1</c> before the first advance / after a reset.</summary>
        public int Slot { get; private set; } = NoSlot;

        /// <summary>
        /// Return to frame 0 and forget the current slot. Called on dialogue box OPEN and CLOSE, so
        /// every conversation starts at frame 0 — including one that reopens on the same emotion.
        /// </summary>
        public void Reset()
        {
            Frame   = 0;
            Elapsed = 0;
            Slot    = NoSlot;
        }

        /// <summary>
        /// Advance the playhead by one draw's worth of time.
        /// </summary>
        /// <param name="slot">The emotion slot being drawn this frame. A change restarts at frame 0.</param>
        /// <param name="elapsedMs">Milliseconds since the previous draw.</param>
        /// <param name="frameCount">Frames in the current slot's animation.</param>
        /// <param name="uniformDurationMs">Per-frame dwell in ms; <c>null</c> defaults to 100.</param>
        /// <param name="perFrameDurations">Per-frame dwell array; overrides <paramref name="uniformDurationMs"/> when its length equals <paramref name="frameCount"/>.</param>
        public void Advance(int slot, double elapsedMs, int frameCount, int? uniformDurationMs, int[]? perFrameDurations)
        {
            if (frameCount <= 0)
            {
                // Nothing to walk. Park at frame 0 rather than dividing by zero.
                Frame   = 0;
                Elapsed = 0;
                Slot    = slot;
                return;
            }

            // Emotion changed mid-conversation → the new expression plays its own beat from the top.
            // BOTH the frame index and the accumulator reset, so the new frame 0 gets its full dwell.
            if (slot != Slot)
            {
                Slot    = slot;
                Frame   = 0;
                Elapsed = 0;
            }

            // A CP condition can swap in a SHORTER variant of the same slot while we sit past its end.
            if (Frame >= frameCount)
                Frame = 0;

            Elapsed += elapsedMs;

            // This frame's dwell: a valid, in-bounds per-frame array overrides the uniform value; the
            // uniform value defaults to 100ms. A duration of 0 FREEZES on this frame (no spinning).
            int duration =
                (perFrameDurations is { } durations && durations.Length == frameCount && Frame < durations.Length)
                    ? durations[Frame]
                    : (uniformDurationMs ?? 100);

            if (duration > 0 && Elapsed >= duration)
            {
                Frame   = (Frame + 1) % frameCount;
                Elapsed = 0;
            }
        }
    }
}
