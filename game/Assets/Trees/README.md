# Quaternius trees

Original artist: Quaternius. Source: [Stylized Tree Pack](https://quaternius.com/packs/stylizedtree.html)
(May 2020), CC0 1.0 Universal. The original license is included in License.txt.

Included original models: Tree_1.fbx, Birch_1.fbx, Pine_1.fbx.
Included original textures: Tree_Leaves.png, Birch_Leaves_Green.png, Pine_Leaves.png.

Retrieved from the author's public Google Drive folder on 2026-09-11:
https://drive.google.com/drive/folders/1GlrFUFcNj6KIuc4-QpVEiRcXVUzSClP2

The original FBX files are retained, without geometry edits. TreeAssets.cs bakes FBX node transforms,
normalizes each model to 10.5 metres tall, merges bark and foliage into one indexed surface and tags
the material/species in UV2, then generates distance LODs for the combined surface. Instances provide size, rotation and colour variation. Source leaf UVs
and textures are preserved; bark uses our procedural material because the source bark downloads
were unavailable. Leaf textures have mipmaps and alpha-border correction.

These are lightweight stylized models, not photogrammetry. Poly Haven's realistic tree assets were
also considered, but their listed source polygon counts were far beyond this forest's instance budget.

Full-detail meshes: broadleaf 804 triangles (3 LODs), pine 664 (1 LOD), birch 2,314 (5 LODs).
