using System;

using Godot;

namespace FullThrust.Game;

public sealed partial class SceneTransition : CanvasLayer {

    private static SceneTransition _active;
    private Control _cover;
    private int _readyFrames;
    private int _frames;
    private double _fade;
    private bool _reloading;

    public static bool Loading => GodotObject.IsInstanceValid(_active) && _active.Visible;

    public static void Begin(Node owner) {

        if (!GodotObject.IsInstanceValid(_active)) {

            _active = new SceneTransition { Layer = 100, ProcessPriority = 1000 };
            SceneTransition overlay = _active;
            owner.GetTree().Root.CallDeferred(Node.MethodName.AddChild, overlay);

        }

        _active._readyFrames = 0;
        _active._frames = 0;
        _active._fade = 0.0;
        _active._reloading = false;
        _active.Visible = true;
        if (_active._cover != null) { _active._cover.Modulate = Colors.White; }

    }

    public override void _Ready() {

        _cover = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        _cover.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_cover);

    }

    public override void _Input(InputEvent @event) {

        if (Visible) { GetViewport().SetInputAsHandled(); }

    }

    public override void _Process(double delta) {

        if (!Visible) { return; }
        _frames++;
        bool ready = !_reloading && _frames > 2 && GodotObject.IsInstanceValid(Planet.Active)
            && Planet.Active.SurfaceReady && Planet.Active.CloudTexturesReady;
        _readyFrames = ready ? _readyFrames + 1 : 0;
        if (_readyFrames < 3) { _fade = 0.0; _cover.Modulate = Colors.White; return; }

        _fade += delta;
        _cover.Modulate = new Color(1.0f, 1.0f, 1.0f, (float)Math.Max(0.0, 1.0 - _fade / 0.18));
        if (_fade >= 0.18) { Hide(); }

    }

    public static async void Restart(Node owner) {

        if (Loading) { return; }
        Begin(owner);
        _active._reloading = true;
        SceneTree tree = owner.GetTree();
        // Present the cover before synchronous scene construction starts.
        await owner.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (DisplayServer.GetName() != "headless") {

            await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        }
        await owner.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Error result = tree.ReloadCurrentScene();
        if (result != Error.Ok) {

            GD.PushError($"Could not restart flight: {result}");
            _active.Hide();

        }

    }

}
