using PlayerPortraitsFramework;
using Xunit;

namespace PlayerPortraitsFramework.Tests;

public class AnimationCursorTests
{
    private const int Slot0 = 0;
    private const int Slot1 = 1;

    [Fact]
    public void FreshCursor_StartsAtFrameZeroWithNoSlot()
    {
        var cursor = new AnimationCursor();
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
        Assert.Equal(-1, cursor.Slot);
    }

    [Fact]
    public void BelowDuration_HoldsTheFrame()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 50, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(50, cursor.Elapsed);
    }

    [Fact]
    public void ReachingDuration_AdvancesAndResetsElapsed()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
    }

    [Fact]
    public void PastTheLastFrame_WrapsToZero()
    {
        var cursor = new AnimationCursor();
        for (int i = 0; i < 4; i++)
            cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void UnsetDuration_DefaultsTo100Ms()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 99, frameCount: 2, uniformDurationMs: null, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        cursor.Advance(Slot0, 1, frameCount: 2, uniformDurationMs: null, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
    }

    [Fact]
    public void PerFrameDurations_OverrideTheUniformValue()
    {
        // Blink pattern: frame 0 dwells 1800ms, the rest 100ms each.
        var durations = new[] { 1800, 100, 100, 100 };
        var cursor = new AnimationCursor();

        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(0, cursor.Frame); // 200 < 1800, still on the long frame

        cursor.Advance(Slot0, 1600, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(1, cursor.Frame);

        cursor.Advance(Slot0, 100, frameCount: 4, uniformDurationMs: 200, perFrameDurations: durations);
        Assert.Equal(2, cursor.Frame);
    }

    [Fact]
    public void PerFrameDurationsOfWrongLength_AreIgnored()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: new[] { 1800, 100 });
        Assert.Equal(1, cursor.Frame); // fell back to the uniform 200ms
    }

    [Fact]
    public void ZeroDuration_FreezesOnTheFrame()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 10_000, frameCount: 4, uniformDurationMs: 0, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void SlotChange_RestartsAtFrameZeroAndClearsElapsed()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(2, cursor.Frame);

        // Emotion changed mid-conversation → the new expression plays its own beat from the top.
        cursor.Advance(Slot1, 16, frameCount: 2, uniformDurationMs: 300, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
        Assert.Equal(Slot1, cursor.Slot);
        Assert.Equal(16, cursor.Elapsed); // this tick's time starts frame 0's dwell, not the old frame's
    }

    [Fact]
    public void SameSlot_DoesNotRestart()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Advance(Slot0, 100, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(1, cursor.Frame);
        Assert.Equal(100, cursor.Elapsed);
    }

    [Fact]
    public void Reset_ReturnsToFrameZeroAndForgetsTheSlot()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot1, 200, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        cursor.Reset();

        Assert.Equal(0, cursor.Frame);
        Assert.Equal(0, cursor.Elapsed);
        Assert.Equal(-1, cursor.Slot);

        // After a reset the SAME slot must restart too (dialogue box reopened on the same emotion).
        cursor.Advance(Slot1, 16, frameCount: 4, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }

    [Fact]
    public void ShrinkingFrameCount_DoesNotLeaveAStaleFrameIndex()
    {
        // A CP condition can swap a slot's art for a shorter variant while the cursor is past its end.
        var cursor = new AnimationCursor();
        for (int i = 0; i < 3; i++)
            cursor.Advance(Slot0, 200, frameCount: 6, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(3, cursor.Frame);

        cursor.Advance(Slot0, 10, frameCount: 2, uniformDurationMs: 200, perFrameDurations: null);
        Assert.True(cursor.Frame < 2);
    }

    [Fact]
    public void ZeroFrameCount_IsSafe()
    {
        var cursor = new AnimationCursor();
        cursor.Advance(Slot0, 200, frameCount: 0, uniformDurationMs: 200, perFrameDurations: null);
        Assert.Equal(0, cursor.Frame);
    }
}
