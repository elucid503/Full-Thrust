using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FullThrust.Sim;

using Godot;

namespace FullThrust.Game;

/// <summary>The solid surface: a quadtree over the six faces of a cube, subdivided towards whoever
/// is looking at it. Every vertex is placed by <see cref="Terrain"/>, the same function the physics
/// tests contact against, so the ground drawn and the ground flown into are one surface.</summary>
public sealed partial class Ground : Node3D {

    /// <summary>Quads across one patch. A patch is always this many, whatever it spans.</summary>
    public const int Grid = 32;

    // A patch splits once the eye is inside this many patch-edges of it. Straight distance rather
    // than a projected error: over a sphere the two agree to within a few percent, and this one
    // needs no bounding volume to be right before the patch has been built.
    private const double SplitFactor = 3.2;

    // Merged back a little further out than it split, so a patch sitting on the boundary does not
    // build and free itself once a frame for as long as the camera hovers there.
    private const double MergeFactor = 4.0;

    // Sixteen halvings of a two-thousand-kilometre face leaves a thirty-metre patch, and a vertex
    // every metre. Past that the detail spectrum has nothing left to say.
    private const int MaxLevel = 16;

    // Mesh upload and render-server adoption happen on the main thread. A millisecond budget rather
    // than a count: a descent from orbit finishes a burst of coarse patches at once, and taking two
    // a frame left the ground crawling towards its resolution for seconds. One always gets through,
    // so the tree can never stall behind a single slow mesh.
    private const double AdoptionBudget = 2.5;

    // In flight at once. Held under the core count so the workers building patches cannot take
    // every thread the frame itself needs.
    private static readonly int PendingLimit = Math.Clamp(System.Environment.ProcessorCount - 2, 4, 12);

    // Skirts, not stitching. Neighbouring patches meet at different levels wherever the tree steps,
    // and a wall dropped from every border vertex covers the gap for a hundred lines less code.
    private const double SkirtQuads = 3.0;

    // Relief a patch is assumed to hold before it has been built. Once it has, it carries the sphere
    // its own vertices actually needed.
    private const double AssumedRelief = 400.0;

    // Deepest sea the depth channel resolves, metres. Square-rooted into eight bits, which leaves
    // a tenth of a metre at the shoreline, where the shading of it actually matters.
    private const double SoundingLimit = 1600.0;

    /// <summary>Face basis: outward normal, then the axes the (s, t) span runs along. Right crossed
    /// into up is the normal on every one, so a patch is wound the same way whichever face it is on.
    /// tools/planet_maps.py projects the imagery onto exactly these.</summary>
    private static readonly Vector3d[,] Faces = {

        { new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(0.0, 0.0, 1.0) },
        { new Vector3d(-1.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 1.0), new Vector3d(0.0, 1.0, 0.0) },
        { new Vector3d(0.0, 1.0, 0.0), new Vector3d(0.0, 0.0, 1.0), new Vector3d(1.0, 0.0, 0.0) },
        { new Vector3d(0.0, -1.0, 0.0), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 0.0, 1.0) },
        { new Vector3d(0.0, 0.0, 1.0), new Vector3d(1.0, 0.0, 0.0), new Vector3d(0.0, 1.0, 0.0) },
        { new Vector3d(0.0, 0.0, -1.0), new Vector3d(0.0, 1.0, 0.0), new Vector3d(1.0, 0.0, 0.0) },

    };

    private sealed class Surface {

        public Vector3[] Vertices;
        public Vector3[] Normals;
        public float[] Tangents;
        public Vector2[] Coordinates;
        public Vector2[] Detail;
        public Color[] Depths;
        public int[] Indices;
        public float[] ParentOffsets;

        public Vector3d Anchor;

        public double Bound;

    }

    private sealed class Patch {

        public int Face;
        public int Level;

        public double S;
        public double T;
        public double Span;

        /// <summary>Body-fixed point the mesh is built about, and the sphere that holds the mesh.</summary>
        public Vector3d Anchor;

        public double Bound;

        /// <summary>Arc the patch spans on the ground, metres.</summary>
        public double Edge;
        public double ActivatedAt = -1.0;
        public float Coarseness;

        public Patch[] Children;

        public MeshInstance3D Instance;

        public Task<Surface> Job;
        public CancellationTokenSource Cancellation;

        public bool Built => Instance != null;

        public bool Grown {

            get {

                if (Children == null) {

                    return false;

                }

                foreach (Patch child in Children) {

                    if (!child.Built) {

                        return false;

                    }

                }

                return true;

            }

        }

    }

    private CelestialBody _body;
    private Terrain _terrain;

    private double _radius;

    private Patch[] _roots;

    private ShaderMaterial[] _materials;

    private readonly List<Task<Surface>> _jobs = new();
    private int _adopted;
    private long _adoptionDeadline;
    public int WorkerFailures { get; private set; }
    public int PendingJobs => _jobs.Count;

    /// <summary>Patches standing in the scene, and how far the tree has gone down. Both are read
    /// straight out by the debug bridge, which is where the tuning is done from.</summary>
    public int PatchCount { get; private set; }

    public int DeepestLevel { get; private set; }

    /// <summary>Milliseconds the last traversal took on the main thread. The tree is walked every
    /// frame and the meshes are handed over on this thread, so it is the one number worth watching.</summary>
    public double SyncMilliseconds { get; private set; }

    public void Build(CelestialBody body, ShaderMaterial[] materials) {

        _body = body;
        _terrain = body.Terrain ?? throw new InvalidOperationException("no terrain survey; run tools/planet_maps.py heightfield");
        _radius = body.Radius;
        _materials = materials;

        _roots = new Patch[6];

        for (int face = 0; face < 6; face++) {

            _roots[face] = Make(face, 0, -1.0, -1.0, 2.0);

        }

    }

    private Patch Make(int face, int level, double s, double t, double span) {

        Patch patch = new Patch {

            Face = face,
            Level = level,

            S = s,
            T = t,
            Span = span,

            Edge = Mathf.Pi * 0.25 * _radius * span,

        };

        patch.Anchor = Direction(face, s + span * 0.5, t + span * 0.5) * _radius;
        patch.Bound = patch.Edge * 0.9 + AssumedRelief;

        return patch;

    }

    private static Vector3d Direction(int face, double s, double t) {

        Vector3d vector = Faces[face, 0] + Faces[face, 1] * s + Faces[face, 2] * t;

        return vector.Normalized;

    }

    /// <summary>One traversal: decide the tree against the eye, hand any finished mesh into the
    /// scene, and re-place everything standing through the floating origin.</summary>
    public void Sync(double time, Vector3d eye) {

        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        _adoptionDeadline = started + (long)(AdoptionBudget * System.Diagnostics.Stopwatch.Frequency * 0.001);

        Vector3d local = _body.ToBodyFixed(eye, time);

        for (int index = _jobs.Count - 1; index >= 0; index--) {

            Task<Surface> job = _jobs[index];

            if (!job.IsCompleted) {

                continue;

            }

            if (job.IsFaulted) {

                WorkerFailures++;
                GD.PushError($"terrain patch build failed: {job.Exception.GetBaseException()}");

            }

            _jobs.RemoveAt(index);

        }
        _adopted = 0;

        PatchCount = 0;
        DeepestLevel = 0;

        foreach (Patch root in _roots) {

            Visit(root, local);

        }

        Basis turn = new Basis(Vector3.Up, (float)_body.SpinAt(time));

        foreach (Patch root in _roots) {

            Place(root, time, turn);

        }

        SyncMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    }

    private void Visit(Patch patch, Vector3d eye) {

        if (!Overhorizon(patch, eye)) {

            Prune(patch);
            Hide(patch);

            return;

        }

        double reach = Distance(patch, eye);

        bool wanted = patch.Level < MaxLevel
            && reach < (patch.Children != null ? MergeFactor : SplitFactor) * patch.Edge;

        if (!wanted) {

            Prune(patch);
            Request(patch);

            return;

        }

        // The parent is not given up until every child can stand in for it, so splitting never
        // opens a hole in the ground while four meshes are being built.
        if (patch.Children == null) {

            Request(patch);

            if (_jobs.Count >= PendingLimit) {

                return;

            }

            Grow(patch);

        }

        if (!patch.Grown) {

            Request(patch);

            foreach (Patch child in patch.Children) {

                Request(child);
                Hide(child);

            }

            return;

        }

        Hide(patch);

        foreach (Patch child in patch.Children) {

            child.ActivatedAt = child.ActivatedAt < 0.0 ? Time.GetTicksMsec() * 0.001 : child.ActivatedAt;
            child.Coarseness = (float)Math.Clamp((reach / patch.Edge - SplitFactor) / (MergeFactor - SplitFactor), 0.0, 1.0);
            Visit(child, eye);

        }

    }

    private void Grow(Patch patch) {

        double half = patch.Span * 0.5;

        patch.Children = new[] {

            Make(patch.Face, patch.Level + 1, patch.S, patch.T, half),
            Make(patch.Face, patch.Level + 1, patch.S + half, patch.T, half),
            Make(patch.Face, patch.Level + 1, patch.S, patch.T + half, half),
            Make(patch.Face, patch.Level + 1, patch.S + half, patch.T + half, half),

        };

    }

    // A patch entirely under the horizon plane cannot be seen from the eye, whatever the frustum
    // says. From orbit that is most of the planet, and culling it here is what keeps the tree small.
    private bool Overhorizon(Patch patch, Vector3d eye) {

        return Vector3d.Dot(patch.Anchor, eye) + patch.Bound * eye.Length >= _radius * _radius;

    }

    private double Distance(Patch patch, Vector3d eye) {

        return Math.Max((eye - patch.Anchor).Length - patch.Bound, 0.0);

    }

    private void Request(Patch patch) {

        PatchCount++;

        DeepestLevel = Math.Max(DeepestLevel, patch.Level);

        // A build that threw would otherwise never complete and never be retried, and the patch
        // would stay a hole in the ground for the rest of the flight.
        if (patch.Job != null && patch.Job.IsCompleted && !patch.Job.IsCompletedSuccessfully) {

            patch.Job = null;
            patch.Cancellation.Dispose();
            patch.Cancellation = null;

        }

        if (patch.Job != null && patch.Job.IsCompletedSuccessfully
            && (_adopted == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < _adoptionDeadline)) {

            Adopt(patch);

        }

        if (patch.Instance != null) {

            patch.Instance.Visible = true;

            return;

        }

        if (patch.Job != null || _jobs.Count >= PendingLimit) {

            return;

        }

        int face = patch.Face;

        double s = patch.S;
        double t = patch.T;
        double span = patch.Span;

        // Workers capture immutable data, never a Godot node that a scene reload can dispose.
        Terrain terrain = _terrain;
        double radius = _radius;
        patch.Cancellation = new CancellationTokenSource();
        CancellationToken cancellation = patch.Cancellation.Token;
        patch.Job = Task.Run(() => Tessellate(terrain, radius, face, s, t, span, cancellation), cancellation);
        _jobs.Add(patch.Job);

    }

    private void Adopt(Patch patch) {

        Surface surface = patch.Job.Result;

        patch.Job = null;
        patch.Cancellation.Dispose();
        patch.Cancellation = null;

        if (surface == null) {

            return;

        }

        patch.Anchor = surface.Anchor;
        patch.Bound = surface.Bound;

        patch.Instance = Assemble(surface, _materials[patch.Face]);

        AddChild(patch.Instance);

        _adopted++;

    }

    private static void Hide(Patch patch) {

        if (patch.Instance != null) {

            patch.Instance.Visible = false;

        }

    }

    private static void Prune(Patch patch) {

        if (patch.Children == null) {

            return;

        }

        foreach (Patch child in patch.Children) {

            Prune(child);

            child.Instance?.QueueFree();
            child.Instance = null;
            child.Cancellation?.Cancel();
            child.Cancellation?.Dispose();
            child.Cancellation = null;
            child.Job = null;

        }

        patch.Children = null;

    }

    private static MeshInstance3D Assemble(Surface surface, Material material) {

        Godot.Collections.Array arrays = new Godot.Collections.Array();

        arrays.Resize((int)Mesh.ArrayType.Max);

        arrays[(int)Mesh.ArrayType.Vertex] = surface.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = surface.Normals;
        arrays[(int)Mesh.ArrayType.Tangent] = surface.Tangents;
        arrays[(int)Mesh.ArrayType.Color] = surface.Depths;
        arrays[(int)Mesh.ArrayType.TexUV] = surface.Coordinates;
        arrays[(int)Mesh.ArrayType.TexUV2] = surface.Detail;
        arrays[(int)Mesh.ArrayType.Index] = surface.Indices;
        arrays[(int)Mesh.ArrayType.Custom0] = surface.ParentOffsets;

        ArrayMesh mesh = new ArrayMesh();

        Mesh.ArrayFormat format = (Mesh.ArrayFormat)((ulong)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: format);

        return new MeshInstance3D {

            Mesh = mesh,

            MaterialOverride = material,

            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // The vessel's shadow cascade is metres across and the ground is a planet; letting the
            // two share a probe or a light cull costs far more than the ground gains.
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,

        };

    }

    private void Place(Patch patch, double time, Basis turn) {

        if (patch.Instance != null && patch.Instance.Visible) {

            // Differenced against the floating origin in double and only then cut to float, so a
            // patch a million metres from the planet's centre still holds still under the camera.
            patch.Instance.Transform = new Transform3D(turn, Frames.Point(_body.ToInertial(patch.Anchor, time)));
            float arrival = patch.ActivatedAt < 0.0 ? 0.0f : (float)Math.Clamp(1.0 - (Time.GetTicksMsec() * 0.001 - patch.ActivatedAt) / 0.3, 0.0, 1.0);
            patch.Instance.SetInstanceShaderParameter("lod_morph", Math.Max(arrival, patch.Coarseness));

        }

        if (patch.Children == null) {

            return;

        }

        foreach (Patch child in patch.Children) {

            Place(child, time, turn);

        }

    }

    /// <summary>Builds one patch off the terrain function. Runs on a worker: it touches nothing but
    /// the survey, which is immutable once loaded.</summary>
    // Scratch for one patch, kept per worker. The sample grid is the same size every time and is
    // dead the moment the mesh is woven, so allocating it fresh only feeds the collector.
    [ThreadStatic] private static Vector3d[] _points;
    [ThreadStatic] private static double[] _sounding;

    private static Surface Tessellate(Terrain terrain, double radius, int face, double s, double t, double span, CancellationToken cancellation) {

        int side = Grid + 3;

        // What one quad of this patch covers on the ground. The terrain leaves off every detail
        // octave finer than that, which is most of them on a patch an orbital view is built from
        // and none of them once the vehicle is near enough to stand on one.
        double spacing = span * Math.PI * 0.25 * radius / Grid;

        Vector3d[] points = _points ??= new Vector3d[side * side];
        double[] sounding = _sounding ??= new double[side * side];

        double lowest = double.MaxValue;
        double highest = double.MinValue;

        for (int row = 0; row < side; row++) {

            cancellation.ThrowIfCancellationRequested();

            double v = t + span * (row - 1) / Grid;

            for (int column = 0; column < side; column++) {

                double u = s + span * (column - 1) / Grid;

                Vector3d direction = Direction(face, u, v);

                double elevation = terrain.Elevation(direction, spacing);

                // Clamped at the datum: below sea level the mesh is the water's own surface, which
                // is what a vehicle touches and what the shader shades as sea. The depth is kept,
                // because the colour of shallow water is the one thing that needs it.
                double standing = Math.Max(elevation, 0.0);

                lowest = Math.Min(lowest, standing);
                highest = Math.Max(highest, standing);

                int index = row * side + column;

                points[index] = direction * (radius + standing);
                sounding[index] = Math.Max(-elevation, 0.0);

            }

        }

        Vector3d anchor = Direction(face, s + span * 0.5, t + span * 0.5) * (radius + (lowest + highest) * 0.5);

        return Weave(radius, s, t, span, points, sounding, anchor);

    }

    private static Surface Weave(double radius, double s, double t, double span, Vector3d[] points, double[] sounding, Vector3d anchor) {

        int side = Grid + 3;
        int line = Grid + 1;

        int ring = line * 4 - 4;

        int count = line * line + ring;

        Vector3[] vertices = new Vector3[count];
        Vector3[] normals = new Vector3[count];
        float[] tangents = new float[count * 4];
        Vector2[] coordinates = new Vector2[count];
        Vector2[] detail = new Vector2[count];
        Color[] depths = new Color[count];
        float[] parentOffsets = new float[count * 4];

        // The detail lattice is metres from an origin each patch picks for itself, because a
        // face-wide coordinate interpolated in single precision steps visibly once a patch is a few
        // metres across. The origin is snapped to a power of two no smaller than the patch, and the
        // shader only ever tiles on powers of two under that, so the origin always lands on a whole
        // number of tiles and the join between two patches cannot be seen.
        double period = Math.Max(4096.0, Math.Pow(2.0, Math.Ceiling(Math.Log2(span * Math.PI * 0.25 * radius))));

        double offsetS = Math.Floor(radius * Math.Atan(s + span * 0.5) / period) * period;
        double offsetT = Math.Floor(-radius * Math.Atan(t + span * 0.5) / period) * period;

        double bound = 0.0;

        for (int row = 0; row < line; row++) {

            for (int column = 0; column < line; column++) {

                int sample = (row + 1) * side + (column + 1);

                Vector3d point = points[sample];

                Vector3d alongS = points[sample + 1] - points[sample - 1];
                Vector3d alongT = points[sample + side] - points[sample - side];

                Vector3d normal = Vector3d.Cross(alongS, alongT).Normalized;

                // Tangent along the face's own s axis, which is the direction both the imagery and
                // the detail lattice run in, so one frame serves every map the ground samples.
                Vector3d tangent = (alongS - normal * Vector3d.Dot(alongS, normal)).Normalized;

                int index = row * line + column;

                Vector3d offset = point - anchor;

                bound = Math.Max(bound, offset.Length);

                vertices[index] = Frames.Direction(offset);
                normals[index] = Frames.Direction(normal);

                int parentColumn = column / 2 * 2;
                int parentRow = row / 2 * 2;
                int parentSample = (parentRow + 1) * side + parentColumn + 1;
                Vector3d parent = points[parentSample];

                if (column % 2 != 0 && row % 2 != 0) {

                    parent = (points[parentSample + 2] + points[parentSample + side * 2]) * 0.5;

                } else if (column % 2 != 0) {

                    parent = (parent + points[parentSample + 2]) * 0.5;

                } else if (row % 2 != 0) {

                    parent = (parent + points[parentSample + side * 2]) * 0.5;

                }

                Vector3 parentOffset = Frames.Direction(parent - point);
                parentOffsets[index * 4] = parentOffset.X;
                parentOffsets[index * 4 + 1] = parentOffset.Y;
                parentOffsets[index * 4 + 2] = parentOffset.Z;

                Vector3 axis = Frames.Direction(tangent);

                tangents[index * 4 + 0] = axis.X;
                tangents[index * 4 + 1] = axis.Y;
                tangents[index * 4 + 2] = axis.Z;
                tangents[index * 4 + 3] = 1.0f;

                double u = s + span * column / Grid;
                double v = t + span * row / Grid;

                coordinates[index] = Coordinate(u, v);

                detail[index] = new Vector2((float)(radius * Math.Atan(u) - offsetS), (float)(-radius * Math.Atan(v) - offsetT));

                depths[index] = new Color((float)Math.Sqrt(Math.Min(sounding[sample] / SoundingLimit, 1.0)), 0.0f, 0.0f, 1.0f);

            }

        }

        int[] indices = new int[Grid * Grid * 6 + ring * 6];

        int cursor = 0;

        for (int row = 0; row < Grid; row++) {

            for (int column = 0; column < Grid; column++) {

                int corner = row * line + column;

                // Godot's front face is the one whose corners run the other way round from the
                // surface's own s-then-t order, which is what VesselView's lathe emits as outward.
                indices[cursor++] = corner;
                indices[cursor++] = corner + line;
                indices[cursor++] = corner + 1;

                indices[cursor++] = corner + 1;
                indices[cursor++] = corner + line;
                indices[cursor++] = corner + line + 1;

            }

        }

        double depth = span * Math.PI * 0.25 * radius / Grid * SkirtQuads;

        Surface surface = new Surface {

            Vertices = vertices,
            Normals = normals,
            Tangents = tangents,
            Coordinates = coordinates,
            Detail = detail,
            Depths = depths,
            Indices = indices,
            ParentOffsets = parentOffsets,

            Anchor = anchor,

            Bound = bound + depth,

        };

        Skirt(surface, cursor, line, Frames.Direction(anchor), depth);

        return surface;

    }

    // The border walked counter-clockwise as seen from outside, with a wall hung straight down from
    // every step of it. Wound the same way as the grid: along the edge, down, then along again.
    private static void Skirt(Surface surface, int cursor, int line, Vector3 anchor, double depth) {

        List<int> border = new List<int>();

        for (int column = 0; column < line - 1; column++) {

            border.Add(column);

        }

        for (int row = 0; row < line - 1; row++) {

            border.Add(row * line + line - 1);

        }

        for (int column = line - 1; column > 0; column--) {

            border.Add((line - 1) * line + column);

        }

        for (int row = line - 1; row > 0; row--) {

            border.Add(row * line);

        }

        int start = line * line;

        for (int step = 0; step < border.Count; step++) {

            int source = border[step];
            int wall = start + step;

            // Towards the planet's centre rather than into the surface: on a steep face the two are
            // far enough apart that a wall hung along the normal can break back out of the hillside.
            Vector3 inward = -(anchor + surface.Vertices[source]).Normalized();

            surface.Vertices[wall] = surface.Vertices[source] + inward * (float)depth;

            surface.Normals[wall] = surface.Normals[source];
            surface.Coordinates[wall] = surface.Coordinates[source];
            surface.Detail[wall] = surface.Detail[source];
            surface.Depths[wall] = surface.Depths[source];

            Array.Copy(surface.Tangents, source * 4, surface.Tangents, wall * 4, 4);
            Array.Copy(surface.ParentOffsets, source * 4, surface.ParentOffsets, wall * 4, 4);

        }

        for (int step = 0; step < border.Count; step++) {

            int next = (step + 1) % border.Count;

            surface.Indices[cursor++] = border[step];
            surface.Indices[cursor++] = border[next];
            surface.Indices[cursor++] = start + step;

            surface.Indices[cursor++] = start + step;
            surface.Indices[cursor++] = border[next];
            surface.Indices[cursor++] = start + next;

        }

    }

    private static Vector2 Coordinate(double s, double t) => new Vector2((float)((s + 1.0) * 0.5), (float)((1.0 - t) * 0.5));

    // Arc from the middle of the face along one axis. The detail lattice is laid out on this rather
    // than on the face coordinate, so a metre of ground stays a metre of lattice wherever it sits
    // and the pattern does not stretch towards the corners of the cube.
    public override void _ExitTree() {

        foreach (Patch root in _roots ?? Array.Empty<Patch>()) {

            Prune(root);
            root.Cancellation?.Cancel();
            root.Cancellation?.Dispose();

        }

        // Observe late faults without retaining the disposed scene or invoking engine APIs.
        foreach (Task<Surface> job in _jobs) {

            _ = job.ContinueWith(completed => { _ = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        }

    }

}
