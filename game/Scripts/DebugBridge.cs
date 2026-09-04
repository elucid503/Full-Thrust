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

        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

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

                case "/key":

                    Respond(context, PressKey(context.Request.QueryString));

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

            return new Dictionary<string, object> { ["error"] = "usage: /tune?target=surface|clouds|atmosphere|vessel&<uniform>=<value>" };

        }

        Dictionary<string, object> applied = new Dictionary<string, object>();

        foreach (string key in query.AllKeys) {

            if (key == null || key == "target") {

                continue;

            }

            // The vessel owns both of its volumes - the plume and the entry sheath - and a
            // uniform belongs to whichever of them declares it, so one target reaches both.
            bool ok = target == "plume" || target == "entry" || target == "vessel"
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

        if (query["stage"] != null) {

            flight.Separate();

        }

        if (double.TryParse(query["altitude"], out double altitude)) {

            double speed = double.TryParse(query["speed"], out double wanted) ? wanted : flight.Vessel.Velocity.Length;

            if (double.TryParse(query["latitude"], out double latitude) && double.TryParse(query["longitude"], out double longitude)) {

                flight.PlaceAt(latitude, longitude, altitude, speed);

            }
            else {

                flight.Place(altitude, speed);

            }

        }

        if (query["site"] != null) {

            flight.PlaceAt(flight.Site.Latitude * 180.0 / Math.PI, flight.Site.Longitude * 180.0 / Math.PI,
                double.TryParse(query["site"], out double above) ? above : 400.0, 0.0);

        }

        if (bool.TryParse(query["pause"], out bool pause)) {

            flight.DebugPaused = pause;

        }

        if (query["relativeX"] != null || query["relativeY"] != null || query["relativeZ"] != null) {

            double Value(string name) => double.TryParse(query[name], out double number) ? number : 0.0;
            flight.PlaceRelative(new Vector3d(Value("relativeX"), Value("relativeY"), Value("relativeZ")),
                new Vector3d(Value("relativeVX"), Value("relativeVY"), Value("relativeVZ")), query["reverse"] == "true");

        }

        if (double.TryParse(query["aoa"], out double aoa) || query["aim"] == "up") {

            Vector3d up = flight.Vessel.Position.Normalized;
            Vector3d along = (flight.Vessel.Velocity - flight.Body.AirVelocityAt(flight.Vessel.Position)).Normalized;
            Vector3d nose = query["aim"] == "up" ? up
                : along * Math.Cos(aoa * Math.PI / 180.0) + up * Math.Sin(aoa * Math.PI / 180.0);
            flight.Vessel.Orientation = QuaternionD.LookAlong(nose, up);
            flight.Vessel.AngularVelocity = Vector3d.Zero;

        }

        if (double.TryParse(query["rcsTorque"], out double torque)) {

            flight.Vessel.ControlTorque = Vector3d.UnitX * (Math.Clamp(torque, -1.0, 1.0) * flight.Vessel.ControlTorqueLimit);

        }

        if (query["restart"] != null) {

            flight.Restart();

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

            ["stages"] = flight.Vessel.StageCount,
            ["debris"] = flight.Debris.Count,

        };

    }

    /// <summary>Points the mouse at a screen position and optionally clicks it, so the interface
    /// can be worked from the command line the same way the rest of the game can.</summary>
    private static Dictionary<string, object> Click(System.Collections.Specialized.NameValueCollection query) {

        if (!float.TryParse(query["x"], out float x) || !float.TryParse(query["y"], out float y)) {

            return new Dictionary<string, object> { ["error"] = "usage: /click?x=<px>&y=<py>[&button=left|right][&press=click|down|up|move|move-held]" };

        }

        Vector2 at = new Vector2(x, y);

        Input.WarpMouse(at);

        bool held = query["button"] == "right";

        MouseButton index = held ? MouseButton.Right : MouseButton.Left;
        MouseButtonMask mask = held ? MouseButtonMask.Right : MouseButtonMask.Left;

        string press = query["press"] ?? "click";

        // A drag is three calls - down, one or more moves, up - so the mask has to say which button
        // is still held while the pointer moves, or the map never sees the motion as a drag at all.
        bool down = press == "down";
        bool up = press == "up";

        Input.ParseInputEvent(new InputEventMouseMotion {

            Position = at,
            GlobalPosition = at,

            ButtonMask = down || press == "move-held" ? mask : 0,

        });

        if (press == "click" || press == "true") {

            foreach (bool state in new[] { true, false }) {

                Input.ParseInputEvent(new InputEventMouseButton {

                    Position = at,
                    GlobalPosition = at,

                    ButtonIndex = index,
                    ButtonMask = state ? mask : 0,

                    Pressed = state,

                });

            }

        }

        if (down || up) {

            Input.ParseInputEvent(new InputEventMouseButton {

                Position = at,
                GlobalPosition = at,

                ButtonIndex = index,
                ButtonMask = down ? mask : 0,

                Pressed = down,

            });

        }

        return new Dictionary<string, object> { ["at"] = at.ToString() };

    }

    private static Dictionary<string, object> PressKey(System.Collections.Specialized.NameValueCollection query) {

        if (!Enum.TryParse(query["code"], true, out Key code) || code == Key.None) {

            return new Dictionary<string, object> { ["error"] = "usage: /key?code=Bracketleft|Bracketright|Z|X|R[&press=down|up|tap]" };

        }

        string press = query["press"] ?? "tap";

        foreach (bool pressed in new[] { true, false }) {

            if ((press == "down" && !pressed) || (press == "up" && pressed)) {

                continue;

            }

            Input.ParseInputEvent(new InputEventKey {

                Keycode = code,
                PhysicalKeycode = code,
                Pressed = pressed,

            });

        }

        return new Dictionary<string, object> { ["key"] = code.ToString(), ["press"] = press };

    }

    private static Dictionary<string, object> AimCamera(System.Collections.Specialized.NameValueCollection query) {

        OrbitCamera camera = OrbitCamera.Active;

        if (camera == null) {

            return new Dictionary<string, object> { ["error"] = "no camera" };

        }

        // Yaw and pitch are measured off the world axes, which means the same pair frames a
        // different scene at every latitude. A depression angle off the local horizon does not.
        if (double.TryParse(query["look"], out double depression) && Flight.Active != null) {

            Vessel vessel = Flight.Active.Vessel;

            Vector3d up = vessel.Position.Normalized;
            Vector3d along = vessel.Velocity - up * Vector3d.Dot(vessel.Velocity, up);

            if (along.LengthSquared < 1.0) {

                along = Vector3d.Cross(Vector3d.UnitZ, up);

            }

            if (double.TryParse(query["bearing"], out double bearing)) {

                Vector3d north = Vector3d.Cross(up, Vector3d.Cross(Vector3d.UnitZ, up).Normalized);
                Vector3d east = Vector3d.Cross(Vector3d.UnitZ, up).Normalized;

                along = north * Math.Cos(bearing * Math.PI / 180.0) + east * Math.Sin(bearing * Math.PI / 180.0);

            }

            double radians = depression * Math.PI / 180.0;

            camera.AimAt(Frames.Direction((along.Normalized * Math.Cos(radians) - up * Math.Sin(radians)).Normalized));

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
            ["renderCpuMs"] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()),
            ["renderGpuMs"] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()),
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
            state["vessel"] = flight.Vessel.Name;
            state["vesselIndex"] = flight.VesselIndex;
            state["vesselCount"] = flight.VesselCount;
            state["contacts"] = flight.ContactCount;
            state["plumeObstacles"] = VesselView.Active?.PlumeObstacles ?? 0;
            state["position"] = flight.Vessel.Position.ToString();
            state["angularVelocity"] = flight.Vessel.AngularVelocity.ToString();
            state["altitude"] = flight.Altitude;
            state["groundAltitude"] = flight.Body.HeightAboveGround(flight.Vessel.Position, flight.Time);
            state["patches"] = Planet.Active?.PatchCount ?? 0;
            state["patchLevel"] = Planet.Active?.DeepestLevel ?? 0;
            state["speed"] = flight.Vessel.Velocity.Length;
            state["apoapsis"] = flight.Orbit.ApoapsisRadius - flight.Body.Radius;
            state["periapsis"] = flight.Orbit.PeriapsisRadius - flight.Body.Radius;
            state["inclination"] = flight.Orbit.Inclination;
            state["period"] = flight.Orbit.Period;
            state["mass"] = flight.Vessel.Mass;
            state["deltaV"] = flight.Vessel.DeltaV;
            state["throttle"] = flight.Vessel.Throttle;
            state["thrust"] = flight.Vessel.CurrentThrust;
            state["rcsThrust"] = flight.Vessel.RcsForce.Length;
            state["warp"] = flight.Warp;
            state["hold"] = flight.Autopilot.Hold.ToString();

            state["eccentricity"] = flight.Orbit.Eccentricity;
            state["timeToApoapsis"] = flight.Orbit.TimeToApoapsis(flight.Time);
            state["timeToPeriapsis"] = flight.Orbit.TimeToPeriapsis(flight.Time);

            state["fuelMass"] = flight.Vessel.FuelMass;
            state["oxidiserMass"] = flight.Vessel.OxidiserMass;

            state["stackDeltaV"] = flight.Vessel.StackDeltaV;

            state["stages"] = flight.Vessel.StageCount;
            state["stage"] = flight.Vessel.Active.Name;
            state["canSeparate"] = flight.Vessel.CanSeparate;

            state["fate"] = flight.Fate.ToString();

            state["centreOfMass"] = flight.Vessel.CentreOfMassZ;

            AeroForces air = flight.Vessel.Aero;

            state["inAtmosphere"] = air.InAir;
            state["density"] = air.Density;
            state["airSpeed"] = air.AirSpeed;
            state["mach"] = air.Mach;
            state["dynamicPressure"] = air.DynamicPressure;
            state["angleOfAttack"] = air.AngleOfAttack;
            state["centreOfPressure"] = air.CentreOfPressure;
            state["heatFlux"] = air.HeatFlux;
            state["load"] = air.Force.Length / flight.Vessel.Mass / flight.Body.SurfaceGravity;

            state["skinTemperature"] = flight.Vessel.SkinTemperature;
            state["skinLimit"] = flight.Vessel.SkinLimit;

            state["atmosphereTop"] = flight.Body.AtmosphereTop;

            List<Dictionary<string, object>> tracked = new List<Dictionary<string, object>>();

            foreach (Flight.Tracked debris in flight.Debris) {

                Vector3d relativePosition = debris.Vessel.Position - flight.Vessel.Position;
                Vector3d relativeVelocity = debris.Vessel.Velocity - flight.Vessel.Velocity;

                tracked.Add(new Dictionary<string, object> {

                    ["name"] = debris.Vessel.Name,
                    ["altitude"] = flight.Body.AltitudeOf(debris.Vessel.Position),
                    ["range"] = (debris.Vessel.Position - flight.Vessel.Position).Length,
                    ["relativePosition"] = new[] { relativePosition.X, relativePosition.Y, relativePosition.Z },
                    ["relativeVelocity"] = new[] { relativeVelocity.X, relativeVelocity.Y, relativeVelocity.Z },
                    ["skinTemperature"] = debris.Vessel.SkinTemperature,
                    ["skinLimit"] = debris.Vessel.SkinLimit,
                    ["throttle"] = debris.Vessel.Throttle,
                    ["thrust"] = debris.Vessel.CurrentThrust,
                    ["rcsThrust"] = debris.Vessel.RcsForce.Length,
                    ["fuelMass"] = debris.Vessel.FuelMass,
                    ["hold"] = debris.Pilot.Hold.ToString(),
                    ["nodeDeltaV"] = debris.Plan?.DeltaV ?? 0.0,
                    ["angularVelocity"] = debris.Vessel.AngularVelocity.ToString(),

                });

            }

            state["debris"] = tracked;

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
                state["nodeScreen"] = MapView.Active != null && MapView.Active.NodeLive ? MapView.Active.NodeAt.ToString() : null;

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
