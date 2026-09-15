using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class GroundScatter : Node3D {

    private double CellSize = 48.0;
    private double Reach = 360.0;
    private int Samples = 40;
    private bool _broad;

    private readonly record struct Key(int Row, int Column, bool Grass);

    private sealed class Grove {

        public Key Key;
        public Vector3d Anchor;
        public Transform3D[] Transforms;
        public Color[] Colours;
        public bool Grass;
        public bool Coastal;
        public ulong Born;
        public bool Faded;
        public MultiMeshInstance3D Instance;

    }

    public ScatterObstacles Obstacles { get; } = new(false);

    private CelestialBody _body;
    private byte[] _biomes;
    private int _width;
    private int _height;
    private int _rows;
    private double _latitudeStep;
    private ArrayMesh[] _meshes;
    private ArrayMesh[] _palmettos;
    private ShaderMaterial _material;
    internal ShaderMaterial SurfaceMaterial => _material;
    private readonly Dictionary<Key, Grove> _groves = new();
    private readonly HashSet<Key> _wanted = new();
    private readonly List<Key> _queue = new();
    private readonly List<Key> _remove = new();
    private Task<Grove> _job;
    private readonly CancellationTokenSource _cancellation = new();
    private Vector3d _lastEye;
    private Vector3d? _physicsFocus;
    private bool _enabled;

    public int ScatterCount { get; private set; }
    public int CellCount => _groves.Count;
    public int Pending => _queue.Count + (_job == null ? 0 : 1);
    public int Failures { get; private set; }

    public void Build(CelestialBody body, Texture2D biomes, bool broad = false) {

        _broad = broad;
        CellSize = broad ? 192.0 : 48.0;
        Reach = broad ? 1280.0 : 360.0;
        Samples = broad ? 16 : 40;
        _body = body;
        using Image image = biomes.GetImage();
        if (image.IsCompressed()) {

            image.Decompress();

        }
        image.Convert(Image.Format.Rgba8);
        _width = image.GetWidth();
        _height = image.GetHeight();
        _biomes = image.GetData();
        _rows = (int)Math.Ceiling(Math.PI * body.Radius / CellSize);
        _latitudeStep = Math.PI / _rows;
        _meshes = new[] { RockMesh(0), GrassMesh(0), RockMesh(1), GrassMesh(1), RockMesh(2), GrassMesh(2) };
        if (broad) { _palmettos = new[] { PalmettoMesh(0), PalmettoMesh(1), PalmettoMesh(2) }; }
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/GroundScatter.gdshader") };
        _material.SetShaderParameter("grass_range", broad ? new Vector2(500, 850) : new Vector2(160, 290));
        _material.SetShaderParameter("stone_range", broad ? new Vector2(800, 1200) : new Vector2(230, 330));
        _material.SetShaderParameter("rock_colour", GD.Load<Texture2D>("res://Assets/Planet/rock_colour.jpg"));
        _material.SetShaderParameter("rock_normal", GD.Load<Texture2D>("res://Assets/Planet/rock_normal.jpg"));

    }

    public void Sync(double time, Vector3d eye, float altitude) {

        Obstacles.Animate(time);
        Vector3d local = _body.ToBodyFixed(eye, time);
        Vector3d? physics = null;
        Flight flight = Flight.Active;
        if (flight != null && flight.Body == _body
            && _body.HeightAboveGround(flight.Vessel.Position, time) < 100.0) {
            physics = _body.ToBodyFixed(flight.Vessel.Position, time);
        }
        bool physicsMoved = physics.HasValue != _physicsFocus.HasValue
            || (physics.HasValue && _physicsFocus.HasValue
                && (physics.Value - _physicsFocus.Value).LengthSquared > CellSize * CellSize * 0.0625);
        if (physicsMoved) { _physicsFocus = physics; }
        bool enabled = altitude < Reach;
        if (physicsMoved || enabled != _enabled || (local - _lastEye).LengthSquared > 16.0 * 16.0) {

            Select(local, enabled);
            _lastEye = local;
            _enabled = enabled;

        }
        if (_job?.IsCompleted == true) {

            if (_job.IsCompletedSuccessfully) {

                Grove grove = _job.Result;
                if (_wanted.Contains(grove.Key)) {

                    Adopt(grove);

                }

            } else if (_job.IsFaulted) {

                Failures++;
                GD.PushError($"GroundScatter generation failed: {_job.Exception.GetBaseException()}");

            }
            _job = null;

        }
        while (_queue.Count > 0 && _groves.ContainsKey(_queue[0])) {

            _queue.RemoveAt(0);

        }
        if (_job == null && _queue.Count > 0) {

            Key key = _queue[0];
            _queue.RemoveAt(0);
            _job = Task.Run(() => Generate(key, _cancellation.Token));

        }
        Basis turn = new Basis(Vector3.Up, (float)_body.SpinAt(time));
        _material.SetShaderParameter("eye_position", Frames.Point(eye));
        _material.SetShaderParameter("wind_time", (float)(time % 4096.0));
        foreach (Grove grove in _groves.Values) {

            if (grove.Instance != null) {

                if (!grove.Faded) {

                    float fade = Mathf.Clamp((Time.GetTicksMsec() - grove.Born) / 450.0f, 0.0f, 1.0f);
                    grove.Instance.SetInstanceShaderParameter("cell_fade", fade);
                    grove.Faded = fade >= 1.0f;

                }

                grove.Instance.Transform = new Transform3D(turn, Frames.Point(_body.ToInertial(grove.Anchor, time)));

            }

        }

    }

    // Contact-critical cells are generated before integration queries them, independently of rendering.
    // Normal camera streaming stays asynchronous; this bounded cache miss path also handles teleporting.
    public void EnsureContacts(Vector3d fixedPosition, double reach) {
        double latitude = Math.Asin(Math.Clamp(fixedPosition.Normalized.Z, -1.0, 1.0));
        double longitude = Math.Atan2(fixedPosition.Y, fixedPosition.X);
        double angular = reach / _body.Radius;
        int low = Math.Clamp((int)((latitude - angular + Math.PI * 0.5) / _latitudeStep), 0, _rows - 1);
        int high = Math.Clamp((int)((latitude + angular + Math.PI * 0.5) / _latitudeStep), 0, _rows - 1);
        for (int r = low; r <= high; r++) {
            int columns = Columns(r);
            double cosine = Math.Cos(-Math.PI * 0.5 + (r + 0.5) * _latitudeStep);
            double span = Math.Min(Math.PI, angular / Math.Max(cosine, 1e-8));
            int first = (int)Math.Floor((longitude - span + Math.PI) / (Math.PI * 2) * columns);
            int last = Math.Min(first + columns - 1,
                (int)Math.Floor((longitude + span + Math.PI) / (Math.PI * 2) * columns));
            for (int c = first; c <= last; c++) {
                Key key = new(r, ((c % columns) + columns) % columns, false);
                if (_groves.ContainsKey(key)) { continue; }
                _wanted.Add(key);
                Adopt(Generate(key, _cancellation.Token));
            }
        }
    }

    private int Columns(int row) {

        double latitude = -Math.PI * 0.5 + (row + 0.5) * _latitudeStep;
        return Math.Max(1, (int)Math.Ceiling(Math.PI * 2.0 * _body.Radius * Math.Cos(latitude) / CellSize));

    }

    private void Select(Vector3d eye, bool enabled) {

        _wanted.Clear();
        _queue.Clear();
        if (enabled) {

            double latitude = Math.Asin(eye.Normalized.Z);
            double longitude = Math.Atan2(eye.Y, eye.X);
            int row = (int)((latitude + Math.PI * 0.5) / _latitudeStep);
            int rowReach = (int)Math.Ceiling(Reach / CellSize) + 2;
            for (int r = Math.Max(0, row - rowReach); r <= Math.Min(_rows - 1, row + rowReach); r++) {

                int columns = Columns(r);
                int centre = (int)((longitude + Math.PI) / (Math.PI * 2.0) * columns);
                // Longitude converges at the poles, where a whole ring can fit inside the view.
                double poleDistance = (Math.PI * 0.5 - Math.Abs(latitude)) * _body.Radius;
                int spread = poleDistance < Reach + CellSize ? columns : rowReach + 1;
                for (int c = centre - spread; c <= centre + spread; c++) {

                    for (int kind = 0; kind < 2; kind++) {

                        Key key = new Key(r, ((c % columns) + columns) % columns, kind == 1);
                        Vector3d point = Centre(key) * _body.Radius;
                        if ((point - eye.Normalized * _body.Radius).Length > Reach + CellSize) {

                            continue;

                        }
                        if (_wanted.Add(key) && !_groves.ContainsKey(key)) {

                            _queue.Add(key);

                        }

                    }

                }

            }
            _queue.Sort((a, b) => (Centre(a) * _body.Radius - eye).LengthSquared.CompareTo(
                (Centre(b) * _body.Radius - eye).LengthSquared));

        }
        // Keep a small physical neighbourhood when the map/free camera looks elsewhere.
        // These cells are prioritised over visual-only requests.
        if (_physicsFocus.HasValue) {
            Vector3d focus = _physicsFocus.Value;
            double latitude = Math.Asin(focus.Normalized.Z);
            double longitude = Math.Atan2(focus.Y, focus.X);
            int row = Math.Clamp((int)((latitude + Math.PI * 0.5) / _latitudeStep), 0, _rows - 1);
            for (int r = Math.Max(0, row - 1); r <= Math.Min(_rows - 1, row + 1); r++) {
                int columns = Columns(r);
                int centre = (int)((longitude + Math.PI) / (Math.PI * 2.0) * columns);
                for (int c = centre - 1; c <= centre + 1; c++) {
                    Key key = new(r, ((c % columns) + columns) % columns, false);
                    _wanted.Add(key);
                    if (!_groves.ContainsKey(key)) {
                        _queue.Remove(key);
                        _queue.Insert(0, key);
                    }
                }
            }
        }
        _remove.Clear();
        foreach (Key key in _groves.Keys) {

            if (!_wanted.Contains(key)) {

                _remove.Add(key);

            }

        }
        foreach (Key key in _remove) {

            Grove grove = _groves[key];
            ScatterCount -= grove.Transforms.Length;
            Obstacles.Remove(key);
            grove.Instance?.QueueFree();
            _groves.Remove(key);

        }

    }

    private Vector3d Centre(Key key) => Direction(key, 0.5, 0.5);

    private Vector3d Direction(Key key, double x, double y) {

        double latitude = -Math.PI * 0.5 + (key.Row + y) * _latitudeStep;
        double longitude = -Math.PI + (key.Column + x) / Columns(key.Row) * Math.PI * 2.0;
        return new Vector3d(Math.Cos(latitude) * Math.Cos(longitude), Math.Cos(latitude) * Math.Sin(longitude), Math.Sin(latitude));

    }

    private Color Cover(Vector3d direction) {

        double x = (Math.Atan2(direction.Y, direction.X) / (Math.PI * 2.0) + 0.5) * _width - 0.5;
        double y = (0.5 - Math.Asin(direction.Z) / Math.PI) * _height - 0.5;
        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        Color upper = Pixel(left, top).Lerp(Pixel(left + 1, top), (float)(x - left));
        Color lower = Pixel(left, top + 1).Lerp(Pixel(left + 1, top + 1), (float)(x - left));
        return upper.Lerp(lower, (float)(y - top));

    }

    private Color Pixel(int x, int y) {

        int index = (Math.Clamp(y, 0, _height - 1) * _width + ((x % _width) + _width) % _width) * 4;
        return new Color(_biomes[index] / 255.0f, _biomes[index + 1] / 255.0f,
            _biomes[index + 2] / 255.0f, _biomes[index + 3] / 255.0f);

    }

    private Grove Generate(Key key, CancellationToken cancellation) {

        try {

            return BuildGrove(key, cancellation);

        } catch (OperationCanceledException) {

            return new Grove { Key = key, Anchor = Vector3d.Zero, Transforms = Array.Empty<Transform3D>(), Colours = Array.Empty<Color>() };

        } catch (ObjectDisposedException) {

            return new Grove { Key = key, Anchor = Vector3d.Zero, Transforms = Array.Empty<Transform3D>(), Colours = Array.Empty<Color>() };

        } catch (AccessViolationException) {

            return new Grove { Key = key, Anchor = Vector3d.Zero, Transforms = Array.Empty<Transform3D>(), Colours = Array.Empty<Color>() };

        }

    }

    private Grove BuildGrove(Key key, CancellationToken cancellation) {

        Vector3d centre = Centre(key);
        Vector3d anchor = centre * (_body.Radius + _body.Terrain.Elevation(centre));
        List<Transform3D> transforms = new();
        List<Color> colours = new();
        uint seed = unchecked((uint)(key.Row * 73856093) ^ (uint)(key.Column * 19349663) ^ (key.Grass ? 83492791u : 2971215073u));
        int samples = key.Grass ? Samples : 7;
        for (int y = 0; y < samples; y++) {

            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < samples; x++) {

                Vector3d direction = Direction(key, (x + Random(ref seed)) / samples, (y + Random(ref seed)) / samples);
                Color cover = Cover(direction);
                double chance = Random(ref seed);
                double scale = key.Grass ? 0.35 + Math.Pow(Random(ref seed), 0.7) * 1.30 : 0.08 + Math.Pow(Random(ref seed), 2.0) * 0.90;
                if (_broad) { scale = key.Grass ? 1.3 + scale : 0.6 + scale * 2.4; }
                double rotation = Random(ref seed) * Math.PI * 2.0;
                double variation = Random(ref seed);
                Vector3d field = direction * (_body.Radius / 18.0);
                double patch = 0.5 + 0.5 * FullThrust.Sim.Noise.Value(field.X, field.Y, field.Z);
                Vector3d broadField = direction * (_body.Radius / 95.0);
                double colony = 0.5 + 0.5 * FullThrust.Sim.Noise.Value(broadField.X + 17.0, broadField.Y, broadField.Z);
                double clustered = Smooth(0.25, 0.72, patch * 0.55 + colony * 0.45);
                double density = key.Grass ? cover.R * (1.0 - cover.B * 0.7) * (0.12 + clustered * 0.95)
                    : (0.15 + cover.B * 0.3) * (0.35 + clustered * 1.3);
                double mosaic = Landscape.Mosaic(direction, _body.Radius);
                double woodland = Landscape.Woodland(mosaic);
                density *= key.Grass ? 0.45 + woodland * 1.15 : 0.7;
                if (chance > density || cover.A > 0.15 || Cleared(direction)) {

                    continue;

                }
                double height = _body.Terrain.Elevation(direction);
                double snowLine = 950.0 + (80.0 - 950.0) * Smooth(0.3, 0.96, Math.Abs(direction.Z));
                if (height < 1.2 || (key.Grass && height > snowLine - 30.0)) {

                    continue;

                }
                Vector3d tangent = Vector3d.Cross(Math.Abs(direction.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, direction).Normalized;
                Vector3d across = Vector3d.Cross(direction, tangent);
                double east = _body.Terrain.Elevation((direction * _body.Radius + tangent).Normalized);
                double north = _body.Terrain.Elevation((direction * _body.Radius + across).Normalized);
                double slope = Math.Sqrt((east - height) * (east - height) + (north - height) * (north - height));
                if (slope > (key.Grass ? 0.55 : 1.4)) {

                    continue;

                }
                Vector3 up = Frames.Direction((direction - tangent * (east - height) - across * (north - height)).Normalized);
                Vector3 right = Frames.Direction(tangent);
                right = (right - up * right.Dot(up)).Normalized();
                Vector3 dimensions = key.Grass ? new Vector3((float)(0.55 + variation), (float)(0.7 + patch * 0.6), (float)(1.4 - variation * 0.7)) : new Vector3((float)(0.75 + variation * 0.5), (float)(0.65 + patch * 0.6), (float)(1.2 - variation * 0.4));
                Basis basis = new Basis(right, up, right.Cross(up)).Rotated(up, (float)rotation) * Basis.FromScale(dimensions * (float)scale);
                transforms.Add(new Transform3D(basis, Frames.Direction(direction * (_body.Radius + height - (key.Grass ? 0.07 : scale * 0.20)) - anchor)));
                Color green = new Color(0.12f, 0.17f, 0.045f).Lerp(new Color(0.30f, 0.25f, 0.09f), cover.B);
                Color stone = new Color(0.25f, 0.235f, 0.20f).Lerp(new Color(0.34f, 0.22f, 0.13f), cover.B);
                green = green.Lerp(new Color(0.32f, 0.245f, 0.095f), (float)(Smooth(0.45, 0.8, colony) * 0.6));
                stone = stone.Lerp(new Color(0.40f, 0.36f, 0.29f), (float)(variation * 0.45));
                Color colour = (key.Grass ? green : stone) * (float)(0.65 + variation * 0.7);
                colour.A = (float)variation;
                colours.Add(colour);

            }

        }
        bool coastal = Landscape.Coastal(centre.Z, anchor.Length - _body.Radius, Cover(centre).B) > 0.35;
        return new Grove { Key = key, Anchor = anchor, Transforms = transforms.ToArray(), Colours = colours.ToArray(), Grass = key.Grass, Coastal = coastal };

    }

    private bool Cleared(Vector3d direction) => Landscape.OnPavement(direction, _body.Terrain, _body.Radius, 0.15);

    private void Adopt(Grove grove) {

        if (_groves.ContainsKey(grove.Key)) {

            return;

        }
        _groves.Add(grove.Key, grove);
        ScatterCount += grove.Transforms.Length;
        if (grove.Transforms.Length == 0) {

            return;

        }
        MultiMesh multi = new MultiMesh {

            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _broad && grove.Grass && grove.Coastal
                ? _palmettos[Math.Abs((grove.Key.Row ^ grove.Key.Column) % 3)]
                : _meshes[Math.Abs((grove.Key.Row ^ grove.Key.Column) % 3) * 2 + (grove.Grass ? 1 : 0)],
            InstanceCount = grove.Transforms.Length,

        };
        for (int index = 0; index < grove.Transforms.Length; index++) {

            multi.SetInstanceTransform(index, grove.Transforms[index]);
            multi.SetInstanceColor(index, grove.Colours[index]);

        }
        grove.Instance = new MultiMeshInstance3D {

            Layers = 4,
            Multimesh = multi,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 4.0f,

        };
        grove.Born = Time.GetTicksMsec();
        grove.Instance.SetInstanceShaderParameter("cell_fade", 0.0f);
        AddChild(grove.Instance);
        if (!grove.Grass) { Obstacles.Add(grove.Key, grove.Anchor, grove.Transforms, multi, grove.Instance); }

    }

    private static ArrayMesh PalmettoMesh(int variant) {

        using SurfaceTool surface = new();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        uint seed = (uint)(6131 + variant * 7919);
        for (int frond = 0; frond < 7; frond++) {

            float angle = frond * 2.399963f + variant;
            Vector3 outward = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            Vector3 side = outward.Cross(Vector3.Up);
            float rise = (float)(0.22 + Random(ref seed) * 0.16);
            Vector3 hub = outward * 0.22f + Vector3.Up * rise;
            Vector3 axis = (outward * 0.8f + Vector3.Up * (float)(0.25 + Random(ref seed) * 0.6)).Normalized();
            Vector3 normal = side.Cross(axis).Normalized();
            void Vertex(Vector3 point, Vector2 uv) {

                surface.SetNormal(normal);
                surface.SetUV(uv);
                surface.SetUV2(Vector2.One);
                surface.AddVertex(point);

            }

            Vertex(Vector3.Zero - side * 0.007f, new Vector2(0.0f, 0.0f));
            Vertex(hub, new Vector2(0.5f, 0.45f));
            Vertex(Vector3.Zero + side * 0.007f, new Vector2(1.0f, 0.0f));
            for (int leaflet = 0; leaflet < 13; leaflet++) {

                float fanAngle = (leaflet - 6) * 0.19f;
                Vector3 direction = axis * Mathf.Cos(fanAngle) + side * Mathf.Sin(fanAngle);
                Vector3 edge = normal.Cross(direction).Normalized();
                float length = (float)(0.29 + Random(ref seed) * 0.10);
                Vector3 tip = hub + direction * length - Vector3.Up * (0.03f + 0.04f * Mathf.Abs(fanAngle));
                Vector3 fold = hub + direction * length * 0.48f + normal * 0.014f;
                Vertex(hub - edge * 0.012f, new Vector2(0.0f, 0.4f));
                Vertex(tip, new Vector2(0.5f, 1.0f));
                Vertex(fold, new Vector2(0.5f, 0.65f));
                Vertex(fold, new Vector2(0.5f, 0.65f));
                Vertex(tip, new Vector2(0.5f, 1.0f));
                Vertex(hub + edge * 0.012f, new Vector2(1.0f, 0.4f));

            }

        }
        surface.Index();
        using ImporterMesh imported = new();
        imported.AddSurface(Mesh.PrimitiveType.Triangles, surface.CommitToArrays());
        imported.GenerateLods(60.0f, 25.0f, new Godot.Collections.Array());
        return imported.GetMesh();

    }

    private static ArrayMesh GrassMesh(int variant) {

        using SurfaceTool surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        uint seed = (uint)(7919 + variant * 104729);
        for (int blade = 0; blade < 9 + variant * 3; blade++) {

            float angle = (float)(Random(ref seed) * Math.PI * 2.0);
            Vector3 side = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            Vector3 root = side * (float)(Random(ref seed) * 0.27);
            float height = (float)(0.25 + Random(ref seed) * 0.48);
            float width = (float)(0.013 + Random(ref seed) * 0.021);
            Vector3 bend = side.Cross(Vector3.Up) * height * 0.32f;
            Vector3 normal = side.Cross(Vector3.Up).Normalized();
            void Vertex(Vector3 point, Vector2 uv) {

                surface.SetNormal(normal);
                surface.SetUV(uv);
                surface.SetUV2(Vector2.One);
                surface.AddVertex(point);

            }
            Vector3 middle = root + Vector3.Up * height * 0.55f + bend * 0.25f;
            Vector3 tip = root + Vector3.Up * height + bend;
            Vertex(root - side * width, new Vector2(0, 0));
            Vertex(middle + side * width * 0.65f, new Vector2(1, 0.55f));
            Vertex(root + side * width, new Vector2(1, 0));
            Vertex(root - side * width, new Vector2(0, 0));
            Vertex(middle - side * width * 0.65f, new Vector2(0, 0.55f));
            Vertex(middle + side * width * 0.65f, new Vector2(1, 0.55f));
            Vertex(middle - side * width * 0.65f, new Vector2(0, 0.55f));
            Vertex(tip, new Vector2(0.5f, 1));
            Vertex(middle + side * width * 0.65f, new Vector2(1, 0.55f));

        }
        return WithLod(surface);

    }

    private static ArrayMesh RockMesh(int variant) {

        using SurfaceTool surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        const int sides = 16;
        const int rings = 8;
        Vector3 Point(int ring, int side) {

            double theta = Math.PI * ring / rings;
            double phi = Math.PI * 2.0 * side / sides;
            double radius = 1.0 + 0.16 * Math.Sin(phi * (3.0 + variant) + theta * 5.0 + variant * 1.7) + 0.08 * Math.Cos(phi * 5.0 - theta * 3.0);
            return new Vector3((float)(Math.Sin(theta) * Math.Cos(phi) * radius),
                (float)(0.43 + Math.Cos(theta) * 0.6), (float)(Math.Sin(theta) * Math.Sin(phi) * radius * 0.72));

        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c) {

            foreach (Vector3 point in new[] { a, b, c }) {

                surface.SetNormal(((point - new Vector3(0, 0.43f, 0)) / new Vector3(1, 0.36f, 0.5184f)).Normalized());
                surface.SetUV(new Vector2(point.X, point.Z));
                surface.SetUV2(Vector2.Zero);
                surface.AddVertex(point);

            }

        }
        for (int ring = 0; ring < rings; ring++) {

            for (int side = 0; side < sides; side++) {

                Vector3 a = Point(ring, side);
                Vector3 b = Point(ring + 1, side);
                Vector3 c = Point(ring + 1, side + 1);
                Vector3 d = Point(ring, side + 1);
                if (ring > 0) { Triangle(a, d, b); }
                if (ring < rings - 1) { Triangle(d, c, b); }

            }

        }
        return WithLod(surface);

    }

    private static ArrayMesh WithLod(SurfaceTool surface) {

        surface.Index();
        using ImporterMesh imported = new();
        imported.AddSurface(Mesh.PrimitiveType.Triangles, surface.CommitToArrays());
        imported.GenerateLods(60.0f, 25.0f, new Godot.Collections.Array());
        return imported.GetMesh();

    }

    private static double Random(ref uint seed) {

        seed = unchecked(seed * 1664525u + 1013904223u);
        return (seed >> 8) / 16777216.0;

    }

    private static double Smooth(double low, double high, double value) {

        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);

    }

    public override void _ExitTree() {

        _cancellation.Cancel();
        // Observe a late worker failure without waiting on the render thread.
        _ = _job?.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    }

}

