using Godot;

namespace FullThrust.Game;

public sealed partial class SurfaceCloudScreen : Node {

    public ShaderMaterial Material { get; } = new() { Shader = GD.Load<Shader>("res://Shaders/SurfaceCloudScreen.gdshader") };
    private SubViewport _buffer;
    private ColorRect _rect;
    public bool Enabled { get; set; } = true;
    public double GpuMilliseconds => RenderingServer.ViewportGetMeasuredRenderTimeGpu(_buffer.GetViewportRid());
    public void MeasureGpu() => RenderingServer.ViewportSetMeasureRenderTime(_buffer.GetViewportRid(), true);

    public override void _Ready() {

        _buffer = new SubViewport {

            Size = new Vector2I(16, 16), Disable3D = true, UseHdr2D = true, TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,

        };
        AddChild(_buffer);
        _rect = new ColorRect { Size = new Vector2(16, 16), Material = Material, Color = Colors.White };
        _buffer.AddChild(_rect);

    }

    public void Sync(MeshInstance3D volume, ShaderMaterial material) {

        Viewport viewport = GetViewport();
        Camera3D camera = viewport.GetCamera3D();
        bool visible = Enabled && volume.IsVisibleInTree() && camera != null
            && camera.Projection == Camera3D.ProjectionType.Perspective && (volume.Layers & camera.CullMask) != 0;
        Rect2 coverage = viewport.GetVisibleRect();
        if (visible) {

            Aabb box = volume.GlobalTransform * volume.CustomAabb;
            foreach (Plane plane in camera.GetFrustum()) {

                if (plane.IsPointOver(box.GetSupport(-plane.Normal))) { visible = false; break; }

            }
            Vector2 low = coverage.End, high = coverage.Position;
            bool crossesEye = false;
            for (int i = 0; i < 8 && visible; i++) {

                Vector3 point = box.GetEndpoint(i);
                if (camera.IsPositionBehind(point)) { crossesEye = true; break; }
                Vector2 pixel = camera.UnprojectPosition(point);
                low = low.Min(pixel);
                high = high.Max(pixel);

            }
            if (!crossesEye && visible) { coverage = new Rect2(low, high - low).Grow(4.0f).Intersection(coverage); }
            visible &= coverage.Size.X * coverage.Size.Y * viewport.Scaling3DScale * viewport.Scaling3DScale > 65536.0f;

        }
        _buffer.RenderTargetUpdateMode = visible ? SubViewport.UpdateMode.Once : SubViewport.UpdateMode.Disabled;
        material.SetShaderParameter("screen_enabled", visible);
        if (!visible) { return; }
        Vector2 size = viewport.GetVisibleRect().Size * viewport.Scaling3DScale * 0.5f;
        Vector2I pixels = new(Mathf.Max(16, Mathf.CeilToInt(size.X)), Mathf.Max(16, Mathf.CeilToInt(size.Y)));
        if (_buffer.Size != pixels) {

            _buffer.Size = pixels;

        }
        Vector2 scale = new Vector2(pixels.X, pixels.Y) / viewport.GetVisibleRect().Size;
        _rect.Position = (coverage.Position * scale).Floor();
        _rect.Size = (coverage.End * scale).Ceil() - _rect.Position;
        Material.SetShaderParameter("buffer_size", new Vector2(pixels.X, pixels.Y));
        Material.SetShaderParameter("local_from_view", volume.GlobalTransform.AffineInverse() * camera.GetCameraTransform());
        Material.SetShaderParameter("inverse_projection", camera.GetCameraProjection().Inverse());
        material.SetShaderParameter("cloud_screen", _buffer.GetTexture());

    }

}
