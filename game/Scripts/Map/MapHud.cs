using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The map's own instruments. Same margins and same corners as the flight interface, so
/// switching modes changes what the frame holds without moving the frame.</summary>
public sealed partial class MapHud : CanvasLayer {

    private const float Margin = 22.0f;

    private Flight _flight;

    private ElementsPanel _elements;
    private PlanView _plan;
    private NodePanel _node;

    private Popover _popover;

    public void Build(Flight flight, MapView map) {

        _flight = flight;

        _elements = Attach(new ElementsPanel());
        _plan = Attach(new PlanView());
        _node = Attach(new NodePanel());

        _popover = Attach(new Popover());

        _plan.Build(flight);
        _node.Build(flight);

    }

    public void Sync() {

        Place();

        // A marker the planet has swallowed stops being updated, so a panel left open on one would
        // sit there showing figures from whenever it went behind.
        if (_popover.Subject is MapPath.Mark stale && !stale.Live) {

            _popover.Dismiss();

        }

        _elements.Sync(_flight);
        _plan.Sync();
        _node.Sync();
        _popover.Sync();

    }

    /// <summary>Raises the reading for a point on a conic, or closes it if it is already up.</summary>
    public void Open(MapPath.Mark mark) {

        if (_popover.Shows(mark)) {

            _popover.Dismiss();

            return;

        }

        _popover.Raise(mark, Title(mark), (rows, actions) => Fill(mark, rows, actions), mark.Screen);

    }

    public void Dismiss() {

        _popover.Dismiss();

    }

    // Recomputed every frame from the viewport, the way the flight interface places itself.
    private void Place() {

        Vector2 screen = GetViewport().GetVisibleRect().Size;

        _elements.Position = new Vector2(Margin, Margin);

        float floor = screen.Y - Margin;

        _plan.Position = new Vector2(Margin, floor - PlanView.Extent.Y);

        _node.Position = new Vector2(screen.X - Margin - NodePanel.Extent.X, floor - NodePanel.Extent.Y);

    }

    private static string Title(MapPath.Mark mark) {

        return mark.Kind switch {

            MapPath.Kind.Apoapsis => "APOAPSIS",
            MapPath.Kind.Periapsis => "PERIAPSIS",

            MapPath.Kind.Ascending => "ASCENDING NODE",
            MapPath.Kind.Descending => "DESCENDING NODE",

            MapPath.Kind.Entry => "ENTRY INTERFACE",
            MapPath.Kind.Impact => "IMPACT",

            _ => "VESSEL",

        };

    }

    private void Fill(MapPath.Mark mark, List<(string Label, string Value)> rows, List<(string Label, Action Run)> actions) {

        if (mark.Kind == MapPath.Kind.Vessel) {

            Vessel vessel = _flight.Vessel;

            rows.Add(("ALTITUDE", Hud.Distance(_flight.Altitude)));
            rows.Add(("SPEED", Hud.Speed(vessel.Velocity.Length)));
            rows.Add(("MASS", $"{vessel.Mass / 1000.0:N1} t"));
            rows.Add(("DELTA-V", Hud.Speed(vessel.DeltaV)));

            return;

        }

        Orbit orbit = _flight.Orbit;

        rows.Add(("ALTITUDE", Hud.Distance(mark.Radius - _flight.Body.Radius)));
        rows.Add(("SPEED", Hud.Speed(orbit.SpeedAt(mark.Radius))));
        rows.Add(("TIME TO", Hud.Clock(mark.Seconds)));

        if (mark.Kind == MapPath.Kind.Ascending || mark.Kind == MapPath.Kind.Descending) {

            rows.Add(("INCLINATION", $"{orbit.Inclination * 180.0 / Math.PI:N1}°"));

        }

        if (mark.Kind == MapPath.Kind.Impact) {

            return;

        }

        actions.Add(("NODE HERE", () => _flight.PlaceNode(mark.Anomaly)));

    }

    private T Attach<T>(T control) where T : Control {

        AddChild(control);

        return control;

    }

}
