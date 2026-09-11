using System;
using System.Collections.Generic;
using FullThrust.Sim;
using Godot;

namespace FullThrust.Game;

/// <summary>Streamed capsule proxies, persistent destruction IDs and a bounded visual fall queue.</summary>
public sealed class ScatterObstacles {
    private sealed class Cell {
        public object Key;
        public Vector3d Anchor;
        public Transform3D[] Transforms;
        public MultiMesh Mesh;
        public MultiMeshInstance3D Owner;
        public double Bound;
        public Vector3 CrownRadii;
    }
    private sealed class Fall {
        public Cell Cell;
        public int Index;
        public double Start;
        public Vector3 Axis;
        public MultiMeshInstance3D Debris;
    }
    private readonly Dictionary<object, Cell> _cells = new();
    private readonly HashSet<(object Cell, int Index)> _destroyed = new();
    private readonly List<Fall> _falls = new();
    private readonly bool _trees;
    public int DestroyedCount => _destroyed.Count;
    public int EffectCount => _falls.Count;
    public ScatterObstacles(bool trees) => _trees = trees;

    private static Transform3D Hidden(Transform3D transform) => new(Basis.FromScale(Vector3.Zero), transform.Origin);

    public void Add(object key, Vector3d anchor, Transform3D[] transforms, MultiMesh mesh, MultiMeshInstance3D owner = null) {
        Cell cell = new() { Key = key, Anchor = anchor, Transforms = transforms, Mesh = mesh, Owner = owner };
        foreach (Transform3D transform in transforms) {
            cell.Bound = Math.Max(cell.Bound, transform.Origin.Length() + transform.Basis.Scale.Length() * 12.0);
        }
        Aabb bounds = mesh.Mesh?.GetAabb() ?? new Aabb(new Vector3(-2.2f, 0, -2.2f), new Vector3(4.4f, 10.5f, 4.4f));
        cell.CrownRadii = new Vector3(
            Mathf.Max(Mathf.Abs(bounds.Position.X), Mathf.Abs(bounds.End.X)), 3.5f,
            Mathf.Max(Mathf.Abs(bounds.Position.Z), Mathf.Abs(bounds.End.Z)));
        _cells.Add(key, cell);
        for (int i = 0; i < transforms.Length; i++) {
            if (_destroyed.Contains((key, i))) { mesh.SetInstanceTransform(i, Hidden(transforms[i])); }
        }
    }

    public void Remove(object key) {
        for (int i = _falls.Count - 1; i >= 0; i--) {
            if (!Equals(_falls[i].Cell.Key, key)) { continue; }
            _falls[i].Debris?.QueueFree();
            _falls.RemoveAt(i);
        }
        _cells.Remove(key);
    }

    public void Animate(double time) {
        for (int i = _falls.Count - 1; i >= 0; i--) {
            Fall fall = _falls[i];
            Transform3D original = fall.Cell.Transforms[fall.Index];
            float age = (float)Math.Max(0.0, time - fall.Start);
            if (age >= (_trees ? 12.0f : 1.2f)) {
                fall.Debris?.QueueFree();
                fall.Cell.Mesh.SetInstanceTransform(fall.Index, Hidden(original));
                _falls.RemoveAt(i);
                continue;
            }
            float scale = _trees ? 1.0f - Mathf.SmoothStep(9.0f, 12.0f, age)
                : 1.0f - Mathf.SmoothStep(0.0f, 0.7f, age);
            if (fall.Debris != null) {
                for (int shard = 0; shard < 4; shard++) {
                    float angle = shard * Mathf.Tau / 4 + fall.Index;
                    Vector3 local = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * age * 1.8f;
                    local.Y = Mathf.Max(0, age * 2.3f - age * age * 4.9f);
                    float shrink = 1 - Mathf.SmoothStep(0.65f, 1.2f, age);
                    Basis fragment = original.Basis.Rotated(fall.Axis, age * (shard + 1))
                        * Basis.FromScale(new Vector3(0.30f, 0.40f, 0.33f) * shrink);
                    fall.Debris.Multimesh.SetInstanceTransform(shard,
                        new Transform3D(fragment, original.Origin + original.Basis * local));
                }
            }
            Basis basis = _trees ? original.Basis.Rotated(fall.Axis,
                Mathf.Pi * 0.48f * Mathf.SmoothStep(0.0f, 2.4f, age)) : original.Basis;
            fall.Cell.Mesh.SetInstanceTransform(fall.Index, new Transform3D(basis.Scaled(Vector3.One * scale), original.Origin));
        }
    }

    public bool Resolve(CelestialBody body, Vessel vessel, Vector3d previous, QuaternionD orientation,
        double startTime, double time, bool damage = false) {
        bool hit = false;
        for (int contact = 0; contact < 8; contact++) {
            if (!ResolveOne(body, vessel, previous, orientation, startTime, time, damage, out bool destroyed)) { break; }
            hit = true;
            if (!destroyed) { break; }
        }
        return hit;
    }

    private bool ResolveOne(CelestialBody body, Vessel vessel, Vector3d previous, QuaternionD orientation,
        double startTime, double time, bool damage, out bool destroyed) {
        destroyed = false;
        if (!vessel.Intact || _cells.Count == 0) { return false; }
        Vector3d start = body.ToBodyFixed(previous, startTime);
        Vector3d end = body.ToBodyFixed(vessel.Position, time);
        double vesselBound = VesselCollision.Radius(vessel);
        Cell closest = null;
        int index = -1;
        double fraction = double.PositiveInfinity;
        Vector3d hitNormal = Vector3d.Zero;
        Vector3d hitPoint = Vector3d.Zero;
        double obstacleRadius = 0.0;
        int samples = Math.Clamp((int)Math.Ceiling(vessel.Length / 0.75), 2, 128);
        double spacing = vessel.Length / samples;
        Span<Vector3d> fromSamples = stackalloc Vector3d[samples + 1];
        Span<Vector3d> toSamples = stackalloc Vector3d[samples + 1];
        Span<double> radii = stackalloc double[samples + 1];
        bool prepared = false;
        foreach (Cell cell in _cells.Values) {
            if (!ScatterCollision.Sweep(start, end, cell.Anchor, cell.Anchor,
                cell.Bound + vesselBound, out _, out _)) { continue; }
            for (int i = 0; i < cell.Transforms.Length; i++) {
                if (_destroyed.Contains((cell.Key, i))) { continue; }
                Transform3D transform = cell.Transforms[i];
                Vector3 size = transform.Basis.Scale;
                double radius = _trees ? 0.30 * Math.Max(size.X, size.Z)
                    : Math.Max(size.X, size.Z * 0.72) * 0.95;
                // Pebbles are cosmetic; avoid spending narrow-phase work on sub-20 cm stones.
                if (!_trees && radius < 0.20) { continue; }
                Vector3d root = cell.Anchor + Frames.Sim(transform.Origin);
                Vector3d up = Frames.Sim(transform.Basis.Y.Normalized());
                Vector3d a = root + up * (_trees ? radius : size.Y * 0.35);
                Vector3d b = root + up * (_trees ? size.Y * 8.5 : size.Y * 0.55);
                for (int component = 0; component < (_trees ? 2 : 1); component++) {
                    if (component == 1) {
                        // Branches/crown are physical too, not just the thin central trunk.
                        Vector3 crown = cell.CrownRadii * size;
                        radius = Math.Max(crown.X, Math.Max(crown.Y, crown.Z));
                        a = root + up * (size.Y * 7.0);
                        b = a;
                    }
                    if (!ScatterCollision.Sweep(start, end, a, b, radius + vesselBound, out _, out _)) { continue; }
                    // Overlapping spheres follow the hull instead of using a rocket-length bounding sphere.
                    if (!prepared) {
                        for (int sample = 0; sample <= samples; sample++) {
                            double z = vessel.Base + spacing * sample;
                            Vector3d offset = Vector3d.UnitZ * (z - vessel.CentreOfMassZ);
                            fromSamples[sample] = body.ToBodyFixed(previous + orientation.Rotate(offset), startTime);
                            toSamples[sample] = body.ToBodyFixed(vessel.Position + vessel.Orientation.Rotate(offset), time);
                            radii[sample] = Math.Sqrt(Math.Pow(vessel.RadiusAt(z), 2.0) + spacing * spacing * 0.25);
                        }
                        prepared = true;
                    }
                    for (int sample = 0; sample <= samples; sample++) {
                        Vector3d from = fromSamples[sample];
                        Vector3d to = toSamples[sample];
                        double hullRadius = radii[sample];
                        double t;
                        Vector3d normal;
                        bool contact;
                        if (component == 1) {
                            // Match the imported crown's width/depth without a sphere protruding above the tree.
                            Basis inverse = transform.Basis.Inverse();
                            float localHull = (float)(hullRadius / Math.Min(size.X, Math.Min(size.Y, size.Z)));
                            Vector3 crownRadii = cell.CrownRadii + Vector3.One * localHull;
                            Vector3 localFrom = inverse * Frames.Direction(from - root) - Vector3.Up * 7.0f;
                            Vector3 localTo = inverse * Frames.Direction(to - root) - Vector3.Up * 7.0f;
                            contact = ScatterCollision.Sweep(Frames.Sim(localFrom / crownRadii), Frames.Sim(localTo / crownRadii),
                                Vector3d.Zero, Vector3d.Zero, 1.0, out t, out normal);
                            normal = Frames.Sim((inverse.Transposed() * (Frames.Direction(normal) / crownRadii)).Normalized());
                        } else {
                            contact = ScatterCollision.Sweep(from, to, a, b, radius + hullRadius, out t, out normal);
                        }
                        if (contact && t < fraction) {
                            closest = cell;
                            index = i;
                            fraction = t;
                            hitNormal = normal;
                            hitPoint = from + (to - from) * t - normal * hullRadius;
                            obstacleRadius = _trees ? 0.30 * Math.Max(size.X, size.Z) : radius;
                        }
                    }
                }
            }
        }
        if (closest == null) { return false; }
        Vector3d worldNormal = body.ToInertial(hitNormal, time);
        Vector3d lever = body.ToInertial(hitPoint - (start + (end - start) * fraction), time);
        Vector3d relative = vessel.Velocity - body.AirVelocityAt(vessel.Position)
            + Vector3d.Cross(vessel.Orientation.Rotate(vessel.AngularVelocity), lever);
        Vector3d torqueAxis = vessel.Orientation.Conjugate.Rotate(Vector3d.Cross(lever, worldNormal));
        Vector3d inverseTorque = new(torqueAxis.X / vessel.Inertia.X,
            torqueAxis.Y / vessel.Inertia.Y, torqueAxis.Z / vessel.Inertia.Z);
        double inverseMass = 1.0 / vessel.Mass + Vector3d.Dot(torqueAxis, inverseTorque);
        void ApplyImpulse(double magnitude) {
            vessel.Velocity += worldNormal * (magnitude / vessel.Mass);
            vessel.AngularVelocity += inverseTorque * magnitude;
        }
        double closing = Math.Max(0.0, -Vector3d.Dot(relative, worldNormal));
        double energy = 0.5 / inverseMass * closing * closing;
        double toughness = (_trees ? 18000.0 : 180000.0) * obstacleRadius * obstacleRadius;
        if (energy > toughness) {
            destroyed = true;
            _destroyed.Add((closest.Key, index));
            if (_falls.Count >= 64) {
                Fall oldest = _falls[0];
                oldest.Cell.Mesh.SetInstanceTransform(oldest.Index, Hidden(oldest.Cell.Transforms[oldest.Index]));
                oldest.Debris?.QueueFree();
                _falls.RemoveAt(0);
            }
            Vector3 up = closest.Transforms[index].Basis.Y.Normalized();
            Vector3 push = Frames.Direction(-hitNormal);
            Vector3 axis = up.Cross(push);
            if (axis.LengthSquared() < 0.001f) { axis = closest.Transforms[index].Basis.X.Normalized(); }
            Fall effect = new() { Cell = closest, Index = index, Start = time, Axis = axis.Normalized() };
            if (!_trees && closest.Owner != null) {
                MultiMesh fragments = new() {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true,
                    Mesh = closest.Mesh.Mesh, InstanceCount = 4
                };
                for (int shard = 0; shard < 4; shard++) {
                    fragments.SetInstanceColor(shard, closest.Mesh.GetInstanceColor(index));
                    fragments.SetInstanceTransform(shard, Hidden(closest.Transforms[index]));
                }
                effect.Debris = new MultiMeshInstance3D {
                    Multimesh = fragments, MaterialOverride = closest.Owner.MaterialOverride,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    GIMode = GeometryInstance3D.GIModeEnum.Disabled
                };
                closest.Owner.AddChild(effect.Debris);
            }
            _falls.Add(effect);
            double retained = Math.Sqrt(Math.Max(0.0, closing * closing - 2.0 * toughness * inverseMass));
            ApplyImpulse((closing - retained) / inverseMass);
        } else {
            vessel.Position = body.ToInertial(start + (end - start) * Math.Max(0.0, fraction - 0.001), time)
                + worldNormal * 0.02;
            ApplyImpulse(closing * 1.05 / inverseMass);
        }
        if (damage && closing > 12.0) {
            vessel.Position = body.ToInertial(start + (end - start) * Math.Max(0.0, fraction - 0.001), time)
                + worldNormal * 0.02;
            vessel.Fate = VesselFate.Impacted;
        }
        return true;
    }
}
