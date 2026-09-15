using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    private const int SootColumns = 24;
    private const int SootRows = 48;
    private double _sootElapsed;
    public float SootCoverage { get; private set; }

    private sealed class SootCoating {

        public Image Image;
        public ImageTexture Texture;
        public readonly float[] Dose = new float[SootColumns * SootRows];
        public readonly List<(MeshInstance3D Mesh, ShaderMaterial Material)> Materials = new();

    }

    private static void PrepareSoot(Piece piece) {

        SootCoating coat = new() { Image = Image.CreateEmpty(SootColumns, SootRows, false, Image.Format.Rf) };
        coat.Image.Fill(Colors.Black);
        coat.Texture = ImageTexture.CreateFromImage(coat.Image);
        piece.Soot = coat;
        foreach (Node child in piece.Node.FindChildren("*", "MeshInstance3D", true, false)) {

            if (child is not MeshInstance3D mesh || mesh.Mesh == null || mesh.Layers == 2) { continue; }
            ShaderMaterial soot = new() { Shader = GD.Load<Shader>("res://Shaders/Soot.gdshader") };
            soot.SetShaderParameter("soot_map", coat.Texture);
            soot.SetShaderParameter("stage_base", (float)piece.Stage.Hull.Base);
            soot.SetShaderParameter("stage_length", (float)piece.Stage.Hull.Length);
            mesh.MaterialOverlay = soot;
            coat.Materials.Add((mesh, soot));

        }

    }

    private void SyncSoot() {

        _sootElapsed += _effectDelta;
        foreach (Piece piece in _pieces) {

            if (piece.Soot == null) { PrepareSoot(piece); }
            foreach (var surface in piece.Soot.Materials) {

                surface.Material.SetShaderParameter("stage_from_model", DatumTransform(surface.Mesh));

            }

        }
        if (_sootElapsed < 0.1) { return; }
        float dt = (float)_sootElapsed;
        _sootElapsed = 0.0;
        var emitters = new List<(Transform3D Local, PlumeFlow Flow, float Power, VesselView Source, Vector3d Origin)>();
        foreach (VesselView source in Views.Values) {

            if (!source._vessel.Intact) { continue; }
            foreach (Piece piece in source._pieces) {

                float soot = Chemistry.For(piece.Stage.Fuel).Soot;
                if (soot <= 0.0f) { continue; }
                foreach (Engine engine in piece.Engines) {

                    if (engine.Power < 0.001f || !source._plumeFlows.TryGetValue(engine.Plume, out PlumeFlow flow)) { continue; }
                    if (source == this && flow.Opposing < 0.001) { continue; }
                    if (engine.Plume.GlobalPosition.DistanceTo(GlobalPosition) > flow.Length * 3.0 + _vessel.Length) { continue; }
                    emitters.Add((engine.Plume.GlobalTransform.AffineInverse() * _body.GlobalTransform, flow, soot * engine.Power, source, source.NozzlePosition(engine.Plume)));

                }

            }

        }
        if (emitters.Count == 0) { return; }
        SootCoverage = 0.0f;
        foreach (Piece piece in _pieces) {

            bool changed = false;
            SootCoating coat = piece.Soot;
            for (int y = 0; y < SootRows; y++) {

                double height = piece.Stage.Hull.Base + (y + 0.5) / SootRows * piece.Stage.Hull.Length;
                double radius = piece.Stage.Hull.RadiusAt(height);
                for (int x = 0; x < SootColumns; x++) {

                    double angle = ((x + 0.5) / SootColumns - 0.5) * Math.Tau;
                    Vector3 point = new((float)(Math.Cos(angle) * radius), (float)height, (float)(Math.Sin(angle) * radius));
                    float exposure = 0.0f;
                    foreach (var emitter in emitters) {

                        float dose = (float)emitter.Flow.SootExposure(Frames.Sim(emitter.Local * point)) * emitter.Power;
                        if (dose <= 0.0001f) { continue; }
                        if (emitter.Source != this) {

                            Vector3d target = _vessel.Position + _vessel.Orientation.Rotate(Frames.Sim(point) - Vector3d.UnitZ * _vessel.CentreOfMassZ);
                            Vector3d path = target - emitter.Origin;
                            var hit = emitter.Source.TraceExhaust(emitter.Origin, path.Normalized, path.Length + 0.05, out _);
                            if (!hit.HasValue || hit.Value.Vessel != _vessel) { continue; }

                        }
                        exposure += dose;

                    }
                    int index = y * SootColumns + x;
                    if (exposure > 0.0001f) {

                        coat.Dose[index] = Mathf.Min(coat.Dose[index] + exposure * dt * 0.8f, 4.0f);
                        coat.Image.SetPixel(x, y, new Color(1.0f - Mathf.Exp(-coat.Dose[index]), 0.0f, 0.0f));
                        changed = true;

                    }
                    SootCoverage = Mathf.Max(SootCoverage, 1.0f - Mathf.Exp(-coat.Dose[index]));

                }

            }
            if (changed) { coat.Texture.Update(coat.Image); }

        }

    }

}
