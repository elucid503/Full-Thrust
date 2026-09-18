namespace FullThrust.Sim;

/// <summary>The flight vehicle as it is flown, tail to nose: a Zenith first stage, a Meridian
/// service stage on top of it and an Aegis capsule on top of that. One place assembles it, so
/// nothing else has to know how many stages there are or where they sit.</summary>
public static class Stack {

    /// <summary>Where the Meridian's own datum lands once it is stacked.</summary>
    public const double ServiceDatum = Zenith.PayloadDatum;

    /// <summary>And where the capsule's does.</summary>
    public const double CapsuleDatum = ServiceDatum + Meridian.PayloadDatum;

    public static Vessel Build() {

        Vessel vessel = new Vessel("Aegis", new[] {

            Zenith.BuildStage(),
            Meridian.BuildStage(ServiceDatum),
            Aegis.BuildStage(CapsuleDatum),

        });

        return vessel;

    }

}
