using System;
using System.Linq;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Mascot;

/// <summary>
/// The move and rotate handles. Move: hold and drag, and on release she stays exactly
/// there (in mid-air too). Rotate: hold and drag like a trackball - sideways turns her
/// round, up and down tips her head over heels about her middle - with some weight to
/// her: she lags a little behind the mouse; let go, a turn carries on spinning until
/// friction stops it and a tip swings back upright like a weighted toy.
/// </summary>
public partial class MascotRoot
{
    private enum Grip
    {
        None,
        Move,
        Rotate,
    }

    /// <summary>How close (in character heights) the cursor brings the handles up.</summary>
    private const float HandleReach = 0.35f;

    private CharacterHandles? _handles;
    private Grip _grip;
    private float _userYaw, _spinTarget, _spinVelocity, _handlesShown;
    private float _userPitch, _pitchTarget, _pitchVelocity;
    private Vector2 _gripCursor;

    /// <summary>Radians of turn per pixel dragged on the rotate handle.</summary>
    private const float TurnPerPixel = 0.012f;

    private void CreateHandles()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);
        _handles = new CharacterHandles();
        layer.AddChild(_handles);
    }

    /// <summary>Places the handles beside the head and fades them in while the cursor is near.</summary>
    private void UpdateHandles(float dt)
    {
        if (_handles == null || _body == null)
            return;
        float scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(GetWindow().CurrentScreen) / 96f, 1f, 3f);
        float radius = 15f * scale;
        var head = _camera.UnprojectPosition(_body.HeadPosition);
        var move = head + new Vector2(_heightPixels * 0.3f, -radius * 0.5f);
        // On the other side when that would leave the window.
        if (move.X + radius * 2f > _windowSize.X)
            move.X = head.X - _heightPixels * 0.3f;
        move.X = Mathf.Clamp(move.X, radius + 2f, _windowSize.X - radius - 2f);
        move.Y = Mathf.Clamp(move.Y, radius + 2f, _windowSize.Y - radius * 3.5f);
        var rotate = move + new Vector2(0f, radius * 2.5f);
        if (_grip == Grip.None)
        {
            _handles.MoveCentre = move;
            _handles.RotateCentre = rotate;
        }
        _handles.Radius = radius;

        var cursor = CursorInWindow();
        int hot = _grip == Grip.Move ? 1 : _grip == Grip.Rotate ? 2 : HandleAt(cursor);
        bool near = _grip != Grip.None || hot != 0 || NearCharacter(cursor, _heightPixels * HandleReach);
        bool allowed = !_dragging || _grip == Grip.Move;
        _handlesShown = Mathf.MoveToward(_handlesShown, near && allowed ? 1f : 0f, dt * (near ? 6f : 3f));
        _handles.Shown = _handlesShown;
        _handles.Hot = hot;
        _handles.QueueRedraw();
    }

    /// <summary>1 when the cursor is on the move handle, 2 on the rotate handle, 0 otherwise.</summary>
    private int HandleAt(Vector2 cursor)
    {
        if (_handles == null || _handlesShown < 0.3f)
            return 0;
        float r = _handles.Radius * 1.15f;
        if (cursor.DistanceTo(_handles.MoveCentre) <= r)
            return 1;
        if (cursor.DistanceTo(_handles.RotateCentre) <= r)
            return 2;
        return 0;
    }

    private bool NearCharacter(Vector2 cursor, float margin)
    {
        if (_tight.Length < 3)
            return false;
        float x0 = _tight.Min(p => p.X) - margin, x1 = _tight.Max(p => p.X) + margin;
        float y0 = _tight.Min(p => p.Y) - margin, y1 = _tight.Max(p => p.Y) + margin;
        return cursor.X >= x0 && cursor.X <= x1 && cursor.Y >= y0 && cursor.Y <= y1;
    }

    /// <summary>A press on a handle starts moving or rotating; returns whether it was one.</summary>
    private bool PressHandle(Vector2 cursor)
    {
        switch (HandleAt(cursor))
        {
            case 1:
                _grip = Grip.Move;
                _pressCursor = cursor;
                StartDrag();
                return true;
            case 2:
                _grip = Grip.Rotate;
                _gripCursor = cursor;
                _spinTarget = _userYaw;
                _pitchTarget = _userPitch;
                _brain!.Hold(1f);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Lets go of a handle; returns whether one was held.</summary>
    private bool ReleaseHandle()
    {
        switch (_grip)
        {
            case Grip.Move:
                _grip = Grip.None;
                _dragging = false;
                _pressed = false;
                // Stays exactly where it was put down, whatever is (or isn't) under it.
                _brain!.PinAt(_brain.X, _brain.Y);
                return true;
            case Grip.Rotate:
                // Let go while turning: it keeps spinning (see UpdateSpin).
                _grip = Grip.None;
                _pressed = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The turn the user gave her. While the rotate handle is held she is pulled toward the
    /// angle the mouse has swept round her by a spring (so she has some weight and lags a
    /// little); let go, she spins on and slows down. Walking or climbing off, the turn is
    /// eased away so she faces where she is going again.
    /// </summary>
    private void UpdateSpin(float dt)
    {
        var brain = _brain!;
        if (_grip == Grip.Rotate)
        {
            var cursor = CursorInWindow();
            var moved = cursor - _gripCursor;
            _gripCursor = cursor;
            // Dragging right turns her to the right; dragging down tips her forward.
            _spinTarget += moved.X * TurnPerPixel;
            _pitchTarget += moved.Y * TurnPerPixel;
            _spinVelocity += ((_spinTarget - _userYaw) * 45f - _spinVelocity * 10f) * dt;
            _pitchVelocity += ((_pitchTarget - _userPitch) * 45f - _pitchVelocity * 10f) * dt;
            brain.Hold(0.5f);
        }
        else
        {
            _spinVelocity *= MathF.Exp(-1.4f * dt);
            if (MathF.Abs(_spinVelocity) < 0.02f)
                _spinVelocity = 0f;
            // Tipped over: her weight swings her back upright (the nearest upright, after
            // a full flip), rocking a little before she settles.
            float upright = MathF.Round(_userPitch / Mathf.Tau) * Mathf.Tau;
            _pitchVelocity += (-(_userPitch - upright) * 30f - _pitchVelocity * 3.5f) * dt;
            if (MathF.Abs(_userPitch - upright) < 0.002f && MathF.Abs(_pitchVelocity) < 0.02f)
            {
                _userPitch = 0f;
                _pitchVelocity = 0f;
            }
        }
        _userYaw += _spinVelocity * dt;
        _userPitch += _pitchVelocity * dt;

        // Spun or flipped fast: dizzy for a moment.
        if (MathF.Abs(_spinVelocity) > 9f || MathF.Abs(_pitchVelocity) > 7f)
            _animator?.Feel(Emotion.Surprised, 0.8f, 1.2f);

        bool moving = brain.State is MascotState.Walk or MascotState.Climb or MascotState.Cling or MascotState.Jump
            or MascotState.Falling or MascotState.Dragged;
        if (_grip != Grip.Rotate && _spinVelocity == 0f)
        {
            // Settle the turn into one revolution (the drawn yaw moves along with it).
            float wrapped = Mathf.Wrap(_userYaw, -Mathf.Pi, Mathf.Pi);
            _yaw += wrapped - _userYaw;
            _userYaw = wrapped;
            _spinTarget = _userYaw;
            if (moving)
                _userYaw = Mathf.Lerp(_userYaw, 0f, 1f - MathF.Exp(-3f * dt));
        }
    }

    /// <summary>True while the user is turning her (or she is still spinning from it).</summary>
    private bool Spinning => _grip == Grip.Rotate || _spinVelocity != 0f;

    /// <summary>
    /// The user's tip (head over heels) applied to the character, about her middle so she
    /// turns in place rather than round her feet.
    /// </summary>
    private void ApplyTip(Vector3 feet)
    {
        if (_userPitch == 0f)
        {
            _pivot.Position = feet;
            _pivot.Rotation = new Vector3(0f, _yaw, 0f);
            return;
        }
        float middle = _heightPixels / _pixelsPerMetre * 0.5f;
        var basis = new Basis(Vector3.Right, _userPitch) * new Basis(Vector3.Up, _yaw);
        _pivot.Basis = basis;
        _pivot.Position = feet + Vector3.Up * middle - basis * (Vector3.Up * middle);
    }
}
