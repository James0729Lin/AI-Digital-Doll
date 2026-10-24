using System;
using DigitalDoll.Core.Mascot;
using Xunit;

namespace DigitalDoll.Core.Tests;

public class MascotBrainTests
{
    private static readonly Ground Floor = new(0f, 1920f, 1000f);

    private static MascotBrain Create(int seed = 1, float startX = 960f) =>
        new(new MascotTuning(), new Random(seed), startX, Floor);

    private static void Run(MascotBrain brain, float seconds, float dt = 1f / 60f)
    {
        for (float t = 0; t < seconds; t += dt)
            brain.Update(dt);
    }

    [Fact]
    public void StartsIdleOnTheFloor()
    {
        var brain = Create();
        Assert.Equal(MascotState.Idle, brain.State);
        Assert.Equal(Floor.FloorY, brain.Y);
    }

    [Fact]
    public void StartPositionIsKeptAwayFromScreenEdges()
    {
        var brain = Create(startX: -500f);
        Assert.Equal(new MascotTuning().EdgeMargin, brain.X);
    }

    [Fact]
    public void EventuallyWalksAndStaysInsideTheGround()
    {
        var brain = Create(seed: 7);
        bool walked = false;
        brain.StateChanged += (_, next) => walked |= next == MascotState.Walk;

        for (int i = 0; i < 60 * 120; i++)
        {
            brain.Update(1f / 60f);
            Assert.InRange(brain.X, Floor.MinX, Floor.MaxX);
            Assert.Equal(Floor.FloorY, brain.Y);
        }
        Assert.True(walked);
    }

    [Fact]
    public void WalkToFacesTheTargetAndArrives()
    {
        var brain = Create(startX: 500f);
        brain.WalkTo(300f);
        Assert.Equal(MascotState.Walk, brain.State);
        Assert.Equal(-1, brain.Facing);

        Run(brain, 3f);
        Assert.Equal(300f, brain.X, 3);
        Assert.Equal(MascotState.Idle, brain.State);
    }

    [Fact]
    public void WalkTargetIsClampedToTheMargin()
    {
        var brain = Create(startX: 500f);
        brain.WalkTo(5000f);
        Assert.Equal(Floor.MaxX - new MascotTuning().EdgeMargin, brain.TargetX);
    }

    [Fact]
    public void DroppedMascotFallsLandsAndReturnsToIdle()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(800f, 200f);
        Assert.Equal(MascotState.Dragged, brain.State);

        brain.EndDrag(300f, 0f);
        Assert.Equal(MascotState.Falling, brain.State);
        Assert.Equal(1, brain.Facing);

        Run(brain, 2f);
        Assert.Equal(Floor.FloorY, brain.Y);
        Assert.NotEqual(MascotState.Falling, brain.State);

        Run(brain, 1f);
        Assert.Contains(brain.State, new[] { MascotState.Idle, MascotState.Walk });
    }

    [Fact]
    public void FallingBouncesOffScreenSides()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(1900f, 100f);
        brain.EndDrag(5000f, 0f);
        Run(brain, 0.2f);
        Assert.InRange(brain.X, Floor.MinX, Floor.MaxX);
        Assert.True(brain.VelocityX <= 0f);
    }

    [Fact]
    public void ShrinkingGroundClampsPosition()
    {
        var brain = Create(startX: 1800f);
        brain.SetGround(new Ground(0f, 1280f, 700f));
        Assert.InRange(brain.X, 0f, 1280f);
        Assert.Equal(700f, brain.Y);
    }

    [Fact]
    public void WalkIsIgnoredWhileDragged()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.WalkTo(100f);
        Assert.Equal(MascotState.Dragged, brain.State);
    }

    [Fact]
    public void PlaceAtPutsTheMascotOnTheFloorInsideTheGround()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 10f);
        brain.PlaceAt(5000f);
        Assert.Equal(Floor.MaxX, brain.X);
        Assert.Equal(Floor.FloorY, brain.Y);
        Assert.Equal(MascotState.Idle, brain.State);
    }
}
