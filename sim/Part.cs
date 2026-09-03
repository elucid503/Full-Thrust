namespace FullThrust.Sim;

/// <summary>Broad class of a part, for anything that has to group or draw parts without naming them.</summary>
public enum PartKind {

    Structure,
    Tank,
    Engine,
    Thruster,

}

/// <summary>One named piece of a vessel, spanning a run of the nose axis from the hull datum.</summary>
public sealed class Part {

    public string Name { get; init; }

    public PartKind Kind { get; init; }

    public double Bottom { get; init; }
    public double Top { get; init; }

    /// <summary>How many are fitted, spaced evenly about the nose axis.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Radius of the ring the copies sit on; zero when the part is on the axis.</summary>
    public double RingRadius { get; init; }

    /// <summary>The part's own outline: half-widths along the nose axis, on the same datum as Bottom
    /// and Top. Null means the part is a run of the mould line and the hull profile is its outline.</summary>
    public Hull.Station[] Profile { get; init; }

    public double Length => Top - Bottom;

    public double Centre => (Bottom + Top) * 0.5;

    /// <summary>True when the part is the hull itself over this span rather than hardware bolted to it.</summary>
    public bool IsMouldLine => Profile == null;

    /// <summary>Widest the part's own outline gets. Zero for a run of the mould line.</summary>
    public double Extent {

        get {

            double widest = 0.0;

            if (Profile != null) {

                foreach (Hull.Station station in Profile) {

                    widest = Math.Max(widest, station.Radius);

                }

            }

            return widest;

        }

    }

}
