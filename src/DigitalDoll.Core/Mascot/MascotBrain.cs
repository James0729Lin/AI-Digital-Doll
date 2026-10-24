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
}

public sealed class MascotTuning
{
    public float WalkSpeed { get; init; } = 90f;
    public float ClimbSpeed { get; init; } = 55f;
    public float Gravity { get; init; } = 2600f;
    public float MaxFallSpeed { get; init; } = 2400f;
    public float MinIdleSeconds { get; init; } = 3f;
    public float MaxIdleSeconds { get; init; } = 9f;
    public float MinWalkDistance { get; init; } = 120f;
    public float MaxWalkDistance { get; init; } = 600f;
    public float EdgeMargin { get; init; } = 60f;
    public float LandingSeconds { get; init; } = 0.45f;
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
    }

    private readonly MascotTuning _tuning;
    private readonly Random _random;
    private float _stateTime;
    private float _waitDuration;

    private Plan _plan;
    private Wall _planWall;
    private Platform _jumpTarget;
    private float _jumpTargetX;

    private float _jumpTime, _jumpDuration;
    private float _jumpStartX, _jumpStartY;

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

    /// <summary>-1 / +1 while on the left / right screen edge, 0 otherwise.</summary>
    public int WallSide { get; private set; }

    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public float StateTime => _stateTime;

    public bool OnFloor => PlatformId == Surroundings.FloorId;

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
        else if (WallSide != 0)
        {
            if (next.FindWall(WallSide) is Wall wall)
            {
                X = wall.X - wall.Side * _tuning.WallOffset;
                Y = Math.Clamp(Y, wall.Top, wall.Bottom);
                TargetY = Math.Clamp(TargetY, wall.Top, wall.Bottom);
            }
            else
            {
                StartFalling(0f, 0f);
            }
        }

        if (State == MascotState.Jump && next.FindPlatform(_jumpTarget.Id) is null)
            StartFalling(VelocityX, VelocityY);
        X = Math.Clamp(X, 0f, next.Width);
    }

    public void Update(float dt)
    {
        if (dt <= 0f)
            return;
        _stateTime += dt;

        switch (State)
        {
            case MascotState.Idle:
                if (_stateTime >= _waitDuration)
                    DecideOnPlatform();
                break;
            case MascotState.Sit:
                if (_stateTime >= _waitDuration)
                    EnterIdle(shortWait: true);
                break;
            case MascotState.Walk:
                UpdateWalk(dt);
                break;
            case MascotState.Climb:
                UpdateClimb(dt);
                break;
            case MascotState.Cling:
                if (_stateTime >= _waitDuration)
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
        _plan = Plan.None;
        EnterWalk(ClampOn(platform, x));
    }

    /// <summary>Climbs to <paramref name="y"/> on the wall the mascot is holding on to.</summary>
    public void ClimbTo(float y)
    {
        if (WallSide != 0 && Surroundings.FindWall(WallSide) is Wall wall && State is MascotState.Cling or MascotState.Climb)
            StartClimb(wall, y);
    }

    /// <summary>Sits down where the mascot stands.</summary>
    public void SitDown()
    {
        if (PlatformId is null || State is not (MascotState.Idle or MascotState.Walk or MascotState.Landing))
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
        WallSide = 0;
        if (State != MascotState.Walk)
            EnterIdle();
    }

    public void BeginDrag()
    {
        VelocityX = 0f;
        VelocityY = 0f;
        PlatformId = null;
        WallSide = 0;
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
            if (gap < _tuning.GrabDistance && Y > wall.Top && Y < wall.Bottom - _tuning.Height * 0.1f)
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
            (3f, () => EnterWalk(PickWalkTarget(here))),
        };
        // Sitting lets the legs hang below the surface, so only on window tops (on the
        // floor they would hang over the taskbar).
        if (here.Id != Surroundings.FloorId)
            options.Add((1.5f, () => { _plan = Plan.Sit; EnterWalk(PickWalkTarget(here, nearby: true)); }));

        foreach (var wall in Surroundings.Walls)
        {
            float end = wall.Side < 0 ? here.X0 : here.X1;
            bool touches = MathF.Abs(end - wall.X) < _tuning.Height * 0.15f && here.Y > wall.Top && here.Y <= wall.Bottom + 1f;
            if (touches)
            {
                var w = wall;
                options.Add((1.3f, () =>
                {
                    _plan = Plan.Climb;
                    _planWall = w;
                    EnterWalk(w.X - w.Side * _tuning.WallOffset);
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
        if (Surroundings.FindWall(WallSide) is not Wall wall)
        {
            StartFalling(0f, 0f);
            return;
        }

        var options = new List<(float Weight, Action Act)>
        {
            (2.5f, () => StartClimb(wall, PickWallTarget(wall))),
            (1f, () => StartClimb(wall, Surroundings.Floor.Y)),
            (0.6f, () => StartFalling(-wall.Side * 120f, 0f)),
        };
        var jumps = ReachablePlatforms(null, X);
        if (jumps.Count > 0)
        {
            options.Add((1.5f, () =>
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
        float step = _tuning.WalkSpeed * dt;
        if (MathF.Abs(delta) <= MathF.Max(step, _tuning.ArriveTolerance))
        {
            X = TargetX;
            Arrive();
            return;
        }
        Facing = delta > 0 ? 1 : -1;
        VelocityX = Facing * _tuning.WalkSpeed;
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
                StartClimb(_planWall, PickWallTarget(_planWall));
                break;
            case Plan.Jump:
                StartJump(_jumpTarget, _jumpTargetX);
                break;
            case Plan.Sit:
                VelocityX = 0f;
                _waitDuration = Lerp(6f, 14f, (float)_random.NextDouble());
                SetState(MascotState.Sit);
                break;
            case Plan.StepOff:
                StartFalling(Facing * _tuning.WalkSpeed * 0.6f, 0f);
                break;
            default:
                EnterIdle();
                break;
        }
    }

    private void StartClimb(Wall wall, float targetY)
    {
        PlatformId = null;
        WallSide = wall.Side;
        Facing = wall.Side;
        X = wall.X - wall.Side * _tuning.WallOffset;
        TargetY = Math.Clamp(targetY, wall.Top, wall.Bottom);
        VelocityX = 0f;
        SetState(MascotState.Climb);
    }

    private void StartCling(Wall wall, float y)
    {
        PlatformId = null;
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
        float delta = TargetY - Y;
        float step = _tuning.ClimbSpeed * dt;
        if (MathF.Abs(delta) <= MathF.Max(step, _tuning.ArriveTolerance))
        {
            Y = TargetY;
            VelocityY = 0f;
            var floor = Surroundings.Floor;
            if (MathF.Abs(Y - floor.Y) < 1f)
            {
                // Back down on the floor.
                WallSide = 0;
                PlatformId = Surroundings.FloorId;
                X = Math.Clamp(X, floor.X0, floor.X1);
                EnterIdle(shortWait: true);
                return;
            }
            if (Surroundings.FindWall(WallSide) is Wall wall)
                StartCling(wall, Y);
            return;
        }
        VelocityY = MathF.Sign(delta) * _tuning.ClimbSpeed;
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
        PlatformId = null;
        WallSide = 0;
        SetState(MascotState.Jump);
    }

    private void UpdateJump(float dt)
    {
        _jumpTime += dt;
        if (_jumpTime >= _jumpDuration)
        {
            X = _jumpTargetX;
            Y = _jumpTarget.Y;
            Land(_jumpTarget);
            return;
        }
        X = _jumpStartX + VelocityX * _jumpTime;
        Y = _jumpStartY + VelocityY * _jumpTime + 0.5f * _tuning.Gravity * _jumpTime * _jumpTime;
    }

    private void StartFalling(float velocityX, float velocityY)
    {
        PlatformId = null;
        WallSide = 0;
        _plan = Plan.None;
        VelocityX = velocityX;
        VelocityY = velocityY;
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
        PlatformId = platform.Id;
        WallSide = 0;
        X = Math.Clamp(X, platform.X0, platform.X1);
        VelocityX = 0f;
        VelocityY = 0f;
        SetState(MascotState.Landing);
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

    private float PickWallTarget(Wall wall) =>
        Lerp(wall.Top, wall.Bottom - _tuning.Height * 0.2f, (float)_random.NextDouble());

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
        _stateTime = 0f;
        if (previous != next)
            StateChanged?.Invoke(previous, next);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
