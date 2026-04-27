using AiPixelScaler.Core.Geometry;
using AiPixelScaler.Core.Sandbox;

namespace AiPixelScaler.Core.Tests;

public class Modulo7_SandboxTests
{
    [Fact]
    public void Kinematic_friction_slows()
    {
        var b = new KinematicBody2D { PosX = 0, PosY = 0, VelX = 100, VelY = 0 };
        var p = new KinematicParams { Acceleration = 500, MaxSpeed = 200, Friction = 0.1 };
        KinematicStep.Integrate(ref b, 0, 0, 1, p);
        Assert.InRange(b.VelX, 88, 92);
    }

    [Fact]
    public void AnimationController_cycles()
    {
        var c = new AnimationController2D { FixedFps = 10 };
        c.Clips["walk"] = new AnimationClip { Frames = new[] { 0, 1, 2, 3 }, Fps = 10 };
        c.SetClip("walk");
        c.Update(0.1);
        c.Update(0.1);
        c.Update(0.1);
        c.Update(0.1);
        Assert.InRange(c.FrameIndex, 0, 3);
    }
}
