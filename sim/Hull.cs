using System.Collections.Generic;

namespace FullThrust.Sim;

/// <summary>Mass, axial centre of mass and principal moments of inertia of one component.</summary>
public readonly struct MassProperties {

    public static readonly MassProperties Empty = new MassProperties(0.0, 0.0, Vector3d.Zero);

    public double Mass { get; }

    /// <summary>Distance of the centre of mass from the hull datum, along the nose axis.</summary>
    public double CentreZ { get; }

    /// <summary>Principal moments about the centre of mass, with Z the nose axis.</summary>
    public Vector3d Inertia { get; }

    public MassProperties(double mass, double centreZ, Vector3d inertia) {

        Mass = mass;
        CentreZ = centreZ;
        Inertia = inertia;

    }

    public static MassProperties Combine(MassProperties a, MassProperties b) {

        double mass = a.Mass + b.Mass;

        if (mass <= 0.0) {

            return Empty;

        }

        double centre = (a.Mass * a.CentreZ + b.Mass * b.CentreZ) / mass;

        double offsetA = a.CentreZ - centre;
        double offsetB = b.CentreZ - centre;

        double transverse = a.Inertia.X + a.Mass * offsetA * offsetA + b.Inertia.X + b.Mass * offsetB * offsetB;
        double axial = a.Inertia.Z + b.Inertia.Z;

        return new MassProperties(mass, centre, new Vector3d(transverse, transverse, axial));

    }

}

/// <summary>Surface of revolution about the nose; renderer and mass share these stations.</summary>
public sealed class Hull {

    /// <summary>Profile control point: a radius at a distance along the nose axis from the tail.</summary>
    public readonly struct Station {

        public double Z { get; }
        public double Radius { get; }

        public Station(double z, double radius) {

            Z = z;
            Radius = radius;

        }

    }

    // Ring/disc formulae need constant radius, so slices stay short enough the profile is nearly cylindrical.
    private const double SliceLength = 0.02;

    private readonly Station[] _stations;

    public IReadOnlyList<Station> Stations => _stations;

    public double TankBottom { get; }
    public double TankTop { get; }

    /// <summary>Structural wall standing inside the mould line. The lathe and the cross-section
    /// diagram both draw the inner surface off it, so it is carried here rather than in either.</summary>
    public double WallThickness { get; init; } = 0.055;

    public Hull(Station[] stations, double tankBottom, double tankTop) {

        if (stations == null || stations.Length < 2) {

            throw new ArgumentException("a hull needs at least two stations", nameof(stations));

        }

        if (tankTop <= tankBottom) {

            throw new ArgumentOutOfRangeException(nameof(tankTop), "the tank must span a positive length");

        }

        _stations = stations;

        TankBottom = tankBottom;
        TankTop = tankTop;

    }

    public double Base => _stations[0].Z;
    public double Tip => _stations[_stations.Length - 1].Z;

    public double Length => Tip - Base;

    public double MaxRadius {

        get {

            double maximum = 0.0;

            foreach (Station station in _stations) {

                maximum = Math.Max(maximum, station.Radius);

            }

            return maximum;

        }

    }

    public double Volume => Sweep(Base, Tip, false).Measure;

    public double TankVolume => Column[Column.Length - 1].Measure;

    /// <summary>Swept area of the mould line over a span; dry mass divides by this, so a run of it carries its share.</summary>
    public double ShellArea(double low, double high) => Sweep(Math.Max(low, Base), Math.Min(high, Tip), true).Measure;

    /// <summary>Radius of the mould line at a station, linear between the two it falls between.</summary>
    public double RadiusAt(double z) {

        int last = _stations.Length - 1;

        if (z <= _stations[0].Z) {

            return _stations[0].Radius;

        }

        if (z >= _stations[last].Z) {

            return _stations[last].Radius;

        }

        int low = 0;
        int high = last;

        // Bisection rather than a scan: the mass sweep and the aerodynamic panels both call this
        // once per slice, and a hull carries dozens of stations.
        while (high - low > 1) {

            int middle = (low + high) >> 1;

            if (_stations[middle].Z <= z) {

                low = middle;

            }
            else {

                high = middle;

            }

        }

        double span = _stations[high].Z - _stations[low].Z;

        if (span <= 0.0) {

            return _stations[high].Radius;

        }

        return _stations[low].Radius + (_stations[high].Radius - _stations[low].Radius) * ((z - _stations[low].Z) / span);

    }

    // The mould line does not move, so the swept shell is the same integral every time it is asked
    // for and the answer only scales with the mass hung on it.
    private Accumulation _shell;
    private bool _swept;

    private Accumulation Shell {

        get {

            if (!_swept) {

                _shell = Sweep(Base, Tip, true);
                _swept = true;

            }

            return _shell;

        }

    }

    /// <summary>Dry structure, taken as a shell of uniform areal density over the whole mould line.</summary>
    public MassProperties Structure(double mass) {

        if (mass <= 0.0) {

            return MassProperties.Empty;

        }

        return Assemble(mass, Shell, (Base + Tip) * 0.5);

    }

    /// <summary>Propellant as a solid column filling the tank from the bottom up.</summary>
    public MassProperties Propellant(double mass, double fillFraction) {

        if (mass <= 0.0 || fillFraction <= 0.0) {

            return MassProperties.Empty;

        }

        return Assemble(mass, Filled(Math.Min(fillFraction, 1.0)), TankBottom);

    }

    private static MassProperties Assemble(double mass, Accumulation sweep, double fallbackCentre) {

        if (sweep.Measure <= 0.0) {

            return new MassProperties(mass, fallbackCentre, Vector3d.Zero);

        }

        double centre = sweep.FirstMoment / sweep.Measure;
        double density = mass / sweep.Measure;

        double axial = sweep.RadialMoment * density;

        // Thin ring/disc: transverse inertia is half the axial plus the mass spread about the centre.
        double spread = (sweep.SecondMoment - sweep.Measure * centre * centre) * density;

        double transverse = axial * 0.5 + spread;

        return new MassProperties(mass, centre, new Vector3d(transverse, transverse, axial));

    }

    private readonly struct Accumulation {

        public double Measure { get; }
        public double FirstMoment { get; }
        public double SecondMoment { get; }
        public double RadialMoment { get; }

        public Accumulation(double measure, double firstMoment, double secondMoment, double radialMoment) {

            Measure = measure;
            FirstMoment = firstMoment;
            SecondMoment = secondMoment;
            RadialMoment = radialMoment;

        }

        public Accumulation Plus(Accumulation other) {

            return new Accumulation(Measure + other.Measure, FirstMoment + other.FirstMoment,
                SecondMoment + other.SecondMoment, RadialMoment + other.RadialMoment);

        }

        public Accumulation Towards(Accumulation other, double fraction) {

            return new Accumulation(

                Measure + (other.Measure - Measure) * fraction,
                FirstMoment + (other.FirstMoment - FirstMoment) * fraction,
                SecondMoment + (other.SecondMoment - SecondMoment) * fraction,
                RadialMoment + (other.RadialMoment - RadialMoment) * fraction

            );

        }

    }

    private Accumulation Sweep(double low, double high, bool shell) {

        if (high <= low) {

            return new Accumulation(0.0, 0.0, 0.0, 0.0);

        }

        int slices = Math.Max(1, (int)Math.Ceiling((high - low) / SliceLength));

        double step = (high - low) / slices;

        double measure = 0.0;
        double firstMoment = 0.0;
        double secondMoment = 0.0;
        double radialMoment = 0.0;

        for (int index = 0; index < slices; index++) {

            double bottom = low + step * index;
            double top = bottom + step;

            double lowerRadius = RadiusAt(bottom);
            double upperRadius = RadiusAt(top);

            double middle = (bottom + top) * 0.5;
            double radius = (lowerRadius + upperRadius) * 0.5;

            double slice;
            double radial;

            if (shell) {

                double slant = Math.Sqrt(step * step + (upperRadius - lowerRadius) * (upperRadius - lowerRadius));

                slice = Math.PI * (lowerRadius + upperRadius) * slant;
                radial = radius * radius;

            }
            else {

                slice = Math.PI / 3.0 * (lowerRadius * lowerRadius + lowerRadius * upperRadius + upperRadius * upperRadius) * step;
                radial = radius * radius * 0.5;

            }

            measure += slice;
            firstMoment += slice * middle;
            radialMoment += slice * radial;

            // Second moment of the slice itself, not of a point at its centre, so the sum stays exact.
            secondMoment += slice * (middle * middle + step * step / 12.0);

        }

        return new Accumulation(measure, firstMoment, secondMoment, radialMoment);

    }

    // Every integral over the tank, accumulated slice by slice from the bottom. Both the fill
    // height and the propellant's mass properties are readings off this table rather than sweeps of
    // their own: they are wanted for every stage on every step of the integrator, and a bisection
    // that re-swept the tank forty-eight times to answer one of them was the whole cost of a frame.
    private Accumulation[] _column;

    private Accumulation[] Column {

        get {

            if (_column == null) {

                _column = Accumulate();

            }

            return _column;

        }

    }

    private Accumulation[] Accumulate() {

        int slices = Math.Max(1, (int)Math.Ceiling((TankTop - TankBottom) / SliceLength));

        Accumulation[] column = new Accumulation[slices + 1];

        column[0] = new Accumulation(0.0, 0.0, 0.0, 0.0);

        for (int index = 0; index < slices; index++) {

            double bottom = TankBottom + (TankTop - TankBottom) * index / slices;
            double top = TankBottom + (TankTop - TankBottom) * (index + 1) / slices;

            column[index + 1] = column[index].Plus(Sweep(bottom, top, false));

        }

        return column;

    }

    /// <summary>Height the propellant surface stands at for a given fill, measured from the datum.</summary>
    public double FillHeight(double fillFraction) {

        Accumulation[] column = Column;

        double capacity = column[column.Length - 1].Measure;

        if (capacity <= 0.0) {

            return TankBottom;

        }

        return Surface(column, capacity * Math.Clamp(fillFraction, 0.0, 1.0), out _);

    }

    // Everything under the propellant's surface, interpolated between the two slice boundaries the
    // surface falls between. A slice is two centimetres, so the interpolation is exact to the
    // thickness of the paint.
    private Accumulation Filled(double fillFraction) {

        Accumulation[] column = Column;

        double capacity = column[column.Length - 1].Measure;

        if (capacity <= 0.0) {

            return new Accumulation(0.0, 0.0, 0.0, 0.0);

        }

        Surface(column, capacity * fillFraction, out Accumulation under);

        return under;

    }

    private double Surface(Accumulation[] column, double wanted, out Accumulation under) {

        int low = 0;
        int high = column.Length - 1;

        while (high - low > 1) {

            int middle = (low + high) >> 1;

            if (column[middle].Measure <= wanted) {

                low = middle;

            }
            else {

                high = middle;

            }

        }

        double span = column[high].Measure - column[low].Measure;

        double fraction = span > 0.0 ? (wanted - column[low].Measure) / span : 0.0;

        under = column[low].Towards(column[high], fraction);

        double step = (TankTop - TankBottom) / (column.Length - 1);

        return TankBottom + step * (low + fraction);

    }

}
