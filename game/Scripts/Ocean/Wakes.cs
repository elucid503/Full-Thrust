using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>What vessels do to the water: a foam collar where a hull pierces it, a trail behind a
/// moving one, a ring wave and spray where one comes down, and a crater under an engine firing into
/// it. Marks are held body-fixed and handed to the water shader in scene space each frame.</summary>
public sealed partial class Wakes : Node3D {

    private const int Capacity = 32;
    private const int Slices = 24;

    private const double TrailSpacing = 1.5;
    private const double TrailLife = 60.0;
    private const double SplashLife = 30.0;

    // Below this the hull only settles; it does not throw water.
    private const double SplashSpeed = 1.0;

    private enum Kind { Collar, Trail, Splash, Jet, Kelvin }

    // Below this a drifting hull's ship waves are ripples under a centimetre long.
    private const double KelvinSpeed = 0.4;

    private readonly record struct Mark(Vector3d Start, Vector3d End, double Radius, Kind Kind, double Born, double Strength, double Height, double Speed);

    private sealed class Hull {

        public bool Wet;
        public Vector3d Velocity;
        public ulong Frame;

        public readonly List<Mark> Trail = new();

    }

    private CelestialBody _body;

    private readonly Dictionary<Vessel, Hull> _hulls = new();
    private readonly List<Mark> _current = new();
    private readonly List<Mark> _splashes = new();
    private readonly List<(Splash Effect, Vector3d Anchor, double Born)> _effects = new();
    private readonly List<Vessel> _gone = new();
    private double _now;

    private readonly Vector4[] _start = new Vector4[Capacity];
    private readonly Vector4[] _end = new Vector4[Capacity];
    private readonly Vector4[] _state = new Vector4[Capacity];
    private int _published;

    public void Build(CelestialBody body) {

        _body = body;

    }

    /// <summary>Reads one vessel against the water: entry, the collar where it floats, its trail.</summary>
    public void Track(Vessel vessel, double time) {

        if (!_hulls.TryGetValue(vessel, out Hull hull)) {

            hull = new Hull();
            _hulls[vessel] = hull;

        }

        hull.Frame = Engine.GetProcessFrames();
        Vector3d up = vessel.Position.Normalized;
        Vector3d relative = vessel.Velocity - _body.SurfaceVelocityAt(vessel.Position);
        bool wet = vessel.InWater;

        // The frame it touches, water drag has already slowed it; last frame's speed is the impact.
        if (wet && !hull.Wet) {

            double entry = -Vector3d.Dot(hull.Velocity, up);

            if (entry > SplashSpeed && Waterline(vessel, time, out Vector3d start, out Vector3d end, out _)) {

                SplashDown(vessel, (start + end) * 0.5, time, entry);

            }

        }

        hull.Wet = wet;
        hull.Velocity = relative;

        if (!wet || !Waterline(vessel, time, out Vector3d from, out Vector3d to, out double radius)) {

            return;

        }

        double drift = (relative - up * Vector3d.Dot(relative, up)).Length;
        double churn = Ocean.Smooth(0.3, 3.0, drift);
        _current.Add(new Mark(from, to, radius, Kind.Collar, time, 0.55 + 0.45 * churn, 0.0, 0.0));

        Vector3d centre = (from + to) * 0.5;

        if (drift > KelvinSpeed) {

            // Ship waves grow with the square of the hull's Froude number, capped where the hull
            // would be ploughing rather than riding.
            Vector3d heading = _body.ToBodyFixed((relative - up * Vector3d.Dot(relative, up)) / drift, time);
            double froude = drift / Math.Sqrt(_body.SurfaceGravity * Math.Max(radius, 0.1));
            double amplitude = Math.Min(0.12 * radius * froude * froude, 0.4);
            _current.Add(new Mark(centre, centre + heading, radius, Kind.Kelvin, time, 0.0, amplitude, drift));

        }

        if (hull.Trail.Count == 0 || (hull.Trail[^1].End - centre).Length > TrailSpacing) {

            Vector3d previous = hull.Trail.Count > 0 ? hull.Trail[^1].End : centre;
            hull.Trail.Add(new Mark(previous, centre, radius, Kind.Trail, time, churn, 0.0, 0.0));

        }

    }

    /// <summary>An engine firing into the water this frame, at an inertial point on the surface.</summary>
    public void Jet(Vector3d point, double time, double radius, double depth, double strength) {

        // Ripples at the crater's scale run out at their own deep-water phase speed.
        double wavelength = Math.Max(radius * 0.8, 0.3);
        double cycles = time * Math.Sqrt(_body.SurfaceGravity * wavelength / Math.Tau) / wavelength;
        double phase = (cycles - Math.Floor(cycles)) * Math.Tau;
        Vector3d anchor = _body.ToBodyFixed(point, time);

        _current.Add(new Mark(anchor, anchor, radius, Kind.Jet, time, Math.Clamp(strength, 0.0, 1.0), depth, phase));

    }

    // The span of hull cutting the surface, body-fixed and laid on the water, and its widest radius.
    private bool Waterline(Vessel vessel, double time, out Vector3d start, out Vector3d end, out double radius) {

        start = Vector3d.Zero;
        end = Vector3d.Zero;
        radius = 0.0;

        Vector3d up = vessel.Position.Normalized;
        Vector3d fixedCentre = _body.ToBodyFixed(vessel.Position, time);
        double level = _body.Radius + Ocean.Sample(_body, fixedCentre, Ocean.BedElevation(_body, vessel.Position, time), time).Height;
        Vector3d nose = vessel.Nose;
        double lean = Math.Abs(Vector3d.Dot(nose, up));
        double tilt = Math.Sqrt(Math.Max(0.0, 1.0 - lean * lean));
        double dz = vessel.Length / Slices;
        int first = -1;
        int last = -1;

        for (int index = 0; index < Slices; index++) {

            double z = vessel.Base + (index + 0.5) * dz;
            double slice = vessel.RadiusAt(z);

            if (slice <= 0.0) {

                continue;

            }

            double above = (vessel.Position + nose * (z - vessel.CentreOfMassZ)).Length - level;

            if (Math.Abs(above) <= slice * tilt + dz * lean * 0.5) {

                first = first < 0 ? index : first;
                last = index;
                radius = Math.Max(radius, slice);

            }

        }

        if (first < 0) {

            return false;

        }

        Vector3d Settle(int index) {

            Vector3d point = vessel.Position + nose * (vessel.Base + (index + 0.5) * dz - vessel.CentreOfMassZ);
            return _body.ToBodyFixed(point.Normalized * level, time);

        }

        start = Settle(first);
        end = Settle(last);

        return true;

    }

    private void SplashDown(Vessel vessel, Vector3d anchor, double time, double speed) {

        double radius = 0.0;

        for (int index = 0; index < Slices; index++) {

            radius = Math.Max(radius, vessel.RadiusAt(vessel.Base + (index + 0.5) * vessel.Length / Slices));

        }

        // The crown rises with the square of the entry speed; the ring runs at the phase speed of a
        // wave a few hull radii long.
        double height = Math.Clamp(0.04 * speed * radius, 0.1, 2.5);
        double ringSpeed = Math.Sqrt(_body.SurfaceGravity * Math.Max(radius * 3.0, 2.0) / Math.Tau);
        _splashes.Add(new Mark(anchor, anchor, radius, Kind.Splash, time, Math.Clamp(speed / 8.0, 0.35, 1.0), height, ringSpeed));

        Vector3 up = Frames.Direction(_body.ToInertial(anchor.Normalized, time));
        float daylight = Mathf.Clamp(up.Dot(Main.SunDirection) * 2.5f + 0.3f, 0.12f, 1.0f);
        Splash effect = Splash.Create((float)radius, (float)speed, daylight);
        AddChild(effect);
        _effects.Add((effect, anchor, time));

    }

    /// <summary>Hands this frame's marks to the water shader, nearest in time first.</summary>
    public void Publish(ShaderMaterial water, double time) {

        _now = time;
        _splashes.RemoveAll(SplashSpent);

        int count = 0;
        Vector3 low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 high = -low;
        float reach = 0.0f;

        bool Add(Mark mark) {

            if (count >= Capacity) {

                return false;

            }

            double age = Math.Max(time - mark.Born, 0.0);
            double radius = mark.Kind == Kind.Trail ? mark.Radius + 0.25 * age : mark.Radius;
            Vector3 start = Frames.Point(_body.ToInertial(mark.Start, time));
            Vector3 end = Frames.Point(_body.ToInertial(mark.End, time));

            _start[count] = new Vector4(start.X, start.Y, start.Z, (float)radius);
            _end[count] = new Vector4(end.X, end.Y, end.Z, (float)mark.Kind);
            _state[count] = new Vector4((float)age, (float)mark.Strength, (float)mark.Height, (float)mark.Speed);

            low = low.Min(start).Min(end);
            high = high.Max(start).Max(end);
            // Ship waves trail thirty transverse wavelengths behind the hull.
            double trail = mark.Kind == Kind.Kelvin ? 30.0 * Math.Tau * mark.Speed * mark.Speed / _body.SurfaceGravity : mark.Speed * age;
            reach = Mathf.Max(reach, (float)(radius * 3.0 + trail + 2.0));
            count++;

            return true;

        }

        foreach (Mark mark in _current) {

            Add(mark);

        }

        for (int index = _splashes.Count - 1; index >= 0; index--) {

            Add(_splashes[index]);

        }

        ulong frame = Engine.GetProcessFrames();

        foreach ((Vessel vessel, Hull hull) in _hulls) {

            hull.Trail.RemoveAll(TrailSpent);

            for (int index = hull.Trail.Count - 1; index >= 0; index--) {

                if (hull.Trail[index].Strength > 0.02 && !Add(hull.Trail[index])) {

                    break;

                }

            }

            if (frame - hull.Frame > 120 && hull.Trail.Count == 0) {

                _gone.Add(vessel);

            }

        }

        foreach (Vessel vessel in _gone) {

            _hulls.Remove(vessel);

        }

        _gone.Clear();

        _current.Clear();
        SyncEffects(time);

        if (count == 0 && _published == 0) {

            return;

        }

        Vector3 centre = (low + high) * 0.5f;
        water.SetShaderParameter("wake_count", count);
        water.SetShaderParameter("wake_start", _start);
        water.SetShaderParameter("wake_end", _end);
        water.SetShaderParameter("wake_state", _state);
        water.SetShaderParameter("wake_bounds", count == 0 ? new Vector4(0.0f, 0.0f, 0.0f, -1.0f)
            : new Vector4(centre.X, centre.Y, centre.Z, (high - centre).Length() + reach));
        _published = count;

    }

    // Cached predicates over the publish time, so the per-frame sweeps allocate nothing.
    private bool SplashSpent(Mark mark) => _now - mark.Born > SplashLife || _now < mark.Born;

    private bool TrailSpent(Mark mark) => _now - mark.Born > TrailLife || _now < mark.Born;

    // Spray is anchored to the turning body while the scene origin follows the vessel.
    private void SyncEffects(double time) {

        for (int index = _effects.Count - 1; index >= 0; index--) {

            (Splash effect, Vector3d anchor, double born) = _effects[index];

            if (time - born > Splash.Life || time < born) {

                effect.QueueFree();
                _effects.RemoveAt(index);

                continue;

            }

            Vector3 up = Frames.Direction(_body.ToInertial(anchor.Normalized, time));
            Vector3 side = Mathf.Abs(up.Y) > 0.99f ? Vector3.Right : Vector3.Up.Cross(up).Normalized();
            effect.GlobalTransform = new Transform3D(new Basis(side, up, side.Cross(up)), Frames.Point(_body.ToInertial(anchor, time)));

        }

    }

}
