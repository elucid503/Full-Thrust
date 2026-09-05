using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Forest : Node3D {

    private const double CellSize = 256.0;
    private const double Reach = 1600.0;
    private const int Samples = 14;

    private readonly record struct Key(int Row, int Column);

    private sealed class Grove {

        public Key Key;
        public Vector3d Anchor;
        public Transform3D[] Trees;
        public Color[] Colours;
        public bool Conifer;
        public MultiMeshInstance3D Instance;

    }

    private CelestialBody _body;
    private byte[] _biomes;
    private int _width;
    private int _height;
    private int _rows;
    private double _latitudeStep;
    private ArrayMesh[] _meshes;
    private ShaderMaterial _material;
    private readonly Dictionary<Key, Grove> _groves = new();
    private readonly HashSet<Key> _wanted = new();
    private readonly List<Key> _queue = new();
    private readonly List<Key> _remove = new();
    private Task<Grove> _job;
    private readonly CancellationTokenSource _cancellation = new();
    private Vector3d _lastEye;
    private bool _enabled;

    public int TreeCount { get; private set; }
    public int CellCount => _groves.Count;
    public int Pending => _queue.Count + (_job == null ? 0 : 1);
    public int Failures { get; private set; }

    public void Build(CelestialBody body, Texture2D biomes) {

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
        _meshes = new[] { TreeMesh(false), TreeMesh(true) };
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Trees.gdshader") };

    }

    public void Sync(double time, Vector3d eye, float altitude) {

        Vector3d local = _body.ToBodyFixed(eye, time);
        bool enabled = altitude < Reach;
        if (enabled != _enabled || (local - _lastEye).LengthSquared > 96.0 * 96.0) {

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
                GD.PushError($"Forest generation failed: {_job.Exception.GetBaseException()}");

            }
            _job = null;

        }
        while (_queue.Count > 0 && _groves.ContainsKey(_queue[0])) {

            _queue.RemoveAt(0);

        }
        if (_job == null && _queue.Count > 0) {

            Key key = _queue[0];
            _queue.RemoveAt(0);
            _job = Task.Run(() => Generate(key, _cancellation.Token), _cancellation.Token);

        }
        Basis turn = new Basis(Vector3.Up, (float)_body.SpinAt(time));
        _material.SetShaderParameter("eye_position", Frames.Point(eye));
        foreach (Grove grove in _groves.Values) {

            if (grove.Instance != null) {

                grove.Instance.Transform = new Transform3D(turn, Frames.Point(_body.ToInertial(grove.Anchor, time)));

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
            for (int r = Math.Max(0, row - 8); r <= Math.Min(_rows - 1, row + 8); r++) {

                int columns = Columns(r);
                int centre = (int)((longitude + Math.PI) / (Math.PI * 2.0) * columns);
                // Longitude converges at the poles, where a whole ring can fit inside the view.
                double poleDistance = (Math.PI * 0.5 - Math.Abs(latitude)) * _body.Radius;
                int spread = poleDistance < Reach + CellSize ? columns : 9;
                for (int c = centre - spread; c <= centre + spread; c++) {

                    Key key = new Key(r, ((c % columns) + columns) % columns);
                    Vector3d point = Centre(key) * _body.Radius;
                    if ((point - eye.Normalized * _body.Radius).Length > Reach + CellSize) {

                        continue;

                    }
                    if (_wanted.Add(key) && !_groves.ContainsKey(key)) {

                        _queue.Add(key);

                    }

                }

            }
            _queue.Sort((a, b) => (Centre(a) * _body.Radius - eye).LengthSquared.CompareTo(
                (Centre(b) * _body.Radius - eye).LengthSquared));

        }
        _remove.Clear();
        foreach (Key key in _groves.Keys) {

            if (!_wanted.Contains(key)) {

                _remove.Add(key);

            }

        }
        foreach (Key key in _remove) {

            Grove grove = _groves[key];
            TreeCount -= grove.Trees.Length;
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

        Vector3d centre = Centre(key);
        Vector3d anchor = centre * (_body.Radius + _body.Terrain.Elevation(centre));
        List<Transform3D> trees = new();
        List<Color> colours = new();
        uint seed = unchecked((uint)(key.Row * 73856093) ^ (uint)(key.Column * 19349663));
        bool conifer = Math.Abs(centre.Z) > 0.70 || anchor.Length - _body.Radius > 380.0;
        for (int y = 0; y < Samples; y++) {

            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < Samples; x++) {

                Vector3d direction = Direction(key, (x + 0.1 + Random(ref seed) * 0.8) / Samples,
                    (y + 0.1 + Random(ref seed) * 0.8) / Samples);
                Color cover = Cover(direction);
                double chance = Random(ref seed);
                double scale = 0.75 + Random(ref seed) * 0.65;
                double rotation = Random(ref seed) * Math.PI * 2.0;
                double variation = Random(ref seed);
                if (chance > cover.G * 0.65 || cover.A > 0.15) {

                    continue;

                }
                double height = _body.Terrain.Elevation(direction);
                double snowLine = 950.0 + (80.0 - 950.0) * Smooth(0.3, 0.96, Math.Abs(direction.Z));
                if (height < 1.0 || height > snowLine - 50.0 || Cleared(direction)) {

                    continue;

                }
                Vector3d tangent = Vector3d.Cross(Math.Abs(direction.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, direction).Normalized;
                Vector3d across = Vector3d.Cross(direction, tangent);
                double east = _body.Terrain.Elevation((direction * _body.Radius + tangent * 12.0).Normalized);
                double north = _body.Terrain.Elevation((direction * _body.Radius + across * 12.0).Normalized);
                if (Math.Abs(east - height) + Math.Abs(north - height) > 9.0) {

                    continue;

                }
                Vector3 up = Frames.Direction(direction);
                Vector3 right = Frames.Direction(tangent);
                Basis basis = new Basis(right, up, right.Cross(up)).Rotated(up, (float)rotation).Scaled(Vector3.One * (float)scale);
                trees.Add(new Transform3D(basis, Frames.Direction(direction * (_body.Radius + height - 0.35) - anchor)));
                colours.Add(new Color((float)(0.78 + variation * 0.25), (float)(0.85 + variation * 0.20), 0.80f, 1.0f));

            }

        }
        return new Grove { Key = key, Anchor = anchor, Trees = trees.ToArray(), Colours = colours.ToArray(), Conifer = conifer };

    }

    private bool Cleared(Vector3d direction) {

        foreach (Terrain.Plateau plateau in _body.Terrain.Plateaus) {

            if ((direction - plateau.Centre).Length * _body.Radius < plateau.InnerRadius + 24.0) {

                return true;

            }

        }
        return false;

    }

    private void Adopt(Grove grove) {

        if (_groves.ContainsKey(grove.Key)) {

            return;

        }
        _groves.Add(grove.Key, grove);
        TreeCount += grove.Trees.Length;
        if (grove.Trees.Length == 0) {

            return;

        }
        MultiMesh multi = new MultiMesh {

            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _meshes[grove.Conifer ? 1 : 0],
            InstanceCount = grove.Trees.Length,

        };
        for (int index = 0; index < grove.Trees.Length; index++) {

            multi.SetInstanceTransform(index, grove.Trees[index]);
            multi.SetInstanceColor(index, grove.Colours[index]);

        }
        grove.Instance = new MultiMeshInstance3D {

            Multimesh = multi,
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 15.0f,

        };
        AddChild(grove.Instance);

    }

    private static ArrayMesh TreeMesh(bool conifer) {

        using SurfaceTool surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetUV(Vector2.Zero);
        Crown(surface, new Vector3(0, 3.4f, 0), new Vector3(0.23f, 3.8f, 0.23f), new Color(0.16f, 0.105f, 0.06f), 3);
        uint seed = conifer ? 91u : 17u;
        for (int cluster = 0; cluster < 64; cluster++) {

            double angle = cluster * 2.399963;
            double level = Random(ref seed);
            double spread = conifer ? 3.4 * (1.0 - level) : 3.5 * Math.Sqrt(1.0 - (level - 0.5) * (level - 0.5) * 3.0);
            double radius = Math.Sqrt(Random(ref seed)) * spread;
            Vector3 centre = new Vector3((float)(Math.Cos(angle) * radius),
                (float)(conifer ? 3.5 + level * 7.5 : 5.0 + level * 5.0), (float)(Math.Sin(angle) * radius));
            float size = conifer ? 1.05f : 1.20f;
            Color colour = conifer ? new Color(0.065f, 0.135f, 0.05f) : new Color(0.10f, 0.18f, 0.045f);
            colour *= (float)(0.8 + Random(ref seed) * 0.35);
            for (int plane = 0; plane < 3; plane++) {

                float turn = (float)(angle + plane * Math.PI / 3.0);
                Vector3 right = new Vector3(Mathf.Cos(turn), 0.0f, Mathf.Sin(turn)) * size;
                Vector3 up = new Vector3(-Mathf.Sin(turn) * 0.35f, 1.0f, Mathf.Cos(turn) * 0.35f) * size;
                Leaf(surface, centre, right, up, colour);

            }

        }
        return surface.Commit();

    }

    private static void Leaf(SurfaceTool surface, Vector3 centre, Vector3 right, Vector3 up, Color colour) {

        void Vertex(float x, float y) {

            surface.SetColor(colour);
            surface.SetUV2(Vector2.One);
            surface.SetUV(new Vector2(x, y) * 0.5f + Vector2.One * 0.5f);
            surface.SetNormal(new Vector3(centre.X * 0.3f + x * 0.4f, 0.6f + y * 0.3f, centre.Z * 0.3f).Normalized());
            surface.AddVertex(centre + right * x + up * y);

        }
        Vertex(-1, -1);
        Vertex(-1, 1);
        Vertex(1, -1);
        Vertex(1, -1);
        Vertex(-1, 1);
        Vertex(1, 1);

    }

    private static void Crown(SurfaceTool surface, Vector3 centre, Vector3 size, Color colour, int seed) {

        const int sides = 7;
        const int rings = 4;
        Vector3 Point(int ring, int side) {

            double theta = Math.PI * ring / rings;
            double phi = Math.PI * 2.0 * side / sides;
            double r = 1.0 + 0.12 * Math.Sin(phi * 3.0 + seed + theta * 7.0);
            return centre + new Vector3((float)(Math.Sin(theta) * Math.Cos(phi) * r),
                (float)Math.Cos(theta), (float)(Math.Sin(theta) * Math.Sin(phi) * r)) * size;

        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c) {

            surface.SetColor(colour);
            surface.SetUV2(Vector2.Zero);
            surface.SetNormal(((a - centre) / (size * size)).Normalized());
            surface.AddVertex(a);
            surface.SetNormal(((b - centre) / (size * size)).Normalized());
            surface.AddVertex(b);
            surface.SetNormal(((c - centre) / (size * size)).Normalized());
            surface.AddVertex(c);

        }
        for (int ring = 0; ring < rings; ring++) {

            for (int side = 0; side < sides; side++) {

                Vector3 a = Point(ring, side);
                Vector3 b = Point(ring + 1, side);
                Vector3 c = Point(ring + 1, side + 1);
                Vector3 d = Point(ring, side + 1);
                if (ring > 0) {

                    Triangle(a, d, b);

                }
                if (ring < rings - 1) {

                    Triangle(d, c, b);

                }

            }

        }

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
