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
