using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class DebugBridge : Node {

    private const string DefaultUrl = "http://localhost:9080/";
    private const string ShotDirectory = "res://.artifacts";

    // A hyperbolic orbit has an infinite period, which plain JSON cannot carry at all.
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions {

        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,

    };

    private readonly ConcurrentQueue<HttpListenerContext> _pending = new();

    private HttpListener _listener;
    private Thread _thread;

    private volatile bool _running;

    public override void _Ready() {

        string url = OS.GetEnvironment("FT_BRIDGE_URL");

        if (string.IsNullOrEmpty(url)) {

            url = DefaultUrl;

        }

        _listener = new HttpListener();
        _listener.Prefixes.Add(url);

        try {

            _listener.Start();

        }
        catch (Exception e) {

            GD.PushWarning($"DebugBridge could not bind {url}: {e.Message}");

            return;

        }

        _running = true;

        _thread = new Thread(Accept) { IsBackground = true };
        _thread.Start();

        GD.Print($"DebugBridge listening on {url}");

    }

    public override void _Process(double delta) {

        while (_pending.TryDequeue(out HttpListenerContext context)) {

            Handle(context);

        }

    }

    public override void _ExitTree() {

        _running = false;

        _listener?.Close();

    }

    private void Accept() {

        while (_running) {

            try {

                _pending.Enqueue(_listener.GetContext());

            }
            catch (Exception) {

                return;

            }

        }

    }

    private void Handle(HttpListenerContext context) {

        string route = context.Request.Url.AbsolutePath.TrimEnd('/');

        try {

            switch (route) {

                case "":
                case "/ping":

                    Respond(context, new Dictionary<string, object> {

                        ["ok"] = true,
                        ["engine"] = (string)Engine.GetVersionInfo()["string"],
                        ["scene"] = GetTree().CurrentScene?.Name.ToString(),

                    });

                    break;

                case "/state":

                    Respond(context, Snapshot());

                    break;

                case "/tune":

                    Respond(context, Tune(context.Request.QueryString));

                    break;

                case "/control":

                    Respond(context, Control(context.Request.QueryString));

                    break;

                case "/camera":

                    Respond(context, AimCamera(context.Request.QueryString));

                    break;

                case "/click":

                    Respond(context, Click(context.Request.QueryString));

                    break;

                case "/screenshot":

                    _ = Capture(context, context.Request.QueryString["path"]);

                    break;

                case "/quit":

                    Respond(context, new Dictionary<string, object> { ["ok"] = true });

                    GetTree().Quit();

                    break;

                default:

                    Respond(context, new Dictionary<string, object> { ["error"] = $"no route {route}" }, 404);

                    break;

            }

        }
        catch (Exception e) {

            Respond(context, new Dictionary<string, object> { ["error"] = e.ToString() }, 500);

        }

    }

    private static Dictionary<string, object> Tune(System.Collections.Specialized.NameValueCollection query) {

        Planet planet = Planet.Active;

        string target = query["target"];

        if (planet == null || string.IsNullOrEmpty(target)) {

            return new Dictionary<string, object> { ["error"] = "usage: /tune?target=surface|clouds|atmosphere|plume&<uniform>=<value>" };

        }

        Dictionary<string, object> applied = new Dictionary<string, object>();

        foreach (string key in query.AllKeys) {

            if (key == null || key == "target") {

                continue;

            }

            bool ok = target == "plume"
                ? VesselView.Active != null && VesselView.Active.Tune(key, query[key])
                : planet.Tune(target, key, query[key]);

            applied[key] = ok ? query[key] : "rejected";

        }

        return applied;

    }

    private static Dictionary<string, object> Control(System.Collections.Specialized.NameValueCollection query) {

        Flight flight = Flight.Active;

        if (flight == null) {

            return new Dictionary<string, object> { ["error"] = "no flight" };

        }

        if (double.TryParse(query["throttle"], out double throttle)) {

            flight.Vessel.Throttle = Math.Clamp(throttle, 0.0, 1.0);

        }

        if (Enum.TryParse(query["hold"], true, out AttitudeHold hold)) {

            flight.Autopilot.Hold = hold;

        }

        if (int.TryParse(query["warp"], out int warp)) {

            flight.SetWarpStep(warp);

        }

        if (bool.TryParse(query["rcs"], out bool rcs)) {

            flight.Vessel.RcsEnabled = rcs;

        }

        if (bool.TryParse(query["map"], out bool map) && map != (MapView.Active?.Open ?? false)) {

            MapView.Active?.Toggle();

        }

        if (double.TryParse(query["nodeAt"], out double trueAnomaly)) {

            flight.PlaceNode(trueAnomaly);

        }

        if (query["clearNode"] != null) {

            flight.ClearNode();

        }

        if (flight.Node != null) {

            if (double.TryParse(query["prograde"], out double prograde)) {

                flight.Node.Prograde = prograde;

            }

            if (double.TryParse(query["normal"], out double normal)) {

                flight.Node.Normal = normal;

            }

            if (double.TryParse(query["radial"], out double radial)) {

                flight.Node.Radial = radial;

            }

        }

        return new Dictionary<string, object> {

            ["throttle"] = flight.Vessel.Throttle,
            ["hold"] = flight.Autopilot.Hold.ToString(),
            ["warp"] = flight.Warp,

            ["rcs"] = flight.Vessel.RcsEnabled,
            ["map"] = MapView.Active?.Open ?? false,

            ["nodeDeltaV"] = flight.Node?.DeltaV ?? 0.0,

        };

    }

    /// <summary>Points the mouse at a screen position and optionally clicks it, so the interface
    /// can be worked from the command line the same way the rest of the game can.</summary>
    private static Dictionary<string, object> Click(System.Collections.Specialized.NameValueCollection query) {

        if (!float.TryParse(query["x"], out float x) || !float.TryParse(query["y"], out float y)) {

            return new Dictionary<string, object> { ["error"] = "usage: /click?x=<px>&y=<py>[&press=false]" };

        }

        Vector2 at = new Vector2(x, y);

        Input.WarpMouse(at);

        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });

        if (!bool.TryParse(query["press"], out bool press) || press) {

            foreach (bool down in new[] { true, false }) {

                Input.ParseInputEvent(new InputEventMouseButton {

                    Position = at,
                    GlobalPosition = at,

                    ButtonIndex = MouseButton.Left,
                    ButtonMask = down ? MouseButtonMask.Left : 0,

                    Pressed = down,

                });

            }

        }

        return new Dictionary<string, object> { ["at"] = at.ToString() };

    }

    private static Dictionary<string, object> AimCamera(System.Collections.Specialized.NameValueCollection query) {

        OrbitCamera camera = OrbitCamera.Active;

        if (camera == null) {

            return new Dictionary<string, object> { ["error"] = "no camera" };

        }

        if (float.TryParse(query["yaw"], out float yaw)) {

            camera.Yaw = yaw;

        }

        if (float.TryParse(query["pitch"], out float pitch)) {

            camera.Pitch = pitch;

        }

        if (float.TryParse(query["distance"], out float distance)) {

            camera.Distance = distance;

        }

        return new Dictionary<string, object> {

            ["yaw"] = camera.Yaw,
            ["pitch"] = camera.Pitch,
            ["distance"] = camera.Distance,

            ["current"] = camera.IsCurrent,
            ["eye"] = camera.Eye.ToString(),
            ["forward"] = camera.Forward.ToString(),
            ["near"] = camera.NearPlane,
            ["far"] = camera.FarPlane,

        };

    }

    private Dictionary<string, object> Snapshot() {

        Vector2I window = DisplayServer.WindowGetSize();

        Dictionary<string, object> state = new Dictionary<string, object> {

            ["fps"] = Engine.GetFramesPerSecond(),
            ["frame"] = Engine.GetProcessFrames(),
            ["uptimeSeconds"] = Time.GetTicksMsec() / 1000.0,

            ["scene"] = GetTree().CurrentScene?.Name.ToString(),
            ["nodeCount"] = GetTree().GetNodeCount(),

            ["windowWidth"] = window.X,
            ["windowHeight"] = window.Y,

        };

        Flight flight = Flight.Active;

        if (flight != null) {

            state["missionTime"] = flight.Time;
            state["altitude"] = flight.Altitude;
            state["speed"] = flight.Vessel.Velocity.Length;
            state["apoapsis"] = flight.Orbit.ApoapsisRadius - flight.Body.Radius;
            state["periapsis"] = flight.Orbit.PeriapsisRadius - flight.Body.Radius;
            state["inclination"] = flight.Orbit.Inclination;
            state["period"] = flight.Orbit.Period;
            state["mass"] = flight.Vessel.Mass;
            state["deltaV"] = flight.Vessel.DeltaV;
            state["throttle"] = flight.Vessel.Throttle;
            state["warp"] = flight.Warp;
            state["hold"] = flight.Autopilot.Hold.ToString();

            state["eccentricity"] = flight.Orbit.Eccentricity;
            state["timeToApoapsis"] = flight.Orbit.TimeToApoapsis(flight.Time);
            state["timeToPeriapsis"] = flight.Orbit.TimeToPeriapsis(flight.Time);

            state["fuelMass"] = flight.Vessel.FuelMass;
            state["oxidiserMass"] = flight.Vessel.OxidiserMass;

            state["rcsEnabled"] = flight.Vessel.RcsEnabled;
            state["rcsPropellant"] = flight.Vessel.RcsPropellantMass;

            state["map"] = MapView.Active?.Open ?? false;
            state["warpingToNode"] = flight.WarpingToNode;

            Maneuver node = flight.Node;

            if (node != null) {

                state["nodeTime"] = node.Time;
                state["nodeDeltaV"] = node.DeltaV;
                state["nodePrograde"] = node.Prograde;
                state["nodeNormal"] = node.Normal;
                state["nodeRadial"] = node.Radial;
                state["nodeBurnSeconds"] = node.BurnSeconds(flight.Vessel);
                state["timeToIgnition"] = flight.TimeToIgnition;

                Orbit planned = flight.PlannedOrbit;

                if (planned != null) {

                    state["plannedApoapsis"] = planned.ApoapsisRadius - flight.Body.Radius;
                    state["plannedPeriapsis"] = planned.PeriapsisRadius - flight.Body.Radius;
                    state["plannedInclination"] = planned.Inclination;

                }

            }

        }

        return state;

    }

    // Capture must wait for a completed frame; the viewport texture is stale before FramePostDraw.
    private async System.Threading.Tasks.Task Capture(HttpListenerContext context, string requested) {

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        string path = requested;

        if (string.IsNullOrEmpty(path)) {

            DirAccess.MakeDirRecursiveAbsolute(ShotDirectory);

            path = $"{ShotDirectory}/shot-{Time.GetTicksMsec()}.png";

        }

        Image image = GetViewport().GetTexture().GetImage();

        Error error = image.SavePng(path);

        Respond(context, new Dictionary<string, object> {

            ["ok"] = error == Error.Ok,
            ["error"] = error == Error.Ok ? null : error.ToString(),

            ["path"] = ProjectSettings.GlobalizePath(path),
            ["width"] = image.GetWidth(),
            ["height"] = image.GetHeight(),

        });

    }

    private static void Respond(HttpListenerContext context, Dictionary<string, object> body, int status = 200) {

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, Json));

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;

        context.Response.OutputStream.Write(payload, 0, payload.Length);
        context.Response.Close();

    }

}
