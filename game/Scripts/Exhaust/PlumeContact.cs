using System;
using System.Runtime.CompilerServices;
using FullThrust.Sim;
using Godot;

namespace FullThrust.Game;

// Two nearby receiving hulls are sufficient for the hot stream and its first wake. Textures are
// shared by every nozzle; transforms are computed from simulation state, never view update order.
internal sealed class PlumeContact {
    private sealed class Field {
        public int Stages;
        public ImageTexture Texture;
        public Vector4 Domain;
        public int Revision;
        public readonly System.Collections.Generic.List<EngineState> Engines = new();
        public Texture2DArray Hardware;
        public Vector4[] EngineDomains = new Vector4[32];
        public Vector4[] EngineBounds = new Vector4[32];
        public Transform3D[] EngineTransforms = new Transform3D[32];
        public Vector3d[] Gimbals = new Vector3d[32];
        public bool Posed;
        public int PoseRevision;
        public Aabb Bounds;
    }
    private sealed class Binding {
        public Field[] Fields = new Field[2];
        public int[] Poses = { -1, -1 };
    }
    private readonly ConditionalWeakTable<ShaderMaterial, Binding> _bindings = new();
    private static readonly ConditionalWeakTable<Vessel, Field> Fields = new();
    private readonly Vessel[] _targets = new Vessel[2];
    private readonly float[] _distances = new float[2];
    private readonly Transform3D[] _transforms = new Transform3D[2];
    private readonly Field[] _fields = new Field[2];
    public float Padding { get; private set; }

    public void Sync(Plume plume, Vessel owner) {
        Array.Clear(_targets);
        Array.Fill(_distances, float.MaxValue);
        Padding = 0.0f;
        if (owner == null || Flight.Active == null) { return; }
        void Consider(Vessel vessel) {
            if (vessel == owner) { return; }
            Vector3 position = plume.ToLocal(Frames.Point(vessel.Position));
            float bound = (float)VesselCollision.Radius(vessel);
            float reach = plume.ExitRadius * (float)ExhaustInteraction.ReachRadii;
            if (position.Y > bound || position.Y < -reach - bound || new Vector2(position.X, position.Z).Length() > bound + reach * 0.65f) { return; }
            float distance = position.LengthSquared();
            int slot = distance < _distances[0] ? 0 : distance < _distances[1] ? 1 : -1;
            if (slot < 0) { return; }
            if (slot == 0) { _targets[1] = _targets[0]; _distances[1] = _distances[0]; }
            _targets[slot] = vessel; _distances[slot] = distance;
        }
        Consider(Flight.Active.Vessel);
        foreach (Flight.Tracked track in Flight.Active.Debris) { Consider(track.Vessel); }
        for (int i = 0; i < 2; i++) {
            Vessel target = _targets[i];
            if (target == null) { continue; }
            Field field = Fields.GetValue(target, Bake);
            if (field.Revision != VesselSurface.Revision(target)) { Fields.Remove(target); field = Fields.GetValue(target, Bake); }
            _fields[i] = field;
            Basis basis = new Basis(Frames.Rotation(target.Orientation));
            Transform3D hull = new Transform3D(basis, Frames.Point(target.Position) - basis.Y * (float)target.CentreOfMassZ);
            _transforms[i] = hull.AffineInverse() * plume.GlobalTransform;
            bool changed = !field.Posed;
            for (int j = 0; j < field.Engines.Count; j++) {
                EngineState engine = field.Engines[j];
                if (field.Posed && (field.Gimbals[j] - engine.Gimbal).LengthSquared < 1e-16) { continue; }
                changed = true;
                field.Gimbals[j] = engine.Gimbal;
                Transform3D mount = new Transform3D(new Basis(Frames.Rotation(engine.ContactRotation)), Frames.Direction(engine.Mount));
                field.EngineTransforms[j] = mount.AffineInverse();
                Vector4 domain = field.EngineDomains[j];
                Vector3 centre = mount * new Vector3(0, domain.Y + domain.W * 0.5f, 0);
                field.EngineBounds[j] = new Vector4(centre.X, centre.Y, centre.Z, new Vector2(domain.Z, domain.W * 0.5f).Length());
            }
            if (changed) {
                field.Posed = true; field.PoseRevision++;
                Vector4 domain = field.Domain;
                field.Bounds = new Aabb(new Vector3(-domain.Z, domain.Y, -domain.Z), new Vector3(domain.Z * 2, domain.W, domain.Z * 2));
                for (int j = 0; j < field.Engines.Count; j++) {
                    Vector4 d = field.EngineDomains[j];
                    Aabb box = new Aabb(new Vector3(-d.Z, d.Y, -d.Z), new Vector3(d.Z * 2, d.W, d.Z * 2));
                    field.Bounds = field.Bounds.Merge(field.EngineTransforms[j].AffineInverse() * box);
                }
            }
            Padding = Mathf.Max(Padding, (float)target.Profile.MaxRadius * 1.5f);
        }
    }

    public void Write(ShaderMaterial material, Vector3 offset, float reach = float.PositiveInfinity) {
        int count = _targets[0] == null ? 0 : _targets[1] == null ? 1 : 2;
        Binding binding = _bindings.GetValue(material, _ => new Binding());
        int written = 0;
        for (int i = 0; i < count; i++) {
            Field field = _fields[i];
            Transform3D transform = _transforms[i] * new Transform3D(Basis.Identity, offset);
            Aabb bounds = transform.AffineInverse() * field.Bounds;
            if (bounds.End.Y < -reach) { continue; }
            string suffix = written.ToString();
            material.SetShaderParameter("contact_transform" + suffix, transform);
            material.SetShaderParameter("contact_bounds_min" + suffix, bounds.Position);
            material.SetShaderParameter("contact_bounds_max" + suffix, bounds.End);
            bool replaced = binding.Fields[written] != field;
            if (replaced) {
                binding.Fields[written] = field;
                material.SetShaderParameter("contact_field" + suffix, field.Texture);
                material.SetShaderParameter("contact_domain" + suffix, field.Domain);
                material.SetShaderParameter("contact_engine_count" + suffix, field.Engines.Count);
                if (field.Engines.Count > 0) {
                    material.SetShaderParameter("contact_hardware" + suffix, field.Hardware);
                    material.SetShaderParameter("contact_engine_domains" + suffix, field.EngineDomains);
                }
            }
            if (field.Engines.Count > 0 && (replaced || binding.Poses[written] != field.PoseRevision)) {
                material.SetShaderParameter("contact_engine_bounds" + suffix, field.EngineBounds);
                Godot.Collections.Array<Transform3D> transforms = new(field.EngineTransforms);
                using Godot.Collections.Array nativeTransforms = (Godot.Collections.Array)transforms;
                material.SetShaderParameter("contact_engine_transforms" + suffix, transforms);
                binding.Poses[written] = field.PoseRevision;
            }
            written++;
        }
        material.SetShaderParameter("contact_count", written);
    }

    private static Field Bake(Vessel vessel) {
        float radius = (float)vessel.Profile.MaxRadius;
        foreach (Stage stage in vessel.Stages) { radius = Mathf.Max(radius, (float)(stage.ContactHull ?? stage.Hull).MaxRadius); }
        float padding = Mathf.Max(radius, 0.5f);
        Vector4 domain = new Vector4(0.0f, (float)vessel.Base - padding, radius + padding, (float)(vessel.Tip - vessel.Base) + padding * 2.0f);
        using Image hull = BakeImage(domain, p => VesselSurface.HullDistance(vessel, p));
        Field field = new Field { Stages = vessel.StageCount, Revision = VesselSurface.Revision(vessel), Domain = domain, Texture = ImageTexture.CreateFromImage(hull) };
        Array.Fill(field.EngineTransforms, Transform3D.Identity);
        Godot.Collections.Array<Image> images = new();
        using Godot.Collections.Array nativeImages = (Godot.Collections.Array)images;
        foreach (Stage stage in vessel.Stages) {
            foreach (EngineState engine in stage.EngineStates) {
                if (field.Engines.Count == 32 || engine.ContactProfile == null || engine.ContactProfile.Length < 2) { continue; }
                Hull.Station[] profile = engine.ContactProfile;
                float widest = 0.0f;
                foreach (Hull.Station ring in profile) { widest = Mathf.Max(widest, (float)ring.Radius); }
                Vector4 section = new Vector4(0, (float)profile[0].Z - 0.1f, widest + 0.2f, (float)(profile[^1].Z - profile[0].Z) + 0.2f);
                field.EngineDomains[field.Engines.Count] = section;
                field.Engines.Add(engine);
                images.Add(BakeImage(section, p => VesselSurface.ProfileDistance(profile, p)));
            }
        }
        if (images.Count > 0) {
            field.Hardware = new Texture2DArray();
            field.Hardware.CreateFromImages(images);
            foreach (Image image in images) { image.Dispose(); }
        }
        return field;
    }

    private static Image BakeImage(Vector4 domain, Func<Vector3d, double> distance) {
        const int width = 128, height = 256;
        float[] pixels = new float[width * height * 3];
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                Vector3d p = new Vector3d((x + 0.5) / width * domain.Z, 0.0, domain.Y + (y + 0.5) / height * domain.W);
                int at = (y * width + x) * 3;
                pixels[at] = (float)distance(p);
            }
        }
        // Differentiate the already-baked distances instead of querying the entire vessel
        // four additional times per texel when a receiver first enters the plume.
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                int left = Math.Max(x - 1, 0), right = Math.Min(x + 1, width - 1);
                int down = Math.Max(y - 1, 0), up = Math.Min(y + 1, height - 1);
                int at = (y * width + x) * 3;
                pixels[at + 1] = (pixels[(y * width + right) * 3] - pixels[(y * width + left) * 3]) / ((right - left) * domain.Z / width);
                pixels[at + 2] = (pixels[(up * width + x) * 3] - pixels[(down * width + x) * 3]) / ((up - down) * domain.W / height);
            }
        }
        byte[] bytes = new byte[pixels.Length * sizeof(float)];
        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        return Image.CreateFromData(width, height, false, Image.Format.Rgbf, bytes);
    }
}
