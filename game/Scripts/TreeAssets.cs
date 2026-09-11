using System;
using System.Collections.Generic;
using Godot;

namespace FullThrust.Game;

/// <summary>Bake the artist's node transforms into one indexed, instancing-friendly surface.</summary>
public static class TreeAssets {
    public static ArrayMesh Load(string name, int species) {
        Node3D scene = GD.Load<PackedScene>($"res://Assets/Trees/{name}.fbx").Instantiate<Node3D>();
        List<(Mesh Mesh, Transform3D Transform)> parts = new();
        void Collect(Node node, Transform3D parent) {
            Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
            if (node is MeshInstance3D instance) { parts.Add((instance.Mesh, transform)); }
            foreach (Node child in node.GetChildren()) { Collect(child, transform); }
        }
        Collect(scene, Transform3D.Identity);
        Aabb bounds = default;
        bool first = true;
        foreach (var part in parts) {
            Aabb box = part.Transform * part.Mesh.GetAabb();
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        float scale = 10.5f / bounds.Size.Y;
        using SurfaceTool surface = new();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var part in parts) {
            for (int index = 0; index < part.Mesh.GetSurfaceCount(); index++) {
                var arrays = part.Mesh.SurfaceGetArrays(index);
                Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
                Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                string material = part.Mesh.SurfaceGetMaterial(index)?.ResourceName ?? "";
                bool foliage = material.Contains("Leaves", StringComparison.OrdinalIgnoreCase);
                int count = indices.Length > 0 ? indices.Length : vertices.Length;
                Basis normalMatrix = part.Transform.Basis.Inverse().Transposed();
                for (int vertex = 0; vertex < count; vertex++) {
                    int at = indices.Length > 0 ? indices[vertex] : vertex;
                    Vector3 position = part.Transform * vertices[at];
                    position.Y -= bounds.Position.Y;
                    surface.SetNormal((normalMatrix * normals[at]).Normalized());
                    surface.SetUV(uvs.Length > at ? uvs[at] : Vector2.Zero);
                    surface.SetUV2(new Vector2(foliage ? 1 : 0, species));
                    surface.SetColor(foliage ? Colors.White : species == 2
                        ? new Color(0.56f, 0.53f, 0.46f) : new Color(0.20f, 0.12f, 0.065f));
                    surface.AddVertex(position * scale);
                }
            }
        }
        surface.Index();
        using ImporterMesh imported = new();
        imported.AddSurface(Mesh.PrimitiveType.Triangles, surface.CommitToArrays());
        imported.GenerateLods(60.0f, 25.0f, new Godot.Collections.Array());
        ArrayMesh result = imported.GetMesh();
        result.SetMeta("lod_count", imported.GetSurfaceLodCount(0));
        scene.Free();
        return result;
    }
}
