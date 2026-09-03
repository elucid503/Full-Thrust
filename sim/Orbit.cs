namespace FullThrust.Sim;

public sealed class Orbit {

    private const double Tau = Math.PI * 2.0;
    private const double Degenerate = 1e-9;

    // A parabola has no semi-major axis, so eccentricity is nudged off 1.0 to keep the conic solvable.
    private const double ParabolicGuard = 1e-8;

    public double SemiMajorAxis { get; }
    public double Eccentricity { get; }
    public double Inclination { get; }
    public double LongitudeOfAscendingNode { get; }
    public double ArgumentOfPeriapsis { get; }

    public double MeanAnomalyAtEpoch { get; }
    public double Epoch { get; }

    public double Mu { get; }

    private Orbit(double semiMajorAxis, double eccentricity, double inclination, double longitudeOfAscendingNode, double argumentOfPeriapsis, double meanAnomalyAtEpoch, double epoch, double mu) {

        SemiMajorAxis = semiMajorAxis;
        Eccentricity = eccentricity;
        Inclination = inclination;
        LongitudeOfAscendingNode = longitudeOfAscendingNode;
        ArgumentOfPeriapsis = argumentOfPeriapsis;

        MeanAnomalyAtEpoch = meanAnomalyAtEpoch;
        Epoch = epoch;

        Mu = mu;

    }

    public bool IsClosed => Eccentricity < 1.0;

    public double PeriapsisRadius => SemiMajorAxis * (1.0 - Eccentricity);
    public double ApoapsisRadius => IsClosed ? SemiMajorAxis * (1.0 + Eccentricity) : double.PositiveInfinity;

    public double MeanMotion => Math.Sqrt(Mu / Math.Abs(SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));
    public double Period => IsClosed ? Tau / MeanMotion : double.PositiveInfinity;

    public double SpecificEnergy => -Mu / (2.0 * SemiMajorAxis);

    public double SpeedAt(double radius) => Math.Sqrt(Mu * (2.0 / radius - 1.0 / SemiMajorAxis));

    /// <summary>Rotation carrying the perifocal frame onto the inertial one.</summary>
    public QuaternionD PerifocalToInertial => QuaternionD.FromAxisAngle(Vector3d.UnitZ, LongitudeOfAscendingNode) * QuaternionD.FromAxisAngle(Vector3d.UnitX, Inclination) * QuaternionD.FromAxisAngle(Vector3d.UnitZ, ArgumentOfPeriapsis);

    public double SemiLatusRectum => SemiMajorAxis * (1.0 - Eccentricity * Eccentricity);

    /// <summary>Unit normal of the orbital plane, along the angular momentum.</summary>
    public Vector3d PlaneNormal => PerifocalToInertial.Rotate(Vector3d.UnitZ);

    /// <summary>True anomaly where the vessel crosses the reference plane going north.</summary>
    public double AscendingNodeTrueAnomaly => Wrap(-ArgumentOfPeriapsis);

    public double DescendingNodeTrueAnomaly => Wrap(Math.PI - ArgumentOfPeriapsis);

    /// <summary>True anomaly the conic runs out at; pi for a closed orbit, the asymptote for an open one.</summary>
    public double TrueAnomalyLimit => IsClosed ? Math.PI : Math.Acos(-1.0 / Eccentricity);

    public double RadiusAtTrueAnomaly(double trueAnomaly) => SemiLatusRectum / (1.0 + Eccentricity * Math.Cos(trueAnomaly));

    public Vector3d PositionAtTrueAnomaly(double trueAnomaly) {

        double radius = RadiusAtTrueAnomaly(trueAnomaly);

        return PerifocalToInertial.Rotate(new Vector3d(radius * Math.Cos(trueAnomaly), radius * Math.Sin(trueAnomaly), 0.0));

    }

    public double MeanAnomalyAt(double time) => MeanAnomalyAtEpoch + MeanMotion * (time - Epoch);

    public double TrueAnomalyAt(double time) {

        if (IsClosed) {

            double eccentricAnomaly = SolveElliptical(Wrap(MeanAnomalyAt(time)), Eccentricity);

            return Wrap(2.0 * Math.Atan2(Math.Sqrt(1.0 + Eccentricity) * Math.Sin(eccentricAnomaly * 0.5), Math.Sqrt(1.0 - Eccentricity) * Math.Cos(eccentricAnomaly * 0.5)));

        }

        double hyperbolicAnomaly = SolveHyperbolic(MeanAnomalyAt(time), Eccentricity);

        return 2.0 * Math.Atan2(Math.Sqrt(Eccentricity + 1.0) * Math.Tanh(hyperbolicAnomaly * 0.5), Math.Sqrt(Eccentricity - 1.0));

    }

    /// <summary>Seconds until the vessel next reaches a true anomaly, or NaN where it never will again.</summary>
    public double TimeToTrueAnomaly(double time, double trueAnomaly) {

        double target = MeanAnomalyFromTrue(Wrap(trueAnomaly), Eccentricity);

        if (!IsClosed) {

            double ahead = (target - MeanAnomalyAt(time)) / MeanMotion;

            return ahead > 0.0 ? ahead : double.NaN;

        }

        return Wrap(target - Wrap(MeanAnomalyAt(time))) / MeanMotion;

    }

    public double TimeToPeriapsis(double time) => TimeToTrueAnomaly(time, 0.0);

    public double TimeToApoapsis(double time) => IsClosed ? TimeToTrueAnomaly(time, Math.PI) : double.PositiveInfinity;

    /// <summary>The orbit that results from adding an impulse at the given time.</summary>
    public Orbit WithImpulse(double time, Vector3d deltaV) {

        (Vector3d position, Vector3d velocity) = StateAt(time);

        return FromStateVectors(position, velocity + deltaV, Mu, time);

    }

    public static Orbit FromStateVectors(Vector3d position, Vector3d velocity, double mu, double epoch) {

        double radius = position.Length;
        double speedSquared = velocity.LengthSquared;
        double radialVelocity = Vector3d.Dot(position, velocity);

        Vector3d angularMomentum = Vector3d.Cross(position, velocity);
        Vector3d node = Vector3d.Cross(Vector3d.UnitZ, angularMomentum);

        Vector3d eccentricityVector = ((position * (speedSquared - mu / radius)) - (velocity * radialVelocity)) / mu;

        double eccentricity = eccentricityVector.Length;
        double energy = speedSquared * 0.5 - mu / radius;

        if (Math.Abs(eccentricity - 1.0) < ParabolicGuard) {

            eccentricity = energy < 0.0 ? 1.0 - ParabolicGuard : 1.0 + ParabolicGuard;

        }

        double semiMajorAxis = -mu / (2.0 * energy);

        double inclination = Math.Acos(Math.Clamp(angularMomentum.Z / angularMomentum.Length, -1.0, 1.0));

        double nodeLength = node.Length;

        double longitudeOfAscendingNode = 0.0;

        if (nodeLength > Degenerate) {

            longitudeOfAscendingNode = Math.Acos(Math.Clamp(node.X / nodeLength, -1.0, 1.0));

            if (node.Y < 0.0) {

                longitudeOfAscendingNode = Tau - longitudeOfAscendingNode;

            }

        }

        double argumentOfPeriapsis = 0.0;

        if (eccentricity > Degenerate && nodeLength > Degenerate) {

            argumentOfPeriapsis = Math.Acos(Math.Clamp(Vector3d.Dot(node, eccentricityVector) / (nodeLength * eccentricity), -1.0, 1.0));

            if (eccentricityVector.Z < 0.0) {

                argumentOfPeriapsis = Tau - argumentOfPeriapsis;

            }

        }
        else if (eccentricity > Degenerate) {

            argumentOfPeriapsis = Math.Atan2(eccentricityVector.Y, eccentricityVector.X);

            if (angularMomentum.Z < 0.0) {

                argumentOfPeriapsis = Tau - argumentOfPeriapsis;

            }

        }

        double trueAnomaly = TrueAnomalyFromState(position, radius, radialVelocity, eccentricityVector, eccentricity, node, nodeLength, angularMomentum);

        double meanAnomaly = MeanAnomalyFromTrue(trueAnomaly, eccentricity);

        return new Orbit(semiMajorAxis, eccentricity, inclination, Wrap(longitudeOfAscendingNode), Wrap(argumentOfPeriapsis), meanAnomaly, epoch, mu);

    }

    public (Vector3d Position, Vector3d Velocity) StateAt(double time) {

        double meanAnomaly = MeanAnomalyAtEpoch + MeanMotion * (time - Epoch);

        Vector3d perifocalPosition;
        Vector3d perifocalVelocity;

        if (IsClosed) {

            double eccentricAnomaly = SolveElliptical(Wrap(meanAnomaly), Eccentricity);

            double sine = Math.Sin(eccentricAnomaly);
            double cosine = Math.Cos(eccentricAnomaly);

            double radius = SemiMajorAxis * (1.0 - Eccentricity * cosine);
            double factor = Math.Sqrt(Mu * SemiMajorAxis) / radius;
            double eccentricityTerm = Math.Sqrt(1.0 - Eccentricity * Eccentricity);

            perifocalPosition = new Vector3d(SemiMajorAxis * (cosine - Eccentricity), SemiMajorAxis * eccentricityTerm * sine, 0.0);
            perifocalVelocity = new Vector3d(-factor * sine, factor * eccentricityTerm * cosine, 0.0);

        }
        else {

            double hyperbolicAnomaly = SolveHyperbolic(meanAnomaly, Eccentricity);

            double sine = Math.Sinh(hyperbolicAnomaly);
            double cosine = Math.Cosh(hyperbolicAnomaly);

            double radius = SemiMajorAxis * (1.0 - Eccentricity * cosine);
            double factor = Math.Sqrt(Mu * -SemiMajorAxis) / radius;
            double eccentricityTerm = Math.Sqrt(Eccentricity * Eccentricity - 1.0);

            perifocalPosition = new Vector3d(SemiMajorAxis * (cosine - Eccentricity), -SemiMajorAxis * eccentricityTerm * sine, 0.0);
            perifocalVelocity = new Vector3d(-factor * sine, factor * eccentricityTerm * cosine, 0.0);

        }

        QuaternionD toInertial = PerifocalToInertial;

        return (toInertial.Rotate(perifocalPosition), toInertial.Rotate(perifocalVelocity));

    }

    private static double TrueAnomalyFromState(Vector3d position, double radius, double radialVelocity, Vector3d eccentricityVector, double eccentricity, Vector3d node, double nodeLength, Vector3d angularMomentum) {

        if (eccentricity > Degenerate) {

            double anomaly = Math.Acos(Math.Clamp(Vector3d.Dot(eccentricityVector, position) / (eccentricity * radius), -1.0, 1.0));

            return radialVelocity < 0.0 ? Tau - anomaly : anomaly;

        }

        if (nodeLength > Degenerate) {

            double latitude = Math.Acos(Math.Clamp(Vector3d.Dot(node, position) / (nodeLength * radius), -1.0, 1.0));

            return position.Z < 0.0 ? Tau - latitude : latitude;

        }

        double longitude = Math.Acos(Math.Clamp(position.X / radius, -1.0, 1.0));

        if (position.Y < 0.0) {

            longitude = Tau - longitude;

        }

        return angularMomentum.Z < 0.0 ? Tau - longitude : longitude;

    }

    private static double MeanAnomalyFromTrue(double trueAnomaly, double eccentricity) {

        if (eccentricity < 1.0) {

            double eccentricAnomaly = Math.Atan2(Math.Sqrt(1.0 - eccentricity * eccentricity) * Math.Sin(trueAnomaly), eccentricity + Math.Cos(trueAnomaly));

            return Wrap(eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly));

        }

        double hyperbolicAnomaly = Math.Asinh(Math.Sqrt(eccentricity * eccentricity - 1.0) * Math.Sin(trueAnomaly) / (1.0 + eccentricity * Math.Cos(trueAnomaly)));

        return eccentricity * Math.Sinh(hyperbolicAnomaly) - hyperbolicAnomaly;

    }

    private static double SolveElliptical(double meanAnomaly, double eccentricity) {

        double anomaly = eccentricity < 0.8 ? meanAnomaly : Math.PI;

        for (int iteration = 0; iteration < 64; iteration++) {

            double delta = (anomaly - eccentricity * Math.Sin(anomaly) - meanAnomaly) / (1.0 - eccentricity * Math.Cos(anomaly));

            anomaly -= delta;

            if (Math.Abs(delta) < 1e-14) {

                break;

            }

        }

        return anomaly;

    }

    private static double SolveHyperbolic(double meanAnomaly, double eccentricity) {

        double anomaly = Math.Asinh(meanAnomaly / eccentricity);

        for (int iteration = 0; iteration < 128; iteration++) {

            double delta = (eccentricity * Math.Sinh(anomaly) - anomaly - meanAnomaly) / (eccentricity * Math.Cosh(anomaly) - 1.0);

            anomaly -= delta;

            if (Math.Abs(delta) < 1e-14) {

                break;

            }

        }

        return anomaly;

    }

    private static double Wrap(double radians) {

        double wrapped = radians % Tau;

        return wrapped < 0.0 ? wrapped + Tau : wrapped;

    }

}
