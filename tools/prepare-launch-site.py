"""Extract and finish the fixed tower from NASA's decoded Gantry GLB.

First decode the NASA download using @gltf-transform/cli copy (see asset README).
Usage: python tools/prepare-launch-site.py decoded.glb game/Assets/LaunchSite/Gantry.glb
No third-party Python packages are needed.
"""
import json
import math
import re
from collections import defaultdict
import struct
import sys

source = open(sys.argv[1], "rb").read()
json_size = struct.unpack_from("<I", source, 12)[0]
document = json.loads(source[20:20 + json_size])
binary = source[28 + json_size:]
assert not document.get("extensionsRequired"), "Decode Draco before adapting"
assert all(not any(k in n for k in ("matrix", "translation", "rotation", "scale"))
           for n in document["nodes"]), "Source transforms changed; re-survey the asset"

# Keep the fixed service tower as complete source components, without the rotating
# service structure or mobile launcher. The tower is 2.4x the source and sits beside
# the independently sized vehicle mount. These coordinates are model-local metres.
scale = 2.4
centre = (0.771, -1.267, 5.076)
offset = (-10.0, 0.0, 3.0)
materials = (
    ("PaintedFrame", [0.48, 0.50, 0.47, 1], 0.12, 0.60),
    ("DeckSteel", [0.055, 0.085, 0.11, 1], 0.32, 0.72),
    ("SafetyYellow", [0.68, 0.36, 0.028, 1], 0.08, 0.53),
    ("GalvanizedServices", [0.24, 0.29, 0.31, 1], 0.65, 0.42),
    ("ServiceEnclosure", [0.05, 0.11, 0.16, 1], 0.18, 0.68),
    ("OxideRed", [0.38, 0.065, 0.022, 1], 0.08, 0.67),
)


def accessor(index):
    a = document["accessors"][index]
    view = document["bufferViews"][a["bufferView"]]
    size = {"SCALAR": 1, "VEC2": 2, "VEC3": 3}[a["type"]]
    fmt = "<" + {5126: "f", 5125: "I", 5123: "H", 5121: "B"}[a["componentType"]] * size
    stride = view.get("byteStride", struct.calcsize(fmt))
    offset = view.get("byteOffset", 0) + a.get("byteOffset", 0)
    return [struct.unpack_from(fmt, binary, offset + i * stride) for i in range(a["count"])]


def normal(a, b, c):
    u = [b[i] - a[i] for i in range(3)]
    v = [c[i] - a[i] for i in range(3)]
    n = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
    length = math.sqrt(sum(x*x for x in n))
    return tuple(x / max(length, 1e-12) for x in n)


def position_key(v):
    return tuple(round(x, 5) for x in v[:3])


groups = [[] for _ in materials]
selected = 0
for mesh in document["meshes"]:
    if "polySurface891" not in mesh["name"]:
        continue
    bounds = [document["accessors"][p["attributes"]["POSITION"]] for p in mesh["primitives"]]
    lo = [min(a["min"][k] for a in bounds) for k in range(3)]
    hi = [max(a["max"][k] for a in bounds) for k in range(3)]
    if not (lo[0] > -0.5 and hi[0] < 2.2 and lo[2] > 3.75 and hi[2] < 6.2):
        continue
    selected += 1
    span = [hi[i] - lo[i] for i in range(3)]
    part = int(re.findall(r"polySurface(\d+)", mesh["name"])[-1])
    for primitive in mesh["primitives"]:
        source_material = primitive["material"]
        if part == 1050:  # Central lift housing, behind the exposed access ladders.
            material = 4
        elif source_material == 1:
            material = 1 if 915 <= part <= 926 else 0
            if lo[1] > 7.7 and hi[1] < 9.15:
                material = 5
        elif source_material == 2:
            material = 3 if span[1] > 1.3 and max(span[0], span[2]) < 0.08 else 2
            if span[1] < 0.04 and min(span[0], span[2]) > 0.10:
                material = 1
        else:
            material = 3
        positions = accessor(primitive["attributes"]["POSITION"])
        uvs = accessor(primitive["attributes"]["TEXCOORD_0"])
        vertices = [tuple((p[i] - centre[i]) * scale + offset[i] for i in range(3)) + uv
                    for p, uv in zip(positions, uvs)]
        indices = [v[0] for v in accessor(primitive["indices"])]
        faces = [[vertices[j] for j in indices[i:i + 3]] for i in range(0, len(indices), 3)]
        # Rebuild corner normals from the geometry. Smooth round pipes, but retain
        # creases over 35 degrees instead of smearing light across beam corners.
        normals = [normal(*face) for face in faces]
        adjacent = defaultdict(set)
        for i, face in enumerate(faces):
            for vertex in face:
                adjacent[position_key(vertex)].add(i)
        for i, face in enumerate(faces):
            triangle = []
            for vertex in face:
                neighbours = [normals[j] for j in adjacent[position_key(vertex)]
                              if sum(a*b for a, b in zip(normals[i], normals[j])) > 0.819152]
                n = tuple(sum(v[k] for v in neighbours) for k in range(3))
                length = math.sqrt(sum(x*x for x in n))
                if length < 1e-9:
                    n = normals[i]
                else:
                    n = tuple(x/length for x in n)
                triangle.append(vertex[:3] + n + vertex[3:])
            groups[material].append(triangle)

assert selected == 373, "Source tower selection changed; inspect before updating"

out = {"asset": {"version": "2.0", "generator": "Full-Thrust NASA gantry adapter",
                 "copyright": "Source: NASA / Michael D. Carbajal; see README.md"},
       "scene": 0, "scenes": [{"nodes": [0]}],
       "nodes": [{"name": "LC39Gantry", "mesh": 0}],
       "meshes": [{"name": "LC39Gantry", "primitives": []}],
       "materials": [], "accessors": [], "bufferViews": [], "buffers": []}
data = bytearray()


def write_accessor(values, kind, component, fmt, bounds=False):
    while len(data) % 4:
        data.append(0)
    start = len(data)
    for v in values:
        data.extend(struct.pack("<" + fmt * len(v), *v))
    view = len(out["bufferViews"])
    out["bufferViews"].append({"buffer": 0, "byteOffset": start, "byteLength": len(data) - start})
    a = {"bufferView": view, "componentType": component, "count": len(values), "type": kind}
    if bounds:
        a.update(min=[min(v[i] for v in values) for i in range(3)],
                 max=[max(v[i] for v in values) for i in range(3)])
    out["accessors"].append(a)
    return len(out["accessors"]) - 1


for group, (name, color, metallic, roughness) in zip(groups, materials):
    lookup, vertices, indices = {}, [], []
    for triangle in group:
        for vertex in triangle:
            normal_length = math.sqrt(sum(x * x for x in vertex[3:6]))
            normal = tuple(x / max(normal_length, 1e-9) for x in vertex[3:6])
            vertex = vertex[:3] + normal + vertex[6:]
            key = struct.pack("<8f", *vertex)
            if key not in lookup:
                lookup[key] = len(vertices)
                vertices.append(vertex)
            indices.append((lookup[key],))
    attributes = {
        "POSITION": write_accessor([v[:3] for v in vertices], "VEC3", 5126, "f", True),
        "NORMAL": write_accessor([v[3:6] for v in vertices], "VEC3", 5126, "f"),
        "TEXCOORD_0": write_accessor([v[6:] for v in vertices], "VEC2", 5126, "f")}
    out["meshes"][0]["primitives"].append({"attributes": attributes,
        "indices": write_accessor(indices, "SCALAR", 5125, "I"), "material": len(out["materials"])})
    out["materials"].append({"name": name, "pbrMetallicRoughness": {
        "baseColorFactor": color, "metallicFactor": metallic, "roughnessFactor": roughness}})

out["buffers"] = [{"byteLength": len(data)}]
encoded = json.dumps(out, separators=(",", ":")).encode()
encoded += b" " * (-len(encoded) % 4)
data += b"\0" * (-len(data) % 4)
with open(sys.argv[2], "wb") as result:
    result.write(struct.pack("<III", 0x46546C67, 2, 28 + len(encoded) + len(data)))
    result.write(struct.pack("<II", len(encoded), 0x4E4F534A) + encoded)
    result.write(struct.pack("<II", len(data), 0x004E4942) + data)
print(f"Gantry: {sum(len(g) for g in groups):,} triangles, 1 mesh, 6 materials; scale {scale:.6f}")
