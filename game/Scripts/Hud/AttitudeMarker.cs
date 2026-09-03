using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The glyph and the wording for one attitude reference. The ball and the menu share both.</summary>
public static class AttitudeMarker {

    private const float Ring = 0.62f;
    private const float Spoke = 0.42f;

    /// <summary>The references the menu offers, in the order they are laid out.</summary>
    public static readonly AttitudeHold[] Selectable = {

        AttitudeHold.Stability,

        AttitudeHold.Prograde,
        AttitudeHold.Retrograde,

        // One of the pair only. Anti-normal is a plane change like its opposite and reachable from
        // the keyboard, but carrying both left the grid a row short of filling its second column.
        AttitudeHold.Normal,

        AttitudeHold.RadialOut,
        AttitudeHold.RadialIn,

        AttitudeHold.Maneuver,

    };

    /// <summary>What the ball marks: the offered references, plus whatever is actually being flown,
    /// so a hold reached from the keyboard still shows the pilot where it is pointing.</summary>
    public static IEnumerable<AttitudeHold> Marked(AttitudeHold active) {

        foreach (AttitudeHold hold in Selectable) {

            yield return hold;

        }

        if (Array.IndexOf(Selectable, active) < 0) {

            yield return active;

        }

    }

    public static string Name(AttitudeHold hold) {

        return hold switch {

            AttitudeHold.Stability => "Hold",

            AttitudeHold.Prograde => "Prograde",
            AttitudeHold.Retrograde => "Retrograde",

            AttitudeHold.Normal => "Normal",
            AttitudeHold.Antinormal => "Anti-normal",

            AttitudeHold.RadialOut => "Radial Out",
            AttitudeHold.RadialIn => "Radial In",

            AttitudeHold.Maneuver => "Node",

            _ => "Free",

        };

    }

    /// <summary>Whether a reference can be flown right now; the node one only exists once a node does.</summary>
    public static bool Available(AttitudeHold hold, Flight flight) {

        return hold != AttitudeHold.Maneuver || (flight.Node != null && !flight.Node.IsEmpty);

    }

    /// <summary>Which reference a marker on the ball stands for, given the frame it is drawn in.</summary>
    public static Vector3d Direction(AttitudeHold hold, Flight flight) {

        return Autopilot.Reference(hold, flight.Vessel.Position, flight.Vessel.Velocity, flight.Autopilot.ManeuverDirection);

    }

    public static void Draw(CanvasItem canvas, AttitudeHold hold, Vector2 at, float size, Color colour, float weight = 1.4f) {

        float ring = size * Ring;
        float spoke = size * Spoke;

        switch (hold) {

            case AttitudeHold.Prograde:

                canvas.DrawArc(at, ring, 0.0f, Mathf.Tau, 32, colour, weight, true);
                canvas.DrawCircle(at, weight * 0.9f, colour);

                Spokes(canvas, at, ring, spoke, colour, weight);

                break;

            case AttitudeHold.Retrograde:

                canvas.DrawArc(at, ring, 0.0f, Mathf.Tau, 32, colour, weight, true);

                Cross(canvas, at, ring * 0.58f, colour, weight);
                Spokes(canvas, at, ring, spoke, colour, weight);

                break;

            case AttitudeHold.Normal:

                Wedge(canvas, at, size, 1.0f, colour, weight);

                break;

            case AttitudeHold.Antinormal:

                Wedge(canvas, at, size, -1.0f, colour, weight);

                break;

            case AttitudeHold.RadialOut:

                Diamond(canvas, at, size, colour, weight);

                canvas.DrawCircle(at, weight * 0.9f, colour);

                break;

            case AttitudeHold.RadialIn:

                Diamond(canvas, at, size, colour, weight);

                Cross(canvas, at, size * 0.34f, colour, weight);

                break;

            case AttitudeHold.Maneuver:

                canvas.DrawArc(at, ring, 0.0f, Mathf.Tau, 32, colour, weight, true);
                canvas.DrawArc(at, ring * 0.58f, 0.0f, Mathf.Tau, 24, colour, weight * 0.8f, true);

                Spokes(canvas, at, ring, spoke, colour, weight);

                break;

            case AttitudeHold.Stability:

                canvas.DrawArc(at, ring, 0.0f, Mathf.Tau, 32, colour, weight, true);
                canvas.DrawCircle(at, weight * 1.1f, colour);

                break;

            default:

                canvas.DrawLine(at - new Vector2(ring, 0.0f), at + new Vector2(ring, 0.0f), colour, weight, true);

                break;

        }

    }

    private static void Spokes(CanvasItem canvas, Vector2 at, float ring, float spoke, Color colour, float weight) {

        canvas.DrawLine(at - new Vector2(ring, 0.0f), at - new Vector2(ring + spoke, 0.0f), colour, weight, true);
        canvas.DrawLine(at + new Vector2(ring, 0.0f), at + new Vector2(ring + spoke, 0.0f), colour, weight, true);

        canvas.DrawLine(at - new Vector2(0.0f, ring), at - new Vector2(0.0f, ring + spoke), colour, weight, true);

    }

    private static void Cross(CanvasItem canvas, Vector2 at, float reach, Color colour, float weight) {

        canvas.DrawLine(at + new Vector2(-reach, -reach), at + new Vector2(reach, reach), colour, weight, true);
        canvas.DrawLine(at + new Vector2(-reach, reach), at + new Vector2(reach, -reach), colour, weight, true);

    }

    private static void Wedge(CanvasItem canvas, Vector2 at, float size, float sense, Color colour, float weight) {

        float half = size * 0.78f;
        float rise = size * 0.72f * sense;

        Vector2[] outline = {

            at + new Vector2(-half, rise * 0.5f),
            at + new Vector2(0.0f, -rise * 0.9f),
            at + new Vector2(half, rise * 0.5f),
            at + new Vector2(-half, rise * 0.5f),

        };

        canvas.DrawPolyline(outline, colour, weight, true);

    }

    private static void Diamond(CanvasItem canvas, Vector2 at, float size, Color colour, float weight) {

        float reach = size * 0.78f;

        Vector2[] outline = {

            at + new Vector2(0.0f, -reach),
            at + new Vector2(reach, 0.0f),
            at + new Vector2(0.0f, reach),
            at + new Vector2(-reach, 0.0f),
            at + new Vector2(0.0f, -reach),

        };

        canvas.DrawPolyline(outline, colour, weight, true);

    }

}
