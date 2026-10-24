using System;

namespace DigitalDoll.Core.Mascot;

public enum MascotState
{
    Idle,
    Walk,
    Dragged,
    Falling,
    Landing,
}

/// <summary>
/// Desktop area the mascot may move in, in window pixels (y grows downward).
/// The floor is the line the feet stand on, normally the top edge of the taskbar.
/// </summary>
public readonly record struct Ground(float MinX, float MaxX, float FloorY);

public sealed class MascotTuning
{
    public float WalkSpeed { get; init; } = 90f;
    public float Gravity { get; init; } = 2600f;
    public float MaxFallSpeed { get; init; } = 2400f;
    public float MinIdleSeconds { get; init; } = 3f;
    public float MaxIdleSeconds { get; init; } = 9f;
    public float MinWalkDistance { get; init; } = 120f;
    public float MaxWalkDistance { get; init; } = 600f;
    public float EdgeMargin { get; init; } = 60f;
    public float LandingSeconds { get; init; } = 0.45f;
    public float ArriveTolerance { get; init; } = 2f;
}

/// <summary>
/// Decides what the mascot does next and integrates its feet position.
/// Pure logic: rendering and animation read <see cref="State"/>, <see cref="X"/>,
/// <see cref="Y"/> and <see cref="Facing"/> every frame.
/// </summary>
public sealed class MascotBrain
{
    private readonly MascotTuning _tuning;
    private readonly Random _random;
    private float _stateTime;
    private float _idleDuration;

    public MascotBrain(MascotTuning tuning, Random random, float startX, Ground ground)
    {
        _tuning = tuning;
        _random = random;
        Ground = ground;
        X = Math.Clamp(startX, ground.MinX + tuning.EdgeMargin, ground.MaxX - tuning.EdgeMargin);
        Y = ground.FloorY;
        EnterIdle();
    }

    public MascotState State { get; private set; }
    public Ground Ground { get; private set; }

    /// <summary>Feet position in window pixels.</summary>
    public float X { get; private set; }
    public float Y { get; private set; }
    public float VelocityX { get; private set; }
    public float VelocityY { get; private set; }

    /// <summary>+1 when facing screen right, -1 when facing screen left.</summary>
    public int Facing { get; private set; } = 1;
    public float TargetX { get; private set; }
    public float StateTime => _stateTime;

    public event Action<MascotState, MascotState>? StateChanged;

    public void SetGround(Ground ground)
    {
        Ground = ground;
        X = Math.Clamp(X, ground.MinX, ground.MaxX);
        if (State is MascotState.Idle or MascotState.Walk or MascotState.Landing)
            Y = ground.FloorY;
        if (State == MascotState.Walk)
            TargetX = ClampTarget(TargetX);
    }

    public void Update(float dt)
    {
        if (dt <= 0f)
            return;
        _stateTime += dt;

        switch (State)
        {
            case MascotState.Idle:
                if (_stateTime >= _idleDuration)
                    EnterWalk(PickWalkTarget());
                break;

            case MascotState.Walk:
                UpdateWalk(dt);
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

    /// <summary>Asks the mascot to walk to a given x (for example toward the cursor).</summary>
    public void WalkTo(float x)
    {
        if (State is MascotState.Dragged or MascotState.Falling)
            return;
        EnterWalk(ClampTarget(x));
    }

    /// <summary>Moves the standing mascot to <paramref name="x"/> on the floor without walking.</summary>
    public void PlaceAt(float x)
    {
        X = Math.Clamp(x, Ground.MinX, Ground.MaxX);
        Y = Ground.FloorY;
        if (State != MascotState.Walk)
            EnterIdle();
    }

    public void BeginDrag()
    {
        VelocityX = 0f;
        VelocityY = 0f;
        SetState(MascotState.Dragged);
    }

    public void DragTo(float x, float y)
    {
        if (State != MascotState.Dragged)
            return;
        X = x;
        Y = y;
    }

    /// <summary>Releases the mascot; it keeps the throw velocity and falls to the floor.</summary>
    public void EndDrag(float velocityX, float velocityY)
    {
        if (State != MascotState.Dragged)
            return;
        VelocityX = velocityX;
        VelocityY = velocityY;
        if (Math.Abs(velocityX) > 1f)
            Facing = velocityX > 0 ? 1 : -1;
        SetState(MascotState.Falling);
    }

    private void UpdateWalk(float dt)
    {
        float delta = TargetX - X;
        float step = _tuning.WalkSpeed * dt;
        if (Math.Abs(delta) <= Math.Max(step, _tuning.ArriveTolerance))
        {
            X = TargetX;
            EnterIdle();
            return;
        }
        Facing = delta > 0 ? 1 : -1;
        VelocityX = Facing * _tuning.WalkSpeed;
        X += Facing * step;
    }

    private void UpdateFalling(float dt)
    {
        VelocityY = Math.Min(VelocityY + _tuning.Gravity * dt, _tuning.MaxFallSpeed);
        VelocityX *= MathF.Pow(0.35f, dt);
        X += VelocityX * dt;
        Y += VelocityY * dt;

        if (X < Ground.MinX)
        {
            X = Ground.MinX;
            VelocityX = -VelocityX * 0.4f;
        }
        else if (X > Ground.MaxX)
        {
            X = Ground.MaxX;
            VelocityX = -VelocityX * 0.4f;
        }

        if (Y >= Ground.FloorY)
        {
            Y = Ground.FloorY;
            VelocityX = 0f;
            VelocityY = 0f;
            SetState(MascotState.Landing);
        }
    }

    private void EnterIdle()
    {
        VelocityX = 0f;
        VelocityY = 0f;
        _idleDuration = Lerp(_tuning.MinIdleSeconds, _tuning.MaxIdleSeconds, (float)_random.NextDouble());
        SetState(MascotState.Idle);
    }

    private void EnterWalk(float target)
    {
        TargetX = target;
        if (Math.Abs(target - X) <= _tuning.ArriveTolerance)
        {
            EnterIdle();
            return;
        }
        Facing = target > X ? 1 : -1;
        SetState(MascotState.Walk);
    }

    private float PickWalkTarget()
    {
        float min = Ground.MinX + _tuning.EdgeMargin;
        float max = Ground.MaxX - _tuning.EdgeMargin;
        if (max <= min)
            return X;

        float distance = Lerp(_tuning.MinWalkDistance, _tuning.MaxWalkDistance, (float)_random.NextDouble());
        int direction = _random.Next(2) == 0 ? -1 : 1;
        // Turn around when the chosen direction does not leave enough room.
        if (direction > 0 && X + _tuning.MinWalkDistance > max)
            direction = -1;
        else if (direction < 0 && X - _tuning.MinWalkDistance < min)
            direction = 1;
        return Math.Clamp(X + direction * distance, min, max);
    }

    private float ClampTarget(float x)
    {
        float min = Ground.MinX + _tuning.EdgeMargin;
        float max = Ground.MaxX - _tuning.EdgeMargin;
        return max <= min ? (Ground.MinX + Ground.MaxX) * 0.5f : Math.Clamp(x, min, max);
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
