using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

public sealed partial class SurfaceFlow : Node3D {

    private const int VolumeWidth = 64;
    private const int VolumeHeight = 32;
    private const float VolumeTop = 64.0f;
    private const int ParcelLimit = 256;
    private readonly SurfaceResponse _water = new();
    private readonly List<Source> _sources = new(8);
    private readonly Parcel[] _parcels = new Parcel[ParcelLimit];
    private readonly float[] _density = new float[VolumeWidth * VolumeWidth * VolumeHeight * 2];
    private readonly byte[][] _slices = new byte[VolumeWidth][];
    private readonly Godot.Collections.Array<Image> _images = new();
    private readonly byte[] _surfacePixels = new byte[SurfaceResponse.Size * SurfaceResponse.Size * 16];
    private float[] _floor = new float[SurfaceResponse.Size * SurfaceResponse.Size];
    private readonly float[] _gx = new float[VolumeWidth];
    private readonly float[] _gy = new float[VolumeHeight];
    private readonly float[] _gz = new float[VolumeWidth];
    private readonly float[] _voxelFloor = new float[VolumeWidth * VolumeWidth];
    private readonly Random _random = new(61291);
    private ImageTexture3D _volume;
    private ImageTexture _surface;
    private Image _surfaceImage;
    private ShaderMaterial _material;
    private MeshInstance3D _mesh;
    private CelestialBody _body;
    private Vector3d _anchor;
    private Vector3d _up;
    private Vector3d _side;
    private Vector3d _ahead;
    private double _accumulator;
    private float _uploadAge;
    private float _volumeAge;
    private bool _volumeEmpty = true;
    private Task<(float[] Floor, bool[] Wet)> _survey;
    private float _idle;
    private int _cursor;
    private float _emission;

    private readonly record struct Source(Vector3 Position, Vector3 Direction, float Radius, float Pressure, bool Water);

    private struct Parcel {

        public Vector3 Position;
        public Vector3 Velocity;
        public float Radius;
        public float Mass;
        public float Age;
        public bool Water;
        public bool Vapour;

    }

    public int ActiveParcels { get; private set; }
    public bool SurveyFailed { get; private set; }
    public double PeakWave => _water.PeakHeight;
    public double Milliseconds { get; private set; }

    public void Build(CelestialBody body, Vector3d anchor, Texture3D noise) {

        _body = body;
        _anchor = anchor;
        _up = anchor.Normalized;
        _side = Vector3d.Cross(Math.Abs(_up.Z) < 0.9 ? Vector3d.UnitZ : Vector3d.UnitX, _up).Normalized;
        _ahead = Vector3d.Cross(_side, _up).Normalized;
        _survey = Task.Run(() => {

            float[] floor = new float[SurfaceResponse.Size * SurfaceResponse.Size];
            bool[] wet = new bool[floor.Length];
            for (int z = 0; z < SurfaceResponse.Size; z++) {

                for (int x = 0; x < SurfaceResponse.Size; x++) {

                    Vector3d point = _anchor + _side * (x * SurfaceResponse.Spacing - SurfaceResponse.HalfExtent)
                        + _ahead * (z * SurfaceResponse.Spacing - SurfaceResponse.HalfExtent);
                    double elevation = body.Terrain.Elevation(point.Normalized);
                    int i = z * SurfaceResponse.Size + x;
                    floor[i] = (float)(Math.Max(elevation, 0.0) + body.Radius - _anchor.Length);
                    wet[i] = elevation < 0.0;

                }

            }
            return (floor, wet);

        });
        _surfaceImage = Image.CreateFromData(SurfaceResponse.Size, SurfaceResponse.Size, false, Image.Format.Rgbaf, _surfacePixels);
        _surface = ImageTexture.CreateFromImage(_surfaceImage);
        for (int z = 0; z < VolumeWidth; z++) {

            _slices[z] = new byte[VolumeWidth * VolumeHeight * 4];
            _images.Add(Image.CreateFromData(VolumeWidth, VolumeHeight, false, Image.Format.Rgba8, _slices[z]));

        }
        _volume = new ImageTexture3D();
        _volume.Create(Image.Format.Rgba8, VolumeWidth, VolumeHeight, VolumeWidth, false, _images);
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/Steam.gdshader"), RenderPriority = 3 };
        _material.SetShaderParameter("density_volume", _volume);
        _material.SetShaderParameter("flow_noise", noise);
        _material.SetShaderParameter("bounds_min", new Vector3(-64.0f, -2.0f, -64.0f));
        _material.SetShaderParameter("bounds_max", new Vector3(64.0f, VolumeTop, 64.0f));
        _mesh = new MeshInstance3D {

            Mesh = new BoxMesh { Size = Vector3.One },
            CustomAabb = new Aabb(new Vector3(-64.0f, -2.0f, -64.0f), new Vector3(128.0f, VolumeTop + 2.0f, 128.0f)),
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = 2,

        };
        AddChild(_mesh);

    }

    public bool Contains(Vector3d point) => (point - _anchor).LengthSquared < 48.0 * 48.0;

    public double HeightOffset(Vector3d point) {

        Vector3 local = Local(point - _anchor);
        return _water.DisplacementAt(local.X, local.Z);

    }

    private Vector3 Local(Vector3d point) => new((float)Vector3d.Dot(point, _side), (float)Vector3d.Dot(point, _up), (float)Vector3d.Dot(point, _ahead));

    public void Strike(Vector3d point, Vector3d direction, float radius, float pressure, bool water) {

        if (_sources.Count >= 8) { return; }
        _sources.Add(new Source(Local(point - _anchor), Local(direction), radius, pressure, water));
        _idle = 0.0f;

    }

    public bool Expired => SurveyFailed || _idle > 20.0f && ActiveParcels == 0 && PeakWave < 0.003;

    public void Advance(float elapsed, double time) {

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_survey != null && _survey.IsFaulted) {

            GD.PushError($"Surface flow survey failed: {_survey.Exception}");
            SurveyFailed = true;
            _survey = null;

        }
        if (_survey != null && _survey.IsCompletedSuccessfully) {

            var survey = _survey.GetAwaiter().GetResult();
            _floor = survey.Floor;
            for (int z = 0; z < SurfaceResponse.Size; z++) {

                for (int x = 0; x < SurfaceResponse.Size; x++) { _water.SetWet(x, z, survey.Wet[z * SurfaceResponse.Size + x]); }

            }
            for (int z = 0; z < VolumeWidth; z++) {

                for (int x = 0; x < VolumeWidth; x++) {

                    _voxelFloor[z * VolumeWidth + x] = FloorAt(new Vector3((x + 0.5f) * 128.0f / VolumeWidth - 64.0f, 0.0f,
                        (z + 0.5f) * 128.0f / VolumeWidth - 64.0f));

                }

            }
            _survey = null;

        }
        _idle += elapsed;
        Vector3 side = Frames.Direction(_body.ToInertial(_side, time));
        Vector3 up = Frames.Direction(_body.ToInertial(_up, time));
        Vector3 ahead = Frames.Direction(_body.ToInertial(_ahead, time));
        GlobalTransform = new Transform3D(new Basis(side, up, ahead), Frames.Point(_body.ToInertial(_anchor, time)));
        _water.ClearForcing();
        foreach (Source source in _sources) {

            if (source.Water) { _water.Press(source.Position.X, source.Position.Z, source.Radius, source.Pressure); }

        }
        _accumulator += Math.Min(elapsed, 0.1f);
        while (_accumulator >= SurfaceResponse.StepSeconds) {

            _water.Step();
            StepParcels((float)SurfaceResponse.StepSeconds);
            _accumulator -= SurfaceResponse.StepSeconds;

        }
        _sources.Clear();
        _uploadAge += elapsed;
        _volumeAge += elapsed;
        if (_uploadAge >= 1.0f / 30.0f) {

            Upload();
            _uploadAge %= 1.0f / 30.0f;

        }
        _mesh.Visible = ActiveParcels > 0;
        _material.SetShaderParameter("sun_local", GlobalBasis.Inverse() * Main.SunDirection);
        _material.SetShaderParameter("daylight", Mathf.Max(up.Dot(Main.SunDirection), 0.0f));
        Milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    }

    public void Bind(ShaderMaterial material, int index, double time, bool texturesChanged) {

        string suffix = index.ToString();
        if (texturesChanged) { material.SetShaderParameter("response_map" + suffix, _surface); }
        material.SetShaderParameter("response_anchor" + suffix, Frames.Direction(_body.ToInertial(_anchor, time)));
        material.SetShaderParameter("response_side" + suffix, Frames.Direction(_body.ToInertial(_side, time)));
        material.SetShaderParameter("response_ahead" + suffix, Frames.Direction(_body.ToInertial(_ahead, time)));

    }

    private void StepParcels(float dt) {

        _emission += dt * 36.0f;
        while (_emission >= 1.0f) {

            _emission -= 1.0f;
            foreach (Source source in _sources) {

                float speed = Mathf.Clamp(Mathf.Sqrt(source.Pressure / (source.Water ? 1000.0f : 350.0f)) * 2.5f, 0.0f, 22.0f);
                if (speed < 0.5f) { continue; }
                float angle = _cursor * 2.3999632f + (float)_random.NextDouble() * 0.35f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                bool vapour = source.Water && _cursor % 3 == 0;
                float radius = Mathf.Clamp(source.Radius * 0.45f, 1.2f, 3.0f);
                Vector3 tangent = new Vector3(source.Direction.X, 0.0f, source.Direction.Z);
                _parcels[_cursor++ % ParcelLimit] = new Parcel {

                    Position = source.Position + radial * source.Radius * 0.75f + Vector3.Up * radius,
                    Velocity = radial * speed + tangent * speed * 1.5f + Vector3.Up * speed * (vapour ? 0.6f : 0.32f),
                    Radius = radius,
                    Mass = radius * radius * radius * Mathf.Clamp(speed * 0.05f, 0.02f, 0.9f),
                    Water = source.Water,
                    Vapour = vapour,

                };

            }

        }
        ActiveParcels = 0;
        for (int i = 0; i < ParcelLimit; i++) {

            ref Parcel parcel = ref _parcels[i];
            if (parcel.Mass <= 0.0001f) { continue; }
            parcel.Age += dt;
            float drag = parcel.Vapour ? 0.55f : (parcel.Water ? 0.8f : 1.3f);
            parcel.Velocity *= Mathf.Exp(-drag * dt);
            parcel.Velocity.Y += (parcel.Vapour ? 2.5f * Mathf.Exp(-parcel.Age / 4.0f) : (parcel.Water ? -9.81f : -0.6f)) * dt;
            parcel.Position += parcel.Velocity * dt;
            parcel.Radius += dt * (parcel.Vapour ? 0.9f : 0.5f);
            parcel.Mass *= Mathf.Exp(-dt / (parcel.Vapour ? 7.0f : 5.0f));
            float floor = FloorAt(parcel.Position);
            if (parcel.Position.Y < floor + 0.25f) {

                if (parcel.Water && !parcel.Vapour) {

                    _water.Deposit(parcel.Position.X, parcel.Position.Z, parcel.Mass * 0.2f);
                    parcel.Mass = 0.0f;
                    continue;

                }
                parcel.Position.Y = floor + 0.25f;
                parcel.Velocity.Y = Math.Max(parcel.Velocity.Y, 0.0f);

            }
            if (parcel.Age > 14.0f || Math.Abs(parcel.Position.X) > 62.0f || Math.Abs(parcel.Position.Z) > 62.0f || parcel.Position.Y > 62.0f) {

                parcel.Mass = 0.0f;
                continue;

            }
            ActiveParcels++;

        }

    }

    private float FloorAt(Vector3 p) {

        int x = Mathf.Clamp(Mathf.RoundToInt((p.X + 64.0f) * 0.5f), 0, SurfaceResponse.Size - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((p.Z + 64.0f) * 0.5f), 0, SurfaceResponse.Size - 1);
        return _floor[z * SurfaceResponse.Size + x] + (float)_water.DisplacementAt(p.X, p.Z);

    }

    private void Upload() {

        for (int z = 0; z < SurfaceResponse.Size; z++) {

            for (int x = 0; x < SurfaceResponse.Size; x++) {

                int offset = (z * SurfaceResponse.Size + x) * 16;
                BitConverter.TryWriteBytes(_surfacePixels.AsSpan(offset, 4), (float)_water.Height(x, z));
                BitConverter.TryWriteBytes(_surfacePixels.AsSpan(offset + 4, 4), (float)_water.VelocityX(x, z));
                BitConverter.TryWriteBytes(_surfacePixels.AsSpan(offset + 8, 4), (float)_water.VelocityZ(x, z));
                BitConverter.TryWriteBytes(_surfacePixels.AsSpan(offset + 12, 4), (float)_water.Foam(x, z));

            }

        }
        _surfaceImage.SetData(SurfaceResponse.Size, SurfaceResponse.Size, false, Image.Format.Rgbaf, _surfacePixels);
        _surface.Update(_surfaceImage);
        if (_volumeAge < 1.0f / 20.0f || (ActiveParcels == 0 && _volumeEmpty)) { return; }
        _volumeAge %= 1.0f / 20.0f;
        _volumeEmpty = ActiveParcels == 0;
        for (int z = 0; z < VolumeWidth; z++) {

            for (int x = 0; x < VolumeWidth; x++) {

                _voxelFloor[z * VolumeWidth + x] = FloorAt(new Vector3((x + 0.5f) * 128.0f / VolumeWidth - 64.0f, 0.0f,
                    (z + 0.5f) * 128.0f / VolumeWidth - 64.0f));

            }

        }
        Array.Clear(_density);
        foreach (Parcel parcel in _parcels) {

            if (parcel.Mass <= 0.0001f) { continue; }
            Splat(parcel);

        }
        for (int z = 0; z < VolumeWidth; z++) {

            for (int y = 0; y < VolumeHeight; y++) {

                for (int x = 0; x < VolumeWidth; x++) {

                    int i = ((z * VolumeHeight + y) * VolumeWidth + x) * 2;
                    int pixel = (y * VolumeWidth + x) * 4;
                    _slices[z][pixel] = (byte)Math.Clamp(_density[i] * 255.0f, 0.0f, 255.0f);
                    _slices[z][pixel + 1] = (byte)Math.Clamp(_density[i + 1] * 255.0f, 0.0f, 255.0f);

                }

            }
            _images[z].SetData(VolumeWidth, VolumeHeight, false, Image.Format.Rgba8, _slices[z]);

        }
        _volume.Update(_images);

    }

    private void Splat(Parcel parcel) {

        float cell = 128.0f / VolumeWidth;
        float dy = (VolumeTop + 2.0f) / VolumeHeight;
        float radius = Mathf.Max(parcel.Radius, cell * 1.25f);
        float support = radius * 2.3f;
        int x0 = Mathf.Clamp((int)((parcel.Position.X - support + 64.0f) / cell), 0, VolumeWidth - 1);
        int x1 = Mathf.Clamp((int)((parcel.Position.X + support + 64.0f) / cell), 0, VolumeWidth - 1);
        int z0 = Mathf.Clamp((int)((parcel.Position.Z - support + 64.0f) / cell), 0, VolumeWidth - 1);
        int z1 = Mathf.Clamp((int)((parcel.Position.Z + support + 64.0f) / cell), 0, VolumeWidth - 1);
        int y0 = Mathf.Clamp((int)((parcel.Position.Y - support + 2.0f) / dy), 0, VolumeHeight - 1);
        int y1 = Mathf.Clamp((int)((parcel.Position.Y + support + 2.0f) / dy), 0, VolumeHeight - 1);
        float concentration = parcel.Mass / (radius * radius * radius);
        float inverseRadius = 1.0f / (radius * radius);
        for (int x = x0; x <= x1; x++) {

            float offset = (x + 0.5f) * cell - 64.0f - parcel.Position.X;
            _gx[x] = Mathf.Exp(-offset * offset * inverseRadius);

        }
        for (int z = z0; z <= z1; z++) {

            float offset = (z + 0.5f) * cell - 64.0f - parcel.Position.Z;
            _gz[z] = Mathf.Exp(-offset * offset * inverseRadius);

        }
        for (int y = y0; y <= y1; y++) {

            float offset = (y + 0.5f) * dy - 2.0f - parcel.Position.Y;
            _gy[y] = Mathf.Exp(-offset * offset * inverseRadius);

        }
        int channel = parcel.Water ? 0 : 1;
        for (int z = z0; z <= z1; z++) {

            for (int y = y0; y <= y1; y++) {

                float weight = concentration * _gz[z] * _gy[y];
                float height = (y + 0.5f) * dy - 2.0f;
                int row = ((z * VolumeHeight + y) * VolumeWidth) * 2 + channel;
                for (int x = x0; x <= x1; x++) {

                    if (height < _voxelFloor[z * VolumeWidth + x]) { continue; }
                    _density[row + x * 2] += weight * _gx[x];

                }

            }

        }

    }

}
