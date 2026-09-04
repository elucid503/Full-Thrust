using System;
using System.Collections.Generic;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

internal sealed class EntryField {

    private const int FieldWidth = 160;
    private const int FieldHeight = 384;
    private const int FootprintSize = 64;
    private const int RingSegments = 48;

    private readonly Vector2[] _profile;
    private readonly float[] _front = new float[FootprintSize * FootprintSize];
    private readonly float[] _back = new float[FootprintSize * FootprintSize];
    private readonly float[] _distance = new float[FootprintSize * FootprintSize];
    private readonly int[] _nearest = new int[FootprintSize * FootprintSize];

    public ImageTexture Distance { get; }
    public ImageTexture Footprint { get; private set; }
    public Vector4 Domain { get; }
    public Vector2 FootprintExtent { get; private set; }
    public float Radius { get; }
    public float Centre { get; }
    public float Base { get; }
    public float Tip { get; }
    public float Ahead { get; private set; }
    public float Behind { get; private set; }
    public float Cosine { get; private set; } = 2.0f;

    public EntryField(Vessel vessel) {

        Base = (float)vessel.Base;
        Tip = (float)vessel.Tip;
        Centre = (Base + Tip) * 0.5f;
        Radius = (float)vessel.Profile.MaxRadius;

        List<Vector2> points = new List<Vector2>();

        foreach (Stage stage in vessel.Stages) {

            foreach (Hull.Station station in stage.Hull.Stations) {

                points.Add(new Vector2((float)station.Radius, (float)station.Z));

            }

        }

        points.Sort((a, b) => a.Y.CompareTo(b.Y));
        _profile = points.ToArray();

        float padding = Radius * 1.2f;
        Domain = new Vector4(0.0f, Base - padding, Radius + padding, Tip - Base + 2.0f * padding);
        float[] pixels = new float[FieldWidth * FieldHeight * 3];

        for (int y = 0; y < FieldHeight; y++) {

            for (int x = 0; x < FieldWidth; x++) {

                Vector2 p = new Vector2((x + 0.5f) / FieldWidth * Domain.Z, Domain.Y + (y + 0.5f) / FieldHeight * Domain.W);
                Vector2 nearest = Vector2.Zero;
                float square = float.MaxValue;

                for (int i = -1; i < _profile.Length; i++) {

                    Vector2 a = i < 0 ? new Vector2(0.0f, Base) : _profile[i];
                    Vector2 b = i + 1 == _profile.Length ? new Vector2(0.0f, Tip) : _profile[i + 1];
                    Vector2 edge = b - a;
                    float t = edge.LengthSquared() > 1.0e-10f ? Mathf.Clamp((p - a).Dot(edge) / edge.LengthSquared(), 0.0f, 1.0f) : 0.0f;
                    Vector2 candidate = a + edge * t;
                    float d = p.DistanceSquaredTo(candidate);

                    if (d < square) {

                        square = d;
                        nearest = candidate;

                    }

                }

                float sign = p.Y > Base && p.Y < Tip && p.X < vessel.RadiusAt(p.Y) ? -1.0f : 1.0f;
                float distance = Mathf.Sqrt(square);
                Vector2 normal = (p - nearest) / Mathf.Max(distance, 0.00001f) * sign;
                int offset = (y * FieldWidth + x) * 3;

                pixels[offset] = distance * sign;
                pixels[offset + 1] = normal.X;
                pixels[offset + 2] = normal.Y;

            }

        }

        using Image image = FloatImage(FieldWidth, FieldHeight, Image.Format.Rgbf, pixels);
        Distance = ImageTexture.CreateFromImage(image);

    }

    public void Project(float cosine) {

        Cosine = cosine;
        float sine = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - cosine * cosine));
        float half = (Tip - Base) * 0.5f;
        FootprintExtent = new Vector2(Radius * Mathf.Abs(cosine) + half * sine, Radius) + Vector2.One * Radius * 0.35f;
        Ahead = 0.0f;
        Behind = 0.0f;

        Array.Fill(_front, float.NegativeInfinity);
        Array.Fill(_back, float.PositiveInfinity);

        Vector3 ProjectPoint(Vector2 station, int segment) {

            float angle = Mathf.Tau * segment / RingSegments;
            float radial = station.X * Mathf.Cos(angle);
            float axial = station.Y - Centre;

            return new Vector3(radial * cosine - axial * sine, radial * sine + axial * cosine, station.X * Mathf.Sin(angle));

        }

        for (int i = -1; i < _profile.Length; i++) {

            Vector2 a = i < 0 ? new Vector2(0.0f, Base) : _profile[i];
            Vector2 b = i + 1 == _profile.Length ? new Vector2(0.0f, Tip) : _profile[i + 1];

            for (int j = 0; j < RingSegments; j++) {

                Vector3 p = ProjectPoint(a, j);
                Vector3 q = ProjectPoint(a, j + 1);
                Vector3 r = ProjectPoint(b, j);
                Vector3 s = ProjectPoint(b, j + 1);

                Raster(p, q, r);
                Raster(q, s, r);

                Ahead = Mathf.Max(Ahead, Mathf.Max(p.Y, r.Y));
                Behind = Mathf.Max(Behind, -Mathf.Min(p.Y, r.Y));

            }

        }

        // Two chamfer sweeps extend the silhouette's depth outside its edge without a rectangular cut-off.
        for (int i = 0; i < _distance.Length; i++) {

            bool covered = float.IsFinite(_front[i]);
            _distance[i] = covered ? 0.0f : 1.0e6f;
            _nearest[i] = covered ? i : -1;

        }

        Sweep(1);
        Sweep(-1);

        float[] data = new float[FootprintSize * FootprintSize * 3];

        for (int i = 0; i < _distance.Length; i++) {

            int source = Math.Max(_nearest[i], 0);
            data[i * 3] = float.IsFinite(_front[source]) ? _front[source] : 0.0f;
            data[i * 3 + 1] = float.IsFinite(_back[source]) ? _back[source] : 0.0f;
            data[i * 3 + 2] = _distance[i];

        }

        // Interior distance locates the shear layer at the footprint's perimeter.
        for (int i = 0; i < _distance.Length; i++) {

            bool covered = float.IsFinite(_front[i]);
            _distance[i] = covered ? 1.0e6f : 0.0f;
            _nearest[i] = covered ? -1 : i;

        }

        Sweep(1);
        Sweep(-1);

        for (int i = 0; i < _distance.Length; i++) {

            if (float.IsFinite(_front[i])) {

                data[i * 3 + 2] = -_distance[i];

            }

        }

        using Image image = FloatImage(FootprintSize, FootprintSize, Image.Format.Rgbf, data);

        if (Footprint == null) {

            Footprint = ImageTexture.CreateFromImage(image);

        }
        else {

            Footprint.Update(image);

        }

    }

    private void Raster(Vector3 a, Vector3 b, Vector3 c) {

        Vector2 Pixel(Vector3 p) => new Vector2((p.X / FootprintExtent.X * 0.5f + 0.5f) * FootprintSize, (p.Z / FootprintExtent.Y * 0.5f + 0.5f) * FootprintSize);

        Vector2 p = Pixel(a);
        Vector2 q = Pixel(b);
        Vector2 r = Pixel(c);
        float area = (q - p).Cross(r - p);

        if (Mathf.Abs(area) < 1.0e-6f) {

            return;

        }

        int minX = Math.Clamp((int)Mathf.Floor(Mathf.Min(p.X, Mathf.Min(q.X, r.X))), 0, FootprintSize - 1);
        int maxX = Math.Clamp((int)Mathf.Ceil(Mathf.Max(p.X, Mathf.Max(q.X, r.X))), 0, FootprintSize - 1);
        int minY = Math.Clamp((int)Mathf.Floor(Mathf.Min(p.Y, Mathf.Min(q.Y, r.Y))), 0, FootprintSize - 1);
        int maxY = Math.Clamp((int)Mathf.Ceil(Mathf.Max(p.Y, Mathf.Max(q.Y, r.Y))), 0, FootprintSize - 1);

        for (int y = minY; y <= maxY; y++) {

            for (int x = minX; x <= maxX; x++) {

                Vector2 at = new Vector2(x + 0.5f, y + 0.5f);
                float u = (q - at).Cross(r - at) / area;
                float v = (r - at).Cross(p - at) / area;
                float w = 1.0f - u - v;

                if (u < -0.001f || v < -0.001f || w < -0.001f) {

                    continue;

                }

                float depth = a.Y * u + b.Y * v + c.Y * w;
                int index = y * FootprintSize + x;
                _front[index] = Mathf.Max(_front[index], depth);
                _back[index] = Mathf.Min(_back[index], depth);

            }

        }

    }

    private void Sweep(int direction) {

        int start = direction > 0 ? 0 : FootprintSize - 1;
        int end = direction > 0 ? FootprintSize : -1;
        Vector2 cell = FootprintExtent * (2.0f / FootprintSize);

        for (int y = start; y != end; y += direction) {

            for (int x = start; x != end; x += direction) {

                int index = y * FootprintSize + x;

                for (int offset = -1; offset <= 2; offset++) {

                    int nx = offset == 2 ? x - direction : x + offset;
                    int ny = offset == 2 ? y : y - direction;

                    if (nx < 0 || nx >= FootprintSize || ny < 0 || ny >= FootprintSize) {

                        continue;

                    }

                    int other = ny * FootprintSize + nx;
                    float cost = new Vector2((nx - x) * cell.X, (ny - y) * cell.Y).Length();

                    if (_distance[other] + cost < _distance[index]) {

                        _distance[index] = _distance[other] + cost;
                        _nearest[index] = _nearest[other];

                    }

                }

            }

        }

    }

    private static Image FloatImage(int width, int height, Image.Format format, float[] values) {

        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);

        return Image.CreateFromData(width, height, false, format, bytes);

    }

}
