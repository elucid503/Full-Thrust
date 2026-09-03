using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;

using Godot;

namespace FullThrust.Game;

public sealed partial class DebugBridge : Node {

    private const string DefaultUrl = "http://localhost:9080/";
    private const string ShotDirectory = "res://.artifacts";

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

    private Dictionary<string, object> Snapshot() {

        Vector2I window = DisplayServer.WindowGetSize();

        return new Dictionary<string, object> {

            ["fps"] = Engine.GetFramesPerSecond(),
            ["frame"] = Engine.GetProcessFrames(),
            ["uptimeSeconds"] = Time.GetTicksMsec() / 1000.0,

            ["scene"] = GetTree().CurrentScene?.Name.ToString(),
            ["nodeCount"] = GetTree().GetNodeCount(),

            ["windowWidth"] = window.X,
            ["windowHeight"] = window.Y,

        };

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

        byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = payload.Length;

        context.Response.OutputStream.Write(payload, 0, payload.Length);
        context.Response.Close();

    }

}
