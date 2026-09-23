using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class Forest : Node3D {

    private bool _distant;
    private double CellSize => _distant ? 1024.0 : 256.0;
    private double Reach => _distant ? 8000.0 : 1600.0;
    private int Samples => _distant ? 56 : 14;

    private readonly record struct Key(int Row, int Column);

    private sealed class Grove {

        public Key Key;
        public Vector3d Anchor;
        public Transform3D[] Trees;
        public Color[] Colours;
        public bool Conifer;
        public bool Coastal;
        public MultiMeshInstance3D Instance;

    }

    public ScatterObstacles Obstacles { get; } = new(true);

    private CelestialBody _body;
    private Terrain _terrain;
    private BiomeMap _biomes;
    private int _rows;
    private double _latitudeStep;
    private Mesh[] _meshes;
    private ShaderMaterial _material;
    internal ShaderMaterial SurfaceMaterial => _material;
    private readonly Dictionary<Key, Grove> _groves = new();
    private readonly HashSet<Key> _wanted = new();
    private readonly List<Key> _queue = new();
    private readonly List<Key> _remove = new();
    private Task<Grove[]> _job;
    private int _batchCount;
    private readonly CancellationTokenSource _cancellation = new();
    private Vector3d _lastEye;
    private Vector3d? _physicsFocus;
    private bool _enabled;

    public int TreeCount { get; private set; }
    public int CellCount => _groves.Count;
    public int Pending => _queue.Count + (_job == null ? 0 : _batchCount);
    public int Failures { get; private set; }

    public void Build(CelestialBody body, Texture2D biomes, bool distant = false) {

        _distant = distant;
        _body = body;
        _terrain = body.Terrain;
        _biomes = BiomeMap.Of(biomes);
        _rows = (int)Math.Ceiling(Math.PI * body.Radius / CellSize);
        _latitudeStep = Math.PI / _rows;
        if (_distant) {

            _meshes = new Mesh[] { new QuadMesh { Size = new Vector2(12.0f, 12.0f) } };
            _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Trees/ForestCanopy.gdshader") };
            return;

        }
        ArrayMesh broadleaf = TreeAssets.Load("Tree_1", 0);
        ArrayMesh pine = TreeAssets.Load("Pine_1", 1);
        ArrayMesh birch = TreeAssets.Load("Birch_1", 2);
        _meshes = new[] { broadleaf, pine, birch, pine, broadleaf, pine };
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Trees/Trees.gdshader") };
        _material.SetParameter("broadleaf_map", GD.Load<Texture2D>("res://Assets/Trees/Broadleaf/Tree_Leaves.png"));
        _material.SetParameter("pine_map", GD.Load<Texture2D>("res://Assets/Trees/Pine/Pine_Leaves.png"));
        _material.SetParameter("birch_map", GD.Load<Texture2D>("res://Assets/Trees/Birch/Birch_Leaves_Green.png"));

    }

    public void Sync(double time, Vector3d eye, float altitude) {

        Obstacles.Animate(time);
        Vector3d local = _body.ToBodyFixed(eye, time);
        Vector3d? physics = null;
        Flight flight = Flight.Active;
        if (!_distant && flight != null && flight.Body == _body
            && _body.HeightAboveGround(flight.Vessel.Position, time) < 100.0) {
            physics = _body.ToBodyFixed(flight.Vessel.Position, time);
        }
        bool physicsMoved = physics.HasValue != _physicsFocus.HasValue
            || (physics.HasValue && _physicsFocus.HasValue
                && (physics.Value - _physicsFocus.Value).LengthSquared > CellSize * CellSize * 0.0625);
        if (physicsMoved) { _physicsFocus = physics; }
        bool enabled = altitude < Reach;
        Vector3d surfaceEye = local.Normalized * _body.Radius;
        double movement = _distant ? 256.0 : 96.0;
        if (physicsMoved || enabled != _enabled || (surfaceEye - _lastEye).LengthSquared > movement * movement) {

            Select(local, enabled);
            _lastEye = surfaceEye;
            _enabled = enabled;

        }
        if (_job?.IsCompleted == true) {

            if (_job.IsCompletedSuccessfully) {

                foreach (Grove grove in _job.Result) {

                    if (_wanted.Contains(grove.Key)) { Adopt(grove); }

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

            // Fill several cells per worker handoff while the loading cover hides uploads.
            _batchCount = Math.Min(_queue.Count, SceneTransition.Loading ? 8 : 1);
            Key[] batch = _queue.GetRange(0, _batchCount).ToArray();
            _queue.RemoveRange(0, _batchCount);
            _job = Task.Run(() => {

                Grove[] result = new Grove[batch.Length];
                for (int i = 0; i < batch.Length; i++) {

                    _cancellation.Token.ThrowIfCancellationRequested();
                    result[i] = Generate(batch[i], _cancellation.Token);

                }
                return result;

            });

        }
        Basis turn = new Basis(Vector3.Up, (float)_body.SpinAt(time));
        _material.SetParameter("eye_position", Frames.Point(eye));
        _material.SetParameter("wind_time", (float)(time % 4096.0));
        foreach (Grove grove in _groves.Values) {

            if (grove.Instance != null) {

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
                Key key = new(r, ((c % columns) + columns) % columns);
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
            int rowReach = (int)Math.Ceiling(Reach / CellSize) + 1;
            for (int r = Math.Max(0, row - rowReach); r <= Math.Min(_rows - 1, row + rowReach); r++) {

                int columns = Columns(r);
                int centre = (int)((longitude + Math.PI) / (Math.PI * 2.0) * columns);
                // Longitude converges at the poles, where a whole ring can fit inside the view.
                double poleDistance = (Math.PI * 0.5 - Math.Abs(latitude)) * _body.Radius;
                int spread = poleDistance < Reach + CellSize ? columns : rowReach + 1;
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
                    Key key = new(r, ((c % columns) + columns) % columns);
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
            TreeCount -= grove.Trees.Length;
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

    private Color Cover(Vector3d direction) => _biomes.Cover(direction);

    private Grove Generate(Key key, CancellationToken cancellation) {

        try {

            return BuildGrove(key, cancellation);

        } catch (ObjectDisposedException) {

            return new Grove { Key = key, Anchor = Vector3d.Zero, Trees = Array.Empty<Transform3D>(), Colours = Array.Empty<Color>() };

        } catch (AccessViolationException) {

            return new Grove { Key = key, Anchor = Vector3d.Zero, Trees = Array.Empty<Transform3D>(), Colours = Array.Empty<Color>() };

        }

    }

    private Grove BuildGrove(Key key, CancellationToken cancellation) {

        Vector3d centre = Centre(key);
        Vector3d anchor = centre * (_body.Radius + _terrain.Elevation(centre));
        List<Transform3D> trees = new();
        List<Color> colours = new();
        uint seed = unchecked((uint)(key.Row * 73856093) ^ (uint)(key.Column * 19349663));
        bool conifer = Math.Abs(centre.Z) > 0.70 || anchor.Length - _body.Radius > 380.0;
        for (int y = 0; y < Samples; y++) {

            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < Samples; x++) {

                Vector3d direction = Direction(key, (x + 0.1 + Landscape.Random(ref seed) * 0.8) / Samples,
                    (y + 0.1 + Landscape.Random(ref seed) * 0.8) / Samples);
                Color cover = Cover(direction);
                double chance = Landscape.Random(ref seed);
                double scale = 0.75 + Landscape.Random(ref seed) * 0.65;
                double rotation = Landscape.Random(ref seed) * Math.PI * 2.0;
                double variation = Landscape.Random(ref seed);
                double mosaic = Landscape.Mosaic(direction, _body.Radius);
                double woodland = Landscape.Woodland(mosaic);
                if (chance > cover.G * (0.035 + woodland * 1.15) || cover.A > 0.15) {

                    continue;

                }
                double height = _terrain.Elevation(direction);
                double snowLine = 950.0 + (80.0 - 950.0) * Ocean.Smooth(0.3, 0.96, Math.Abs(direction.Z));
                if (height < 1.0 || height > snowLine - 50.0 || Cleared(direction)) {

                    continue;

                }
                Vector3d tangent = Vector3d.Cross(Math.Abs(direction.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, direction).Normalized;
                Vector3d across = Vector3d.Cross(direction, tangent);
                double east = _terrain.Elevation((direction * _body.Radius + tangent * 12.0).Normalized);
                double north = _terrain.Elevation((direction * _body.Radius + across * 12.0).Normalized);
                if (Math.Abs(east - height) + Math.Abs(north - height) > 9.0) {

                    continue;

                }
                Vector3 up = Frames.Direction(direction);
                Vector3 right = Frames.Direction(tangent);
                double coastal = Landscape.Coastal(direction.Z, height, cover.B);
                Vector3 dimensions = new((float)(1.0 + coastal * 0.2), (float)(1.0 - coastal * (0.35 + variation * 0.2)), (float)(1.0 + coastal * 0.2));
                Basis basis = new Basis(right, up, right.Cross(up)).Rotated(up, (float)rotation) * Basis.FromScale(dimensions * (float)scale);
                trees.Add(new Transform3D(basis, Frames.Direction(direction * (_body.Radius + height - 0.35) - anchor)));
                colours.Add(new Color((float)(0.78 + variation * 0.25), (float)(0.85 + variation * 0.20), 0.80f, 1.0f));

            }

        }
        bool coast = Landscape.Coastal(centre.Z, anchor.Length - _body.Radius, Cover(centre).B) > 0.35;
        return new Grove { Key = key, Anchor = anchor, Trees = trees.ToArray(), Colours = colours.ToArray(), Conifer = conifer, Coastal = coast };

    }

    private bool Cleared(Vector3d direction) => Landscape.OnPavement(direction, _terrain, _body.Radius, 0.6);

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
            Mesh = _distant ? _meshes[0] : grove.Coastal ? _meshes[(grove.Key.Row ^ grove.Key.Column) % 3 == 0 ? 1 : 0]
                : _meshes[Math.Abs((grove.Key.Row ^ grove.Key.Column) % 3) * 2 + (grove.Conifer ? 1 : 0)],
            InstanceCount = grove.Trees.Length,

        };
        for (int index = 0; index < grove.Trees.Length; index++) {

            multi.SetInstanceTransform(index, grove.Trees[index]);
            multi.SetInstanceColor(index, grove.Colours[index]);

        }
        grove.Instance = new MultiMeshInstance3D {

            Layers = 4,
            Multimesh = multi,
            MaterialOverride = _material,
            CastShadow = _distant ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            ExtraCullMargin = 15.0f,
            VisibilityRangeEnd = (float)(Reach + CellSize),

        };
        AddChild(grove.Instance);
        if (!_distant) { Obstacles.Add(grove.Key, grove.Anchor, grove.Trees, multi, grove.Instance); }

    }

    public override void _ExitTree() {

        _cancellation.Cancel();
        // Observe a late worker failure without waiting on the render thread.
        _ = _job?.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

    }

}
