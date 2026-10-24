using System;
using System.Collections.Generic;
using DigitalDoll.Core.Acting;

namespace DigitalDoll.Core.Mascot;

public enum TouchZone
{
    Head,
    Body,
}

/// <summary>What the character does about being touched.</summary>
public sealed record TouchReaction(Emotion Emotion, float Intensity, Gesture? Gesture, bool Flee = false);

/// <summary>
/// Reads the mouse as touch: clicks are pokes, and the cursor moving back and forth
/// over the character is petting (on the head) or rubbing (on the body). Head pats
/// are liked for a while; rubbing quickly gets embarrassing; either one for too long,
/// or poke after poke, makes the character cross and it walks off.
/// </summary>
public sealed class TouchTracker
{
    /// <summary>Stroking energy (character heights of travel, decaying) needed to count as petting.</summary>
    private const float StartEnergy = 0.45f;
    private const float StopEnergy = 0.15f;
    private const float EnergyDecay = 2.5f;
    private const float StartDelay = 0.35f;

    public const float HeadPatPatience = 12f;
    public const float RubPatience = 5f;

    private readonly Random _random;
    private readonly List<float> _pokes = new();
    private float _time;
    private float _energy;
    private float _sustained;
    private float _grumpyUntil;
    private Gesture? _lastPoke;

    public TouchTracker(Random random) => _random = random;

    public bool Petting { get; private set; }
    public TouchZone Zone { get; private set; }

    /// <summary>Seconds the current petting has lasted.</summary>
    public float PetTime { get; private set; }

    /// <summary>True for a while after the character got fed up.</summary>
    public bool Grumpy => _time < _grumpyUntil;

    /// <summary>
    /// Advances by <paramref name="dt"/>. <paramref name="travel"/> is how far the cursor
    /// moved over the character this frame, in character heights (0 when it is not over it).
    /// </summary>
    public TouchReaction? Update(float dt, float travel, TouchZone zone)
    {
        _time += dt;
        _energy = _energy * MathF.Exp(-EnergyDecay * dt) + travel;
        _pokes.RemoveAll(t => _time - t > 4f);

        if (!Petting)
        {
            _sustained = _energy > StartEnergy ? _sustained + dt : 0f;
            if (_sustained < StartDelay)
                return null;
            Petting = true;
            Zone = zone;
            PetTime = 0f;
            if (Grumpy)
                return new TouchReaction(Emotion.Angry, 0.7f, Gesture.Pout);
            return zone == TouchZone.Head
                ? new TouchReaction(Emotion.Happy, 0.8f, null)
                : new TouchReaction(Emotion.Shy, 0.8f, Gesture.Giggle);
        }

        if (_energy < StopEnergy)
        {
            Petting = false;
            _sustained = 0f;
            return null;
        }

        if (zone != Zone)
        {
            // Moving from the head to the body (or back) starts a new kind of touch.
            Zone = zone;
            if (!Grumpy && zone == TouchZone.Body)
                return new TouchReaction(Emotion.Shy, 0.8f, Gesture.Shy);
        }
        PetTime += dt;
        float patience = Grumpy ? 1.5f : Zone == TouchZone.Head ? HeadPatPatience : RubPatience;
        if (PetTime >= patience)
            return FedUp();
        return null;
    }

    /// <summary>A click on the character.</summary>
    public TouchReaction Poke(TouchZone zone)
    {
        _pokes.Add(_time);
        int count = _pokes.Count;
        if (Grumpy || count >= 6)
            return FedUp();
        if (count >= 4)
            return new TouchReaction(Emotion.Angry, 0.6f, Gesture.Pout);

        var options = zone == TouchZone.Head
            ? new[] { Gesture.Startle, Gesture.Tilt, Gesture.FixHair, Gesture.Giggle }
            : new[] { Gesture.Startle, Gesture.Giggle, Gesture.Tilt, Gesture.Shy, Gesture.Wave };
        Gesture pick;
        do
            pick = options[_random.Next(options.Length)];
        while (pick == _lastPoke);
        _lastPoke = pick;
        var emotion = pick switch
        {
            Gesture.Startle => Emotion.Surprised,
            Gesture.Shy => Emotion.Shy,
            Gesture.Tilt => Emotion.Neutral,
            _ => Emotion.Happy,
        };
        return new TouchReaction(emotion, 0.7f, pick);
    }

    private TouchReaction FedUp()
    {
        Petting = false;
        _energy = 0f;
        _sustained = 0f;
        _pokes.Clear();
        _grumpyUntil = _time + 8f;
        return new TouchReaction(Emotion.Angry, 0.9f, Gesture.Pout, Flee: true);
    }
}
