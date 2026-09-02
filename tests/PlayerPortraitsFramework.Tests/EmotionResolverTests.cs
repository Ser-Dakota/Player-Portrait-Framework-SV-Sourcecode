using PlayerPortraitsFramework;
using Xunit;

namespace PlayerPortraitsFramework.Tests;

public class ResolveModeTests
{
    [Fact]
    public void NullPack_IsNone()
    {
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(null));
    }

    [Fact]
    public void EmptyPack_IsNone()
    {
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(new PackRegistration()));
    }

    [Fact]
    public void PortraitRectOnly_IsSimple()
    {
        var pack = new PackRegistration { Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 } };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void ValidAnimationOnly_IsSimple()
    {
        var pack = new PackRegistration
        {
            Animation = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 4 }
        };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void InvalidAnimationWithNoPortrait_IsNone()
    {
        var pack = new PackRegistration
        {
            Animation = new AnimationSettings { FrameWidth = 0, FrameHeight = 0, FrameCount = 0 }
        };
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void ValidEmotionSheet_IsEmotionStatic()
    {
        var pack = new PackRegistration
        {
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 1024, SlotHeight = 1024 }
        };
        Assert.Equal(PackMode.EmotionStatic, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionSheetWithZeroDimensions_IsNone()
    {
        var pack = new PackRegistration
        {
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 0, SlotHeight = 1024 }
        };
        Assert.Equal(PackMode.None, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimations_IsEmotionAnimated()
    {
        var pack = new PackRegistration
        {
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 4 }
            }
        };
        Assert.Equal(PackMode.EmotionAnimated, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimationsWithNoValidEntries_FallsThrough()
    {
        var pack = new PackRegistration
        {
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 0, FrameHeight = 0, FrameCount = 0 }
            },
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 }
        };
        Assert.Equal(PackMode.Simple, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionAnimated_WinsOverSheetAndSimple()
    {
        // Modes are mutually exclusive by contract; if an author declares several anyway,
        // resolution must be deterministic: animated > sheet > simple.
        var pack = new PackRegistration
        {
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 },
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 512, SlotHeight = 512 },
            EmotionAnimations = new Dictionary<string, AnimationSettings>
            {
                ["0"] = new AnimationSettings { FrameWidth = 1024, FrameHeight = 1024, FrameCount = 2 }
            }
        };
        Assert.Equal(PackMode.EmotionAnimated, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionStatic_WinsOverSimple()
    {
        var pack = new PackRegistration
        {
            Portrait = new PortraitRect { X = 0, Y = 0, W = 1024, H = 1024 },
            EmotionSheet = new EmotionSheetSettings { SlotWidth = 512, SlotHeight = 512 }
        };
        Assert.Equal(PackMode.EmotionStatic, EmotionResolver.ResolveMode(pack));
    }

    [Fact]
    public void EmotionSheet_DefaultsToTwoColumns()
    {
        Assert.Equal(2, new EmotionSheetSettings().Columns);
    }
}

public class ResolveSlotTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(37, 37)]
    public void NoMap_IsOneToOne(int npcIndex, int expected)
    {
        Assert.Equal(expected, EmotionResolver.ResolveSlot(npcIndex, null));
    }

    [Fact]
    public void EmptyMap_IsOneToOne()
    {
        Assert.Equal(3, EmotionResolver.ResolveSlot(3, new Dictionary<string, int>()));
    }

    [Fact]
    public void ListedIndex_IsRemapped()
    {
        // The spec's example: {"1": 4, "2": 0, "3": 0}
        var map = new Dictionary<string, int> { ["1"] = 4, ["2"] = 0, ["3"] = 0 };
        Assert.Equal(4, EmotionResolver.ResolveSlot(1, map));
        Assert.Equal(0, EmotionResolver.ResolveSlot(2, map));
        Assert.Equal(0, EmotionResolver.ResolveSlot(3, map));
    }

    [Fact]
    public void UnlistedIndex_StaysOneToOne()
    {
        var map = new Dictionary<string, int> { ["1"] = 4, ["2"] = 0, ["3"] = 0 };
        Assert.Equal(0, EmotionResolver.ResolveSlot(0, map));
        Assert.Equal(4, EmotionResolver.ResolveSlot(4, map));
        Assert.Equal(9, EmotionResolver.ResolveSlot(9, map));
    }

    [Fact]
    public void NegativeNpcIndex_ClampsToZero()
    {
        Assert.Equal(0, EmotionResolver.ResolveSlot(-1, null));
    }

    [Fact]
    public void NegativeNpcIndex_ClampsBeforeLookup()
    {
        // -1 becomes 0 first, so it picks up slot 0's remap rather than missing the map entirely.
        var map = new Dictionary<string, int> { ["0"] = 7 };
        Assert.Equal(7, EmotionResolver.ResolveSlot(-1, map));
    }

    [Fact]
    public void NegativeMappedValue_ClampsToZero()
    {
        var map = new Dictionary<string, int> { ["2"] = -5 };
        Assert.Equal(0, EmotionResolver.ResolveSlot(2, map));
    }
}

public class SheetCellTests
{
    // A 2-column sheet of 1024px slots, 3 rows tall → slots 0..5.
    private static EmotionSheetSettings Sheet(int columns = 2) =>
        new() { SlotWidth = 1024, SlotHeight = 1024, Columns = columns };

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1024, 0)]
    [InlineData(2, 0, 1024)]
    [InlineData(3, 1024, 1024)]
    [InlineData(4, 0, 2048)]
    [InlineData(5, 1024, 2048)]
    public void TwoColumnGrid_WalksRowsThenColumns(int slot, int expectedX, int expectedY)
    {
        bool ok = EmotionResolver.TryGetSheetCell(slot, Sheet(), 2048, 3072, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void ColumnsOverride_IsHonoured()
    {
        // 3 columns: slot 3 wraps to row 1, column 0.
        bool ok = EmotionResolver.TryGetSheetCell(3, Sheet(columns: 3), 3072, 2048, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(0, x);
        Assert.Equal(1024, y);
    }

    [Fact]
    public void ZeroOrNegativeColumns_DefaultsToTwo()
    {
        bool ok = EmotionResolver.TryGetSheetCell(2, Sheet(columns: 0), 2048, 2048, out int x, out int y);
        Assert.True(ok);
        Assert.Equal(0, x);
        Assert.Equal(1024, y);
    }

    [Fact]
    public void SlotBelowTheSheet_FallsBackToZero()
    {
        // Sheet is only 2 rows (4 slots); slot 4 would start at y=2048, past the bottom.
        bool ok = EmotionResolver.TryGetSheetCell(4, Sheet(), 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlotPastTheRightEdge_FallsBackToZero()
    {
        // Sheet is only 1 column wide but declares 2 → slot 1 would start at x=1024, past the edge.
        bool ok = EmotionResolver.TryGetSheetCell(1, Sheet(), 1024, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void NegativeSlot_FallsBackToZero()
    {
        bool ok = EmotionResolver.TryGetSheetCell(-3, Sheet(), 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void NullSheet_FallsBackToZero()
    {
        bool ok = EmotionResolver.TryGetSheetCell(0, null, 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void InvalidSheetDimensions_FallBackToZero()
    {
        var bad = new EmotionSheetSettings { SlotWidth = 0, SlotHeight = 1024 };
        bool ok = EmotionResolver.TryGetSheetCell(0, bad, 2048, 2048, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void SlotZero_OnASheetTooSmallForOneCell_FallsBackToZero()
    {
        // Degenerate art: even slot 0 does not fit. Still returns 0,0 — the caller draws a clipped
        // cell rather than crashing, and the warn-once log tells the author.
        bool ok = EmotionResolver.TryGetSheetCell(0, Sheet(), 512, 512, out int x, out int y);
        Assert.False(ok);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
