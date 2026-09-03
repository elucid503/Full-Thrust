namespace FullThrust.Sim;

public readonly struct Vector3d {

    public static readonly Vector3d Zero = new Vector3d(0.0, 0.0, 0.0);
    public static readonly Vector3d UnitX = new Vector3d(1.0, 0.0, 0.0);
    public static readonly Vector3d UnitY = new Vector3d(0.0, 1.0, 0.0);
    public static readonly Vector3d UnitZ = new Vector3d(0.0, 0.0, 1.0);

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Vector3d(double x, double y, double z) {

        X = x;
        Y = y;
        Z = z;

    }

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    public Vector3d Normalized {

        get {

            double length = Length;

            if (length <= 0.0) {

                return Zero;

            }

            return this * (1.0 / length);

        }

    }

    public static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static double Angle(Vector3d a, Vector3d b) => Math.Acos(Math.Clamp(Dot(a.Normalized, b.Normalized), -1.0, 1.0));

    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator -(Vector3d a) => new Vector3d(-a.X, -a.Y, -a.Z);
    public static Vector3d operator *(Vector3d a, double scalar) => new Vector3d(a.X * scalar, a.Y * scalar, a.Z * scalar);
    public static Vector3d operator *(double scalar, Vector3d a) => a * scalar;
    public static Vector3d operator /(Vector3d a, double scalar) => a * (1.0 / scalar);

    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";

}
