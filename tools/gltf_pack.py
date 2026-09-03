#!/usr/bin/env python3
"""Pack a downloaded .gltf + .bin pair into the single .glb Godot imports, restating its materials.

Sketchfab exports carry whatever factors the uploader last had in their viewport, and a hand-modelled
engine usually arrives fully metallic over a near-black albedo. That is physically a mirror with no
diffuse response at all, so under a single sun and a star sky it renders as a silhouette. Restating
the factors here rather than in the scene keeps the fix with the asset and out of the renderer.

    tools/gltf_pack.py <source.gltf> <target.glb> [Name=r,g,b,metallic,roughness ...]
"""

import json
import pathlib
import struct
import sys


def restate(gltf, overrides):

    for material in gltf.get("materials", []):

        name = material.get("name") or "None"

        if name not in overrides:

            continue

        red, green, blue, metallic, roughness = overrides[name]

        material["pbrMetallicRoughness"] = {
            "baseColorFactor": [red, green, blue, 1.0],
            "metallicFactor": metallic,
            "roughnessFactor": roughness,
        }

        print(f"{name}: albedo {red},{green},{blue} metallic {metallic} roughness {roughness}")


def pack(source, target, overrides):

    source = pathlib.Path(source)

    gltf = json.loads(source.read_text())

    if len(gltf["buffers"]) != 1:

        raise SystemExit(f"{source}: expected one buffer, found {len(gltf['buffers'])}")

    buffer = (source.parent / gltf["buffers"][0]["uri"]).read_bytes()

    gltf["buffers"][0].pop("uri", None)
    gltf["buffers"][0]["byteLength"] = len(buffer)

    restate(gltf, overrides)

    # Both chunks are padded to four bytes, JSON with spaces and the binary with zeroes.
    text = json.dumps(gltf, separators=(",", ":")).encode()
    text += b" " * ((4 - len(text) % 4) % 4)

    body = buffer + b"\x00" * ((4 - len(buffer) % 4) % 4)

    blob = b"glTF" + struct.pack("<II", 2, 28 + len(text) + len(body))
    blob += struct.pack("<I", len(text)) + b"JSON" + text
    blob += struct.pack("<I", len(body)) + b"BIN\x00" + body

    pathlib.Path(target).write_bytes(blob)

    print(f"wrote {target}, {len(blob)} bytes")


if __name__ == "__main__":

    if len(sys.argv) < 3:

        raise SystemExit(__doc__)

    table = {}

    for argument in sys.argv[3:]:

        key, _, values = argument.partition("=")

        table[key] = [float(value) for value in values.split(",")]

    pack(sys.argv[1], sys.argv[2], table)
