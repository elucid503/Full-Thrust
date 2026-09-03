namespace FullThrust.Sim;

public readonly struct QuaternionD {

    public static readonly QuaternionD Identity = new QuaternionD(0.0, 0.0, 0.0, 1.0);

    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double W { get; }

    public QuaternionD(double x, double y, double z, double w) {

        X = x;
        Y = y;
        Z = z;
        W = w;

    }

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

    public QuaternionD Conjugate => new QuaternionD(-X, -Y, -Z, W);

    public Vector3d VectorPart => new Vector3d(X, Y, Z);

    public QuaternionD Normalized {

        get {

            double length = Length;

            if (length <= 0.0) {

                return Identity;

            }

            double inverse = 1.0 / length;

            return new QuaternionD(X * inverse, Y * inverse, Z * inverse, W * inverse);

        }

    }

    public static QuaternionD FromAxisAngle(Vector3d axis, double radians) {

        Vector3d unit = axis.Normalized;

        double half = radians * 0.5;
        double sine = Math.Sin(half);

        return new QuaternionD(unit.X * sine, unit.Y * sine, unit.Z * sine, Math.Cos(half));

    }

    // Shortest arc between two directions; the antiparallel case has no unique axis, so any perpendicular will do.
    public static QuaternionD FromTo(Vector3d from, Vector3d to) {

        Vector3d a = from.Normalized;
        Vector3d b = to.Normalized;

        double dot = Vector3d.Dot(a, b);

        if (dot >= 1.0 - 1e-12) {

            return Identity;

        }

        if (dot <= -1.0 + 1e-12) {

            Vector3d perpendicular = Vector3d.Cross(a, Math.Abs(a.X) < 0.9 ? Vector3d.UnitX : Vector3d.UnitY);

            return FromAxisAngle(perpendicular, Math.PI);

        }

        Vector3d axis = Vector3d.Cross(a, b);

        return new QuaternionD(axis.X, axis.Y, axis.Z, 1.0 + dot).Normalized;

    }

    public Vector3d Rotate(Vector3d value) {

        Vector3d vector = VectorPart;
        Vector3d cross = Vector3d.Cross(vector, value);

        return value + (cross * (2.0 * W)) + (Vector3d.Cross(vector, cross) * 2.0);

    }

    // Body-frame angular velocity, so the rate quaternion multiplies on the right.
    public static QuaternionD Integrate(QuaternionD orientation, Vector3d angularVelocity, double dt) {

        QuaternionD rate = new QuaternionD(angularVelocity.X, angularVelocity.Y, angularVelocity.Z, 0.0);

        QuaternionD derivative = orientation * rate;

        double half = 0.5 * dt;

        QuaternionD stepped = new QuaternionD(

            orientation.X + derivative.X * half,
            orientation.Y + derivative.Y * half,
            orientation.Z + derivative.Z * half,
            orientation.W + derivative.W * half

        );

        return stepped.Normalized;

    }

    public static QuaternionD operator *(QuaternionD a, QuaternionD b) {

        return new QuaternionD(

            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z

        );

    }

    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6}, {W:G6})";

}
