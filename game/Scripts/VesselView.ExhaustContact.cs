using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {

    private readonly List<(Node3D Node, TriangleMesh Surface)> _exhaustSurfaces = new();
    private readonly List<(TriangleMesh Surface, Transform3D Transform, Transform3D Inverse)> _exhaustColliders = new();
    private readonly Dictionary<MeshInstance3D, ImageTexture> _exhaustMasks = new();
    private readonly Dictionary<MeshInstance3D, (float Length, float Spread, float Radius)> _exhaustShapes = new();
    private readonly Dictionary<MeshInstance3D, float> _exhaustMaskTimes = new();
    private readonly HashSet<MeshInstance3D> _blockedPlumes = new();
    private double _exhaustRadius;

    private void BakeExhaustSurfaces() {

        _exhaustSurfaces.Clear();
        _exhaustColliders.Clear();
        _exhaustRadius = 0.0;
        foreach (Piece piece in _pieces) {

            if (piece.ExhaustSurfaces == null) { BakePieceExhaust(piece); }
            _exhaustSurfaces.AddRange(piece.ExhaustSurfaces);
            _exhaustRadius = Math.Max(_exhaustRadius, piece.ExhaustRadius);

        }

    }

    private void BakePieceExhaust(Piece piece) {

        Dictionary<Node3D, List<Vector3>> groups = new();
        HashSet<Node3D> pivots = new();
        foreach (Engine engine in piece.Engines) { pivots.Add(engine.Pivot); }

        foreach (MeshInstance3D mesh in Meshes(piece.Node)) {

            if (mesh.Mesh == null || mesh.Layers == 2) { continue; }
            Node3D root = piece.Node;
            for (Node parent = mesh.GetParent(); parent != piece.Node && parent != _body; parent = parent.GetParent()) {

                if (parent is Node3D node && pivots.Contains(node)) { root = node; break; }

            }

            if (!groups.TryGetValue(root, out List<Vector3> faces)) {

                faces = new List<Vector3>();
                groups.Add(root, faces);

            }

            Transform3D transform = DatumTransform(root).AffineInverse() * DatumTransform(mesh);
            foreach (Vector3 point in mesh.Mesh.GetFaces()) { faces.Add(transform * point); }

        }

        piece.ExhaustSurfaces = new();
        foreach (var group in groups) {

            Transform3D transform = DatumTransform(group.Key);
            foreach (Vector3 point in group.Value) {

                piece.ExhaustRadius = Math.Max(piece.ExhaustRadius, transform.Origin.Length() + (transform.Basis * point).Length());

            }

            TriangleMesh surface = new TriangleMesh();
            surface.CreateFromFaces(group.Value.ToArray());
            piece.ExhaustSurfaces.Add((group.Key, surface));

        }

    }

    private void PrepareExhaustColliders() {

        _exhaustColliders.Clear();
        foreach (var mesh in _exhaustSurfaces) {

            Transform3D transform = DatumTransform(mesh.Node);
            _exhaustColliders.Add((mesh.Surface, transform, transform.AffineInverse()));

        }

    }

    private Transform3D DatumTransform(Node3D node) {

        if (node == _body) { return Transform3D.Identity; }
        Transform3D transform = node.Transform;

        for (Node parent = node.GetParent(); parent != _body; parent = parent.GetParent()) {

            if (parent is Node3D spatial) { transform = spatial.Transform * transform; }

        }

        return transform;

    }

    private Vector3d NozzlePosition(MeshInstance3D volume) => _vessel.Position + _vessel.Orientation.Rotate(
        Frames.Sim(DatumTransform(volume).Origin) - Vector3d.UnitZ * _vessel.CentreOfMassZ);

    private ExhaustInteraction.Hit? TraceExhaust(Vector3d origin, Vector3d direction, double reach, out Vector3 normal) {

        ExhaustInteraction.Hit? nearest = null;
        normal = Vector3.Zero;

        foreach (VesselView view in Views.Values) {

            if (view == this || !view._vessel.Intact) { continue; }

            Vessel target = view._vessel;
            Vector3d offset = target.Position - origin;
            double along = Math.Clamp(Vector3d.Dot(offset, direction), 0.0, reach);
            double radius = view._exhaustRadius + Math.Abs(target.CentreOfMassZ);
            if ((offset - direction * along).LengthSquared > radius * radius) { continue; }

            Vector3 localOrigin = Frames.Direction(target.Orientation.Conjugate.Rotate(origin - target.Position))
                + Vector3.Up * (float)target.CentreOfMassZ;
            Vector3 localEnd = localOrigin + Frames.Direction(target.Orientation.Conjugate.Rotate(direction)) * (float)reach;

            foreach (var mesh in view._exhaustColliders) {

                Transform3D transform = mesh.Transform;
                Transform3D inverse = mesh.Inverse;
                var hit = mesh.Surface.IntersectSegment(inverse * localOrigin, inverse * localEnd);
                if (hit.Count == 0) { continue; }

                Vector3 point = transform * hit["position"].AsVector3();
                double distance = (point - localOrigin).Length();
                if (nearest.HasValue && distance >= nearest.Value.Distance) { continue; }

                nearest = new ExhaustInteraction.Hit(target, origin + direction * distance, distance);
                normal = Frames.Direction(target.Orientation.Rotate(Frames.Sim(
                    transform.Basis.Inverse().Transposed() * hit["normal"].AsVector3()))).Normalized();

            }

        }

        return nearest;

    }

    public static void UpdateExhaustLoads() {

        foreach (VesselView view in Views.Values) {

            view.PrepareExhaustColliders();
            view._vessel.ExhaustForce = Vector3d.Zero;
            view._vessel.ExhaustTorque = Vector3d.Zero;

        }

        foreach (VesselView view in Views.Values) {

            Vessel source = view._vessel;
            if (!source.Intact) { continue; }

            foreach (Piece piece in view._pieces) {

                foreach (Engine engine in piece.Engines) {

                    if (piece.Stage != source.Active || source.CurrentThrust <= 0.0) { continue; }
                    if (!piece.Stage.IsEngineLit(engine.Index) || !view._exhaustShapes.TryGetValue(engine.Plume, out var shape)) { continue; }

                    Vector3d axis = source.Orientation.Rotate(Frames.Sim(-view.DatumTransform(engine.Plume).Basis.Y)).Normalized;
                    var emitter = new ExhaustInteraction.Emitter(view.NozzlePosition(engine.Plume), axis,
                        shape.Radius, shape.Length, shape.Spread * 2.0, source.CurrentThrust / Math.Max(source.EnginesLit, 1));
                    ExhaustInteraction.Accumulate(emitter, (origin, direction, reach) => view.TraceExhaust(origin, direction, reach, out _));

                }

                if (!source.HasRcs || !piece.Stage.HasReactionControl || source.RcsDuty <= 0.0) { continue; }
                foreach (Jet jet in piece.Jets) {

                    if (jet.Command <= 0.0 || !view._exhaustShapes.TryGetValue(jet.Volume, out var shape)) { continue; }
                    Vector3d axis = source.Orientation.Rotate(Frames.Sim(-view.DatumTransform(jet.Volume).Basis.Y)).Normalized;
                    var emitter = new ExhaustInteraction.Emitter(view.NozzlePosition(jet.Volume), axis,
                        shape.Radius, shape.Length, shape.Spread * 2.0, piece.Stage.RcsThrustNewtons * jet.Command / piece.Jets.Count);
                    ExhaustInteraction.Accumulate(emitter, (origin, direction, reach) => view.TraceExhaust(origin, direction, reach, out _));

                }

            }

        }

    }

    private void MeshObstacle(MeshInstance3D volume, ShaderMaterial material, float length, float exit, float spread) {

        bool nearby = false;
        Vector3d nozzle = NozzlePosition(volume);
        foreach (VesselView view in Views.Values) {

            if (view == this || !view._vessel.Intact) { continue; }
            double reach = length * 1.5 + view._exhaustRadius + Math.Abs(view._vessel.CentreOfMassZ);
            nearby |= (view._vessel.Position - nozzle).LengthSquared < reach * reach;

        }

        if (!nearby) {

            material.SetShaderParameter("ship_enabled", false);
            _blockedPlumes.Remove(volume);
            _exhaustMaskTimes.Remove(volume);
            return;

        }

        if (_exhaustMaskTimes.TryGetValue(volume, out float updated) && _effectTime - updated < 0.04f) {

            if (_blockedPlumes.Contains(volume)) { PlumeObstacles++; }
            return;

        }

        _exhaustMaskTimes[volume] = _effectTime;
        const int size = 24;
        float slope = Mathf.Max(spread * 2.0f, 0.02f);
        float apex = exit / Mathf.Max(spread, 0.01f);
        Basis rotation = new Basis(Frames.Rotation(_vessel.Orientation)) * DatumTransform(volume).Basis;
        Vector3d origin = NozzlePosition(volume) + Frames.Sim(rotation.Y * apex);
        using Image mask = Image.CreateEmpty(size, size, false, Image.Format.Rgbaf);
        bool blocked = false;

        for (int y = 0; y < size; y++) {

            for (int x = 0; x < size; x++) {

                Vector3 ray = new Vector3(((x + 0.5f) / size * 2.0f - 1.0f) * slope, -1.0f,
                    ((y + 0.5f) / size * 2.0f - 1.0f) * slope).Normalized();
                Vector3d direction = Frames.Sim(rotation * ray);
                // Begin at the lip so neighbouring bells behind the outlet cannot shadow the jet.
                double start = apex / -ray.Y;
                var hit = TraceExhaust(origin + direction * start, direction, (length + apex) / -ray.Y - start, out Vector3 normal);
                Vector3 localNormal = rotation.Inverse() * normal;
                float depth = hit.HasValue ? (float)(hit.Value.Distance + start) * -ray.Y - apex : length + 1.0f;
                mask.SetPixel(x, y, new Color(depth, localNormal.X, localNormal.Y, localNormal.Z));
                blocked |= hit.HasValue;

            }

        }

        if (!_exhaustMasks.TryGetValue(volume, out ImageTexture texture)) {

            texture = ImageTexture.CreateFromImage(mask);
            _exhaustMasks.Add(volume, texture);
            material.SetShaderParameter("obstacle_mask", texture);

        }
        else { texture.Update(mask); }

        material.SetShaderParameter("ship_enabled", blocked);
        material.SetShaderParameter("obstacle_apex", apex);
        material.SetShaderParameter("obstacle_slope", slope);
        if (blocked) { PlumeObstacles++; _blockedPlumes.Add(volume); }
        else { _blockedPlumes.Remove(volume); }

    }

}
