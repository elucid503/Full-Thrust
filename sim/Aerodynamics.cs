using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>The mould line reduced to the handful of integrals aerodynamics actually needs. Built
/// once per vessel, so a force evaluation costs no geometry at all.</summary>
public sealed class AeroProfile {

    // Fine enough that a nose cap a few centimetres deep still lands on several slices.
    private const int Slices = 480;

    /// <summary>Datum ends of the mould line, along the nose axis.</summary>
    public double Base { get; }
    public double Tip { get; }

    public double Length => Tip - Base;

    public double MaxRadius { get; }

    /// <summary>Frontal area every coefficient here is quoted on.</summary>
    public double ReferenceArea { get; }

    public double BaseArea { get; }
    public double TipArea { get; }

    public double Volume { get; }

    /// <summary>Side-on area, which is what a crossflow acts on.</summary>
    public double PlanformArea { get; }

    /// <summary>Centroid of the planform, where the crossflow load is taken to act.</summary>
    public double PlanformCentre { get; }

    /// <summary>Where slender-body theory puts the potential normal force: the centroid of the
    /// cross-sectional area distribution, which for a cone is a third of the way up from its base.</summary>
    public double PotentialCentre { get; }

    /// <summary>Radius of curvature of the mould line at each end. It sets both the stagnation
    /// heating and where the pressure on that end acts. Fitted to the profile, not declared.</summary>
    public double BaseCurvature { get; }
    public double TipCurvature { get; }

    /// <summary>Centre of curvature of each end: every element of pressure on a spherical face
    /// points at it, so the whole resultant's line of action runs through it.</summary>
    public double BaseFaceCentre => Base + BaseCurvature;
    public double TipFaceCentre => Tip - TipCurvature;

    /// <summary>How much of an end's load turns with the flow rather than staying on the axis.
    /// One for a hemisphere, nearly nothing for a face that is almost flat.</summary>
    public double BaseTilt { get; }
    public double TipTilt { get; }

    private AeroProfile(double low, double high, Func<double, double> radiusAt) {

        Base = low;
        Tip = high;

        double step = (high - low) / Slices;

        double widest = 0.0;
        double volume = 0.0;
        double planform = 0.0;
        double planformMoment = 0.0;

        for (int index = 0; index < Slices; index++) {

            double bottom = low + step * index;
            double top = bottom + step;

            double lower = radiusAt(bottom);
            double upper = radiusAt(top);

            widest = Math.Max(widest, Math.Max(lower, upper));

            volume += Math.PI / 3.0 * (lower * lower + lower * upper + upper * upper) * step;

            double side = (lower + upper) * step;

            planform += side;
            planformMoment += side * (bottom + top) * 0.5;

        }

        MaxRadius = widest;
        ReferenceArea = Math.PI * widest * widest;

        BaseArea = Math.PI * radiusAt(low) * radiusAt(low);
        TipArea = Math.PI * radiusAt(high) * radiusAt(high);

        Volume = volume;

        PlanformArea = planform;
        PlanformCentre = planform > 0.0 ? planformMoment / planform : (low + high) * 0.5;

        // Integrating by parts: the first moment of the area distribution is the area at the ends
        // less the volume between them. A cylinder has no distribution at all, so it falls back.
        double spread = TipArea - BaseArea;

        PotentialCentre = Math.Abs(spread) > 1e-9
            ? (high * TipArea - low * BaseArea - volume) / spread
            : PlanformCentre;

        BaseCurvature = Curvature(radiusAt, low, high, radiusAt(low));
        TipCurvature = Curvature(radiusAt, high, low, radiusAt(high));

        BaseTilt = Tilt(BaseCurvature);
        TipTilt = Tilt(TipCurvature);

    }

    /// <summary>Builds the profile from a mould line, sampled rather than listed, so any hull the
    /// lathe can turn has an aerodynamic shape without declaring one.</summary>
    public static AeroProfile Build(double low, double high, Func<double, double> radiusAt) {

        if (high <= low) {

            throw new ArgumentOutOfRangeException(nameof(high), "a profile needs a positive length");

        }

        return new AeroProfile(low, high, radiusAt);

    }

    /// <summary>Circle fitted through three stations at one end of the profile. A spherical cap
    /// returns its own radius exactly; a flat end is collinear and falls back to its own width.</summary>
    private double Curvature(Func<double, double> radiusAt, double edge, double away, double endRadius) {

        double step = (away - edge) * 0.004;

        double fallback = Math.Max(endRadius * 1.3, 0.02);

        double z0 = edge;
        double z1 = edge + step;
        double z2 = edge + step * 2.0;

        double r0 = radiusAt(z0);
        double r1 = radiusAt(z1);
        double r2 = radiusAt(z2);

        double area = (z1 - z0) * (r2 - r0) - (z2 - z0) * (r1 - r0);

        if (Math.Abs(area) < 1e-12) {

            return fallback;

        }

        double a = Math.Sqrt((z1 - z0) * (z1 - z0) + (r1 - r0) * (r1 - r0));
        double b = Math.Sqrt((z2 - z1) * (z2 - z1) + (r2 - r1) * (r2 - r1));
        double c = Math.Sqrt((z2 - z0) * (z2 - z0) + (r2 - r0) * (r2 - r0));

        double radius = a * b * c / (2.0 * Math.Abs(area));

        // A fit far larger than the body is a nearly flat end read as a huge sphere; a flat end
        // stagnates like a disc of its own width, not like a mile-wide one.
        return radius > MaxRadius * 6.0 || double.IsNaN(radius) ? fallback : Math.Max(radius, 0.02);

    }

    /// <summary>Newtonian pressure on a spherical face, integrated to first order in incidence:
    /// the share of the load that leans with the flow. It falls to nothing as the face flattens,
    /// which is why a flat base raises no restoring moment and a dished one raises a great deal.</summary>
    private double Tilt(double curvature) {

        double sine = Math.Min(MaxRadius / curvature, 1.0);
        double cosine = Math.Sqrt(Math.Max(1.0 - sine * sine, 0.0));

        double wetted = 1.0 - cosine * cosine * cosine * cosine;

        return wetted > 1e-9 ? sine * sine * sine * sine / wetted : 0.0;

    }

}

/// <summary>What the air is doing to a vessel this instant: the loads, and the figures a pilot
/// reads them by.</summary>
public readonly struct AeroForces {

    /// <summary>Force in world axes.</summary>
    public Vector3d Force { get; }

    /// <summary>Moment about the centre of mass, in body axes, which is the frame the rigid body
    /// integrates in.</summary>
    public Vector3d Torque { get; }

    public double Density { get; }
    public double AirSpeed { get; }

    public double DynamicPressure { get; }
    public double Mach { get; }

    /// <summary>Angle between the nose and the air the vessel is flying through, radians. Zero is
    /// nose first and pi is base first.</summary>
    public double AngleOfAttack { get; }

    /// <summary>Where the transverse load acts, on the same datum as the hull.</summary>
    public double CentreOfPressure { get; }

    /// <summary>Convective heating at the stagnation point, watts per square metre.</summary>
    public double HeatFlux { get; }

    public AeroForces(Vector3d force, Vector3d torque, double density, double airSpeed, double dynamicPressure, double mach, double angleOfAttack, double centreOfPressure, double heatFlux) {

        Force = force;
        Torque = torque;

        Density = density;
        AirSpeed = airSpeed;

        DynamicPressure = dynamicPressure;
        Mach = mach;

        AngleOfAttack = angleOfAttack;
        CentreOfPressure = centreOfPressure;

        HeatFlux = heatFlux;

    }

    public double Drag => Force.Length;

    public bool InAir => Density > 0.0;

}

/// <summary>Loads on a body of revolution: Newtonian pressure on whichever end is into the flow,
/// slender-body lift off the run where the cross-section changes, and Allen and Perkins' crossflow
/// over the side of it. Every figure is read off the mould line, so a hull nobody has flown before
/// has a lift curve, a drag curve and a centre of pressure without being given any of them.</summary>
public static class Aerodynamics {

    /// <summary>Sutton and Graves' constant for air, SI. Stagnation flux goes as the root of
    /// density over nose radius and the cube of speed, and this is the coefficient in front.</summary>
    public const double SuttonGraves = 1.7415e-4;

    // Not all of the crossflow a cylinder would see reaches a body of finite length; the standard
    // fineness-ratio correction for a slender stage sits about here.
    private const double CrossflowEfficiency = 0.70;

    // How much of the speed has to be axial before the flow is treated as coming over one end
    // rather than the other. Wide enough that a vehicle passing through side-on flight crosses
    // between the two curves smoothly instead of stepping.
    private const double EndBlend = 0.15;

    /// <summary>Axial drag on the frontal area with the pointed end into the flow: the subsonic
    /// floor, the transonic rise, and the supersonic tail.</summary>
    private static readonly (double Mach, double Coefficient)[] ForeDrag = {

        (0.0, 0.20),
        (0.80, 0.22),
        (1.00, 0.38),
        (1.20, 0.46),
        (2.00, 0.34),
        (4.00, 0.25),
        (8.00, 0.21),

    };

    /// <summary>The same, blunt end first. A flat or dished base does not shed its wave drag as the
    /// Mach number climbs, which is the whole reason a capsule is shaped like one.</summary>
    private static readonly (double Mach, double Coefficient)[] BaseDrag = {

        (0.0, 1.05),
        (0.80, 1.12),
        (1.20, 1.42),
        (2.00, 1.55),
        (4.00, 1.60),
        (8.00, 1.62),

    };

    /// <summary>Drag of a circular cylinder to a flow across it, which is what the viscous part of
    /// the normal force is built on.</summary>
    private static readonly (double Mach, double Coefficient)[] CrossDrag = {

        (0.0, 1.20),
        (0.80, 1.25),
        (1.20, 1.60),
        (2.00, 1.45),
        (4.00, 1.30),
        (8.00, 1.25),

    };

    /// <summary>The air as one station of the body sees it. Taken at the point a load acts rather
    /// than at the centre of mass, so a vehicle that is already turning meets a flow that has
    /// turned with it - which is the whole of aerodynamic damping.</summary>
    private readonly struct Incidence {

        public double Axial { get; }
        public double Across { get; }

        public double Alpha { get; }

        /// <summary>Unit vector along the crossflow, or zero where there is none.</summary>
        public Vector3d Sideways { get; }

        public double Pressure { get; }

        public Incidence(Vector3d flow, double density) {

            Axial = flow.Z;

            Vector3d sideways = new Vector3d(flow.X, flow.Y, 0.0);

            Across = sideways.Length;

            Alpha = Math.Atan2(Across, Axial);

            Sideways = Across > 0.0 ? sideways / Across : Vector3d.Zero;

            Pressure = 0.5 * density * flow.LengthSquared;

        }

    }

    /// <summary>Loads on a vessel at a state, or nothing at all where there is no air to speak of.</summary>
    public static AeroForces Compute(Vessel vessel, CelestialBody body, Vector3d position, Vector3d velocity) {

        if (!body.HasAtmosphere || vessel.Profile == null) {

            return default;

        }

        double altitude = body.AltitudeOf(position);
        double density = body.Atmosphere.DensityAt(altitude);

        if (density <= 0.0) {

            return default;

        }

        Vector3d through = velocity - body.AirVelocityAt(position);

        double speed = through.Length;

        if (speed <= 0.0) {

            return default;

        }

        AeroProfile profile = vessel.Profile;

        Vector3d local = vessel.Orientation.Conjugate.Rotate(through);
        Vector3d spin = vessel.AngularVelocity;

        double centreOfMass = vessel.CentreOfMassZ;

        double mach = speed / body.Atmosphere.SpeedOfSoundAt(altitude);

        Incidence datum = new Incidence(local, density);

        // Which end is into the flow decides which drag curve applies, how blunt the stagnation
        // point is, and where the pressure on that end acts.
        double forward = Blend(datum.Axial / speed);

        double axialCoefficient = Mix(Sample(BaseDrag, mach), Sample(ForeDrag, mach), forward);

        double faceCentre = Mix(profile.BaseFaceCentre, profile.TipFaceCentre, forward);
        double faceTilt = Mix(profile.BaseTilt, profile.TipTilt, forward);

        Vector3d force = Vector3d.Zero;
        Vector3d torque = Vector3d.Zero;

        double transverse = 0.0;
        double moment = 0.0;

        Incidence face = At(local, spin, faceCentre - centreOfMass, density);

        double faceForce = 0.5 * density * face.Axial * face.Axial * profile.ReferenceArea * axialCoefficient;

        Vector3d faceLoad = new Vector3d(0.0, 0.0, face.Axial > 0.0 ? -faceForce : faceForce)
            - face.Sideways * (faceTilt * faceForce * Math.Sin(face.Alpha));

        Apply(faceCentre - centreOfMass, faceLoad, ref force, ref torque, ref transverse, ref moment, faceCentre);

        Incidence potentialAt = At(local, spin, profile.PotentialCentre - centreOfMass, density);

        // Attached flow off a pointed end only: past ninety degrees there is no potential lift left
        // to collect, which is what leaves a capsule with nothing but its crossflow and its face.
        double potential = Math.Max(potentialAt.Pressure * (profile.BaseArea - profile.TipArea) * Math.Sin(2.0 * potentialAt.Alpha) * Math.Cos(potentialAt.Alpha * 0.5), 0.0);

        Apply(profile.PotentialCentre - centreOfMass, potentialAt.Sideways * -potential, ref force, ref torque, ref transverse, ref moment, profile.PotentialCentre);

        Incidence planformAt = At(local, spin, profile.PlanformCentre - centreOfMass, density);

        double crossflow = planformAt.Pressure * CrossflowEfficiency * Sample(CrossDrag, mach) * profile.PlanformArea * Math.Sin(planformAt.Alpha) * Math.Sin(planformAt.Alpha);

        Apply(profile.PlanformCentre - centreOfMass, planformAt.Sideways * -crossflow, ref force, ref torque, ref transverse, ref moment, profile.PlanformCentre);

        double bluntness = Mix(profile.BaseCurvature, profile.TipCurvature, forward);

        double flux = SuttonGraves * Math.Sqrt(density / bluntness) * speed * speed * speed;

        double centreOfPressure = transverse > 0.0 ? moment / transverse : profile.PlanformCentre;

        return new AeroForces(

            vessel.Orientation.Rotate(force),
            torque,

            density,
            speed,

            datum.Pressure,
            mach,

            datum.Alpha,
            centreOfPressure,

            flux

        );

    }

    public static AeroForces Compute(Vessel vessel, CelestialBody body) => Compute(vessel, body, vessel.Position, vessel.Velocity);

    private static Incidence At(Vector3d local, Vector3d spin, double arm, double density) {

        // The spin's contribution at a station on the axis, written out rather than crossed: the
        // arm has no transverse part, so two of the three terms are zero.
        return new Incidence(local + new Vector3d(spin.Y * arm, -spin.X * arm, 0.0), density);

    }

    private static void Apply(double arm, Vector3d load, ref Vector3d force, ref Vector3d torque, ref double transverse, ref double moment, double station) {

        force += load;

        torque += new Vector3d(-arm * load.Y, arm * load.X, 0.0);

        double sideways = Math.Sqrt(load.X * load.X + load.Y * load.Y);

        transverse += sideways;
        moment += sideways * station;

    }

    /// <summary>Smooth step from base-first at minus one to nose-first at plus one.</summary>
    private static double Blend(double cosine) {

        double t = Math.Clamp((cosine + EndBlend) / (EndBlend * 2.0), 0.0, 1.0);

        return t * t * (3.0 - 2.0 * t);

    }

    private static double Mix(double aft, double fore, double forward) => aft + (fore - aft) * forward;

    /// <summary>A coefficient off its curve, linear between the tabulated Mach numbers and flat
    /// beyond either end of them.</summary>
    private static double Sample(IReadOnlyList<(double Mach, double Coefficient)> curve, double mach) {

        if (mach <= curve[0].Mach) {

            return curve[0].Coefficient;

        }

        for (int index = 1; index < curve.Count; index++) {

            if (mach > curve[index].Mach) {

                continue;

            }

            (double lowMach, double low) = curve[index - 1];
            (double highMach, double high) = curve[index];

            return low + (high - low) * (mach - lowMach) / (highMach - lowMach);

        }

        return curve[curve.Count - 1].Coefficient;

    }

}
