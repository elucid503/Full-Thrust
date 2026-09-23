using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    // A 1.5 MN engine is at full level this far away; bigger clusters carry further by the root of their thrust.
    private const float EngineReach = 90.0f;
    private const double ReferenceThrust = 1_500_000.0;

    // Level against delivered thrust. One is plain proportion; above it, half the thrust sounds nearer half as loud.
    private const float LoudnessExponent = 1.3f;

    private const float JoltLight = 1.0f;
    private const float JoltMedium = 4.0f;
    private const float JoltHeavy = 10.0f;

    /// <summary>One stage's engines heard as a single cluster, plus the ground they are firing at.
    /// It hangs off the stage's own node, so it leaves with the stage when it is dropped.</summary>
    private sealed class EngineVoice {

        public Node3D Node;
        public float Reach;

        public SoundLoop Near;
        public SoundLoop Far;
        public SoundLoop Crackle;
        public SoundLoop Rumble;
        public SoundLoop Inside;
        public SoundLoop Pad;
        public SoundLoop Steam;

        public float Roar;
        public float Tail;

    }

    private Node3D _voice;
    private SoundLoop _airflow;
    private SoundLoop _gust;
    private SoundLoop _buffet;
    private SoundLoop _rattle;
    private SoundLoop _plasma;
    private SoundLoop _thrusters;
    private SoundLoop _scrape;
    private SoundLoop _lap;

    private bool _heardWet;
    private VesselFate _heardFate = VesselFate.Flying;
    private bool _behindShock = true;
    private double _joltQuiet;

    private void SyncSound() {

        Soundscape scape = Soundscape.Active;

        if (scape == null) {

            return;

        }

        if (_voice == null) {

            BuildVoice();

        }

        Flight flight = Flight.Active;
        double clock = scape.Clock;
        double delta = GetProcessDeltaTime();

        // Warped time is not heard time; the vessel falls silent until it is flying at one again.
        float live = flight.Warp > 1.0 ? 0.0f : 1.0f;
        bool ridden = scape.Attached && _vessel == flight.Vessel;
        float inside = ridden ? live : 0.0f;

        float gain = scape.Reach(_vessel.Position, _vessel.Velocity, out double delay, out float doppler);
        gain *= Shock(scape, flight, ridden) * live;

        foreach (Piece piece in _pieces) {

            if (piece.Engines.Count > 0) {

                SyncEngines(scape, piece, ridden, clock, delay, gain, doppler, inside, delta);

            }

        }

        SyncFlight(clock);
        // Air tearing past the vehicle is also tearing past a camera flying with it, so it is heard at full wherever it rides.
        _airflow.UnitSize = ridden ? 150.0f : 25.0f;
        _gust.UnitSize = ridden ? 150.0f : 40.0f;

        _airflow.Hear(clock, delay, gain, doppler, delta);
        _gust.Hear(clock, delay, gain, doppler, delta);
        _buffet.Hear(clock, delay, gain, doppler, delta);
        _rattle.Hear(clock, 0.0, inside, 1.0f, delta);
        _plasma.Hear(clock, delay, gain, doppler, delta);
        _thrusters.Hear(clock, delay, gain, doppler, delta);
        _scrape.Hear(clock, delay, gain, doppler, delta);
        _lap.Hear(clock, delay, gain, doppler, delta);

        if (_effectDelta > 0.0f) {

            Events(scape, ridden);

        }

    }

    private void BuildVoice() {

        _voice = new Node3D { Name = "Voice" };
        AddChild(_voice);

        _airflow = Loop("Airflow", "Atmosphere/airflow.ogg", 25.0f);
        _gust = Loop("Gust", "Atmosphere/gust.ogg", 40.0f);
        _buffet = Loop("Buffet", "Atmosphere/buffet.ogg", 30.0f);
        _rattle = Loop("Rattle", "Atmosphere/buffet.ogg", 30.0f, null, Soundscape.Hull);
        _plasma = Loop("Plasma", "Atmosphere/plasma.ogg", 60.0f);
        _thrusters = Loop("Thrusters", "Rcs/Hydrazine/fire.ogg", 8.0f);
        _scrape = Loop("Scrape", "Events/scrape.ogg", 20.0f);
        _lap = Loop("Lap", "Events/water_lap.ogg", 10.0f);

    }

    private SoundLoop Loop(string name, string path, float reach, Node3D parent = null, string bus = Soundscape.World) {

        SoundLoop loop = SoundLoop.Create(name, path, bus, reach);
        (parent ?? _voice).AddChild(loop);

        if (bus == Soundscape.Hull) {

            loop.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
            loop.PanningStrength = 0.0f;

        }

        return loop;

    }

    private EngineVoice Voice(Piece piece) {

        float reach = EngineReach * Mathf.Sqrt((float)(piece.Stage.ThrustNewtons / ReferenceThrust));
        Node3D node = new Node3D { Name = "EngineVoice" };
        piece.Node.AddChild(node);

        return new EngineVoice {

            Node = node,
            Reach = reach,

            Near = Loop("Near", "Engine/Kerolox/roar_near.ogg", reach, node),
            Far = Loop("Far", "Engine/Kerolox/roar_far.ogg", reach * 3.0f, node),
            Crackle = Loop("Crackle", "Engine/Kerolox/crackle.ogg", reach * 2.0f, node),
            Rumble = Loop("Rumble", "Engine/Kerolox/rumble.ogg", reach * 2.0f, node),
            Inside = Loop("Inside", "Engine/Kerolox/rumble.ogg", reach, node, Soundscape.Hull),
            Pad = Loop("Pad", "Surface/pad_roar.ogg", reach, node),
            Steam = Loop("Steam", "Surface/steam.ogg", reach, node),

        };

    }

    private void SyncEngines(Soundscape scape, Piece piece, bool ridden, double clock, double delay, float gain, float doppler, float inside, double delta) {

        EngineVoice voice = piece.Voice ??= Voice(piece);
        Stage stage = piece.Stage;

        Vector3 centre = Vector3.Zero;
        float power = 0.0f;
        int burning = 0;
        int stopped = 0;
        int starved = 0;
        int purged = 0;

        foreach (Engine engine in piece.Engines) {

            centre += engine.Plume.GlobalPosition;
            power += engine.Power;

            EnginePhase phase = stage.EngineStates[engine.Index].Phase;
            bool lit = phase is EnginePhase.Igniting or EnginePhase.Running;
            bool wasLit = engine.Heard is EnginePhase.Igniting or EnginePhase.Running;

            if (wasLit) {

                burning++;

            }

            if (!lit && wasLit && _vessel.Intact) {

                // A tank running dry chokes the engine; a commanded cutoff closes it cleanly.
                if (stage.PropellantMass <= 0.0 || phase == EnginePhase.Exhausted) {

                    starved++;

                }
                else {

                    stopped++;

                }

            }

            if (phase == EnginePhase.Purging && engine.Heard != EnginePhase.Purging) {

                purged++;

            }

            engine.Heard = phase;

        }

        int count = piece.Engines.Count;
        voice.Node.GlobalPosition = centre / count;
        power /= count;

        float distance = (float)(scape.Ear - (Frames.Origin + Frames.Sim(voice.Node.GlobalPosition))).Length;
        float reach = voice.Reach;
        float level = Mathf.Pow(power, LoudnessExponent);
        float air = Mathf.Sqrt(Mathf.Clamp((float)(Flight.Active.Body.AirDensityAt(_vessel.Position) / 1.225), 0.0f, 1.0f));

        // The chamber cuts in milliseconds, but the flow still in the plume carries the roar a moment longer.
        float cut = voice.Roar;
        voice.Roar = Mathf.Max(level, voice.Roar * Mathf.Exp(-_effectDelta / 0.15f));
        float roar = voice.Roar;

        voice.Near.Feed(clock, roar * (1.0f - Mathf.SmoothStep(reach * 1.5f, reach * 12.0f, distance)), 0.94f + 0.10f * power);
        voice.Far.Feed(clock, roar * Mathf.SmoothStep(reach * 0.3f, reach * 3.0f, distance), 0.97f + 0.05f * power);
        voice.Crackle.Feed(clock, roar * air * Mathf.SmoothStep(reach * 0.5f, reach * 3.0f, distance), 1.0f);
        voice.Rumble.Feed(clock, roar * 0.5f, 0.9f + 0.15f * power);
        // The structure the engines were shaking takes most of a second to settle.
        voice.Tail = Mathf.Max(level, voice.Tail * Mathf.Exp(-_effectDelta / 0.35f));
        voice.Inside.Feed(clock, voice.Tail * 0.8f, 0.8f + 0.25f * voice.Tail);

        voice.Near.Hear(clock, delay, gain, doppler, delta);
        voice.Far.Hear(clock, delay, gain, doppler, delta);
        voice.Crackle.Hear(clock, delay, gain, doppler, delta);
        voice.Rumble.Hear(clock, delay, gain, doppler, delta);
        voice.Inside.Hear(clock, 0.0, inside, 1.0f, delta);

        ExhaustImpact impact = piece.Impact;
        float wash = impact != null ? impact.Strength : 0.0f;

        if (wash > 0.0f) {

            Vector3 ground = Frames.Point(impact.Point);
            voice.Pad.GlobalPosition = ground;
            voice.Steam.GlobalPosition = ground;

        }

        voice.Pad.Feed(clock, impact != null && !impact.Water ? wash : 0.0f, 1.0f);
        voice.Steam.Feed(clock, impact != null && impact.Water ? wash : 0.0f, 1.0f);
        voice.Pad.Hear(clock, delay, gain, doppler, delta);
        voice.Steam.Hear(clock, delay, gain, doppler, delta);

        if (_effectDelta <= 0.0f) {

            return;

        }

        // A cutoff is as loud as the share of the roar it took away.
        float lost = cut / Math.Max(burning, 1);

        if (stopped > 0) {

            scape.Emit("Engine/Kerolox/shutdown.wav", voice.Node, Vector3.Zero, reach, lost * stopped);

        }

        if (starved > 0) {

            scape.Emit("Engine/Kerolox/flameout.wav", voice.Node, Vector3.Zero, reach, lost * starved, ridden);

        }

        if (purged > 0) {

            scape.Emit("Engine/Kerolox/purge.wav", voice.Node, Vector3.Zero, reach * 0.3f, 0.7f, ridden);

        }

    }

    // Everything the airframe itself says: air over the skin, the valves, the ground or sea under it.
    private void SyncFlight(double clock) {

        AeroForces air = _vessel.Aero;
        float mach = air.InAir ? (float)air.Mach : 0.0f;
        float pressure = air.InAir ? (float)air.DynamicPressure : 0.0f;
        float speed = (float)air.AirSpeed;

        float transonic = Mathf.SmoothStep(0.75f, 0.95f, mach) * (1.0f - Mathf.SmoothStep(1.1f, 1.4f, mach));
        float incidence = Mathf.Abs(Mathf.Sin((float)air.AngleOfAttack));
        float hypersonic = Mathf.SmoothStep(2.5f, 6.0f, mach);

        // A hiss that rises with speed, handing over to a roar as the dynamic pressure builds and the flow goes transonic.
        float hiss = Mathf.SmoothStep(20.0f, 200.0f, speed) * Mathf.Min(Mathf.Sqrt(pressure / 5_000.0f), 1.0f);
        float roar = Mathf.Min(Mathf.Sqrt(pressure / 25_000.0f), 1.0f) * (0.4f + 0.6f * Mathf.SmoothStep(0.6f, 1.2f, mach));
        float shake = Mathf.Min(Mathf.Sqrt(pressure / 40_000.0f), 1.0f) * (0.3f + 0.7f * Mathf.Max(transonic, incidence));

        _airflow.Feed(clock, hiss * (1.0f - 0.5f * Mathf.SmoothStep(0.8f, 1.5f, mach)), 0.8f + 0.6f * Mathf.SmoothStep(0.0f, 600.0f, speed));
        _gust.Feed(clock, roar, 0.85f + 0.3f * Mathf.SmoothStep(0.5f, 5.0f, mach));
        _buffet.Feed(clock, shake, 0.9f + 0.2f * transonic);
        _rattle.Feed(clock, shake * 0.8f, 0.9f + 0.2f * transonic);
        _plasma.Feed(clock, Mathf.Max(Mathf.Min(_sheathHeat, 1.0f), 0.6f * hypersonic * Mathf.Min(Mathf.Sqrt(pressure / 5_000.0f), 1.0f)), 1.0f);

        float duty = 0.0f;

        foreach (Piece piece in _pieces) {

            foreach (Jet jet in piece.Jets) {

                duty += jet.Duty;

            }

        }

        _thrusters.Feed(clock, Mathf.Min(duty / 4.0f, 1.0f), 1.0f);

        float slide = (float)_vessel.ContactSlide;
        _scrape.Feed(clock, _vessel.Intact ? Mathf.SmoothStep(0.5f, 12.0f, slide) : 0.0f, 0.8f + 0.4f * Mathf.SmoothStep(0.0f, 20.0f, slide));
        _lap.Feed(clock, _vessel.InWater && _vessel.Intact ? 0.7f : 0.0f, 1.0f);

    }

    /// <summary>Stops every voice of a view that has left range and is no longer being synced.</summary>
    public void Hush() {

        foreach (Node node in FindChildren("*", "AudioStreamPlayer3D", true, false)) {

            ((AudioStreamPlayer3D)node).Stop();

        }

    }

    // Ahead of a supersonic body is air that has not heard it yet. The ear crossing into the cone is the boom.
    private float Shock(Soundscape scape, Flight flight, bool ridden) {

        AeroForces air = _vessel.Aero;

        if (!air.InAir || air.Mach <= 1.0) {

            _behindShock = true;
            return 1.0f;

        }

        Vector3d moving = (_vessel.Velocity - flight.Body.AirVelocityAt(_vessel.Position)).Normalized;
        Vector3d toEar = scape.Ear - _vessel.Position;
        double distance = toEar.Length;
        double behind = distance > 0.01 ? Vector3d.Dot(toEar / distance, -moving) : 1.0;
        double edge = Math.Sqrt(1.0 - 1.0 / (air.Mach * air.Mach));
        bool inside = behind > edge;

        // The chase camera rides along inside the cone; it hears the flow change, not a crack.
        if (inside && !_behindShock && !ridden && flight.Warp <= 1.0) {

            scape.Boom(distance, Mathf.Clamp(1.0f - (float)distance / 20_000.0f, 0.2f, 1.0f));

        }

        _behindShock = inside;

        return Mathf.SmoothStep((float)edge - 0.05f, (float)edge + 0.02f, (float)behind);

    }

    private void Events(Soundscape scape, bool ridden) {

        Flight flight = Flight.Active;

        if (_vessel.Intact) {

            foreach (Piece piece in _pieces) {

                foreach (Jet jet in piece.Jets) {

                    if (!jet.Firing && jet.Duty > 0.2f) {

                        scape.Emit(Soundscape.Pick("Rcs/Hydrazine/puff", 4), jet.Plume, Vector3.Zero, 6.0f, 0.8f, ridden);

                    }

                    jet.Firing = jet.Firing ? jet.Duty > 0.05f : jet.Duty > 0.2f;

                }

            }

        }

        float jolt = (float)_vessel.ContactJolt;
        _joltQuiet -= _effectDelta;

        // A crash is heard as the explosion alone; the blow that caused it would only sound like a knock.
        if (_vessel.Intact && jolt > JoltLight && _joltQuiet <= 0.0) {

            string take = jolt < JoltMedium ? Soundscape.Pick("Events/impact_light", 3)
                : jolt < JoltHeavy ? Soundscape.Pick("Events/impact_medium", 3)
                : Soundscape.Pick("Events/impact_heavy", 3);

            scape.Emit(take, _voice, Vector3.Zero, 10.0f + jolt * 3.0f, Mathf.Clamp(jolt / JoltHeavy, 0.35f, 1.0f), ridden);
            _joltQuiet = 0.15;

        }

        if (_vessel.InWater && !_heardWet) {

            float speed = (float)(_vessel.Velocity - flight.Body.AirVelocityAt(_vessel.Position)).Length;
            string splash = speed > 7.0f ? "Events/splash_large.wav" : "Events/splash_small.wav";
            scape.Emit(splash, _voice, Vector3.Zero, 15.0f + speed * 2.0f, Mathf.Clamp(speed / 15.0f, 0.3f, 1.0f), ridden);

        }

        _heardWet = _vessel.InWater;

        if (_vessel.Fate != _heardFate) {

            if (_vessel.Fate == VesselFate.Impacted) {

                scape.Emit(Soundscape.Pick("Events/explosion", 2), _voice, Vector3.Zero, 300.0f, 1.0f, ridden);

            }
            else if (_vessel.Fate == VesselFate.BurnedUp) {

                scape.Emit("Events/breakup.wav", _voice, Vector3.Zero, 120.0f, 1.0f, ridden);

            }

            _heardFate = _vessel.Fate;

        }

    }

}
