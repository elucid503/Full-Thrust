using System;
using System.Reflection;
using System.Threading.Tasks;

using Godot;

namespace FullThrust.Game;

public sealed partial class SmokeProbe : Node3D {

    public override async void _Ready() {

        try {

            GetTree().Root.Mode = Window.ModeEnum.Windowed;
            GetTree().Root.Size = new Vector2I(1280, 720);

            AddChild(new DirectionalLight3D { Transform = new Transform3D(Basis.FromEuler(new Vector3(-0.9f, 0.4f, 0.0f)), Vector3.Zero) });
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.4f, 0.6f, 0.9f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
            AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400.0f, 400.0f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.5f, 0.2f) } });

            Camera3D camera = new Camera3D { Position = new Vector3(0.0f, 6.0f, 24.0f), Fov = 55.0f };
            AddChild(camera);
            camera.LookAt(new Vector3(0.0f, 2.0f, 0.0f), Vector3.Up);
            camera.Current = true;

            PlumeTemplate template = PlumeTemplate.Load("Kerolox");
            ExhaustImpact impact = ExhaustImpact.Create(template, 0.4f, 6);
            AddChild(impact);

            FieldInfo dustField = typeof(ExhaustImpact).GetField("_dust", BindingFlags.Instance | BindingFlags.NonPublic);
            ParticleProcessMaterial dust = (ParticleProcessMaterial)dustField.GetValue(impact);
            GpuParticles3D smoke = impact.GetNode<GpuParticles3D>("Smoke");
            GpuParticles3D spray = impact.GetNode<GpuParticles3D>("Spray");

            string variant = OS.GetEnvironment("FT_SMOKE");
            if (variant == "noturb") { dust.TurbulenceEnabled = false; }
            if (variant == "nocurves") { dust.ScaleCurve = null; dust.DampingCurve = null; }
            if (variant == "plain") { dust.TurbulenceEnabled = false; dust.ScaleCurve = null; dust.DampingCurve = null; dust.AngularVelocityMin = 0.0f; dust.AngularVelocityMax = 0.0f; dust.AngleMin = 0.0f; dust.AngleMax = 0.0f; }
            if (variant == "sprayskin") { ((PrimitiveMesh)smoke.DrawPass1).Material = ((QuadMesh)spray.DrawPass1).Material; }
            if (variant == "sprayprocess") { smoke.ProcessMaterial = spray.ProcessMaterial; }

            smoke.Position = Vector3.Zero;
            smoke.AmountRatio = 1.0f;
            smoke.Emitting = true;
            if (((PrimitiveMesh)smoke.DrawPass1).Material is ShaderMaterial volume) { volume.SetShaderParameter("tint", new Color(0.58f, 0.53f, 0.47f)); }

            for (int i = 0; i < 180; i++) { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }

            using Image shot = GetViewport().GetTexture().GetImage();
            shot.SavePng($"res://.artifacts/smoke-probe-{(variant == "" ? "full" : variant)}.png");
            GD.Print($"SMOKE PROBE {variant}: extent={smoke.CaptureAabb().Size.Length():F1}");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
