using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FullThrust.Sim;

public static class VesselCollision {

    public readonly record struct Contact(Vector3d Point, Vector3d Normal, double Depth);

    private readonly record struct Support(Vector3d Point, Vector3d A, Vector3d B);
    private readonly record struct Face(int A, int B, int C, Vector3d Normal, double Distance);

    // A bay is a tube, and no support function describes one: the hull it is cut into is split into
    // a solid below the floor and a ring of convex wedges around the wall. Enough wedges that the
    // chord each spans leaves the clear radius within a percent of the wall the lathe draws.
    private const int BayWedges = 32;
    private const int WedgeArc = 3;

    /// <summary>One convex piece of a vessel: a run of the mould line, or an explicit point cloud
    /// for the pieces - bay walls - that are not bodies of revolution.</summary>
    private sealed class Lobe {

        public Hull.Station[] Rings;
        public Vector3d[] Points;
        public EngineState Engine;
        public Vector3d ToVessel(Vector3d point) => Engine == null ? point : Engine.Mount + Engine.ContactRotation.Rotate(point);

        // Enough to reject most pairs before a GJK run: a stack with a bay carries a dozen pieces,
        // and all but one or two of them are nowhere near whatever it is touching.
        public Vector3d Centre;
        public double Bound;
        public Vector3d HalfExtent;

        public Lobe Measured() {

            Vector3d low = new Vector3d(double.MaxValue, double.MaxValue, double.MaxValue);
            Vector3d high = new Vector3d(double.MinValue, double.MinValue, double.MinValue);

            if (Points != null) {

                foreach (Vector3d point in Points) {

                    low = new Vector3d(Math.Min(low.X, point.X), Math.Min(low.Y, point.Y), Math.Min(low.Z, point.Z));
                    high = new Vector3d(Math.Max(high.X, point.X), Math.Max(high.Y, point.Y), Math.Max(high.Z, point.Z));

                }

            }
            else {

                foreach (Hull.Station ring in Rings) {

                    low = new Vector3d(Math.Min(low.X, -ring.Radius), Math.Min(low.Y, -ring.Radius), Math.Min(low.Z, ring.Z));
                    high = new Vector3d(Math.Max(high.X, ring.Radius), Math.Max(high.Y, ring.Radius), Math.Max(high.Z, ring.Z));

                }

            }

            Centre = (low + high) * 0.5;
            Bound = (high - low).Length * 0.5;
            HalfExtent = (high - low) * 0.5;

            return this;

        }

    }

    private sealed class Shape {

        public int Stages;
        public int Revision;
        public readonly List<Hull.Station> Rings = new();
        public readonly List<Lobe> Lobes = new();
        public double Low = double.MaxValue;
        public double High = double.MinValue;
        public double Width;

        public Shape(Vessel vessel) {

            Stages = vessel.StageCount;
            Revision = VesselSurface.Revision(vessel);

            // One lobe per stage rather than one for the vessel: a single hull over the whole stack
            // would bridge a bay's mouth to the stage above and fill in the cavity that is the point.
            foreach (Stage stage in vessel.Stages) {

                Hull hull = stage.ContactHull ?? stage.Hull;
                List<Hull.Station> solid = new List<Hull.Station>(hull.Stations);

                Rings.AddRange(solid);

                // The bell is a piece of its own. Folded into the hull it would bridge its mouth up
                // to the skirt and make the whole tail a solid cone - which is precisely the
                // clearance a bay exists to give the stage hanging in it.
                foreach (EngineState engine in stage.EngineStates) {
                    Hull.Station[] profile = engine.ContactProfile;
                    if (profile == null || profile.Length < 2) { continue; }
                    // Each bell is mounted separately; splitting its contour preserves the throat
                    // instead of filling it with the convex hull of the whole engine.
                    int start = 0;
                    for (int i = 1; i < profile.Length; i++) {
                        bool end = i == profile.Length - 1;
                        if (!end) {
                            double before = (profile[i].Radius - profile[i - 1].Radius) / Math.Max(profile[i].Z - profile[i - 1].Z, 1e-12);
                            double after = (profile[i + 1].Radius - profile[i].Radius) / Math.Max(profile[i + 1].Z - profile[i].Z, 1e-12);
                            end = after > before + 1e-9;
                        }
                        if (!end) { continue; }
                        Lobes.Add(new Lobe { Rings = profile[start..(i + 1)], Engine = engine }.Measured());
                        start = i;
                    }
                    double swing = engine.ContactBound + Math.Abs(engine.ContactCentreZ);
                    Low = Math.Min(Low, engine.Mount.Z - swing);
                    High = Math.Max(High, engine.Mount.Z + swing);
                    Width = Math.Max(Width, Math.Sqrt(engine.Mount.X * engine.Mount.X + engine.Mount.Y * engine.Mount.Y) + swing);
                    foreach (Hull.Station ring in profile) {
                        double radial = Math.Sqrt(engine.Mount.X * engine.Mount.X + engine.Mount.Y * engine.Mount.Y);
                        Rings.Add(new Hull.Station(engine.Mount.Z + ring.Z, radial + ring.Radius));
                    }
                }

                if (!hull.HasBay) {

                    Lobes.Add(new Lobe { Rings = solid.ToArray() }.Measured());

                    continue;

                }

                Lobes.Add(new Lobe { Rings = Below(solid, hull) }.Measured());

                Wall(Lobes, hull);

            }

            foreach (Hull.Station ring in Rings) {

                Low = Math.Min(Low, ring.Z); High = Math.Max(High, ring.Z); Width = Math.Max(Width, ring.Radius);

            }

            // Ground only needs the outer radial envelope. Cluster copies previously asked
            // the terrain for the same positions once per bell, at every integration step.
            Rings.Sort((a, b) => a.Z.CompareTo(b.Z));
            List<Hull.Station> unique = new();
            foreach (Hull.Station ring in Rings) {
                if (unique.Count > 0 && Math.Abs(unique[^1].Z - ring.Z) < 1e-6) {
                    if (ring.Radius > unique[^1].Radius) { unique[^1] = ring; }
                } else { unique.Add(ring); }
            }
            Rings.Clear();
            Rings.AddRange(VesselSurface.SimplifyProfile(unique));
            Lobes.Sort((a, b) => b.Bound.CompareTo(a.Bound));

        }

    }

    /// <summary>Everything under a bay floor, closed off by a ring on the floor itself.</summary>
    private static Hull.Station[] Below(List<Hull.Station> solid, Hull hull) {

        List<Hull.Station> kept = new List<Hull.Station> { new Hull.Station(hull.BayFloor, hull.RadiusAt(hull.BayFloor)) };

        foreach (Hull.Station ring in solid) {

            if (ring.Z <= hull.BayFloor) {

                kept.Add(ring);

            }

        }

        return kept.ToArray();

    }

    private static void Wall(List<Lobe> lobes, Hull hull) {

        double inner = hull.BayRadius;

        if (inner <= 0.0) {

            return;

        }

        for (int wedge = 0; wedge < BayWedges; wedge++) {

            List<Vector3d> points = new List<Vector3d>();

            for (int step = 0; step <= WedgeArc; step++) {

                double angle = Math.Tau * (wedge + (double)step / WedgeArc) / BayWedges;

                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                double floorRadius = hull.RadiusAt(hull.BayFloor);
                double mouthRadius = hull.RadiusAt(hull.Tip);

                points.Add(new Vector3d(cos * inner, sin * inner, hull.BayFloor));
                points.Add(new Vector3d(cos * floorRadius, sin * floorRadius, hull.BayFloor));
                points.Add(new Vector3d(cos * inner, sin * inner, hull.Tip));
                points.Add(new Vector3d(cos * mouthRadius, sin * mouthRadius, hull.Tip));

            }

            lobes.Add(new Lobe { Points = points.ToArray() }.Measured());

        }

    }

    private static readonly ConditionalWeakTable<Vessel, Shape> Shapes = new();

    private static Shape Geometry(Vessel vessel) {

        Shape shape = Shapes.GetValue(vessel, v => new Shape(v));

        if (shape.Stages != vessel.StageCount || shape.Revision != VesselSurface.Revision(vessel)) {

            Shapes.Remove(vessel);
            shape = Shapes.GetValue(vessel, v => new Shape(v));

        }

        return shape;

    }

    public static double Radius(Vessel vessel) {
        Shape shape = Geometry(vessel);
        double axial = Math.Max(Math.Abs(shape.Low - vessel.CentreOfMassZ), Math.Abs(shape.High - vessel.CentreOfMassZ));
        return Math.Sqrt(axial * axial + shape.Width * shape.Width);
    }

    public static double ContactThickness(Vessel vessel) {
        double thickness = vessel.Profile.MaxRadius * 0.2;
        foreach (Stage stage in vessel.Stages) {
            Hull hull = stage.ContactHull ?? stage.Hull;
            if (hull.HasBay) { thickness = Math.Min(thickness, hull.WallThickness * 0.4); }
        }
        return Math.Max(thickness, 0.005);
    }

    public static IReadOnlyList<Hull.Station> Stations(Vessel vessel) => Geometry(vessel).Rings;

    private static Vector3d Furthest(Vessel vessel, Lobe lobe, Vector3d direction) {

        Vector3d local = vessel.Orientation.Conjugate.Rotate(direction);
        if (lobe.Engine != null) { local = lobe.Engine.ContactRotation.Conjugate.Rotate(local); }
        Vector3d best = Vector3d.Zero;
        double distance = double.NegativeInfinity;

        if (lobe.Points != null) {

            foreach (Vector3d point in lobe.Points) {

                Vector3d offset = new Vector3d(point.X, point.Y, point.Z - vessel.CentreOfMassZ);
                double projection = Vector3d.Dot(offset, local);

                if (projection > distance) {

                    distance = projection;
                    best = offset;

                }

            }

            return vessel.Orientation.Rotate(best);

        }

        double radial = Math.Sqrt(local.X * local.X + local.Y * local.Y);

        foreach (Hull.Station ring in lobe.Rings) {

            double z = ring.Z;
            double projection = radial * ring.Radius + local.Z * z;

            if (projection > distance) {

                distance = projection;
                best = radial > 1.0e-12
                    ? new Vector3d(local.X * ring.Radius / radial, local.Y * ring.Radius / radial, z)
                    : new Vector3d(0.0, 0.0, z);

            }

        }

        return vessel.Orientation.Rotate(lobe.ToVessel(best) - Vector3d.UnitZ * vessel.CentreOfMassZ);

    }

    private readonly record struct Couple(Vessel A, Lobe LobeA, Vessel B, Lobe LobeB);

    private static Support Extreme(Couple pair, Vector3d direction) {

        Vector3d pa = Furthest(pair.A, pair.LobeA, direction);
        Vector3d pb = pair.B.Position - pair.A.Position + Furthest(pair.B, pair.LobeB, -direction);
        return new Support(pa - pb, pa, pb);

    }

    public static bool Find(Vessel a, Vessel b, out Contact contact) {

        contact = default;
        double reach = Radius(a) + Radius(b);

        if ((b.Position - a.Position).LengthSquared > reach * reach) {

            return false;

        }

        bool touching = false;

        // The deepest overlap of any pair of pieces is the one worth resolving this step: a bell
        // caught in a bay can touch two wedges at once, and pushing on both would jitter it.
        Shape shapeA = Geometry(a), shapeB = Geometry(b);
        Span<Vector3d> centresA = shapeA.Lobes.Count <= 256 ? stackalloc Vector3d[shapeA.Lobes.Count] : new Vector3d[shapeA.Lobes.Count];
        Span<Vector3d> centresB = shapeB.Lobes.Count <= 256 ? stackalloc Vector3d[shapeB.Lobes.Count] : new Vector3d[shapeB.Lobes.Count];
        Span<Vector3d> extentsA = shapeA.Lobes.Count <= 256 ? stackalloc Vector3d[shapeA.Lobes.Count] : new Vector3d[shapeA.Lobes.Count];
        Span<Vector3d> extentsB = shapeB.Lobes.Count <= 256 ? stackalloc Vector3d[shapeB.Lobes.Count] : new Vector3d[shapeB.Lobes.Count];
        for (int i = 0; i < centresA.Length; i++) {
            Lobe lobe = shapeA.Lobes[i];
            centresA[i] = a.Orientation.Rotate(lobe.ToVessel(lobe.Centre) - Vector3d.UnitZ * a.CentreOfMassZ);
            extentsA[i] = WorldExtent(a, lobe);
        }
        for (int i = 0; i < centresB.Length; i++) {
            Lobe lobe = shapeB.Lobes[i];
            centresB[i] = b.Position - a.Position + b.Orientation.Rotate(lobe.ToVessel(lobe.Centre) - Vector3d.UnitZ * b.CentreOfMassZ);
            extentsB[i] = WorldExtent(b, lobe);
        }
        for (int i = 0; i < centresA.Length; i++) {
            Lobe lobeA = shapeA.Lobes[i];

            for (int j = 0; j < centresB.Length; j++) {
                Lobe lobeB = shapeB.Lobes[j];

                double bound = lobeA.Bound + lobeB.Bound;
                Vector3d delta = centresB[j] - centresA[i];
                Vector3d extent = extentsA[i] + extentsB[j];
                if (Math.Abs(delta.X) > extent.X || Math.Abs(delta.Y) > extent.Y || Math.Abs(delta.Z) > extent.Z || delta.LengthSquared > bound * bound) {

                    continue;

                }

                // Moving beyond an enclosing box also separates its contents. Once a deeper
                // contact is known, a pair with less possible penetration cannot replace it.
                double maximumDepth = Math.Min(extent.X - Math.Abs(delta.X), Math.Min(extent.Y - Math.Abs(delta.Y), extent.Z - Math.Abs(delta.Z)));
                if (touching && maximumDepth <= contact.Depth) { continue; }

                if (Find(new Couple(a, lobeA, b, lobeB), out Contact hit) && (!touching || hit.Depth > contact.Depth)) {

                    contact = hit;
                    touching = true;

                }

            }

        }

        return touching;

    }

    private static Vector3d WorldExtent(Vessel vessel, Lobe lobe) {
        QuaternionD rotation = lobe.Engine == null ? vessel.Orientation : vessel.Orientation * lobe.Engine.ContactRotation;
        Vector3d x = rotation.Rotate(Vector3d.UnitX * lobe.HalfExtent.X);
        Vector3d y = rotation.Rotate(Vector3d.UnitY * lobe.HalfExtent.Y);
        Vector3d z = rotation.Rotate(Vector3d.UnitZ * lobe.HalfExtent.Z);
        return new Vector3d(Math.Abs(x.X) + Math.Abs(y.X) + Math.Abs(z.X), Math.Abs(x.Y) + Math.Abs(y.Y) + Math.Abs(z.Y), Math.Abs(x.Z) + Math.Abs(y.Z) + Math.Abs(z.Z));
    }

    private static bool Find(Couple pair, out Contact contact) {

        contact = default;

        Vector3d direction = pair.B.Position - pair.A.Position + new Vector3d(0.00013, 0.00027, 0.00039);
        List<Support> simplex = new(4) { Extreme(pair, direction) };
        direction = -simplex[0].Point;

        for (int iteration = 0; iteration < 48; iteration++) {

            if (direction.LengthSquared < 1.0e-20) {

                direction = Vector3d.UnitX;

            }

            Support support = Extreme(pair, direction);

            if (Vector3d.Dot(support.Point, direction) < 0.0) {

                return false;

            }

            simplex.Insert(0, support);

            if (Enclose(simplex, ref direction)) {

                return Expand(pair, simplex, out contact);

            }

        }

        return false;

    }

    private static Vector3d Perpendicular(Vector3d edge, Vector3d towards) {

        Vector3d direction = Vector3d.Cross(Vector3d.Cross(edge, towards), edge);
        return direction.LengthSquared > 1.0e-20 ? direction
            : Vector3d.Cross(edge, Math.Abs(edge.X) < Math.Abs(edge.Y) ? Vector3d.UnitX : Vector3d.UnitY);

    }

    private static bool Enclose(List<Support> s, ref Vector3d direction) {

        Vector3d a = s[0].Point;
        Vector3d ab = s[1].Point - a;

        if (s.Count == 2) {

            direction = Perpendicular(ab, -a);
            return false;

        }

        Vector3d ac = s[2].Point - a;
        Vector3d normal = Vector3d.Cross(ab, ac);

        if (s.Count == 3) {

            if (Vector3d.Dot(Vector3d.Cross(normal, ac), -a) > 0.0) {

                if (Vector3d.Dot(ac, -a) > 0.0) {

                    s.RemoveAt(1);
                    direction = Perpendicular(ac, -a);

                }
                else {

                    s.RemoveAt(2);
                    direction = Perpendicular(ab, -a);

                }

            }
            else if (Vector3d.Dot(Vector3d.Cross(ab, normal), -a) > 0.0) {

                s.RemoveAt(2);
                direction = Perpendicular(ab, -a);

            }
            else if (Vector3d.Dot(normal, -a) > 0.0) {

                direction = normal;

            }
            else {

                (s[1], s[2]) = (s[2], s[1]);
                direction = -normal;

            }

            return false;

        }

        for (int b = 1; b <= 3; b++) {
            int c = b % 3 + 1;
            int opposite = c % 3 + 1;

            Vector3d outward = Vector3d.Cross(s[b].Point - a, s[c].Point - a);

            if (Vector3d.Dot(outward, s[opposite].Point - a) > 0.0) {

                outward = -outward;

            }

            if (Vector3d.Dot(outward, -a) > 1.0e-12) {

                Support sb = s[b];
                Support sc = s[c];
                s.RemoveRange(1, 3);
                s.Add(sb);
                s.Add(sc);
                direction = outward;
                return false;

            }

        }

        return true;

    }

    private static void AddFace(List<Support> points, List<Face> faces, int a, int b, int c) {

        Vector3d cross = Vector3d.Cross(points[b].Point - points[a].Point, points[c].Point - points[a].Point);

        if (cross.LengthSquared < 1.0e-20) {

            return;

        }

        Vector3d normal = cross.Normalized;
        double distance = Vector3d.Dot(normal, points[a].Point);

        if (distance < 0.0) {

            (b, c) = (c, b);
            normal = -normal;
            distance = -distance;

        }

        faces.Add(new Face(a, b, c, normal, distance));

    }

    private static bool Expand(Couple pair, List<Support> points, out Contact contact) {

        List<Face> faces = new(128);
        List<(int A, int B)> edges = new(64);
        points.EnsureCapacity(84);
        AddFace(points, faces, 0, 1, 2);
        AddFace(points, faces, 0, 3, 1);
        AddFace(points, faces, 0, 2, 3);
        AddFace(points, faces, 1, 3, 2);
        contact = default;

        for (int iteration = 0; iteration < 80 && faces.Count > 0; iteration++) {

            int nearest = 0;

            for (int index = 1; index < faces.Count; index++) {

                if (faces[index].Distance < faces[nearest].Distance) {

                    nearest = index;

                }

            }

            Face face = faces[nearest];
            Support point = Extreme(pair, face.Normal);

            if (Vector3d.Dot(point.Point, face.Normal) - face.Distance < 0.001 || iteration == 79) {

                Vector3d origin = points[face.A].Point;
                Vector3d u = points[face.B].Point - origin;
                Vector3d v = points[face.C].Point - origin;
                Vector3d w = face.Normal * face.Distance - origin;
                double uu = Vector3d.Dot(u, u), uv = Vector3d.Dot(u, v), vv = Vector3d.Dot(v, v);
                double denominator = uu * vv - uv * uv;
                double wb = denominator > 1.0e-24 ? (vv * Vector3d.Dot(w, u) - uv * Vector3d.Dot(w, v)) / denominator : 0.0;
                double wc = denominator > 1.0e-24 ? (uu * Vector3d.Dot(w, v) - uv * Vector3d.Dot(w, u)) / denominator : 0.0;
                double wa = 1.0 - wb - wc;
                Vector3d pa = points[face.A].A * wa + points[face.B].A * wb + points[face.C].A * wc;
                Vector3d pb = points[face.A].B * wa + points[face.B].B * wb + points[face.C].B * wc;
                contact = new Contact(pair.A.Position + (pa + pb) * 0.5, face.Normal, face.Distance);
                return face.Distance > 0.00001;

            }

            edges.Clear();

            void Edge(int first, int second) {

                if (!edges.Remove((second, first))) {

                    edges.Add((first, second));

                }

            }

            for (int index = faces.Count - 1; index >= 0; index--) {

                Face old = faces[index];

                if (Vector3d.Dot(old.Normal, point.Point - points[old.A].Point) > 1.0e-9) {

                    Edge(old.A, old.B);
                    Edge(old.B, old.C);
                    Edge(old.C, old.A);
                    faces.RemoveAt(index);

                }

            }

            int added = points.Count;
            points.Add(point);

            foreach ((int first, int second) in edges) {

                AddFace(points, faces, first, second, added);

            }

        }

        return false;

    }

    private static Vector3d InverseInertia(Vessel vessel, Vector3d torque) {

        Vector3d local = vessel.Orientation.Conjugate.Rotate(torque);
        return vessel.Orientation.Rotate(new Vector3d(local.X / vessel.Inertia.X, local.Y / vessel.Inertia.Y, local.Z / vessel.Inertia.Z));

    }

    private static void Impulse(Vessel vessel, Vector3d lever, Vector3d impulse) {

        vessel.Velocity += impulse / vessel.Mass;
        vessel.AngularVelocity += vessel.Orientation.Conjugate.Rotate(InverseInertia(vessel, Vector3d.Cross(lever, impulse)));

    }

    public static void Resolve(Vessel a, Vessel b, Contact contact) {

        Vector3d ra = contact.Point - a.Position;
        Vector3d rb = contact.Point - b.Position;
        Vector3d relative = b.Velocity + Vector3d.Cross(b.Orientation.Rotate(b.AngularVelocity), rb)
            - a.Velocity - Vector3d.Cross(a.Orientation.Rotate(a.AngularVelocity), ra);
        double closing = Vector3d.Dot(relative, contact.Normal);
        double inverseMass = 1.0 / a.Mass + 1.0 / b.Mass;

        double EffectiveMass(Vector3d axis) {

            return inverseMass + Vector3d.Dot(axis,
                Vector3d.Cross(InverseInertia(a, Vector3d.Cross(ra, axis)), ra)
                + Vector3d.Cross(InverseInertia(b, Vector3d.Cross(rb, axis)), rb));

        }

        if (closing < 0.0) {

            double magnitude = -(closing < -0.5 ? 1.08 : 1.0) * closing / EffectiveMass(contact.Normal);
            Vector3d impulse = contact.Normal * magnitude;
            Impulse(a, ra, -impulse);
            Impulse(b, rb, impulse);
            relative = b.Velocity + Vector3d.Cross(b.Orientation.Rotate(b.AngularVelocity), rb)
                - a.Velocity - Vector3d.Cross(a.Orientation.Rotate(a.AngularVelocity), ra);
            Vector3d tangent = relative - contact.Normal * Vector3d.Dot(relative, contact.Normal);

            if (tangent.LengthSquared > 1.0e-12) {

                Vector3d axis = tangent.Normalized;
                Vector3d friction = -axis * Math.Min(tangent.Length / EffectiveMass(axis), magnitude * 0.3);
                Impulse(a, ra, -friction);
                Impulse(b, rb, friction);

            }

        }

        Vector3d correction = contact.Normal * (Math.Max(0.0, contact.Depth - 0.0001) * 0.9 / inverseMass);
        a.Position -= correction / a.Mass;
        b.Position += correction / b.Mass;

    }

}
