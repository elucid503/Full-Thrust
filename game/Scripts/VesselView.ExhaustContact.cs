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
    private Vector3 _exhaustCentre;
    private Vector3 _exhaustLow;
    private Vector3 _exhaustHigh;
    private int _maskUpdates;
    private readonly Queue<MeshInstance3D> _maskQueue = new();
    private readonly HashSet<MeshInstance3D> _queuedMasks = new();
    private ulong _colliderFrame = ulong.MaxValue;
    private readonly Dictionary<MeshInstance3D, int> _maskSignatures = new();

    private static Vector3d BoundsCentre(VesselView view) => view._vessel.Position + view._vessel.Orientation.Rotate(
        Frames.Sim(view._exhaustCentre) - Vector3d.UnitZ * view._vessel.CentreOfMassZ);

    private static double BoundsRadius(VesselView view) => view._exhaustRadius;

    private bool InExhaustCone(Vector3d origin, Vector3d axis, double length, double radius, double spread, VesselView target) {

        Vector3d offset = BoundsCentre(target) - origin;
        double bound = BoundsRadius(target);
        double along = Vector3d.Dot(offset, axis);
        if (along < -bound || along > length + bound) { return false; }
        double width = bound + radius + Math.Clamp(along + bound, 0.0, length) * spread;
        return (offset - axis * along).LengthSquared < width * width;

    }

    private bool HasExhaustTarget(ExhaustInteraction.Emitter emitter) {

        foreach (VesselView view in Views.Values) {

            if (view != this && view._vessel.Intact && InExhaustCone(emitter.Position, emitter.Axis, emitter.Length, emitter.Radius, emitter.Spread, view)) { return true; }

        }
        return false;

    }

    private void BakeExhaustSurfaces() {

        _exhaustSurfaces.Clear();
        _exhaustColliders.Clear();
        Vector3 low = Vector3.One * float.PositiveInfinity;
        Vector3 high = Vector3.One * float.NegativeInfinity;
        _colliderFrame = ulong.MaxValue;
        foreach (Piece piece in _pieces) {

            if (piece.ExhaustSurfaces == null) { BakePieceExhaust(piece); }
            _exhaustSurfaces.AddRange(piece.ExhaustSurfaces);
            low = low.Min(piece.ExhaustLow);
            high = high.Max(piece.ExhaustHigh);

        }

        _exhaustLow = low - Vector3.One * 0.5f;
        _exhaustHigh = high + Vector3.One * 0.5f;
        _exhaustCentre = (low + high) * 0.5f;
        _exhaustRadius = (high - low).Length() * 0.5 + 0.5;

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
        piece.ExhaustLow = Vector3.One * float.PositiveInfinity;
        piece.ExhaustHigh = Vector3.One * float.NegativeInfinity;
        foreach (var group in groups) {

            Transform3D transform = DatumTransform(group.Key);
            foreach (Vector3 point in group.Value) {

                piece.ExhaustLow = piece.ExhaustLow.Min(transform * point);
                piece.ExhaustHigh = piece.ExhaustHigh.Max(transform * point);

            }

            TriangleMesh surface = new TriangleMesh();
            surface.CreateFromFaces(group.Value.ToArray());
            piece.ExhaustSurfaces.Add((group.Key, surface));

        }

    }

    private void PrepareExhaustColliders() {

        if (_colliderFrame == Godot.Engine.GetProcessFrames()) { return; }
        _colliderFrame = Godot.Engine.GetProcessFrames();
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

    private static bool SegmentBounds(Vector3 from, Vector3 to, Vector3 low, Vector3 high) {

        float enter = 0.0f, leave = 1.0f;
        Vector3 delta = to - from;
        for (int axis = 0; axis < 3; axis++) {

            if (Mathf.Abs(delta[axis]) < 0.000001f) {

                if (from[axis] < low[axis] || from[axis] > high[axis]) { return false; }
                continue;

            }
            float a = (low[axis] - from[axis]) / delta[axis];
            float b = (high[axis] - from[axis]) / delta[axis];
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            leave = Mathf.Min(leave, Mathf.Max(a, b));
            if (leave < enter) { return false; }

        }
        return true;

    }

    private ExhaustInteraction.Hit? TraceExhaust(Vector3d origin, Vector3d direction, double reach, out Vector3 normal) {

        ExhaustInteraction.Hit? nearest = null;
        normal = Vector3.Zero;

        foreach (VesselView view in Views.Values) {

            if (view == this || !view._vessel.Intact) { continue; }

            Vessel target = view._vessel;
            Vector3d offset = BoundsCentre(view) - origin;
            double along = Math.Clamp(Vector3d.Dot(offset, direction), 0.0, reach);
            double radius = BoundsRadius(view);
            if ((offset - direction * along).LengthSquared > radius * radius) { continue; }

            Vector3 localOrigin = Frames.Direction(target.Orientation.Conjugate.Rotate(origin - target.Position))
                + Vector3.Up * (float)target.CentreOfMassZ;
            Vector3 localEnd = localOrigin + Frames.Direction(target.Orientation.Conjugate.Rotate(direction)) * (float)reach;

            if (!SegmentBounds(localOrigin, localEnd, view._exhaustLow, view._exhaustHigh)) { continue; }

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
                    if (!view._exhaustShapes.TryGetValue(engine.Plume, out var shape)) { continue; }
                    EngineState state = piece.Stage.EngineStates[engine.Index];
                    if (state.Power <= 0.0) { continue; }

                    Vector3d axis = -source.Orientation.Rotate(state.Direction);
                    Vector3d nozzle = source.Position + source.Orientation.Rotate(state.Mount + state.Direction * engine.Plume.Position.Y - Vector3d.UnitZ * source.CentreOfMassZ);
                    var emitter = new ExhaustInteraction.Emitter(nozzle, axis,
                        shape.Radius, shape.Length, shape.Spread * 2.0, piece.Stage.ThrustNewtons / Math.Max(piece.Stage.EngineCount, 1) * state.Power * piece.Stage.PressureThrustFactor);
                    if (!view.HasExhaustTarget(emitter)) { continue; }
                    ExhaustInteraction.Accumulate(emitter, (origin, direction, reach) => view.TraceExhaust(origin, direction, reach, out _));

                }

                if (!source.HasRcs || !piece.Stage.HasReactionControl || source.RcsDuty <= 0.0) { continue; }
                foreach (Jet jet in piece.Jets) {

                    if (jet.Command <= 0.0 || !view._exhaustShapes.TryGetValue(jet.Volume, out var shape)) { continue; }
                    Vector3d axis = source.Orientation.Rotate(Frames.Sim(-view.DatumTransform(jet.Volume).Basis.Y)).Normalized;
                    var emitter = new ExhaustInteraction.Emitter(view.NozzlePosition(jet.Volume), axis,
                        shape.Radius, shape.Length, shape.Spread * 2.0, piece.Stage.RcsThrustNewtons * jet.Command / piece.Jets.Count);
                    if (!view.HasExhaustTarget(emitter)) { continue; }
                    ExhaustInteraction.Accumulate(emitter, (origin, direction, reach) => view.TraceExhaust(origin, direction, reach, out _));

                }

            }

        }

    }

    private void MeshObstacle(MeshInstance3D volume, ShaderMaterial material, float length, float exit, float spread) {

        bool nearby = false;
        Vector3d nozzle = NozzlePosition(volume);
        Vector3d axis = _vessel.Orientation.Rotate(Frames.Sim(-DatumTransform(volume).Basis.Y)).Normalized;
        HashCode signature = new();
        foreach (VesselView view in Views.Values) {

            if (view == this || !view._vessel.Intact || !InExhaustCone(nozzle, axis, length, exit, spread * 2.0, view)) { continue; }
            nearby = true;
            Transform3D relative = volume.GlobalTransform.AffineInverse() * view._body.GlobalTransform;
            signature.Add(view.GetInstanceId());
            signature.Add(relative.Origin.Snapped(Vector3.One * 0.025f));
            signature.Add(relative.Basis.X.Snapped(Vector3.One * 0.002f));
            signature.Add(relative.Basis.Y.Snapped(Vector3.One * 0.002f));

        }
        signature.Add(Mathf.RoundToInt(length * 20.0f));
        signature.Add(Mathf.RoundToInt(spread * 500.0f));
        int fingerprint = signature.ToHashCode();

        if (!nearby) {

            material.SetShaderParameter("ship_enabled", false);
            _blockedPlumes.Remove(volume);
            _exhaustMaskTimes.Remove(volume);
            _queuedMasks.Remove(volume);
            return;

        }

        while (_maskQueue.Count > 0 && (!IsInstanceValid(_maskQueue.Peek()) || !_maskQueue.Peek().Visible || !_queuedMasks.Contains(_maskQueue.Peek()))) {

            _queuedMasks.Remove(_maskQueue.Dequeue());

        }
        bool unchanged = _maskSignatures.TryGetValue(volume, out int previous) && previous == fingerprint;
        if ((unchanged && _exhaustMaskTimes.ContainsKey(volume))
            || (_exhaustMaskTimes.TryGetValue(volume, out float updated) && _effectTime - updated < 0.06f)) {

            if (unchanged) { _queuedMasks.Remove(volume); }
            if (_blockedPlumes.Contains(volume)) { PlumeObstacles++; }
            return;

        }

        if (_queuedMasks.Add(volume)) { _maskQueue.Enqueue(volume); }
        if (_maskUpdates >= 1 || _maskQueue.Peek() != volume) {

            if (_blockedPlumes.Contains(volume)) { PlumeObstacles++; }
            return;

        }
        _maskQueue.Dequeue();
        _queuedMasks.Remove(volume);
        _maskUpdates++;
        _maskSignatures[volume] = fingerprint;
        _exhaustMaskTimes[volume] = _effectTime;
        int size = exit < 0.15f ? 12 : 20;
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
