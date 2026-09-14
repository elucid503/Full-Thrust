using System;
using System.Collections.Generic;
using System.Reflection;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class VehicleMountChecks : Node {

    public override void _Ready() {

        try {

            MethodInfo mounts = typeof(VesselView).GetMethod("Mounts", BindingFlags.NonPublic | BindingFlags.Static);
            int count = 0;
            foreach (double datum in new[] { 0.0, 22.5 }) {

                Stage stage = Meridian.BuildStage(datum);
                foreach (Part part in stage.Parts) {

                    if (part.Kind != PartKind.Thruster) { continue; }
                    var placements = (IEnumerable<(Vector3 Position, Vector3 Axis, Vector3 Side, float Scale)>)mounts.Invoke(null, new object[] { stage, part });
                    int nozzles = 0;
                    foreach (var mount in placements) {

                        Vector3 foot = mount.Position - mount.Axis * (0.126f * mount.Scale);
                        Vector3 lip = mount.Position + mount.Axis * (0.135f * mount.Scale);
                        float footRadius = new Vector2(foot.X, foot.Z).Length();
                        float lipRadius = new Vector2(lip.X, lip.Z).Length();
                        double inset = stage.Hull.RadiusAt(foot.Y) - footRadius;
                        if (inset < 0.31 || inset > 0.33) { throw new InvalidOperationException("RCS chamber must sit at the recessed port floor"); }
                        if (lipRadius > stage.Hull.RadiusAt(lip.Y) - 0.05 || lipRadius < footRadius + 0.15) {

                            throw new InvalidOperationException("RCS emitter must stay inside the port, forward of its backing");

                        }
                        Vector3 radial = new Vector3(foot.X, 0, foot.Z).Normalized();
                        if (mount.Axis.Dot(radial) < 0.7f || Math.Abs(mount.Axis.Dot(mount.Side)) > 0.001f) {

                            throw new InvalidOperationException("RCS mount must face outward in an orthogonal frame");

                        }
                        if (Math.Sign(mount.Axis.Y) != (nozzles % 2 == 0 ? -1 : 1)) { throw new InvalidOperationException("RCS pair must cant in opposite axial directions"); }
                        nozzles++;
                        count++;

                    }
                    if (nozzles != part.Count * 2) { throw new InvalidOperationException("Missing RCS nozzle"); }

                }

            }
            GD.Print($"Vehicle mounts: {count} recessed, outward-facing nozzle placements passed");
            GetTree().Quit();

        } catch (Exception exception) {

            GD.PushError(exception.ToString());
            GetTree().Quit(1);

        }

    }

}
