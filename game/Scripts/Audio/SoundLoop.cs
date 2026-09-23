using System;

using Godot;

namespace FullThrust.Game;

/// <summary>A looping world voice. The source writes its level and pitch as they are now; the
/// listener hears them as they were when the sound left the source.</summary>
public sealed partial class SoundLoop : AudioStreamPlayer3D {

    // Sixty samples a second, thirty seconds deep: ten kilometres of sea-level air.
    private const double Rate = 60.0;
    private const int Depth = 1800;

    private const float Silent = 0.0005f;

    private readonly float[] _levels = new float[Depth];
    private readonly float[] _pitches = new float[Depth];

    private long _first = -1;
    private long _written = -1;
    private float _heard;

    public static SoundLoop Create(string name, string path, string bus, float reach) {

        return new SoundLoop {

            Name = name,
            Stream = Soundscape.Load(path),
            Bus = bus,

            UnitSize = reach,
            MaxDistance = reach * 400.0f,
            MaxDb = 3.0f,

            AttenuationFilterCutoffHz = 7000.0f,
            AttenuationFilterDb = -18.0f,
            DopplerTracking = DopplerTrackingEnum.Disabled,

        };

    }

    public void Feed(double clock, float level, float pitch) {

        long slot = (long)(clock * Rate);

        if (_first < 0) {

            _first = slot;
            _written = slot - 1;

        }

        for (long index = Math.Max(_written + 1, slot - Depth + 1); index <= slot; index++) {

            _levels[index % Depth] = level;
            _pitches[index % Depth] = pitch;

        }

        _written = Math.Max(_written, slot);

    }

    public void Hear(double clock, double delay, float gain, float doppler, double delta) {

        if (_written < 0) {

            return;

        }

        long slot = Math.Clamp((long)((clock - delay) * Rate), Math.Max(_first, _written - Depth + 1), _written);

        // A few tens of milliseconds of glide keeps frame-rate steps out of the level.
        float wanted = _levels[slot % Depth] * gain;
        _heard = Mathf.Lerp(_heard, wanted, 1.0f - Mathf.Exp(-(float)delta / 0.04f));

        if (_heard < Silent) {

            if (Playing) {

                Stop();

            }

            return;

        }

        VolumeDb = Mathf.LinearToDb(_heard);
        PitchScale = Mathf.Clamp(_pitches[slot % Depth] * doppler, 0.25f, 4.0f);

        if (!Playing) {

            // Loops of the same recording on neighbouring voices must not start in phase.
            Play((float)GD.RandRange(0.0, Stream.GetLength()));

        }

    }

}
