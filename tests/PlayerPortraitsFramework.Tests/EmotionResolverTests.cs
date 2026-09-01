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
