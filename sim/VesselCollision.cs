using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FullThrust.Sim;

public static class VesselCollision {

    public readonly record struct Contact(Vector3d Point, Vector3d Normal, double Depth);

    private readonly record struct Support(Vector3d Point, Vector3d A, Vector3d B);
    private readonly record struct Face(int A, int B, int C, Vector3d Normal, double Distance);

    private sealed class Shape {

        public int Stages;
        public readonly List<Hull.Station> Rings = new();
        public double Radius;

        public Shape(Vessel vessel) {

            Stages = vessel.StageCount;

            foreach (Stage stage in vessel.Stages) {

                Rings.AddRange(stage.Hull.Stations);

                foreach (Part part in stage.Parts) {

                    if (part.Kind == PartKind.Engine && part.RingRadius == 0.0 && part.Profile != null) {

                        Rings.AddRange(part.Profile);

                    }

                }

            }

            foreach (Hull.Station ring in Rings) {

                double axial = Math.Max(Math.Abs(ring.Z - vessel.Base), Math.Abs(ring.Z - vessel.Tip));
                Radius = Math.Max(Radius, Math.Sqrt(axial * axial + ring.Radius * ring.Radius));

            }

        }

    }

    private static readonly ConditionalWeakTable<Vessel, Shape> Shapes = new();

    private static Shape Geometry(Vessel vessel) {

        Shape shape = Shapes.GetValue(vessel, v => new Shape(v));

        if (shape.Stages != vessel.StageCount) {

            Shapes.Remove(vessel);
            shape = Shapes.GetValue(vessel, v => new Shape(v));

        }

        return shape;

    }

    public static double Radius(Vessel vessel) => Geometry(vessel).Radius;

    private static Vector3d Furthest(Vessel vessel, Vector3d direction) {

        Vector3d local = vessel.Orientation.Conjugate.Rotate(direction);
        double radial = Math.Sqrt(local.X * local.X + local.Y * local.Y);
        Vector3d best = Vector3d.Zero;
        double distance = double.NegativeInfinity;

        foreach (Hull.Station ring in Geometry(vessel).Rings) {

            double z = ring.Z - vessel.CentreOfMassZ;
            double projection = radial * ring.Radius + local.Z * z;

            if (projection > distance) {

                distance = projection;
                best = radial > 1.0e-12
                    ? new Vector3d(local.X * ring.Radius / radial, local.Y * ring.Radius / radial, z)
                    : new Vector3d(0.0, 0.0, z);

            }

        }

        return vessel.Orientation.Rotate(best);

    }

    private static Support Extreme(Vessel a, Vessel b, Vector3d direction) {

        Vector3d pa = Furthest(a, direction);
        Vector3d pb = b.Position - a.Position + Furthest(b, -direction);
        return new Support(pa - pb, pa, pb);

    }

    public static bool Find(Vessel a, Vessel b, out Contact contact) {

        contact = default;
        double reach = Radius(a) + Radius(b);

        if ((b.Position - a.Position).LengthSquared > reach * reach) {

            return false;

        }

        Vector3d direction = b.Position - a.Position + new Vector3d(0.00013, 0.00027, 0.00039);
        List<Support> simplex = new(4) { Extreme(a, b, direction) };
        direction = -simplex[0].Point;

        for (int iteration = 0; iteration < 48; iteration++) {

            if (direction.LengthSquared < 1.0e-20) {

                direction = Vector3d.UnitX;

            }

            Support support = Extreme(a, b, direction);

            if (Vector3d.Dot(support.Point, direction) < 0.0) {

                return false;

            }

            simplex.Insert(0, support);

            if (Enclose(simplex, ref direction)) {

                return Expand(a, b, simplex, out contact);

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

        foreach ((int b, int c, int opposite) in new[] { (1, 2, 3), (2, 3, 1), (3, 1, 2) }) {

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

    private static bool Expand(Vessel a, Vessel b, List<Support> points, out Contact contact) {

        List<Face> faces = new();
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
            Support point = Extreme(a, b, face.Normal);

            if (Vector3d.Dot(point.Point, face.Normal) - face.Distance < 0.0001 || iteration == 79) {

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
                contact = new Contact(a.Position + (pa + pb) * 0.5, face.Normal, face.Distance);
                return face.Distance > 0.00001;

            }

            List<(int A, int B)> edges = new();

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
