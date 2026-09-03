using System;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The attitude ball, flanked by the throttle and vertical speed tapes.</summary>
public sealed partial class Navball : Control {

    public const int Diameter = 152;

    private const int Margin = 9;
    private const int TapeWidth = 14;
    private const int Gap = 6;

    private const float MarkerSize = 8.5f;

    // The ball is shaded rather than lit: a sphere with no terminator on it reads as a flat disc.
    private const float Curve = 0.22f;

    private static readonly Color Sky = new Color(0.475f, 0.541f, 0.612f);
    private static readonly Color Ground = new Color(0.145f, 0.161f, 0.184f);
    private static readonly Color Line = new Color(0.945f, 0.961f, 0.976f);

    // Every thirty degrees, and no finer. A ball this size carries a ten degree ladder as noise:
    // the lines land within a pixel or two of each other and the markers have to compete with them.
    private const int Parallels = 5;
    private const int Meridians = 4;

    private const int Step = 30;

    // The tape is logarithmic and fixed. An auto-ranging tape rescales every time the reading
    // crosses a step, which fills the bar, resets it, and fills it again while the climb is steady:
    // the pointer ends up saying more about the scale than about the vessel.
    private const double Ceiling = 5000.0;

    private static readonly double[] Decades = { 10.0, 100.0, 1000.0 };

    public static readonly Vector2 Extent = new Vector2(

        Margin * 2 + TapeWidth * 2 + Gap * 2 + Diameter,
        Margin * 2 + Diameter

    );

    private readonly byte[] _pixels = new byte[Diameter * Diameter * 4];

    private readonly float[] _parallelSine = new float[Parallels];
    private readonly float[] _boundarySine = new float[Parallels - 1];

    private readonly Vector3[] _meridian = new Vector3[Meridians];

    private ImageTexture _texture;

    private Flight _flight;

    private Vector3 _up;
    private Vector3 _east;
    private Vector3 _north;

    private double _verticalSpeed;

    private Rect2 _ball;
    private Rect2 _leftTape;
    private Rect2 _rightTape;

    public override void _Ready() {

        CustomMinimumSize = Extent;
        Size = Extent;

        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Linear;

        for (int index = 0; index < Parallels; index++) {

            _parallelSine[index] = Mathf.Sin(Mathf.DegToRad(-60.0f + Step * index));

        }

        for (int index = 0; index < Parallels - 1; index++) {

            _boundarySine[index] = Mathf.Sin(Mathf.DegToRad(-45.0f + Step * index));

        }

        _ball = new Rect2(Margin + TapeWidth + Gap, Margin, Diameter, Diameter);

        _leftTape = new Rect2(Margin, Margin, TapeWidth, Diameter);
        _rightTape = new Rect2(Extent.X - Margin - TapeWidth, Margin, TapeWidth, Diameter);

        _texture = ImageTexture.CreateFromImage(Image.CreateFromData(Diameter, Diameter, false, Image.Format.Rgba8, _pixels));

    }

    public void Sync(Flight flight) {

        _flight = flight;

        Vessel vessel = flight.Vessel;

        QuaternionD inverse = vessel.Orientation.Conjugate;

        Vector3d upWorld = vessel.Position.Normalized;

        Vector3d eastWorld = Vector3d.Cross(Vector3d.UnitZ, upWorld);

        // Over a pole the polar axis gives no east at all, so the track supplies one instead.
        if (eastWorld.LengthSquared < 1e-12) {

            eastWorld = Vector3d.Cross(vessel.Velocity, upWorld);

        }

        eastWorld = eastWorld.Normalized;

        Vector3d northWorld = Vector3d.Cross(upWorld, eastWorld);

        _up = Single(inverse.Rotate(upWorld));
        _east = Single(inverse.Rotate(eastWorld));
        _north = Single(inverse.Rotate(northWorld));

        for (int index = 0; index < Meridians; index++) {

            float angle = Mathf.Pi * index / Meridians;

            _meridian[index] = _east * Mathf.Cos(angle) - _north * Mathf.Sin(angle);

        }

        _verticalSpeed = Vector3d.Dot(vessel.Velocity, upWorld);

        Raster();

        QueueRedraw();

    }

    public override void _Draw() {

        if (_flight == null) {

            return;

        }

        DrawStyleBox(HudTheme.Panel(), new Rect2(Vector2.Zero, Size));

        DrawTextureRect(_texture, _ball, false);

        DrawArc(_ball.GetCenter(), Diameter * 0.5f - 0.5f, 0.0f, Mathf.Tau, 96, HudTheme.Edge, 1.0f, true);

        DrawGraduations();
        DrawMarkers();
        DrawReticle();

        DrawThrottleTape();
        DrawVerticalSpeedTape();

    }

    /// <summary>The ball itself. Every pixel is a direction, so the grid needs no geometry and never distorts.</summary>
    private void Raster() {

        float radius = Diameter * 0.5f;
        float centre = radius;

        for (int py = 0; py < Diameter; py++) {

            float ny = (py + 0.5f - centre) / radius;

            for (int px = 0; px < Diameter; px++) {

                float nx = (px + 0.5f - centre) / radius;

                int at = (py * Diameter + px) * 4;

                float squared = nx * nx + ny * ny;

                if (squared >= 1.0f) {

                    _pixels[at + 3] = 0;

                    continue;

                }

                float bz = Mathf.Sqrt(1.0f - squared);

                float bx = nx;
                float by = -ny;

                float u = bx * _up.X + by * _up.Y + bz * _up.Z;

                float shade = 1.0f - Curve + Curve * bz;

                Color colour = u >= 0.0f ? Sky : Ground;

                float mask = Math.Max(Parallel(nx, ny, bz, radius, u), Meridian(bx, by, bz, nx, ny, radius, u));

                float red = Mathf.Lerp(colour.R, Line.R, mask) * shade;
                float green = Mathf.Lerp(colour.G, Line.G, mask) * shade;
                float blue = Mathf.Lerp(colour.B, Line.B, mask) * shade;

                // One pixel of feather on the limb; a hard edge on a 152 px ball reads as a cut-out.
                float cover = Mathf.Clamp((1.0f - Mathf.Sqrt(squared)) * radius, 0.0f, 1.0f);

                _pixels[at] = Byte(red);
                _pixels[at + 1] = Byte(green);
                _pixels[at + 2] = Byte(blue);
                _pixels[at + 3] = Byte(cover);

            }

        }

        _texture.Update(Image.CreateFromData(Diameter, Diameter, false, Image.Format.Rgba8, _pixels));

    }

    private float Parallel(float nx, float ny, float bz, float radius, float u) {

        if (Math.Abs(u) > 0.9659f) {

            return 0.0f;

        }

        int band = 0;

        while (band < Parallels - 1 && u > _boundarySine[band]) {

            band++;

        }

        bool horizon = band == Parallels / 2;

        float weight = horizon ? 1.0f : 0.46f;
        float thickness = horizon ? 1.9f : 1.15f;

        return Edge(u - _parallelSine[band], Gradient(_up, nx, ny, bz, radius), thickness) * weight;

    }

    private float Meridian(float bx, float by, float bz, float nx, float ny, float radius, float u) {

        // Held almost to the pole. Cut earlier and a ball seen down its own axis loses every
        // meridian at once, leaving concentric parallels that read as a target rather than a sphere.
        float fade = Mathf.Clamp((0.9986f - Math.Abs(u)) * 90.0f, 0.0f, 1.0f);

        if (fade <= 0.0f) {

            return 0.0f;

        }

        int nearest = 0;
        float closest = float.MaxValue;

        for (int index = 0; index < Meridians; index++) {

            Vector3 axis = _meridian[index];

            float value = Math.Abs(bx * axis.X + by * axis.Y + bz * axis.Z);

            if (value < closest) {

                closest = value;
                nearest = index;

            }

        }

        Vector3 chosen = _meridian[nearest];

        float signed = bx * chosen.X + by * chosen.Y + bz * chosen.Z;

        bool cardinal = nearest % (Meridians / 2) == 0;

        float mask = Edge(signed, Gradient(chosen, nx, ny, bz, radius), cardinal ? 1.3f : 1.0f);

        return mask * fade * (cardinal ? 0.46f : 0.20f);

    }

    // How fast a plane's signed distance changes across the screen, so a line can be a fixed number
    // of pixels wide wherever it falls, including where the sphere turns away at the limb.
    private static float Gradient(Vector3 axis, float nx, float ny, float bz, float radius) {

        float alongX = (axis.X - axis.Z * nx / bz) / radius;
        float alongY = (-axis.Y - axis.Z * ny / bz) / radius;

        return Mathf.Sqrt(alongX * alongX + alongY * alongY);

    }

    private static float Edge(float distance, float gradient, float thickness) {

        if (gradient <= 0.0f) {

            return 0.0f;

        }

        float pixels = Math.Abs(distance) / gradient;

        return Mathf.Clamp((thickness * 0.5f + 0.5f - pixels), 0.0f, 1.0f);

    }

    private void DrawGraduations() {

        for (int index = 0; index < 4; index++) {

            float longitude = Mathf.Pi * 0.5f * index;

            string cardinal = index == 0 ? "N" : index == 1 ? "E" : index == 2 ? "S" : "W";

            Plot(Surface(0.0f, longitude), out Vector2 at, out float visibility);

            if (visibility > 0.0f) {

                Centred(HudTheme.Strong, HudTheme.Small, at, cardinal, HudTheme.Ink * Alpha(visibility * 0.9f));

            }

        }

    }

    private void DrawMarkers() {

        AttitudeHold active = _flight.Autopilot.Hold;

        foreach (AttitudeHold hold in AttitudeMarker.Marked(active)) {

            if (hold == AttitudeHold.Stability || !AttitudeMarker.Available(hold, _flight)) {

                continue;

            }

            Vector3d direction = AttitudeMarker.Direction(hold, _flight);

            if (direction.LengthSquared <= 0.0) {

                continue;

            }

            Plot(Single(_flight.Vessel.Orientation.Conjugate.Rotate(direction.Normalized)), out Vector2 at, out float visibility);

            if (visibility <= 0.0f) {

                continue;

            }

            Color ink = HudTheme.Dim;

            // The reference being flown is the bright one. Brightness is the whole state language here.
            if (hold == active) {

                DrawArc(at, MarkerSize * 1.3f, 0.0f, Mathf.Tau, 28, HudTheme.Ink * Alpha(visibility * 0.55f), 1.2f, true);

                ink = HudTheme.Ink;

            }

            AttitudeMarker.Draw(this, hold, at, MarkerSize, ink * Alpha(visibility * 0.92f));

        }

    }

    /// <summary>Where the nose actually points: fixed at the centre, because the ball moves and it does not.</summary>
    private void DrawReticle() {

        Vector2 centre = _ball.GetCenter();

        Color ink = HudTheme.Ink * Alpha(0.9f);

        DrawLine(centre + new Vector2(-15.0f, 0.0f), centre + new Vector2(-5.0f, 0.0f), ink, 1.6f, true);
        DrawLine(centre + new Vector2(5.0f, 0.0f), centre + new Vector2(15.0f, 0.0f), ink, 1.6f, true);

        DrawLine(centre + new Vector2(-5.0f, 0.0f), centre + new Vector2(0.0f, 4.0f), ink, 1.6f, true);
        DrawLine(centre + new Vector2(5.0f, 0.0f), centre + new Vector2(0.0f, 4.0f), ink, 1.6f, true);

        DrawCircle(centre, 1.6f, ink);

    }

    private void DrawThrottleTape() {

        DrawStyleBox(HudTheme.Track(), _leftTape);

        float value = (float)Math.Clamp(_flight.Vessel.Throttle, 0.0, 1.0);

        float top = _leftTape.Position.Y + _leftTape.Size.Y * (1.0f - value);

        DrawRect(new Rect2(_leftTape.Position.X, top, _leftTape.Size.X, _leftTape.End.Y - top), HudTheme.Ink * Alpha(0.30f));

        for (int step = 0; step <= 4; step++) {

            bool major = step % 2 == 0;

            float y = Mathf.Round(_leftTape.End.Y - _leftTape.Size.Y * step / 4.0f) + 0.5f;

            float reach = _leftTape.Size.X * (major ? 0.66f : 0.38f);

            DrawLine(new Vector2(_leftTape.Position.X, y), new Vector2(_leftTape.Position.X + reach, y), HudTheme.Ink * Alpha(major ? 0.70f : 0.38f), 1.0f, true);

        }

        top = Pointer(_leftTape, top);

        DrawLine(new Vector2(_leftTape.Position.X, top), new Vector2(_leftTape.End.X, top), HudTheme.Ink, 2.0f, true);

    }

    private void DrawVerticalSpeedTape() {

        DrawStyleBox(HudTheme.Track(), _rightTape);

        float middle = _rightTape.GetCenter().Y;
        float half = _rightTape.Size.Y * 0.5f;

        float y = Pointer(_rightTape, middle - half * Logarithmic(_verticalSpeed));

        DrawRect(new Rect2(_rightTape.Position.X, Math.Min(middle, y), _rightTape.Size.X, Math.Abs(y - middle)), HudTheme.Ink * Alpha(0.30f));

        Graduation(_rightTape, middle, 0.85f, 0.66f);

        foreach (double decade in Decades) {

            float offset = half * Logarithmic(decade);

            Graduation(_rightTape, middle - offset, 0.55f, 0.5f);
            Graduation(_rightTape, middle + offset, 0.55f, 0.5f);

        }

        DrawLine(new Vector2(_rightTape.Position.X, y), new Vector2(_rightTape.End.X, y), HudTheme.Ink, 2.0f, true);

    }

    /// <summary>Fraction of the tape a rate falls at. Decades are evenly spaced, so a metre a
    /// second off the top of a hover and a kilometre a second off a burn both have somewhere to sit.</summary>
    private static float Logarithmic(double rate) {

        double magnitude = Math.Log(1.0 + Math.Abs(rate)) / Math.Log(1.0 + Ceiling);

        return (float)Math.Clamp(rate >= 0.0 ? magnitude : -magnitude, -1.0, 1.0);

    }

    private void Graduation(Rect2 tape, float y, float alpha, float reach) {

        if (y < tape.Position.Y || y > tape.End.Y) {

            return;

        }

        y = Mathf.Round(y) + 0.5f;

        DrawLine(new Vector2(tape.End.X - tape.Size.X * reach, y), new Vector2(tape.End.X, y), HudTheme.Ink * Alpha(alpha), 1.0f, true);

    }

    // A two pixel pointer sitting on the very end of a tape is half outside it.
    private static float Pointer(Rect2 tape, float y) => Mathf.Clamp(y, tape.Position.Y + 1.0f, tape.End.Y - 1.0f);

    /// <summary>A direction in the ball's own frame, from a pitch and a heading.</summary>
    private Vector3 Surface(float latitude, float longitude) {

        return _up * Mathf.Sin(latitude) + (_north * Mathf.Cos(longitude) + _east * Mathf.Sin(longitude)) * Mathf.Cos(latitude);

    }

    /// <summary>Projects a body direction onto the disc, fading it out as it turns over the limb.</summary>
    private void Plot(Vector3 direction, out Vector2 at, out float visibility) {

        float radius = Diameter * 0.5f;

        at = _ball.GetCenter() + new Vector2(direction.X, -direction.Y) * radius;

        visibility = Mathf.Clamp(direction.Z * 9.0f, 0.0f, 1.0f);

    }

    private void Centred(Font font, int size, Vector2 at, string text, Color colour) {

        float width = HudTheme.Width(font, size, text);

        HudTheme.Write(this, font, size, at + new Vector2(-width * 0.5f, font.GetAscent(size) * 0.5f - font.GetDescent(size) * 0.5f), text, colour);

    }

    private static Color Alpha(float alpha) => new Color(1.0f, 1.0f, 1.0f, alpha);

    private static Vector3 Single(Vector3d value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);

    private static byte Byte(float value) => (byte)Mathf.Clamp(Mathf.RoundToInt(value * 255.0f), 0, 255);

}
