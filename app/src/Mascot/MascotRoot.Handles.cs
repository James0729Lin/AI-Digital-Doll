using System;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Mascot;

/// <summary>
/// Moving and rotating, picked from the right-click menu; the next click (or Esc) ends
/// them. Move: she follows the mouse, and stays exactly where she is put down (in
/// mid-air too). Rotate: moving the mouse turns her like a trackball - sideways turns her
/// round, up and down tips her head over heels about her middle - with some weight to
/// her: she lags a little behind the mouse; once done, a turn carries on spinning until
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

    private Grip _grip;
    private float _userYaw, _spinTarget, _spinVelocity;
    private float _userPitch, _pitchTarget, _pitchVelocity;
    private Vector2 _gripCursor;

    /// <summary>Radians of turn per pixel dragged on the rotate handle.</summary>
    private const float TurnPerPixel = 0.012f;

    /// <summary>Starts moving her with the mouse (picked from the menu), held by her middle.</summary>
    private void StartMoving()
    {
        var brain = _brain;
        if (brain == null)
            return;
        _grip = Grip.Move;
        _pressCursor = new Vector2(brain.X, brain.Y - _heightPixels * 0.5f);
        StartDrag();
        _chat?.Say(Tr("MENU_MOVE_HINT"));
    }

    /// <summary>Starts turning her with the mouse (picked from the menu).</summary>
    private void StartRotating()
    {
        if (_brain == null)
            return;
        _grip = Grip.Rotate;
        _gripCursor = CursorInWindow();
        _spinTarget = _userYaw;
        _pitchTarget = _userPitch;
        _brain.Hold(1f);
        _chat?.Say(Tr("MENU_ROTATE_HINT"));
    }

    /// <summary>Ends moving or rotating; returns whether either was going on.</summary>
    private bool FinishGrip()
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
                // Done while turning: it keeps spinning (see UpdateSpin).
                _grip = Grip.None;
                _pressed = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The turn the user gave her. While rotating she is pulled toward the angle the mouse
    /// has swept round her by a spring (so she has some weight and lags a little); once
    /// done, she spins on and slows down. Walking or climbing off, the turn is
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
