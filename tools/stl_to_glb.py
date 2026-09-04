#!/usr/bin/env python3
"""Convert a binary STL into the .glb Godot imports, with normals a renderer can actually use.

A print-ready STL is a triangle soup with one flat normal per face and no vertices shared between
them, so imported as-is every curved surface reads as a faceted lump. This welds coincident
vertices, averages the face normals meeting at each one by area, and keeps a face's own normal
wherever the crease is sharper than the threshold - which is what leaves panel edges hard and
barrels smooth.

Optionally splits the mesh into two primitives at a height, so a heat shield and a backshell arrive
as separate surfaces the renderer can paint differently. Materials are deliberately not authored
here: the STL carries none, and an imported material is not usable as shipped anyway.

    tools/stl_to_glb.py <source.stl> <target.glb> [--crease <degrees>] [--split <y>] [--axis y|z]
"""

import json
import math
import pathlib
import struct
import sys


def read_stl(path):

    data = path.read_bytes()

    count = struct.unpack_from("<I", data, 80)[0]

    if 84 + count * 50 != len(data):

        raise SystemExit(f"{path} is not a binary STL of {count} triangles")

    triangles = []

    for index in range(count):

        offset = 84 + index * 50

        values = struct.unpack_from("<12f", data, offset)

        triangles.append((values[3:6], values[6:9], values[9:12]))

    return triangles


CELLS = [(x, y, z) for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1)]


def weld(triangles, tolerance):

    """Maps every corner onto a shared vertex index within a tolerance of it.

    The neighbouring cells are searched as well as the corner's own. Bucketing alone splits two
    corners that are a float's breadth apart but fall either side of a cell boundary, and every
    pair it splits becomes a shading seam - which on a lathe-like surface reads as a line ruled
    down the model.
    """

    lookup = {}
    points = []
    faces = []

    for corners in triangles:

        face = []

        for point in corners:

            cell = (
                math.floor(point[0] / tolerance),
                math.floor(point[1] / tolerance),
                math.floor(point[2] / tolerance),
            )

            index = None

            for offset in CELLS:

                found = lookup.get((cell[0] + offset[0], cell[1] + offset[1], cell[2] + offset[2]))

                if found is None:

                    continue

                near = points[found]

                if (abs(near[0] - point[0]) <= tolerance
                        and abs(near[1] - point[1]) <= tolerance
                        and abs(near[2] - point[2]) <= tolerance):

                    index = found

                    break

            if index is None:

                index = len(points)

                lookup[cell] = index
                points.append(point)

            face.append(index)

        faces.append(tuple(face))

    return points, faces


def face_normal(a, b, c):

    ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
    vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]

    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx

    return nx, ny, nz


def normalise(vector):

    length = math.sqrt(vector[0] ** 2 + vector[1] ** 2 + vector[2] ** 2)

    if length <= 0.0:

        return 0.0, 1.0, 0.0

    return vector[0] / length, vector[1] / length, vector[2] / length


def shade(points, faces, crease):

    """Area-weighted vertex normals, with a face keeping its own wherever the crease is too sharp."""

    accumulated = [[0.0, 0.0, 0.0] for _ in points]
    normals = []

    for a, b, c in faces:

        # The cross product's length is twice the triangle's area, so an unnormalised sum is
        # already weighted the way it should be.
        raw = face_normal(points[a], points[b], points[c])

        normals.append(raw)

        for index in (a, b, c):

            accumulated[index][0] += raw[0]
            accumulated[index][1] += raw[1]
            accumulated[index][2] += raw[2]

    smoothed = [normalise(vector) for vector in accumulated]

    limit = math.cos(math.radians(crease))

    corners = []

    for index, (a, b, c) in enumerate(faces):

        flat = normalise(normals[index])

        for vertex in (a, b, c):

            soft = smoothed[vertex]

            blended = soft if soft[0] * flat[0] + soft[1] * flat[1] + soft[2] * flat[2] > limit else flat

            corners.append((points[vertex], blended))

    return corners


def build(corners, faces, split):

    """Turns shaded corners into indexed primitives, deduplicated on position and normal."""

    lookup = {}

    positions = []
    normals = []

    groups = [[], []]

    for index in range(len(faces)):

        indices = []

        for step in range(3):

            point, normal = corners[index * 3 + step]

            key = (
                round(point[0], 5), round(point[1], 5), round(point[2], 5),
                round(normal[0], 4), round(normal[1], 4), round(normal[2], 4),
            )

            at = lookup.get(key)

            if at is None:

                at = len(positions)

                lookup[key] = at

                positions.append(point)
                normals.append(normal)

            indices.append(at)

        # A triangle belongs to the lower primitive only if all of it does, so the seam between the
        # two runs along an edge of the mesh rather than cutting triangles in half.
        low = split is not None and max(corners[index * 3 + step][0][1] for step in range(3)) <= split

        groups[0 if low else 1].append(indices)

    return positions, normals, groups


def pack(target, positions, normals, groups, axis):

    payload = bytearray()

    views = []
    accessors = []

    def stage(data, alignment=4):

        while len(payload) % alignment:

            payload.append(0)

        offset = len(payload)

        payload.extend(data)

        views.append({"buffer": 0, "byteOffset": offset, "byteLength": len(data)})

        return len(views) - 1

    # Blender's exporter and Godot both take glTF's Y-up, Z-forward convention; a model authored
    # Z-up is turned here rather than in the scene, so the asset arrives already square.
    if axis == "z":

        positions = [(x, z, -y) for x, y, z in positions]
        normals = [(x, z, -y) for x, y, z in normals]

    flat = bytearray()

    for point in positions:

        flat.extend(struct.pack("<3f", *point))

    position_view = stage(flat)

    flat = bytearray()

    for normal in normals:

        flat.extend(struct.pack("<3f", *normal))

    normal_view = stage(flat)

    low = [min(point[axis] for point in positions) for axis in range(3)]
    high = [max(point[axis] for point in positions) for axis in range(3)]

    accessors.append({
        "bufferView": position_view, "componentType": 5126, "count": len(positions),
        "type": "VEC3", "min": low, "max": high,
    })

    accessors.append({
        "bufferView": normal_view, "componentType": 5126, "count": len(normals), "type": "VEC3",
    })

    primitives = []

    for group in groups:

        if not group:

            continue

        flat = bytearray()

        for triangle in group:

            flat.extend(struct.pack("<3I", *triangle))

        view = stage(flat)

        accessors.append({
            "bufferView": view, "componentType": 5125, "count": len(group) * 3, "type": "SCALAR",
        })

        primitives.append({
            "attributes": {"POSITION": 0, "NORMAL": 1},
            "indices": len(accessors) - 1,
            "material": len(primitives),
        })

    gltf = {
        "asset": {"version": "2.0", "generator": "full-thrust stl_to_glb"},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"mesh": 0, "name": target.stem}],
        "meshes": [{"name": target.stem, "primitives": primitives}],
        "materials": [
            {"name": f"{target.stem}_{index}", "pbrMetallicRoughness": {
                "baseColorFactor": [0.8, 0.8, 0.8, 1.0], "metallicFactor": 0.2, "roughnessFactor": 0.6}}
            for index in range(len(primitives))
        ],
        "accessors": accessors,
        "bufferViews": views,
        "buffers": [{"byteLength": len(payload)}],
    }

    body = json.dumps(gltf, separators=(",", ":")).encode()

    while len(body) % 4:

        body += b" "

    while len(payload) % 4:

        payload.append(0)

    glb = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(body) + 8 + len(payload))
    glb += struct.pack("<II", len(body), 0x4E4F534A) + body
    glb += struct.pack("<II", len(payload), 0x004E4942) + bytes(payload)

    target.write_bytes(glb)


def main():

    arguments = sys.argv[1:]

    if len(arguments) < 2:

        raise SystemExit(__doc__)

    source = pathlib.Path(arguments[0])
    target = pathlib.Path(arguments[1])

    crease = 40.0
    split = None
    axis = "y"

    added = None
    scale = (1.0, 1.0, 1.0)
    lift = 0.0

    rest = arguments[2:]

    while rest:

        flag = rest.pop(0)

        if flag == "--crease":

            crease = float(rest.pop(0))

        elif flag == "--split":

            split = float(rest.pop(0))

        elif flag == "--axis":

            axis = rest.pop(0)

        elif flag == "--add":

            added = pathlib.Path(rest.pop(0))

        elif flag == "--add-scale":

            scale = tuple(float(value) for value in rest.pop(0).split(","))

        elif flag == "--add-lift":

            lift = float(rest.pop(0))

        else:

            raise SystemExit(f"unknown flag {flag}")

    triangles = read_stl(source)

    # A part that ships as its own print - a bay cover, a plug - is placed here and welded into the
    # model rather than instanced beside it, so it shades as one surface with what it closes.
    if added is not None:

        triangles += [
            tuple((corner[0] * scale[0], corner[1] * scale[1] + lift, corner[2] * scale[2]) for corner in triangle)
            for triangle in read_stl(added)
        ]

    span = max(
        max(corner[index] for triangle in triangles for corner in triangle) -
        min(corner[index] for triangle in triangles for corner in triangle)
        for index in range(3)
    )

    points, faces = weld(triangles, span * 1e-4)

    corners = shade(points, faces, crease)

    positions, normals, groups = build(corners, faces, split)

    pack(target, positions, normals, groups, axis)

    print(f"{len(faces)} triangles, {len(points)} welded, {len(positions)} shaded")
    print(f"primitives: {[len(group) for group in groups if group]}")
    print(f"wrote {target} ({target.stat().st_size / 1024:.0f} KB)")


if __name__ == "__main__":

    main()
