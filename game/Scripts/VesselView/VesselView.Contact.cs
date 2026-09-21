using System;
using FullThrust.Sim;
using Godot;

namespace FullThrust.Game;

public sealed partial class VesselView {
    // Imported capsule and engine silhouettes are measured after scaling and seating. Sampling
    // triangle edges at each section catches long triangles without depending on vertex density.
    private static Hull.Station[] ContactProfile(Node3D model) {
        const int sections = 64;
        Aabb bounds = Bounds(model, model.Transform);
        double bottom = bounds.Position.Y, height = bounds.Size.Y;
        double[] radii = new double[sections + 1];
        void Edge(Vector3 a, Vector3 b) {
            if (a.Y > b.Y) { (a, b) = (b, a); }
            int first = Math.Clamp((int)Math.Ceiling((a.Y - bottom) / height * sections - 0.0001), 0, sections);
            int last = Math.Clamp((int)Math.Floor((b.Y - bottom) / height * sections + 0.0001), 0, sections);
            for (int i = first; i <= last; i++) {
                float y = (float)(bottom + height * i / sections);
                float t = Math.Abs(b.Y - a.Y) < 1e-7 ? 0.0f : Mathf.Clamp((y - a.Y) / (b.Y - a.Y), 0.0f, 1.0f);
                Vector3 point = a.Lerp(b, t);
                radii[i] = Math.Max(radii[i], new Vector2(point.X, point.Z).Length());
                if (Math.Abs(b.Y - a.Y) < 1e-7) { radii[i] = Math.Max(radii[i], new Vector2(b.X, b.Z).Length()); }
            }
        }
        foreach (MeshInstance3D mesh in Meshes(model)) {
            Transform3D transform = mesh.Transform;
            for (Node parent = mesh.GetParent(); parent != null && parent != model; parent = parent.GetParent()) {
                if (parent is Node3D spatial) { transform = spatial.Transform * transform; }
            }
            if (mesh != model) { transform = model.Transform * transform; }
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++) {
                var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                int count = indices.Length > 0 ? indices.Length : vertices.Length;
                for (int i = 0; i + 2 < count; i += 3) {
                    Vector3 a = transform * vertices[indices.Length > 0 ? indices[i] : i];
                    Vector3 b = transform * vertices[indices.Length > 0 ? indices[i + 1] : i + 1];
                    Vector3 c = transform * vertices[indices.Length > 0 ? indices[i + 2] : i + 2];
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
            }
        }
        Hull.Station[] profile = new Hull.Station[sections + 1];
        for (int i = 0; i <= sections; i++) { profile[i] = new Hull.Station(bottom + height * i / sections, radii[i]); }
        return profile;
    }
}
