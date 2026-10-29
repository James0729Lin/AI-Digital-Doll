using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Xunit;

namespace DigitalDoll.Core.Tests;

public class MascotBrainTests
{
    private const float Width = 1920f, Height = 1040f, Character = 300f;
    private static readonly MascotTuning Tuning = new() { Height = Character };

    private static Surroundings Screen() => Surroundings.Screen(Width, Height, Character);

    private static Surroundings WithWindow(float left, float top, float right, float bottom, long id = 7) =>
        Surroundings.FromWindows(Width, Height, Character, new[] { new DesktopWindow(id, left, top, right, bottom) });

    private static MascotBrain Create(int seed = 1, float startX = 960f, Surroundings? surroundings = null) =>
        new(Tuning, new Random(seed), startX, surroundings ?? Screen());

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
        Assert.Equal(Height - 1f, brain.Y);
        Assert.True(brain.OnFloor);
    }

    [Fact]
    public void StartPositionIsKeptAwayFromScreenEdges()
    {
        var brain = Create(startX: -500f);
        Assert.Equal(Tuning.EdgeMargin, brain.X);
    }

    [Fact]
    public void StaysOnScreenAndOnSomethingForMinutes()
    {
        foreach (int seed in new[] { 1, 2, 3, 4, 5 })
        {
            var brain = Create(seed, surroundings: WithWindow(300, 500, 1100, 900));
            var seen = new HashSet<MascotState>();
            for (int i = 0; i < 60 * 600; i++)
            {
                brain.Update(1f / 60f);
                seen.Add(brain.State);
                Assert.InRange(brain.X, 0f, Width);
                Assert.InRange(brain.Y, 0f, Height);
                if (brain.State is MascotState.Idle or MascotState.Walk or MascotState.Sit)
                {
                    var platform = brain.Surroundings.FindPlatform(brain.PlatformId!.Value);
                    Assert.NotNull(platform);
                    Assert.Equal(platform!.Value.Y, brain.Y);
                }
                if (brain.State is MascotState.Climb or MascotState.Cling)
                    Assert.NotEqual(0, brain.WallSide);
            }
            Assert.Contains(MascotState.Walk, seen);
        }
    }

    [Fact]
    public void EventuallyClimbsAWallAndUsesTheWindowTop()
    {
        bool climbed = false, onWindow = false;
        foreach (int seed in Enumerable.Range(1, 20))
        {
            var brain = Create(seed, surroundings: WithWindow(300, 500, 1100, 900));
            for (int i = 0; i < 60 * 600 && !(climbed && onWindow); i++)
            {
                brain.Update(1f / 60f);
                climbed |= brain.State == MascotState.Climb;
                onWindow |= brain.PlatformId is long id && id != Surroundings.FloorId && brain.State == MascotState.Idle;
            }
        }
        Assert.True(climbed, "never climbed a screen edge");
        Assert.True(onWindow, "never stood on the window");
    }

    [Theory]
    [InlineData(900f)]   // floating window: jump up to its side first
    [InlineData(1100f)]  // window reaching below the floor: climb straight up
    public void ClimbsAWindowSideAndPullsUpOntoIt(float bottom)
    {
        bool done = false;
        foreach (int seed in Enumerable.Range(1, 30))
        {
            var brain = Create(seed, surroundings: WithWindow(500, 400, 1400, bottom));
            bool climbedWindow = false;
            for (int i = 0; i < 60 * 600 && !done; i++)
            {
                brain.Update(1f / 60f);
                if (brain.State == MascotState.Climb && brain.WallId is long w && w / 16 == 7)
                    climbedWindow = true;
                if (climbedWindow && brain.State == MascotState.Landing && brain.PlatformId == 7 * 16)
                    done = true;
            }
            if (done)
                break;
        }
        Assert.True(done, "never climbed a window side onto its top");
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
        Assert.Equal(Width - Tuning.EdgeMargin, brain.TargetX);
    }

    [Fact]
    public void DroppedAboveAWindowLandsOnIt()
    {
        var brain = Create(surroundings: WithWindow(300, 500, 1100, 900));
        brain.BeginDrag();
        brain.DragTo(700f, 200f);
        brain.EndDrag(0f, 0f);
        Assert.Equal(MascotState.Falling, brain.State);

        Run(brain, 2f);
        Assert.Equal(500f, brain.Y);
        Assert.Equal(7 * 16, brain.PlatformId);
    }

    [Fact]
    public void DroppedBesideAWindowFallsToTheFloor()
    {
        var brain = Create(surroundings: WithWindow(300, 500, 1100, 900));
        brain.BeginDrag();
        brain.DragTo(1500f, 200f);
        brain.EndDrag(0f, 0f);
        Run(brain, 2f);
        Assert.True(brain.OnFloor);
        Assert.Equal(Height - 1f, brain.Y);
    }

    [Fact]
    public void ReleasedNextToTheScreenEdgeClingsToIt()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 500f);
        brain.EndDrag(0f, 0f);
        Assert.Equal(MascotState.Cling, brain.State);
        Assert.Equal(-1, brain.WallSide);
        Assert.Equal(-1, brain.Facing);
        Assert.Equal(500f, brain.Y);
    }

    [Fact]
    public void ClimbsAlongTheWallAndBackToTheFloor()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 600f);
        brain.EndDrag(0f, 0f);
        brain.ClimbTo(400f);
        Assert.Equal(MascotState.Climb, brain.State);
        Run(brain, 5f, 1f / 60f);
        Assert.True(brain.Y < 600f);

        brain.ClimbTo(Height - 1f);
        for (int i = 0; i < 60 * 20 && !brain.OnFloor; i++)
            brain.Update(1f / 60f);
        Assert.True(brain.OnFloor);
        Assert.Equal(0, brain.WallSide);
    }

    [Fact]
    public void ClimbsUpInBurstsNotAtASteadySpeed()
    {
        var brain = Create(surroundings: Screen());
        brain.BeginDrag();
        brain.DragTo(10f, 900f);
        brain.EndDrag(0f, 0f);
        brain.ClimbTo(100f);
        var speeds = new List<float>();
        for (int i = 0; i < 60 * 3; i++)
        {
            brain.Update(1f / 60f);
            if (brain.State == MascotState.Climb && brain.Grip == ClimbGrip.Steady)
                speeds.Add(-brain.VelocityY);
        }
        Assert.True(speeds.Max() > Tuning.ClimbSpeed * 1.8f, "a heave should be fast");
        Assert.True(speeds.Min() < Tuning.ClimbSpeed * 0.5f, "between heaves it should almost stop");
    }

    [Fact]
    public void SlipsDownThenHangsOnAndClimbsOn()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 600f);
        brain.EndDrag(0f, 0f);
        brain.ClimbTo(100f);
        Run(brain, 0.5f);
        float y = brain.Y;
        brain.Slip();
        Assert.Equal(ClimbGrip.Slipping, brain.Grip);
        Run(brain, 0.5f);
        Assert.True(brain.Y > y, "it should slide down");
        Assert.Equal(ClimbGrip.Hanging, brain.Grip);
        Assert.Equal(MascotState.Climb, brain.State);
        Assert.Equal(Surroundings.LeftWallId, brain.WallId);
        Run(brain, 1.5f);
        Assert.Equal(ClimbGrip.Steady, brain.Grip);
    }

    [Fact]
    public void SlipsNowAndThenWhileClimbingButNeverOffTheWall()
    {
        bool slipped = false;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var brain = Create(seed);
            brain.BeginDrag();
            brain.DragTo(10f, Height - 20f);
            brain.EndDrag(0f, 0f);
            brain.ClimbTo(50f);
            for (int i = 0; i < 60 * 30 && brain.State == MascotState.Climb; i++)
            {
                brain.Update(1f / 60f);
                slipped |= brain.Grip == ClimbGrip.Slipping;
                Assert.InRange(brain.Y, 0f, Height);
            }
        }
        Assert.True(slipped);
    }

    [Fact]
    public void HoldsAPoseThenCarriesOn()
    {
        var brain = Create();
        Assert.True(brain.StrikePose(grounded: false, seconds: 3f));
        Assert.Equal(MascotState.Pose, brain.State);
        float x = brain.X;
        Run(brain, 2.9f);
        Assert.Equal(MascotState.Pose, brain.State);
        Assert.Equal(x, brain.X);
        Run(brain, 0.2f);
        Assert.Equal(MascotState.Idle, brain.State);

        // A pose on the ground gets up afterwards, or first when asked to walk.
        brain.StrikePose(grounded: true, seconds: 3f);
        Run(brain, 3.1f);
        Assert.Equal(MascotState.GetUp, brain.State);
        brain.StrikePose(grounded: true, seconds: 30f);
        brain.WalkTo(1500f);
        Assert.Equal(MascotState.GetUp, brain.State);
    }

    [Fact]
    public void StrikesPosesOnItsOwnWhenAllowed()
    {
        var tuning = new MascotTuning { Height = Character, RandomPoses = true };
        var poses = new HashSet<string>();
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var brain = new MascotBrain(tuning, new Random(seed), 960f, Screen());
            for (int i = 0; i < 60 * 300; i++)
            {
                brain.Update(1f / 60f);
                if (brain.State == MascotState.Pose)
                {
                    Assert.NotNull(brain.PoseId);
                    Assert.NotNull(PoseCatalog.Find(brain.PoseId!));
                    poses.Add(brain.PoseId!);
                }
            }
        }
        Assert.True(poses.Count >= 3, "several different poses");

        // Animals (no RandomPoses) never pose on their own.
        var animal = Create();
        for (int i = 0; i < 60 * 300; i++)
        {
            animal.Update(1f / 60f);
            Assert.NotEqual(MascotState.Pose, animal.State);
        }
    }

    [Fact]
    public void RequestsOnAWallAreCarriedOutAfterHoppingDown()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 500f);
        brain.EndDrag(0f, 0f);
        Assert.Equal(MascotState.Cling, brain.State);

        brain.Dance();
        Assert.Equal(MascotState.Falling, brain.State);
        Assert.True(brain.ControlledFall);
        bool danced = false;
        for (int i = 0; i < 60 * 8 && !danced; i++)
        {
            brain.Update(1f / 60f);
            danced = brain.State == MascotState.Dance;
        }
        Assert.True(danced, "it should dance once it has landed");
        Assert.True(brain.OnFloor);

        // A pose asked for while falling is struck on landing too.
        brain.BeginDrag();
        brain.DragTo(900f, 600f);
        brain.EndDrag(0f, 0f);
        Assert.True(brain.StrikePose(grounded: false, seconds: 3f, id: "salute"));
        bool posed = false;
        for (int i = 0; i < 60 * 8 && !posed; i++)
        {
            brain.Update(1f / 60f);
            posed = brain.State == MascotState.Pose && brain.PoseId == "salute";
        }
        Assert.True(posed);

        // Being carried cancels what was asked for in the air.
        brain.BeginDrag();
        Assert.False(brain.StrikePose(grounded: false, seconds: 3f, id: "salute"));
    }

    [Fact]
    public void PutDownInMidAirItStaysThere()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var tuning = new MascotTuning { Height = Character, RandomPoses = true };
            var brain = new MascotBrain(tuning, new Random(seed), 960f, Screen());
            brain.BeginDrag();
            brain.DragTo(700f, 400f);
            brain.PinAt(700f, 400f);
            Assert.True(brain.Pinned);
            for (int i = 0; i < 60 * 120; i++)
            {
                brain.Update(1f / 60f);
                // The windows are refreshed several times a second: the spot stays.
                if (i % 15 == 0)
                    brain.SetSurroundings(Screen());
                Assert.Equal(400f, brain.Y);
                Assert.InRange(brain.X, 700f - Character * 0.35f, 700f + Character * 0.35f);
                Assert.True(brain.State is not (MascotState.Falling or MascotState.Walk or MascotState.Climb), brain.State.ToString());
            }
        }
    }

    [Fact]
    public void PickedUpFromItsSpotItFallsAsUsual()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.PinAt(700f, 400f);
        brain.BeginDrag();
        Assert.False(brain.Pinned);
        brain.DragTo(700f, 400f);
        brain.EndDrag(0f, 0f);
        Assert.Equal(MascotState.Falling, brain.State);
        Run(brain, 2f);
        Assert.True(brain.OnFloor);
        Assert.Null(brain.Surroundings.FindPlatform(MascotBrain.PinId));
    }

    [Fact]
    public void CannotPoseInTheAir()
    {
        var brain = Create();
        brain.BeginDrag();
        Assert.False(brain.StrikePose(grounded: false, seconds: 3f));
        Assert.Equal(MascotState.Dragged, brain.State);
    }

    [Fact]
    public void DancesAndSitsOnTheFloorWhenAsked()
    {
        var brain = Create();
        brain.Dance();
        Assert.Equal(MascotState.Dance, brain.State);
        brain.SitOnFloor();
        Assert.Equal(MascotState.FloorSit, brain.State);
        brain.GetUp();
        Assert.Equal(MascotState.GetUp, brain.State);
    }

    [Fact]
    public void SitsOnTheGroundWhenAskedOnTheFloor()
    {
        var brain = Create();
        brain.SitDown();
        Assert.Equal(MascotState.FloorSit, brain.State);
        Run(brain, Tuning.MinFloorSitSeconds - 0.1f);
        Assert.Equal(MascotState.FloorSit, brain.State);
        int frames = 0;
        while (brain.State == MascotState.FloorSit && frames++ < 60 * 30)
            brain.Update(1f / 60f);
        Assert.Equal(MascotState.GetUp, brain.State);
    }

    [Fact]
    public void SitsOnTheEdgeWhenAskedOnAWindow()
    {
        var brain = Create(surroundings: WithWindow(300, 500, 1100, 900));
        brain.BeginDrag();
        brain.DragTo(700f, 400f);
        brain.EndDrag(0f, 0f);
        Run(brain, 1f);
        brain.SitDown();
        Assert.Equal(MascotState.Sit, brain.State);
    }

    [Fact]
    public void WalkingToSomewhereFromTheGroundGetsUpFirst()
    {
        var brain = Create();
        brain.SitDown();
        brain.WalkTo(1500f);
        Assert.Equal(MascotState.GetUp, brain.State);
        Run(brain, Tuning.GetUpSeconds + 0.1f);
        Assert.Equal(MascotState.Walk, brain.State);
    }

    [Fact]
    public void DoesMoreThanWalkOnAnEmptyFloor()
    {
        var seen = new HashSet<MascotState>();
        bool jogged = false;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var brain = Create(seed);
            for (int i = 0; i < 60 * 300; i++)
            {
                brain.Update(1f / 60f);
                seen.Add(brain.State);
                jogged |= brain.Running;
            }
        }
        Assert.Contains(MascotState.FloorSit, seen);
        Assert.Contains(MascotState.Dance, seen);
        Assert.True(jogged, "it should jog now and then");
    }

    [Fact]
    public void HoldStopsDancing()
    {
        var brain = Create();
        brain.Preview(MascotState.Dance);
        brain.Hold(2f);
        Assert.Equal(MascotState.Idle, brain.State);
    }

    [Fact]
    public void SteppingOffAWindowOnPurposeLandsOnTheFeet()
    {
        // A window top 1.5 heights above the floor: falling off it by accident tumbles,
        // hopping down on purpose does not.
        var brain = Create(surroundings: WithWindow(300, 590, 1100, 1000));
        int controlled = 0;
        for (int seed = 1; seed <= 20 && controlled < 3; seed++)
        {
            brain = Create(seed, surroundings: WithWindow(300, 590, 1100, 1000));
            brain.BeginDrag();
            brain.DragTo(700f, 500f);
            brain.EndDrag(0f, 0f);
            for (int i = 0; i < 60 * 300; i++)
            {
                var before = brain.State;
                bool wasControlled = brain.ControlledFall;
                brain.Update(1f / 60f);
                if (before == MascotState.Falling && brain.State != MascotState.Falling && wasControlled && brain.OnFloor)
                {
                    Assert.Equal(MascotState.Landing, brain.State);
                    controlled++;
                    break;
                }
            }
        }
        Assert.True(controlled > 0, "it should hop down off the window on its own now and then");
    }

    [Fact]
    public void MovesWithItsWindowAndFallsWhenItCloses()
    {
        var brain = Create(surroundings: WithWindow(300, 500, 1100, 900));
        brain.BeginDrag();
        brain.DragTo(700f, 400f);
        brain.EndDrag(0f, 0f);
        Run(brain, 1f);
        Assert.Equal(500f, brain.Y);

        float x = brain.X;
        brain.SetSurroundings(WithWindow(400, 450, 1200, 850));
        Assert.Equal(x + 100f, brain.X, 3);
        Assert.Equal(450f, brain.Y);

        brain.SetSurroundings(Screen());
        Assert.Equal(MascotState.Falling, brain.State);
        bool tumbled = false;
        for (int i = 0; i < 120; i++)
        {
            brain.Update(1f / 60f);
            tumbled |= brain.State == MascotState.Tumble;
        }
        Assert.True(brain.OnFloor);
        Assert.True(tumbled, "a long fall should end in a tumble");
        Run(brain, 3f);
        Assert.NotEqual(MascotState.Tumble, brain.State);
    }

    [Fact]
    public void StayingKeepsToTheWindowAndFollowsIt()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var brain = Create(seed, surroundings: WithWindow(300, 500, 1100, 900));
            brain.BeginDrag();
            brain.DragTo(700f, 400f);
            brain.EndDrag(0f, 0f);
            brain.Staying = true;
            for (int i = 0; i < 60 * 300; i++)
            {
                brain.Update(1f / 60f);
                if (brain.State is not (MascotState.Falling or MascotState.Landing))
                    Assert.Equal(7 * 16, brain.PlatformId);
            }
            brain.SetSurroundings(WithWindow(500, 450, 1300, 850));
            Assert.Equal(450f, brain.Y);
            Assert.Equal(7 * 16, brain.PlatformId);
        }
    }

    [Fact]
    public void StayingOnAWallKeepsHoldingIt()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 500f);
        brain.EndDrag(0f, 0f);
        brain.Staying = true;
        for (int i = 0; i < 60 * 120; i++)
        {
            brain.Update(1f / 60f);
            Assert.Equal(Surroundings.LeftWallId, brain.WallId);
        }
    }

    [Fact]
    public void ShortDropsLandOnTheFeet()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(800f, Height - 60f);
        brain.EndDrag(0f, 0f);
        bool tumbled = false;
        for (int i = 0; i < 60; i++)
        {
            brain.Update(1f / 60f);
            tumbled |= brain.State == MascotState.Tumble;
        }
        Assert.False(tumbled);
    }

    [Fact]
    public void ThrownMascotBouncesOffScreenSides()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(1700f, 100f);
        brain.EndDrag(5000f, 0f);
        Run(brain, 0.3f);
        Assert.InRange(brain.X, 0f, Width);
        Assert.True(brain.VelocityX <= 0f);
    }

    [Fact]
    public void LandsThenReturnsToIdle()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(800f, 300f);
        brain.EndDrag(300f, 0f);
        Assert.Equal(1, brain.Facing);
        Run(brain, 2f);
        Assert.True(brain.OnFloor);
        Run(brain, 1f);
        Assert.NotEqual(MascotState.Falling, brain.State);
    }

    [Fact]
    public void ShrinkingScreenKeepsTheMascotInside()
    {
        var brain = Create(startX: 1800f);
        brain.SetSurroundings(Surroundings.Screen(1280f, 700f, Character));
        Assert.InRange(brain.X, 0f, 1280f);
        Assert.Equal(699f, brain.Y);
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
    public void PlaceAtPutsTheMascotOnTheFloor()
    {
        var brain = Create();
        brain.BeginDrag();
        brain.DragTo(10f, 10f);
        brain.PlaceAt(5000f);
        Assert.Equal(Width, brain.X);
        Assert.Equal(Height - 1f, brain.Y);
        Assert.Equal(MascotState.Idle, brain.State);
    }
}

public class SurroundingsTests
{
    [Fact]
    public void FloorAndScreenEdgesAlwaysExist()
    {
        var s = Surroundings.Screen(1920f, 1040f, 300f);
        Assert.Single(s.Platforms);
        Assert.Equal(1039f, s.Floor.Y);
        Assert.Equal(2, s.Walls.Count);
        Assert.Equal(-1, s.FindWall(Surroundings.LeftWallId)!.Value.Side);
        Assert.Equal(1920f, s.FindWall(Surroundings.RightWallId)!.Value.X);
    }

    [Fact]
    public void CoveredPartsOfAWindowTopAreNotPlatforms()
    {
        var s = Surroundings.FromWindows(1920f, 1040f, 300f, new[]
        {
            new DesktopWindow(1, 600, 350, 900, 800), // on top, covers the middle of the next edge
            new DesktopWindow(2, 400, 500, 1400, 900),
        });
        var pieces = s.Platforms.Where(p => p.Id / 16 == 2).OrderBy(p => p.X0).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal((400f, 600f), (pieces[0].X0, pieces[0].X1));
        Assert.Equal((900f, 1400f), (pieces[1].X0, pieces[1].X1));
        Assert.Contains(s.Platforms, p => p.Id / 16 == 1 && p.Y == 350f);
    }

    [Fact]
    public void WindowSidesAreClimbableUpToTheirTop()
    {
        var s = Surroundings.FromWindows(1920f, 1040f, 300f, new[] { new DesktopWindow(5, 400, 500, 1400, 900) });
        var left = s.Walls.Single(w => w.Id / 16 == 5 && w.X == 400f);
        var right = s.Walls.Single(w => w.Id / 16 == 5 && w.X == 1400f);
        Assert.Equal((1, 500f, 900f), (left.Side, left.Top, left.Bottom));
        Assert.Equal(-1, right.Side);
        Assert.Equal(5 * 16, left.TopPlatform);
        Assert.Equal(5 * 16, right.TopPlatform);
    }

    [Fact]
    public void CoveredPartsOfAWindowSideAreNotClimbable()
    {
        var s = Surroundings.FromWindows(1920f, 1040f, 300f, new[]
        {
            new DesktopWindow(1, 300, 600, 700, 750), // covers the middle of the next window's left side
            new DesktopWindow(2, 500, 400, 1400, 1000),
        });
        var pieces = s.Walls.Where(w => w.Id / 16 == 2 && w.X == 500f).OrderBy(w => w.Top).ToList();
        Assert.Equal(2, pieces.Count);
        Assert.Equal((400f, 600f), (pieces[0].Top, pieces[0].Bottom));
        Assert.Equal((750f, 1000f), (pieces[1].Top, pieces[1].Bottom));
        Assert.NotNull(pieces[0].TopPlatform);
        Assert.Null(pieces[1].TopPlatform);
    }

    [Fact]
    public void WindowsTooHighOrTooLowOrTooNarrowAreSkipped()
    {
        var s = Surroundings.FromWindows(1920f, 1040f, 300f, new[]
        {
            new DesktopWindow(1, 0, 0, 1920, 1040),    // maximised: top at the screen edge
            new DesktopWindow(2, 100, 1000, 900, 1040), // nearly on the floor
            new DesktopWindow(3, 100, 600, 150, 900),  // narrower than the character
        });
        Assert.Single(s.Platforms);
    }
}

public class TouchAndRestTests
{
    private const float Width = 1920f, Height = 1040f, Character = 300f;
    private static readonly MascotTuning Tuning = new() { Height = Character };

    private static MascotBrain Create(int seed = 1) =>
        new(Tuning, new Random(seed), 960f, Surroundings.Screen(Width, Height, Character));

    private static void Run(MascotBrain brain, float seconds, float dt = 1f / 60f)
    {
        for (float t = 0; t < seconds; t += dt)
            brain.Update(dt);
    }

    [Fact]
    public void LiesDownAndGetsUpWhenWoken()
    {
        var brain = Create();
        brain.LieDown();
        Run(brain, 0.1f);
        Assert.Equal(MascotState.Lie, brain.State);
        Run(brain, 5f);
        Assert.Equal(MascotState.Lie, brain.State);
        brain.Wake();
        Assert.Equal(MascotState.GetUp, brain.State);
        Run(brain, Tuning.GetUpSeconds + 0.1f);
        Assert.Equal(MascotState.Idle, brain.State);
    }

    [Fact]
    public void SometimesLiesDownOnItsOwn()
    {
        bool lay = false;
        for (int seed = 1; seed <= 10 && !lay; seed++)
        {
            var brain = Create(seed);
            for (int i = 0; i < 60 * 300 && !lay; i++)
            {
                brain.Update(1f / 60f);
                lay = brain.State == MascotState.Lie;
            }
        }
        Assert.True(lay);
    }

    [Fact]
    public void LongFallsOftenEndFlatOnTheFace()
    {
        int sprawled = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var brain = Create(seed);
            brain.BeginDrag();
            brain.DragTo(900f, 50f);
            brain.EndDrag(0f, 0f);
            var seen = new HashSet<MascotState>();
            for (int i = 0; i < 60 * 6; i++)
            {
                brain.Update(1f / 60f);
                seen.Add(brain.State);
            }
            if (seen.Contains(MascotState.Sprawl))
            {
                sprawled++;
                Assert.Contains(MascotState.GetUp, seen);
            }
            else
            {
                Assert.Contains(MascotState.Tumble, seen);
            }
        }
        Assert.InRange(sprawled, 5, 19);
    }

    [Fact]
    public void HoldStopsWalkingAndDelaysDecisions()
    {
        var brain = Create();
        brain.WalkTo(1500f);
        Run(brain, 0.5f);
        brain.Hold(10f);
        Assert.Equal(MascotState.Idle, brain.State);
        float x = brain.X;
        Run(brain, 9.5f);
        Assert.Equal(MascotState.Idle, brain.State);
        Assert.Equal(x, brain.X);
    }

    [Fact]
    public void FleesAwayFromTheCursorAtARun()
    {
        var brain = Create();
        brain.Flee(brain.X - 10f, brain.Y - 100f);
        Assert.Equal(MascotState.Walk, brain.State);
        Assert.True(brain.Running);
        Assert.True(brain.TargetX > brain.X);
        Run(brain, 0.2f);
        Assert.True(brain.VelocityX > Tuning.WalkSpeed * 2f);

        // Lying down: gets up first, then runs.
        brain = Create();
        brain.LieDown();
        Run(brain, 0.1f);
        brain.Flee(brain.X + 10f, brain.Y);
        Assert.Equal(MascotState.GetUp, brain.State);
        Run(brain, Tuning.GetUpSeconds + 0.05f);
        Assert.Equal(MascotState.Walk, brain.State);
        Assert.True(brain.Running);
        Assert.True(brain.TargetX < brain.X);
    }

    [Fact]
    public void PettingIsDetectedAfterSustainedStroking()
    {
        var touch = new TouchTracker(new Random(1));
        // Passing over quickly is not petting.
        TouchReaction? reaction = null;
        for (int i = 0; i < 10; i++)
            reaction ??= touch.Update(1f / 60f, 0.1f, TouchZone.Head);
        for (int i = 0; i < 60; i++)
            reaction ??= touch.Update(1f / 60f, 0f, TouchZone.Head);
        Assert.Null(reaction);
        Assert.False(touch.Petting);

        // Rubbing back and forth (about 1.5 heights a second).
        for (int i = 0; i < 60 && reaction == null; i++)
            reaction = touch.Update(1f / 60f, 0.025f, TouchZone.Head);
        Assert.NotNull(reaction);
        Assert.True(touch.Petting);
        Assert.Equal(Emotion.Happy, reaction!.Emotion);

        // Long enough and it gets fed up.
        TouchReaction? fed = null;
        for (int i = 0; i < 60 * 20 && fed == null; i++)
            fed = touch.Update(1f / 60f, 0.025f, TouchZone.Head);
        Assert.True(fed!.Flee);
        Assert.True(touch.Grumpy);
    }

    [Fact]
    public void RubbingTheBodyIsEmbarrassingAndShorterLived()
    {
        var touch = new TouchTracker(new Random(1));
        TouchReaction? first = null, fed = null;
        float t = 0f;
        for (int i = 0; i < 60 * 20 && fed == null; i++)
        {
            var r = touch.Update(1f / 60f, 0.025f, TouchZone.Body);
            if (r != null && first == null)
                first = r;
            else if (r?.Flee == true)
                fed = r;
            t += 1f / 60f;
        }
        Assert.Equal(Emotion.Shy, first!.Emotion);
        Assert.NotNull(fed);
        Assert.InRange(t, TouchTracker.RubPatience, TouchTracker.RubPatience + 1f);
    }

    [Fact]
    public void PokesVaryAndTooManyAnnoy()
    {
        var touch = new TouchTracker(new Random(2));
        var first = touch.Poke(TouchZone.Body);
        var second = touch.Poke(TouchZone.Body);
        Assert.NotEqual(first.Gesture, second.Gesture);
        touch.Poke(TouchZone.Body);
        Assert.Equal(Emotion.Angry, touch.Poke(TouchZone.Body).Emotion);
        touch.Poke(TouchZone.Body);
        Assert.True(touch.Poke(TouchZone.Body).Flee);
    }
}
