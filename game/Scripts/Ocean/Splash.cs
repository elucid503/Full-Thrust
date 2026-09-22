using System;

using Godot;

namespace FullThrust.Game;

/// <summary>A splashdown: a crown of droplets thrown up round the hull, and the spray that hangs
/// over it. One burst; the water shader carries the ring wave and foam that follow.</summary>
public sealed partial class Splash : Node3D {

    public const double Life = 7.0;

    public static Splash Create(float radius, float speed, float daylight) {

        Splash splash = new Splash { Name = "Splash", TopLevel = true };

        StandardMaterial3D droplets = ExhaustImpact.Skin(0.2f);
        droplets.AlbedoColor = new Color(0.84f, 0.91f, 0.97f, 0.9f) * daylight;

        StandardMaterial3D mist = ExhaustImpact.Skin(0.8f);
        mist.AlbedoColor = new Color(0.90f, 0.94f, 0.98f, 0.55f) * daylight;

        float thrown = Mathf.Clamp(radius * speed * 30.0f, 60.0f, 900.0f);

        GpuParticles3D crown = Burst("Crown", (int)thrown, 2.6f, droplets, new ParticleProcessMaterial {

            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = radius * 1.15f,
            EmissionRingInnerRadius = radius * 0.85f,
            EmissionRingHeight = 0.2f,

            // A crown sheet leaves near vertical and leans outward as it breaks into drops.
            Direction = Vector3.Up,
            Spread = 22.0f,
            InitialVelocityMin = speed * 0.35f,
            InitialVelocityMax = speed * 0.95f,
            RadialVelocityMin = speed * 0.12f,
            RadialVelocityMax = speed * 0.4f,
            Gravity = new Vector3(0.0f, -9.81f, 0.0f),
            DampingMin = 0.2f,
            DampingMax = 0.6f,
            LifetimeRandomness = 0.35f,

            ScaleMin = Mathf.Max(radius * 0.18f, 0.08f),
            ScaleMax = Mathf.Max(radius * 0.45f, 0.2f),
            ScaleCurve = ExhaustImpact.Ramp(new[] { (0.0f, 0.4f), (0.25f, 1.0f), (1.0f, 1.3f) }),

            ColorRamp = ExhaustImpact.Fade(new[] { (0.0f, 0.0f), (0.05f, 0.9f), (0.7f, 0.6f), (1.0f, 0.0f) }),

        }, radius, speed);

        GpuParticles3D spray = Burst("Spray", (int)(thrown * 0.25f), 6.0f, mist, new ParticleProcessMaterial {

            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = radius * 1.4f,
            EmissionRingInnerRadius = radius * 0.6f,
            EmissionRingHeight = radius * 0.5f,

            Direction = Vector3.Up,
            Spread = 60.0f,
            InitialVelocityMin = speed * 0.1f,
            InitialVelocityMax = speed * 0.35f,
            RadialVelocityMin = speed * 0.2f,
            RadialVelocityMax = speed * 0.5f,
            Gravity = new Vector3(0.0f, -0.4f, 0.0f),
            DampingMin = 1.5f,
            DampingMax = 3.0f,
            LifetimeRandomness = 0.3f,

            ScaleMin = radius * 1.2f,
            ScaleMax = radius * 2.4f,
            ScaleCurve = ExhaustImpact.Ramp(new[] { (0.0f, 0.3f), (0.15f, 1.0f), (1.0f, 2.2f) }),
            AngleMin = -180.0f,
            AngleMax = 180.0f,

            ColorRamp = ExhaustImpact.Fade(new[] { (0.0f, 0.0f), (0.08f, 0.7f), (0.5f, 0.35f), (1.0f, 0.0f) }),

        }, radius, speed);

        splash.AddChild(crown);
        splash.AddChild(spray);

        return splash;

    }

    private static GpuParticles3D Burst(string name, int amount, float life, Material skin, ParticleProcessMaterial process, float radius, float speed) {

        float extent = radius * 4.0f + speed * speed / 9.81f + 10.0f;

        return new GpuParticles3D {

            Name = name,
            Amount = Math.Max(amount, 8),
            Lifetime = life,
            OneShot = true,
            Explosiveness = 0.92f,
            LocalCoords = true,
            Emitting = true,
            FixedFps = 30,
            Interpolate = true,
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = skin },
            VisibilityAabb = new Aabb(new Vector3(-extent, -2.0f, -extent), new Vector3(extent * 2.0f, extent + 2.0f, extent * 2.0f)),
            Layers = 2,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

        };

    }

}
