using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The one ear. Sound reaches it through the air between source and listener: late by
/// distance over the speed of sound, shifted by their closing speed, and thinned by whichever end
/// has less air. The chase camera rides the vehicle, so it also hears the hull.</summary>
public sealed partial class Soundscape : Node3D {

    public const string World = "World";
    public const string Hull = "Hull";
    private const string Ambience = "Ambience";

    private const string Root = "res://Assets/Audio/";

    private const float SeaLevelDensity = 1.225f;

    // Beds sit under the flight, not over it; the bus sets the overall level, these the balance.
    private const float WindLevel = 0.6f;
    private const float OceanLevel = 0.6f;
    private const float PadLevel = 0.45f;

    // A listener in the thinnest air still hears a muffled world rather than a switched-off one.
    private const float ThinCutoff = 900.0f;

    private static readonly Dictionary<string, AudioStream> Streams = new();

    // Headroom under the limiter. Kept here, not on the bus, because the loading duck rides the master fader.
    private static float _masterDb = -6.0f;

    public static Soundscape Active { get; private set; }

    /// <summary>Real seconds since the scene started; the time base every delay is measured on.</summary>
    public double Clock { get; private set; }

    /// <summary>True when the ear is the chase camera, which travels with the flown vessel.</summary>
    public bool Attached { get; private set; }

    public Vector3d Ear { get; private set; }

    private sealed class Shot {

        public AudioStreamPlayer3D Player;
        public Vector3d? Fixed;
        public double Start;
        public bool Started;

    }

    private readonly List<Shot> _shots = new();

    private AudioListener3D _listener;
    private AudioEffectLowPassFilter _worldFilter;
    private int _master;

    private Vector3d _earVelocity;
    private float _earAir;
    private float _soundSpeed = 340.0f;
    private bool _clamped = true;

    private AudioStreamPlayer _windCalm;
    private AudioStreamPlayer _windGale;
    private AudioStreamPlayer _oceanCalm;
    private AudioStreamPlayer _oceanRough;
    private AudioStreamPlayer _pad;

    public static AudioStream Load(string path) {

        if (!Streams.TryGetValue(path, out AudioStream stream)) {

            stream = GD.Load<AudioStream>(Root + path);

            if (stream is AudioStreamOggVorbis loop) {

                loop.Loop = true;

            }

            Streams[path] = stream;

        }

        return stream;

    }

    /// <summary>One of a numbered set of takes, so a repeated event is not the same recording twice.</summary>
    public static string Pick(string stem, int count) => $"{stem}_{GD.RandRange(1, count)}.wav";

    public override void _Ready() {

        Active = this;

        _listener = new AudioListener3D { Name = "Ear" };
        AddChild(_listener);
        _listener.MakeCurrent();

        _worldFilter = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(AudioServer.GetBusIndex(World), 0);
        _master = AudioServer.GetBusIndex("Master");

        _windCalm = Bed("WindCalm", "Surface/wind_calm.ogg");
        _windGale = Bed("WindGale", "Surface/wind_gale.ogg");
        _oceanCalm = Bed("OceanCalm", "Surface/ocean_calm.ogg");
        _oceanRough = Bed("OceanRough", "Surface/ocean_rough.ogg");
        _pad = Bed("Pad", "Surface/pad_ambience.ogg");

        Flight.Active.Staged += Staged;
        Flight.Active.Scrubbed += Scrubbed;

    }

    private AudioStreamPlayer Bed(string name, string path) {

        AudioStreamPlayer bed = new AudioStreamPlayer { Name = name, Stream = Load(path), Bus = Ambience, VolumeDb = -80.0f };
        AddChild(bed);
        return bed;

    }

    /// <summary>Moves the ear to the flight camera. <paramref name="attached"/> is true for the chase camera.</summary>
    public void Sync(double delta, Transform3D view, bool attached) {

        Flight flight = Flight.Active;
        CelestialBody body = flight.Body;

        Clock += delta;
        Attached = attached;

        _listener.GlobalTransform = view;
        Ear = Frames.Origin + Frames.Sim(view.Origin);
        _earVelocity = attached ? flight.Vessel.Velocity : body.AirVelocityAt(Ear);

        double altitude = body.AltitudeOf(Ear);
        _earAir = body.HasAtmosphere ? Mathf.Clamp((float)body.Atmosphere.DensityAt(altitude) / SeaLevelDensity, 0.0f, 1.0f) : 0.0f;
        _soundSpeed = body.HasAtmosphere ? Mathf.Max((float)body.Atmosphere.SpeedOfSoundAt(altitude), 200.0f) : 340.0f;

        _worldFilter.CutoffHz = Mathf.Lerp(ThinCutoff, 20_000.0f, Mathf.Sqrt(_earAir));

        // The cover hides a cut; the sound goes down with it rather than jumping underneath.
        float master = SceneTransition.Loading ? -60.0f : _masterDb;
        AudioServer.SetBusVolumeDb(_master, Mathf.Lerp(AudioServer.GetBusVolumeDb(_master), master, 1.0f - Mathf.Exp(-(float)delta / 0.08f)));

        if (_clamped && !flight.Clamped && VesselView.Active != null) {

            Emit("Events/clamp_release.wav", VesselView.Active, Vector3.Zero, 30.0f, 1.0f, attached);

        }

        _clamped = flight.Clamped;

        SyncBeds(delta, flight, body, altitude);
        SyncShots(flight, body);

    }

    /// <summary>How much of a source at <paramref name="source"/> reaches the ear, how long it
    /// takes, and the pitch its motion relative to the ear puts on it.</summary>
    public float Reach(Vector3d source, Vector3d velocity, out double delay, out float doppler) {

        CelestialBody body = Flight.Active.Body;
        Vector3d offset = Ear - source;
        double distance = offset.Length;

        delay = distance / _soundSpeed;
        doppler = 1.0f;

        float sourceAir = (float)(body.AirDensityAt(source) / SeaLevelDensity);
        float path = Mathf.Min(Mathf.Min(sourceAir, _earAir), 1.0f);

        if (path <= 0.0f) {

            return 0.0f;

        }

        if (distance > 0.01) {

            Vector3d toward = offset / distance;
            double emitter = Vector3d.Dot(velocity - body.AirVelocityAt(source), toward);
            double receiver = Vector3d.Dot(_earVelocity - body.AirVelocityAt(Ear), -toward);
            doppler = Mathf.Clamp((float)((_soundSpeed + receiver) / Math.Max(_soundSpeed - emitter, 1.0)), 0.5f, 2.0f);

        }

        // Square root keeps the fade long: a tenth of an atmosphere is still clearly heard.
        return Mathf.Min(Mathf.Sqrt(path) * 1.4f, 1.0f);

    }

    /// <summary>A one-shot riding a node. With <paramref name="hull"/> the ridden vehicle also
    /// carries it to the chase camera through its structure, undelayed and muffled.</summary>
    public void Emit(string path, Node3D parent, Vector3 local, float reach, float level, bool hull = false, double after = 0.0) {

        if (!IsInstanceValid(parent)) {

            return;

        }

        Vector3d source = Frames.Origin + Frames.Sim(parent.ToGlobal(local));
        float gain = Reach(source, Vector3d.Zero, out double delay, out _);

        if (gain > 0.0f) {

            Queue(Voice(path, reach, level * gain, World), parent, local, null, Clock + delay + after);

        }

        if (hull) {

            AudioStreamPlayer3D inside = Voice(path, reach, level, Hull);
            inside.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            inside.PanningStrength = 0.0f;
            Queue(inside, parent, local, null, Clock + after);

        }

    }

    /// <summary>A one-shot fixed to the ground at a body-fixed point, starting <paramref name="after"/> seconds late.</summary>
    public void Emit(string path, Vector3d bodyFixed, float reach, float level, double after = 0.0) {

        Flight flight = Flight.Active;
        float gain = Reach(flight.Body.ToInertial(bodyFixed, flight.Time), Vector3d.Zero, out double delay, out _);

        if (gain > 0.0f) {

            Queue(Voice(path, reach, level * gain, World), this, Vector3.Zero, bodyFixed, Clock + delay + after);

        }

    }

    /// <summary>The double crack of a shock front sweeping over the ear.</summary>
    public void Boom(double distance, float level) {

        AudioStreamPlayer boom = new AudioStreamPlayer {

            Stream = Load(distance > 2500.0 ? "Atmosphere/sonic_boom_far.wav" : "Atmosphere/sonic_boom.wav"),
            Bus = World,
            VolumeDb = Mathf.LinearToDb(level * Mathf.Sqrt(_earAir)),

        };

        AddChild(boom);
        boom.Finished += boom.QueueFree;
        boom.Play();

    }

    private static AudioStreamPlayer3D Voice(string path, float reach, float level, string bus) {

        return new AudioStreamPlayer3D {

            Stream = Load(path),
            Bus = bus,
            VolumeDb = Mathf.LinearToDb(level),

            UnitSize = reach,
            MaxDistance = reach * 400.0f,
            MaxDb = 3.0f,
            PitchScale = (float)GD.RandRange(0.94, 1.06),

            AttenuationFilterCutoffHz = 7000.0f,
            AttenuationFilterDb = -18.0f,
            DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,

        };

    }

    private void Queue(AudioStreamPlayer3D player, Node3D parent, Vector3 local, Vector3d? bodyFixed, double start) {

        player.Position = local;
        parent.AddChild(player);
        player.Finished += player.QueueFree;
        _shots.Add(new Shot { Player = player, Fixed = bodyFixed, Start = start });

    }

    private void SyncShots(Flight flight, CelestialBody body) {

        for (int index = _shots.Count - 1; index >= 0; index--) {

            Shot shot = _shots[index];

            if (!IsInstanceValid(shot.Player)) {

                _shots.RemoveAt(index);
                continue;

            }

            // The origin follows the vessel, so ground-fixed sounds are put back every frame.
            if (shot.Fixed.HasValue) {

                shot.Player.GlobalPosition = Frames.Point(body.ToInertial(shot.Fixed.Value, flight.Time));

            }

            if (!shot.Started && Clock >= shot.Start) {

                shot.Player.Play();
                shot.Started = true;

            }

            // Once playing, a sound riding a node needs nothing more; it frees itself when done.
            if (shot.Started && !shot.Fixed.HasValue) {

                _shots.RemoveAt(index);

            }

        }

    }

    private void SyncBeds(double delta, Flight flight, CelestialBody body, double altitude) {

        double time = flight.Time;
        float air = Mathf.Sqrt(_earAir);

        // On the chase camera the rush of the vehicle's own flight replaces the breeze.
        float still = Attached ? 1.0f - Mathf.SmoothStep(20.0f, 80.0f, (float)flight.Vessel.Aero.AirSpeed) : 1.0f;
        float wind = (float)(body.Weather?.WindSpeedAt(time) ?? 0.0);
        float ground = 1.0f - Mathf.SmoothStep(400.0f, 900.0f, (float)body.HeightAboveGround(Ear, time));
        float breeze = air * still * ground * Mathf.SmoothStep(0.5f, 12.0f, wind);
        float gale = Mathf.SmoothStep(10.0f, 26.0f, wind);

        bool overWater = body.Terrain != null && body.Terrain.Elevation(body.ToBodyFixed(Ear, time)) < 0.0;
        float shore = overWater ? 1.0f - Mathf.SmoothStep(15.0f, 400.0f, (float)altitude) : 0.0f;
        float rough = Mathf.SmoothStep(6.0f, 20.0f, (float)(body.Weather?.SeaSpeedAt(time) ?? 0.0));

        double fromPad = (Ear - flight.Site.PositionAt(body, time)).Length;
        float pad = (1.0f - Mathf.SmoothStep(300.0f, 2500.0f, (float)fromPad))
            * (1.0f - Mathf.SmoothStep(50.0f, 400.0f, (float)(altitude - flight.Site.Height)));

        Fade(_windCalm, WindLevel * breeze * (1.0f - gale), delta);
        Fade(_windGale, WindLevel * breeze * gale, delta);
        Fade(_oceanCalm, OceanLevel * shore * (1.0f - rough), delta);
        Fade(_oceanRough, OceanLevel * shore * rough, delta);
        Fade(_pad, PadLevel * pad * air, delta);

    }

    private static void Fade(AudioStreamPlayer bed, float level, double delta) {

        float current = Mathf.DbToLinear(bed.VolumeDb);
        current = Mathf.Lerp(current, level, 1.0f - Mathf.Exp(-(float)delta / 0.6f));
        bed.VolumeDb = Mathf.LinearToDb(current);

        if (current < 0.001f) {

            if (bed.Playing) {

                bed.Stop();

            }

            return;

        }

        if (!bed.Playing) {

            bed.Play((float)GD.RandRange(0.0, bed.Stream.GetLength()));

        }

    }

    // The joint is at the base of what is still flying; the bolts fire, then the springs push.
    private void Staged(Vessel spent) {

        if (VesselView.Active == null) {

            return;

        }

        Emit("Events/staging.wav", VesselView.Active, Vector3.Zero, 50.0f, 1.0f, Attached);
        Emit("Events/separation.wav", VesselView.Active, Vector3.Zero, 20.0f, 0.8f, Attached, 0.06);

    }

    // Debris is struck off the books in the same step it is lost, so its end is heard from here.
    private void Scrubbed(Vessel debris) {

        Flight flight = Flight.Active;
        Vector3d where = flight.Body.ToBodyFixed(debris.Position, flight.Time);

        if (debris.Fate == VesselFate.Impacted) {

            Emit(Pick("Events/explosion", 2), where, 300.0f, 1.0f);

        }
        else if (debris.Fate == VesselFate.BurnedUp) {

            Emit("Events/breakup.wav", where, 120.0f, 1.0f);

        }

    }

    /// <summary>Bus levels from the debug bridge, in decibels: World, Hull, Ambience or Master.</summary>
    public static bool Tune(string bus, string value) {

        if (bus == "Master") {

            _masterDb = value.ToFloat();
            return true;

        }

        int index = AudioServer.GetBusIndex(bus);

        if (index < 0) {

            return false;

        }

        AudioServer.SetBusVolumeDb(index, value.ToFloat());
        return true;

    }

}
