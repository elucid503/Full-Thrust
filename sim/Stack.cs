namespace FullThrust.Sim;

/// <summary>The flight vehicle as it is flown: an Aegis capsule on a Meridian service stage. One
/// place assembles it, so nothing else has to know how many stages there are or where they sit.</summary>
public static class Stack {

    public static Vessel Build() {

        Vessel vessel = new Vessel("Aegis", new[] {

            Meridian.BuildStage(),
            Aegis.BuildStage(Meridian.PayloadDatum),

        });

        return vessel;

    }

}
