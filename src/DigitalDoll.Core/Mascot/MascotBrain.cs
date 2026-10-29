using System;
using System.Collections.Generic;

namespace DigitalDoll.Core.Mascot;

public enum MascotState
{
    Idle,
    Walk,
    Sit,
    Climb,
    Cling,
    Jump,
    Dragged,
    Falling,
    Landing,

    /// <summary>A hard landing after a long fall: on its bottom for a moment, then up again.</summary>
    Tumble,

    /// <summary>Lying down for a rest (dozing off) on the surface it stands on.</summary>
    Lie,

    /// <summary>Flat on its face after a very long fall.</summary>
    Sprawl,

    /// <summary>Getting back on its feet after lying, sprawling or sitting on the ground.</summary>
    GetUp,

    /// <summary>Sitting on the ground with the knees up (where the legs cannot hang over an edge).</summary>
    FloorSit,

    /// <summary>Dancing on the spot for a little while.</summary>
    Dance,

    /// <summary>Holding a named pose on the spot (see <c>PoseCatalog</c>).</summary>
    Pose,
}

/// <summary>How a climbing mascot is holding on to its wall.</summary>
public enum ClimbGrip
{
    /// <summary>Climbing normally (or not climbing at all).</summary>
    Steady,

    /// <summary>Lost its grip and sliding down the wall, scrabbling.</summary>
    Slipping,

    /// <summary>Caught itself after a slip: hanging on for a moment before going on.</summary>
    Hanging,
}

public sealed class MascotTuning
{
    public float WalkSpeed { get; init; } = 90f;
    public float ClimbSpeed { get; init; } = 55f;

    /// <summary>Pull-ups per second while climbing up: it moves in bursts, not at a steady glide.</summary>
    public float ClimbStrokeRate { get; init; } = 1.3f;

    /// <summary>Chance per second of slipping while climbing up.</summary>
    public float SlipChance { get; init; } = 0.16f;
    public float HangSeconds { get; init; } = 0.8f;
    public float Gravity { get; init; } = 2600f;
    public float MaxFallSpeed { get; init; } = 2400f;
    public float MinIdleSeconds { get; init; } = 3f;
    public float MaxIdleSeconds { get; init; } = 9f;
    public float MinWalkDistance { get; init; } = 120f;
    public float MaxWalkDistance { get; init; } = 600f;
    public float EdgeMargin { get; init; } = 60f;
    public float LandingSeconds { get; init; } = 0.45f;
    public float TumbleSeconds { get; init; } = 2.2f;
    public float SprawlSeconds { get; init; } = 3f;
    public float GetUpSeconds { get; init; } = 1.1f;
    public float MinLieSeconds { get; init; } = 14f;
    public float MaxLieSeconds { get; init; } = 35f;
    public float MinFloorSitSeconds { get; init; } = 8f;
    public float MaxFloorSitSeconds { get; init; } = 20f;
    public float MinDanceSeconds { get; init; } = 4f;
    public float MaxDanceSeconds { get; init; } = 9f;

    /// <summary>Walking speed multiplier when running off.</summary>
    public float RunFactor { get; init; } = 2.4f;
    public float ArriveTolerance { get; init; } = 2f;

    /// <summary>Character height in pixels; reach, jump range and wall offset scale with it.</summary>
    public float Height { get; init; } = 300f;

    /// <summary>Distance from a wall to the feet while climbing (half the body depth).</summary>
    public float WallOffset => Height * 0.09f;
    public float JumpUp => Height * 1.4f;
    public float JumpAcross => Height * 1.6f;
    public float GrabDistance => Height * 0.18f;
}

/// <summary>
/// Decides what the mascot does next and integrates its feet position. It stands and
/// walks on platforms (the floor and the tops of windows), climbs the screen edges,
/// jumps between surfaces and falls when it loses its footing. Pure logic: rendering
/// reads <see cref="State"/>, <see cref="X"/>, <see cref="Y"/>, <see cref="Facing"/>
/// and <see cref="WallSide"/> every frame.
/// </summary>
public sealed class MascotBrain
{
    private enum Plan
    {
        None,
        Climb,
        Jump,
        StepOff,
        Sit,
        JumpToWall,
        ClimbDown,
        Lie,
        FloorSit,
    }

    private readonly MascotTuning _tuning;
    private readonly Random _random;
    private float _stateTime;
    private float _waitDuration;

    private Plan _plan;
    private Wall _planWall;
    private Platform _jumpTarget;
    private float _jumpTargetX;

    private Wall? _jumpWall;
    private float _fallStartY;
    private long? _wallId;
    private float _jumpTime, _jumpDuration;
    private float _jumpStartX, _jumpStartY;
    private float _hold;
    private bool _controlledFall;
    private bool _poseGrounded;
    private float _climbStroke, _slipTime, _hangTime;
    private (float X, bool Run)? _afterGetUp;

    public MascotBrain(MascotTuning tuning, Random random, float startX, Surroundings surroundings)
    {
        _tuning = tuning;
        _random = random;
        Surroundings = surroundings;
        var floor = surroundings.Floor;
        X = Math.Clamp(startX, floor.X0 + tuning.EdgeMargin, floor.X1 - tuning.EdgeMargin);
        Y = floor.Y;
        PlatformId = Surroundings.FloorId;
        EnterIdle();
    }

    public MascotState State { get; private set; }
    public Surroundings Surroundings { get; private set; }

    /// <summary>Feet position in window pixels.</summary>
    public float X { get; private set; }
    public float Y { get; private set; }
    public float VelocityX { get; private set; }
    public float VelocityY { get; private set; }

    /// <summary>+1 when facing screen right, -1 when facing screen left.</summary>
    public int Facing { get; private set; } = 1;

    /// <summary>The platform under the feet, or null while climbing or in the air.</summary>
    public long? PlatformId { get; private set; }

    /// <summary>While climbing: the direction of the wall (-1 left, +1 right); 0 otherwise.</summary>
    public int WallSide { get; private set; }

    /// <summary>The wall being climbed (a screen edge or a window side), if any.</summary>
    public long? WallId => _wallId;

    /// <summary>
    /// When set, the mascot keeps to the surface it is on (a window top or side, or the
    /// floor): it still walks, sits and climbs along it, but never leaves on its own.
    /// It follows the window when it moves; dragging it elsewhere makes it stay there.
    /// </summary>
    public bool Staying { get; set; }

    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public float StateTime => _stateTime;

    public bool OnFloor => PlatformId == Surroundings.FloorId;

    /// <summary>True while running off (walking at <see cref="MascotTuning.RunFactor"/> times the speed).</summary>
    public bool Running { get; private set; }

    /// <summary>
    /// True while dropping down on purpose (stepping off a ledge, letting go of a wall):
    /// feet first and ready to land, instead of thrown or knocked off.
    /// </summary>
    public bool ControlledFall => State == MascotState.Falling && _controlledFall;

    /// <summary>While climbing: slipping down, hanging on after a slip, or climbing normally.</summary>
    public ClimbGrip Grip => State != MascotState.Climb ? ClimbGrip.Steady
        : _slipTime > 0f ? ClimbGrip.Slipping
        : _hangTime > 0f ? ClimbGrip.Hanging
        : ClimbGrip.Steady;

    /// <summary>Seconds left during which the mascot stays where it is (see <see cref="Hold"/>).</summary>
    public float HoldTime => _hold;

    public event Action<MascotState, MascotState>? StateChanged;

    /// <summary>
    /// New surroundings (windows moved, opened or closed). A mascot on a window moves
    /// with it and falls if the window disappears or slides away under its feet.
    /// </summary>
    public void SetSurroundings(Surroundings next)
    {
        var previous = Surroundings;
        Surroundings = next;

        if (PlatformId is long id)
        {
            var before = previous.FindPlatform(id);
            var now = next.FindPlatform(id);
            if (now is not Platform platform)
            {
                StartFalling(0f, 0f);
                return;
            }
            if (before is Platform old && id != Surroundings.FloorId)
                X += platform.X0 - old.X0;
            Y = platform.Y;
            if (id == Surroundings.FloorId)
            {
                // The floor only changes with the screen; keep the mascot on it.
                X = Math.Clamp(X, platform.X0, platform.X1);
            }
            else if (!platform.Spans(X))
            {
                StartFalling(0f, 0f);
                return;
            }
            if (State == MascotState.Walk)
                TargetX = Math.Clamp(TargetX, platform.X0, platform.X1);
        }
        else if (_wallId is long wallId)
        {
            if (next.FindWall(wallId) is Wall wall)
            {
                X = wall.X - wall.Side * _tuning.WallOffset;
                Y = Math.Clamp(Y, wall.Top, wall.Bottom);
                TargetY = Math.Clamp(TargetY, wall.Top, wall.Bottom);
            }
            else
            {
                StartFalling(0f, 0f);
                return;
            }
        }

        if (State == MascotState.Jump && (_jumpWall is Wall jw ? next.FindWall(jw.Id) is null : next.FindPlatform(_jumpTarget.Id) is null))
            StartFalling(VelocityX, VelocityY);
        X = Math.Clamp(X, 0f, next.Width);
    }

    public void Update(float dt)
    {
        if (dt <= 0f)
            return;
        _stateTime += dt;
        _hold = MathF.Max(0f, _hold - dt);

        switch (State)
        {
            case MascotState.Idle:
                if (_stateTime >= _waitDuration && _hold <= 0f)
                    DecideOnPlatform();
                break;
            case MascotState.Sit:
                if (_stateTime >= _waitDuration && _hold <= 0f)
                    EnterIdle(shortWait: true);
                break;
            case MascotState.Lie:
                if (_stateTime >= _waitDuration)
                    SetState(MascotState.GetUp);
                break;
            case MascotState.FloorSit:
                if (_stateTime >= _waitDuration && _hold <= 0f)
                    SetState(MascotState.GetUp);
                break;
            case MascotState.Dance:
                if (_stateTime >= _waitDuration)
                    EnterIdle(shortWait: true);
                break;
            case MascotState.Pose:
                // Talking keeps the pose; one on the ground gets up afterwards.
                if (_stateTime >= _waitDuration && _hold <= 0f)
                {
                    if (_poseGrounded)
                        SetState(MascotState.GetUp);
                    else
                        EnterIdle(shortWait: true);
                }
                break;
            case MascotState.Sprawl:
                if (_stateTime >= _tuning.SprawlSeconds)
                    SetState(MascotState.GetUp);
                break;
            case MascotState.GetUp:
                if (_stateTime >= _tuning.GetUpSeconds)
                    FinishGettingUp();
                break;
            case MascotState.Walk:
                UpdateWalk(dt);
                break;
            case MascotState.Climb:
                UpdateClimb(dt);
                break;
            case MascotState.Cling:
                if (_stateTime >= _waitDuration && _hold <= 0f)
                    DecideOnWall();
                break;
            case MascotState.Jump:
                UpdateJump(dt);
                break;
            case MascotState.Falling:
                UpdateFalling(dt);
                break;
            case MascotState.Landing:
                if (_stateTime >= _tuning.LandingSeconds)
                    EnterIdle();
                break;
            case MascotState.Tumble:
                if (_stateTime >= _tuning.TumbleSeconds)
                    EnterIdle(shortWait: true);
                break;
            case MascotState.Dragged:
                break;
        }
    }

    /// <summary>Asks the mascot to walk to a given x on its platform (for example toward the cursor).</summary>
    public void WalkTo(float x)
    {
        if (PlatformId is not long id || Surroundings.FindPlatform(id) is not Platform platform)
            return;
        if (State is MascotState.Dragged or MascotState.Falling or MascotState.Jump)
            return;
        if (GettingUpFirst(x, run: false))
            return;
        _plan = Plan.None;
        _hold = 0f;
        EnterWalk(ClampOn(platform, x));
    }

    /// <summary>Lies down where the mascot stands (if there is room).</summary>
    public void LieDown()
    {
        if (PlatformId is not long id || Surroundings.FindPlatform(id) is not Platform platform)
            return;
        if (State is not (MascotState.Idle or MascotState.Walk or MascotState.Sit or MascotState.Landing or MascotState.FloorSit or MascotState.Dance or MascotState.Pose))
            return;
        _plan = Plan.Lie;
        EnterWalk(LieSpot(platform, X));
    }

    /// <summary>
    /// For previews and snapshots: switches to a state shown on the spot (Sit, Lie,
    /// Sprawl, GetUp, Tumble, FloorSit, Dance) while standing on a platform.
    /// </summary>
    public void Preview(MascotState state)
    {
        if (PlatformId is null || state is not (MascotState.Sit or MascotState.Lie or MascotState.Sprawl or MascotState.GetUp
                or MascotState.Tumble or MascotState.FloorSit or MascotState.Dance or MascotState.Pose))
            return;
        _plan = Plan.None;
        _waitDuration = float.MaxValue;
        SetState(state);
    }

    /// <summary>
    /// Holds a pose on the spot for <paramref name="seconds"/> (longer while it talks);
    /// <paramref name="grounded"/> poses (kneeling, sitting, lying) get up afterwards.
    /// Returns false when it cannot pose now (in the air, climbing, being carried).
    /// </summary>
    public bool StrikePose(bool grounded, float seconds)
    {
        if (PlatformId is null || State is MascotState.Dragged or MascotState.Falling or MascotState.Jump)
            return false;
        _plan = Plan.None;
        _afterGetUp = null;
        VelocityX = 0f;
        _poseGrounded = grounded;
        _waitDuration = seconds;
        SetState(MascotState.Pose);
        return true;
    }

    /// <summary>Dances on the spot (when standing on something).</summary>
    public void Dance()
    {
        if (PlatformId is null || State is MascotState.Dragged or MascotState.Falling or MascotState.Jump)
            return;
        if (State is MascotState.Lie or MascotState.Sprawl or MascotState.FloorSit or MascotState.Sit
            || (State == MascotState.Pose && _poseGrounded))
        {
            SetState(MascotState.GetUp);
            return;
        }
        _plan = Plan.None;
        StartDance();
    }

    /// <summary>Gets up from lying, sitting or a pose on the ground; does nothing when already up.</summary>
    public void GetUp()
    {
        if (State is MascotState.Lie or MascotState.Sprawl or MascotState.FloorSit || (State == MascotState.Pose && _poseGrounded))
            SetState(MascotState.GetUp);
        else if (State is MascotState.Sit or MascotState.Pose)
            EnterIdle(shortWait: true);
    }

    /// <summary>Sits on the ground where the mascot stands.</summary>
    public void SitOnFloor()
    {
        if (PlatformId is null || State is not (MascotState.Idle or MascotState.Walk or MascotState.Landing or MascotState.Dance or MascotState.Pose))
            return;
        _plan = Plan.FloorSit;
        Arrive();
    }

    /// <summary>Wakes a lying mascot (it gets up); does nothing otherwise.</summary>
    public void Wake()
    {
        if (State == MascotState.Lie)
            SetState(MascotState.GetUp);
    }

    /// <summary>
    /// Keeps the mascot where it is for a while (while it talks or reacts): it stops
    /// walking or dancing, keeps sitting or holding on, and a lying mascot gets up.
    /// </summary>
    public void Hold(float seconds)
    {
        _hold = MathF.Max(_hold, seconds);
        switch (State)
        {
            case MascotState.Walk or MascotState.Dance:
                _plan = Plan.None;
                EnterIdle(shortWait: true);
                break;
            case MascotState.Climb when _wallId is long id && Surroundings.FindWall(id) is Wall wall:
                StartCling(wall, Y);
                break;
            case MascotState.Lie:
                SetState(MascotState.GetUp);
                break;
        }
    }

    /// <summary>Runs off, away from a point (the cursor): along its surface, or up or down its wall.</summary>
    public void Flee(float fromX, float fromY)
    {
        _hold = 0f;
        if (State is MascotState.Climb or MascotState.Cling && _wallId is long wallId && Surroundings.FindWall(wallId) is Wall wall)
        {
            float away = fromY < Y ? 1f : -1f;
            float target = Y + away * _tuning.Height * 1.2f;
            if (target < wall.Top || target > wall.Bottom)
                target = Y - away * _tuning.Height * 1.2f;
            StartClimb(wall, target);
            return;
        }
        if (PlatformId is not long id || Surroundings.FindPlatform(id) is not Platform platform)
            return;
        if (State is MascotState.Dragged or MascotState.Falling or MascotState.Jump)
            return;

        float distance = _tuning.Height * Lerp(1.5f, 2.5f, (float)_random.NextDouble());
        int side = fromX < X ? 1 : fromX > X ? -1 : (_random.Next(2) == 0 ? -1 : 1);
        float room = side > 0 ? platform.X1 - X : X - platform.X0;
        // Not much room on that side: dodge past the cursor to the other.
        if (room < _tuning.Height * 0.6f)
            side = -side;
        float targetX = ClampOn(platform, X + side * distance);
        if (GettingUpFirst(targetX, run: true))
            return;
        _plan = Plan.None;
        EnterWalk(targetX);
        Running = State == MascotState.Walk;
    }

    /// <summary>Makes a climbing mascot lose its grip and slide down a little (also for previews).</summary>
    public void Slip()
    {
        if (State != MascotState.Climb || _slipTime > 0f)
            return;
        _hangTime = 0f;
        _slipTime = 0.45f;
        VelocityY = _tuning.ClimbSpeed * 0.5f;
    }

    /// <summary>Climbs to <paramref name="y"/> on the wall the mascot is holding on to.</summary>
    public void ClimbTo(float y)
    {
        if (_wallId is long id && Surroundings.FindWall(id) is Wall wall && State is MascotState.Cling or MascotState.Climb)
            StartClimb(wall, y);
    }

    /// <summary>Sits down where the mascot stands (on the ground when on the floor).</summary>
    public void SitDown()
    {
        if (PlatformId is null || State is not (MascotState.Idle or MascotState.Walk or MascotState.Landing or MascotState.Tumble or MascotState.Dance or MascotState.Pose))
            return;
        _plan = Plan.Sit;
        Arrive();
    }

    /// <summary>Moves the standing mascot to <paramref name="x"/> on the floor without walking.</summary>
    public void PlaceAt(float x)
    {
        var floor = Surroundings.Floor;
        X = Math.Clamp(x, floor.X0, floor.X1);
        Y = floor.Y;
        PlatformId = Surroundings.FloorId;
        LeaveWall();
        if (State != MascotState.Walk)
            EnterIdle();
    }

    public void BeginDrag()
    {
        VelocityX = 0f;
        VelocityY = 0f;
        PlatformId = null;
        LeaveWall();
        _plan = Plan.None;
        SetState(MascotState.Dragged);
    }

    public void DragTo(float x, float y)
    {
        if (State != MascotState.Dragged)
            return;
        X = x;
        Y = y;
    }

    /// <summary>Releases the mascot: it grabs a nearby screen edge, or keeps the throw velocity and falls.</summary>
    public void EndDrag(float velocityX, float velocityY)
    {
        if (State != MascotState.Dragged)
            return;
        foreach (var wall in Surroundings.Walls)
        {
            float gap = MathF.Abs(X - (wall.X - wall.Side * _tuning.WallOffset));
            if (gap < _tuning.GrabDistance && Y > wall.Top && Y < wall.Bottom - 1f)
            {
                StartCling(wall, Y);
                return;
            }
        }
        if (MathF.Abs(velocityX) > 1f)
            Facing = velocityX > 0 ? 1 : -1;
        StartFalling(velocityX, velocityY);
    }

    // ---- Decisions -------------------------------------------------------------

    private void DecideOnPlatform()
    {
        if (PlatformId is not long id || Surroundings.FindPlatform(id) is not Platform here)
        {
            StartFalling(0f, 0f);
            return;
        }

        var options = new List<(float Weight, Action Act)>
        {
            (2.2f, () => EnterWalk(PickWalkTarget(here))),
            // Now and then a jog instead of a walk.
            (0.6f, () =>
            {
                EnterWalk(PickWalkTarget(here));
                Running = State == MascotState.Walk;
            }),
            (0.5f, StartDance),
        };
        if (here.Width >= _tuning.Height * 1.3f)
            options.Add((Staying ? 0.6f : 0.45f, () => { _plan = Plan.Lie; EnterWalk(LieSpot(here, PickWalkTarget(here, nearby: true))); }));
        if (here.Width >= _tuning.Height * 0.8f)
            options.Add((Staying ? 1.2f : 0.8f, () => { _plan = Plan.FloorSit; EnterWalk(PickWalkTarget(here, nearby: true)); }));
        if (Staying)
        {
            options.Add((2f, () => EnterIdle()));
            if (here.Id != Surroundings.FloorId)
                options.Add((2f, () => { _plan = Plan.Sit; EnterWalk(PickWalkTarget(here, nearby: true)); }));
            Choose(options);
            return;
        }
        // Sitting lets the legs hang below the surface, so only on window tops (on the
        // floor they would hang over the taskbar).
        if (here.Id != Surroundings.FloorId)
            options.Add((1.5f, () => { _plan = Plan.Sit; EnterWalk(PickWalkTarget(here, nearby: true)); }));

        foreach (var wall in Surroundings.Walls)
        {
            var w = wall;
            float stand = w.X - w.Side * _tuning.WallOffset;
            if (MathF.Abs(w.Top - here.Y) < 1f && w.TopPlatform == here.Id)
            {
                // Down the side of the window we are standing on.
                float edge = Math.Clamp(w.X, here.X0 + 1f, here.X1 - 1f);
                options.Add((0.9f, () => { _plan = Plan.ClimbDown; _planWall = w; EnterWalk(edge); }));
                continue;
            }
            if (!here.Spans(stand) || w.Top > here.Y - _tuning.Height * 0.3f)
                continue;
            if (w.Bottom >= here.Y - 2f)
            {
                // The wall reaches down to our feet: walk over and climb.
                options.Add((w.TopPlatform != null ? 2f : 1.2f, () =>
                {
                    _plan = Plan.Climb;
                    _planWall = w;
                    EnterWalk(stand);
                }));
            }
            else if (here.Y - w.Bottom <= _tuning.JumpUp * 0.8f)
            {
                // The window floats above us: jump up and grab its side.
                options.Add((w.TopPlatform != null ? 1.6f : 0.8f, () =>
                {
                    _plan = Plan.JumpToWall;
                    _planWall = w;
                    EnterWalk(stand);
                }));
            }
        }

        var jumps = ReachablePlatforms(here, X);
        if (jumps.Count > 0)
        {
            options.Add((2f, () =>
            {
                var (target, takeoff, landing) = jumps[_random.Next(jumps.Count)];
                _plan = Plan.Jump;
                _jumpTarget = target;
                _jumpTargetX = landing;
                EnterWalk(takeoff);
            }));
        }

        if (here.Id != Surroundings.FloorId)
        {
            options.Add((0.8f, () =>
            {
                _plan = Plan.StepOff;
                bool left = _random.Next(2) == 0;
                EnterWalk(left ? here.X0 - 1f : here.X1 + 1f, clamp: false);
            }));
        }

        Choose(options);
    }

    private void DecideOnWall()
    {
        if (_wallId is not long id || Surroundings.FindWall(id) is not Wall wall)
        {
            StartFalling(0f, 0f);
            return;
        }

        var floor = Surroundings.Floor;
        if (Staying)
        {
            // Move a little up or down the same wall, or keep holding on.
            Choose(new List<(float Weight, Action Act)>
            {
                (1f, () => StartClimb(wall, PickWallTarget(wall))),
                (1f, () => StartCling(wall, Y)),
            });
            return;
        }
        var options = new List<(float Weight, Action Act)>
        {
            (1.5f, () => StartClimb(wall, PickWallTarget(wall))),
            (0.5f, () => StartFalling(-wall.Side * 120f, 0f, controlled: true)),
        };
        if (wall.TopPlatform is long top && Surroundings.FindPlatform(top) is not null)
            options.Add((2.5f, () => StartClimb(wall, wall.Top)));
        if (wall.Bottom >= floor.Y - 1f)
            options.Add((1f, () => StartClimb(wall, floor.Y)));
        var jumps = ReachablePlatforms(null, X);
        if (jumps.Count > 0)
        {
            options.Add((1.2f, () =>
            {
                var (target, _, landing) = jumps[_random.Next(jumps.Count)];
                StartJump(target, landing);
            }));
        }
        Choose(options);
    }

    private void Choose(List<(float Weight, Action Act)> options)
    {
        float total = 0f;
        foreach (var o in options)
            total += o.Weight;
        float pick = (float)_random.NextDouble() * total;
        foreach (var (weight, act) in options)
        {
            pick -= weight;
            if (pick <= 0f)
            {
                act();
                return;
            }
        }
        options[^1].Act();
    }

    /// <summary>Platforms within jumping range: (target, take-off x, landing x).</summary>
    private List<(Platform Target, float Takeoff, float Landing)> ReachablePlatforms(Platform? from, float x)
    {
        var result = new List<(Platform, float, float)>();
        foreach (var target in Surroundings.Platforms)
        {
            if (from is Platform f && target.Id == f.Id)
                continue;
            float dy = target.Y - Y;
            if (dy < -_tuning.JumpUp || dy > _tuning.Height * 3f || MathF.Abs(dy) < 4f)
                continue;
            float inset = MathF.Min(_tuning.Height * 0.2f, target.Width / 2f);
            float landing = Math.Clamp(x, target.X0 + inset, target.X1 - inset);
            float takeoff = from is Platform p ? Math.Clamp(landing, p.X0 + 2f, p.X1 - 2f) : x;
            if (MathF.Abs(landing - takeoff) > _tuning.JumpAcross)
                continue;
            result.Add((target, takeoff, landing));
        }
        return result;
    }

    // ---- States ----------------------------------------------------------------

    private void UpdateWalk(float dt)
    {
        float delta = TargetX - X;
        float speed = _tuning.WalkSpeed * (Running ? _tuning.RunFactor : 1f);
        float step = speed * dt;
        if (MathF.Abs(delta) <= MathF.Max(step, _tuning.ArriveTolerance))
        {
            X = TargetX;
            Arrive();
            return;
        }
        Facing = delta > 0 ? 1 : -1;
        VelocityX = Facing * speed;
        X += Facing * step;

        // Walking off the end of a window top.
        if (PlatformId is long id && Surroundings.FindPlatform(id) is Platform here && !here.Spans(X))
            StartFalling(VelocityX * 0.6f, 0f);
    }

    private void Arrive()
    {
        var plan = _plan;
        _plan = Plan.None;
        switch (plan)
        {
            case Plan.Climb:
                // Window sides are mostly climbed to get on top of the window.
                bool toTop = _planWall.TopPlatform != null && _random.NextDouble() < 0.7;
                StartClimb(_planWall, toTop ? _planWall.Top : PickWallTarget(_planWall));
                break;
            case Plan.Jump:
                StartJump(_jumpTarget, _jumpTargetX);
                break;
            // The legs hang over the edge of a window top; on the floor they would hang
            // over the taskbar, so it sits on the ground there instead.
            case Plan.Sit when OnFloor:
            case Plan.FloorSit:
                VelocityX = 0f;
                _waitDuration = Lerp(_tuning.MinFloorSitSeconds, _tuning.MaxFloorSitSeconds, (float)_random.NextDouble());
                SetState(MascotState.FloorSit);
                break;
            case Plan.Sit:
                VelocityX = 0f;
                _waitDuration = Lerp(6f, 14f, (float)_random.NextDouble());
                SetState(MascotState.Sit);
                break;
            case Plan.StepOff:
                // A little hop off the edge.
                StartFalling(Facing * _tuning.WalkSpeed * 1.2f, -_tuning.Height * 1.2f, controlled: true);
                break;
            case Plan.JumpToWall:
                StartJumpToWall(_planWall);
                break;
            case Plan.ClimbDown:
                StartClimb(_planWall, PickWallTarget(_planWall, below: _planWall.Top + _tuning.Height * 0.3f));
                break;
            case Plan.Lie:
                VelocityX = 0f;
                _waitDuration = Lerp(_tuning.MinLieSeconds, _tuning.MaxLieSeconds, (float)_random.NextDouble());
                SetState(MascotState.Lie);
                break;
            default:
                EnterIdle();
                break;
        }
    }

    private void StartClimb(Wall wall, float targetY)
    {
        PlatformId = null;
        _wallId = wall.Id;
        WallSide = wall.Side;
        Facing = wall.Side;
        X = wall.X - wall.Side * _tuning.WallOffset;
        TargetY = Math.Clamp(targetY, wall.Top, wall.Bottom);
        VelocityX = 0f;
        _slipTime = _hangTime = 0f;
        SetState(MascotState.Climb);
    }

    private void StartCling(Wall wall, float y)
    {
        PlatformId = null;
        _wallId = wall.Id;
        WallSide = wall.Side;
        Facing = wall.Side;
        X = wall.X - wall.Side * _tuning.WallOffset;
        Y = Math.Clamp(y, wall.Top, wall.Bottom);
        VelocityX = 0f;
        VelocityY = 0f;
        _waitDuration = Lerp(2f, 6f, (float)_random.NextDouble());
        SetState(MascotState.Cling);
    }

    private void UpdateClimb(float dt)
    {
        var holding = _wallId is long holdId ? Surroundings.FindWall(holdId) : null;
        if (_slipTime > 0f)
        {
            // Sliding down, faster and faster, until it catches hold again.
            _slipTime -= dt;
            VelocityY = MathF.Min(VelocityY + _tuning.Gravity * 0.35f * dt, _tuning.ClimbSpeed * 4f);
            Y += VelocityY * dt;
            if (holding is Wall w)
                Y = MathF.Min(Y, w.Bottom - 1f);
            if (_slipTime <= 0f)
            {
                _hangTime = _tuning.HangSeconds * Lerp(0.7f, 1.3f, (float)_random.NextDouble());
                VelocityY = 0f;
            }
            return;
        }
        if (_hangTime > 0f)
        {
            _hangTime -= dt;
            VelocityY = 0f;
            return;
        }

        float delta = TargetY - Y;
        float speed;
        if (delta < 0f)
        {
            // Up in pull-ups: a strong heave, then a moment to get a new grip.
            _climbStroke += dt * MathF.Tau * _tuning.ClimbStrokeRate;
            float pull = MathF.Max(0f, MathF.Sin(_climbStroke));
            speed = _tuning.ClimbSpeed * (0.3f + 2.2f * pull * MathF.Sqrt(pull));
            // Now and then it loses its grip (not right away, and only with room to slide).
            if (_stateTime > 1f && holding is Wall wall && Y + _tuning.Height * 0.8f < wall.Bottom
                && _random.NextDouble() < _tuning.SlipChance * dt)
            {
                _slipTime = Lerp(0.3f, 0.55f, (float)_random.NextDouble());
                VelocityY = _tuning.ClimbSpeed * 0.5f;
                return;
            }
        }
        else
        {
            // Down carefully, at an even pace.
            _climbStroke += dt * MathF.Tau * _tuning.ClimbStrokeRate * 0.7f;
            speed = _tuning.ClimbSpeed * 0.8f;
        }
        float step = speed * dt;
        if (MathF.Abs(delta) <= MathF.Max(step, _tuning.ArriveTolerance))
        {
            Y = TargetY;
            VelocityY = 0f;
            var floor = Surroundings.Floor;
            if (MathF.Abs(Y - floor.Y) < 1f)
            {
                // Back down on the floor.
                LeaveWall();
                PlatformId = Surroundings.FloorId;
                X = Math.Clamp(X, floor.X0, floor.X1);
                EnterIdle(shortWait: true);
                return;
            }
            if (_wallId is long id && Surroundings.FindWall(id) is Wall wall)
            {
                // At the top edge of a window: pull up onto it.
                if (!Staying && MathF.Abs(Y - wall.Top) < 1f && wall.TopPlatform is long top && Surroundings.FindPlatform(top) is Platform platform)
                {
                    LeaveWall();
                    X = Math.Clamp(wall.X + wall.Side * _tuning.Height * 0.15f, platform.X0, platform.X1);
                    Y = platform.Y;
                    Land(platform);
                    return;
                }
                StartCling(wall, Y);
            }
            return;
        }
        VelocityY = MathF.Sign(delta) * speed;
        Y += MathF.Sign(delta) * step;
    }

    private void StartJump(Platform target, float landingX)
    {
        _jumpTarget = target;
        _jumpTargetX = landingX;
        _jumpStartX = X;
        _jumpStartY = Y;
        float dx = landingX - X, dy = target.Y - Y;
        _jumpDuration = Math.Clamp(MathF.Sqrt(dx * dx + dy * dy) / (_tuning.Height * 2.2f), 0.45f, 1.1f);
        VelocityX = dx / _jumpDuration;
        VelocityY = (dy - 0.5f * _tuning.Gravity * _jumpDuration * _jumpDuration) / _jumpDuration;
        if (MathF.Abs(dx) > 1f)
            Facing = dx > 0 ? 1 : -1;
        _jumpTime = 0f;
        _jumpWall = null;
        PlatformId = null;
        LeaveWall();
        SetState(MascotState.Jump);
    }

    private void StartJumpToWall(Wall wall)
    {
        // Aim a little above the bottom of the side so the hands find something to hold.
        float y = MathF.Max(wall.Top, wall.Bottom - _tuning.Height * 0.15f);
        StartJump(new Platform(-99, X, X, y), X);
        _jumpWall = wall;
    }

    private void UpdateJump(float dt)
    {
        _jumpTime += dt;
        if (_jumpTime >= _jumpDuration)
        {
            X = _jumpTargetX;
            Y = _jumpTarget.Y;
            if (_jumpWall is Wall wall)
            {
                _jumpWall = null;
                StartCling(wall, Y);
                return;
            }
            Land(_jumpTarget);
            return;
        }
        X = _jumpStartX + VelocityX * _jumpTime;
        Y = _jumpStartY + VelocityY * _jumpTime + 0.5f * _tuning.Gravity * _jumpTime * _jumpTime;
    }

    private void StartFalling(float velocityX, float velocityY, bool controlled = false)
    {
        PlatformId = null;
        LeaveWall();
        _jumpWall = null;
        _plan = Plan.None;
        VelocityX = velocityX;
        VelocityY = velocityY;
        _fallStartY = Y;
        _controlledFall = controlled;
        SetState(MascotState.Falling);
    }

    private void UpdateFalling(float dt)
    {
        float previousY = Y;
        VelocityY = MathF.Min(VelocityY + _tuning.Gravity * dt, _tuning.MaxFallSpeed);
        VelocityX *= MathF.Pow(0.35f, dt);
        X += VelocityX * dt;
        Y += VelocityY * dt;

        if (X < 0f)
        {
            X = 0f;
            VelocityX = -VelocityX * 0.4f;
        }
        else if (X > Surroundings.Width)
        {
            X = Surroundings.Width;
            VelocityX = -VelocityX * 0.4f;
        }

        if (VelocityY < 0f)
            return;

        // Land on the highest surface crossed during this step.
        Platform? landing = null;
        foreach (var platform in Surroundings.Platforms)
        {
            if (platform.Spans(X) && platform.Y >= previousY - 0.5f && platform.Y <= Y
                && (landing is not Platform best || platform.Y < best.Y))
                landing = platform;
        }
        if (landing is Platform hit)
        {
            Y = hit.Y;
            Land(hit);
        }
        else if (Y > Surroundings.Floor.Y)
        {
            Y = Surroundings.Floor.Y;
            X = Math.Clamp(X, Surroundings.Floor.X0, Surroundings.Floor.X1);
            Land(Surroundings.Floor);
        }
    }

    private void Land(Platform platform)
    {
        float drop = State == MascotState.Falling ? Y - _fallStartY : 0f;
        PlatformId = platform.Id;
        LeaveWall();
        X = Math.Clamp(X, platform.X0, platform.X1);
        VelocityX = 0f;
        VelocityY = 0f;
        // Jumping down on purpose it lands on its feet unless the drop is very long;
        // thrown or knocked off, a long fall knocks it flat on its face more often than not.
        bool controlled = _controlledFall;
        _controlledFall = false;
        if (controlled)
            SetState(drop > _tuning.Height * 2.5f ? MascotState.Tumble : MascotState.Landing);
        else if (drop > _tuning.Height * 2f && _random.NextDouble() < 0.65)
            SetState(MascotState.Sprawl);
        else
            SetState(drop > _tuning.Height * 0.9f ? MascotState.Tumble : MascotState.Landing);
    }

    private void StartDance()
    {
        VelocityX = 0f;
        _waitDuration = Lerp(_tuning.MinDanceSeconds, _tuning.MaxDanceSeconds, (float)_random.NextDouble());
        SetState(MascotState.Dance);
    }

    private void EnterIdle(bool shortWait = false)
    {
        VelocityX = 0f;
        VelocityY = 0f;
        _waitDuration = shortWait
            ? Lerp(0.8f, 2f, (float)_random.NextDouble())
            : Lerp(_tuning.MinIdleSeconds, _tuning.MaxIdleSeconds, (float)_random.NextDouble());
        SetState(MascotState.Idle);
    }

    private void EnterWalk(float target, bool clamp = true)
    {
        if (clamp && PlatformId is long id && Surroundings.FindPlatform(id) is Platform platform)
            target = Math.Clamp(target, platform.X0, platform.X1);
        TargetX = target;
        if (MathF.Abs(target - X) <= _tuning.ArriveTolerance)
        {
            X = target;
            Arrive();
            return;
        }
        Facing = target > X ? 1 : -1;
        SetState(MascotState.Walk);
    }

    private float PickWalkTarget(Platform platform, bool nearby = false)
    {
        float margin = MathF.Min(_tuning.EdgeMargin, platform.Width / 3f);
        float min = platform.X0 + margin, max = platform.X1 - margin;
        if (max <= min)
            return (platform.X0 + platform.X1) / 2f;

        float distance = nearby
            ? Lerp(0f, _tuning.MinWalkDistance, (float)_random.NextDouble())
            : Lerp(_tuning.MinWalkDistance, _tuning.MaxWalkDistance, (float)_random.NextDouble());
        int direction = _random.Next(2) == 0 ? -1 : 1;
        if (direction > 0 && X + _tuning.MinWalkDistance > max)
            direction = -1;
        else if (direction < 0 && X - _tuning.MinWalkDistance < min)
            direction = 1;
        return Math.Clamp(X + direction * distance, min, max);
    }

    private float PickWallTarget(Wall wall, float? below = null)
    {
        float top = below ?? wall.Top;
        float bottom = MathF.Max(top, wall.Bottom - _tuning.Height * 0.2f);
        return Lerp(top, bottom, (float)_random.NextDouble());
    }

    /// <summary>Where to lie down near <paramref name="x"/>: the body needs about a height of room.</summary>
    private float LieSpot(Platform platform, float x)
    {
        float half = _tuning.Height * 0.6f;
        return platform.Width <= half * 2f ? (platform.X0 + platform.X1) / 2f : Math.Clamp(x, platform.X0 + half, platform.X1 - half);
    }

    /// <summary>When lying, sprawled or sitting on the ground, gets up first and walks (or runs) to x afterwards.</summary>
    private bool GettingUpFirst(float x, bool run)
    {
        bool groundPose = State == MascotState.Pose && _poseGrounded;
        if (State is not (MascotState.Lie or MascotState.Sprawl or MascotState.GetUp or MascotState.FloorSit) && !groundPose)
            return false;
        _afterGetUp = (x, run);
        if (State is MascotState.Lie or MascotState.FloorSit || groundPose)
            SetState(MascotState.GetUp);
        return true;
    }

    private void FinishGettingUp()
    {
        var next = _afterGetUp;
        _afterGetUp = null;
        EnterIdle(shortWait: true);
        if (next is (float x, bool run))
        {
            EnterWalk(x);
            Running = run && State == MascotState.Walk;
        }
    }

    private void LeaveWall()
    {
        _wallId = null;
        WallSide = 0;
    }

    private float ClampOn(Platform platform, float x)
    {
        float margin = MathF.Min(_tuning.EdgeMargin, platform.Width / 3f);
        return platform.Width <= margin * 2f
            ? (platform.X0 + platform.X1) / 2f
            : Math.Clamp(x, platform.X0 + margin, platform.X1 - margin);
    }

    private void SetState(MascotState next)
    {
        var previous = State;
        State = next;
        if (next != MascotState.Walk)
            Running = false;
        if (next is MascotState.Dragged or MascotState.Falling)
            _afterGetUp = null;
        _stateTime = 0f;
        if (previous != next)
            StateChanged?.Invoke(previous, next);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
