#!/usr/bin/env python3
"""Keep only the main body of a generated GLB.

Image-to-3D reconstructs everything in the reference photograph, so a NASA fact-sheet plate comes
back with the caption board and dimension arrows attached. Those arrive as separate shells, so
welding the mesh, grouping faces into connected components and keeping the largest one removes
them without touching the part itself. Welding is done on a throwaway copy and only used to label
faces, so the exported mesh keeps its original UV seams and PBR material.
"""

import sys

import numpy as np
import trimesh


def trim(path):

    scene = trimesh.load(path, process=False)

    name, mesh = next(iter(scene.geometry.items()))

    welded = mesh.copy()
    welded.merge_vertices(merge_tex=True, merge_norm=True)

    groups = trimesh.graph.connected_components(welded.face_adjacency, nodes=np.arange(len(welded.faces)))

    if len(groups) < 2:

        print(f"{path}: single body, left alone")

        return

    largest = max(groups, key=len)

    mask = np.zeros(len(mesh.faces), dtype=bool)
    mask[largest] = True

    dropped = len(mesh.faces) - mask.sum()

    mesh.update_faces(mask)
    mesh.remove_unreferenced_vertices()

    scene.geometry[name] = mesh
    scene.export(path)

    print(f"{path}: kept {mask.sum()} faces, dropped {dropped} across {len(groups) - 1} stray shells")


if __name__ == "__main__":

    for argument in sys.argv[1:]:
        trim(argument)
