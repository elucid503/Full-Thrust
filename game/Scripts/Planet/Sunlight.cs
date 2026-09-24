using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

// Sunlight and planet-shine at a point: the CPU half of Sunlight.gdshaderinc, for the vehicle's lights.
public sealed class Sunlight {

    public static readonly Vector3 RayleighCoefficients = new Vector3(5.8e-6f, 1.35e-5f, 3.31e-5f);
    public const float MieCoefficient = 7.0e-6f;
    public static readonly Vector3 OzoneCoefficients = new Vector3(0.650e-6f, 1.881e-6f, 0.085e-6f);

    // Bond albedo: oceans, land and cloud together, as the planet returns the light it is given.
    private const double Albedo = 0.30;

    private const double SolarRadius = 0.00465;

    // The light every hand-set colour in the scene was tuned under: a noon sun at sea level.
    private static readonly Color NoonColour = new Color(1.0f, 0.973f, 0.941f);

    private const int Rings = 8;
    private const int Spokes = 16;

    private readonly double _radius;
    private readonly double _top;
    private readonly double _rayleighHeight;
    private readonly double _mieHeight;
    private readonly double _ozoneHeight;
    private readonly double _ozoneWidth;

    // Linear radiance above the air, scaled by pi as Godot scales a directional light.
    public Vector3 Radiance { get; }

    public Sunlight(double radius, double top, double rayleighHeight, double mieHeight, double ozoneHeight, double ozoneWidth) {

        _radius = radius;
        _top = top;
        _rayleighHeight = rayleighHeight;
        _mieHeight = mieHeight;
        _ozoneHeight = ozoneHeight;
        _ozoneWidth = ozoneWidth;

        Color noon = NoonColour.SrgbToLinear();
        Vector3 overhead = Extinction(radius, 1.0);

        Radiance = new Vector3(noon.R / overhead.X, noon.G / overhead.Y, noon.B / overhead.Z) * Mathf.Pi;

    }

    // Fraction of the sun above the air reaching a planet-centred point, per channel.
    public Vector3 Transmittance(Vector3d position, Vector3d sun) {

        double distance = position.Length;
        double cosine = Vector3d.Dot(position, sun) / distance;

        double ratio = Math.Min(_radius / distance, 1.0);
        double dip = -Math.Sqrt(1.0 - ratio * ratio);
        double disc = Ocean.Smooth(dip - SolarRadius, dip + SolarRadius, cosine);

        if (disc <= 0.0) {

            return Vector3.Zero;

        }

        if (distance <= _top) {

            return Extinction(distance, cosine) * (float)disc;

        }

        // Above the air the ray may still graze it on the way to the sun; integrate from where it enters.
        double along = distance * cosine;
        double entry = along * along - (distance - _top) * (distance + _top);

        if (along >= 0.0 || entry <= 0.0) {

            return Vector3.One * (float)disc;

        }

        Vector3d enters = position + sun * (-along - Math.Sqrt(entry));

        return Extinction(_top, Vector3d.Dot(enters, sun) / enters.Length) * (float)disc;

    }

    // The sunlit planet as one directional source: where its light comes from, and how much of the sun's.
    public Vector3d Reflected(Vector3d position, Vector3d sun, out double irradiance) {

        double distance = position.Length;
        Vector3d nadir = position / -distance;

        double ratio = Math.Min(_radius / distance, 1.0);
        double rim = Math.Sqrt(1.0 - ratio * ratio);

        Vector3d side = Vector3d.Cross(Math.Abs(nadir.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, nadir).Normalized;
        Vector3d across = Vector3d.Cross(nadir, side);

        // Equal solid angle per sample, so the sum needs no weights.
        double solidAngle = Math.Tau * (1.0 - rim) / (Rings * Spokes);
        Vector3d total = Vector3d.Zero;

        for (int ring = 0; ring < Rings; ring++) {

            double cosine = 1.0 - (1.0 - rim) * (ring + 0.5) / Rings;
            double sine = Math.Sqrt(Math.Max(1.0 - cosine * cosine, 0.0));

            for (int spoke = 0; spoke < Spokes; spoke++) {

                double turn = Math.Tau * (spoke + 0.5 * (ring & 1) + 0.5) / Spokes;
                Vector3d ray = nadir * cosine + (side * Math.Cos(turn) + across * Math.Sin(turn)) * sine;

                double half = Vector3d.Dot(position, ray);
                double hit = half * half - (distance - _radius) * (distance + _radius);

                if (hit < 0.0) {

                    continue;

                }

                Vector3d ground = position + ray * (-half - Math.Sqrt(hit));
                double lit = Vector3d.Dot(ground, sun) / _radius;

                if (lit <= 0.0) {

                    continue;

                }

                // A Lambertian surface's radiance under unit irradiance.
                total += ray * (Albedo / Math.PI * lit * solidAngle);

            }

        }

        irradiance = total.Length;

        return irradiance > 0.0 ? total / irradiance : nadir;

    }

    private Vector3 Extinction(double distance, double cosine) {

        var depth = AtmosphereLookup.Integrate(_radius, _top, Math.Max(distance - _radius, 0.0), cosine,
            _rayleighHeight, _mieHeight, _ozoneHeight, _ozoneWidth, 48);

        return new Vector3(
            (float)Math.Exp(-(RayleighCoefficients.X * depth.X + MieCoefficient * 1.1 * depth.Y + OzoneCoefficients.X * depth.Z)),
            (float)Math.Exp(-(RayleighCoefficients.Y * depth.X + MieCoefficient * 1.1 * depth.Y + OzoneCoefficients.Y * depth.Z)),
            (float)Math.Exp(-(RayleighCoefficients.Z * depth.X + MieCoefficient * 1.1 * depth.Y + OzoneCoefficients.Z * depth.Z)));

    }

}
