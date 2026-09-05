#!/usr/bin/env python3
"""Godot's headless importer skips 3D texture detection, so generated maps land uncompressed.

Run after `godot --headless --import` has pulled in new GLB assets, then import once more.
"""

import pathlib
import re
import sys

WANTED = {
    "compress/mode": "2",
    "compress/high_quality": "true",
    "mipmaps/generate": "true",
    "detect_3d/compress_to": "0",
}

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else "game/Assets")

for path in root.rglob("*.import"):

    text = path.read_text()

    if 'importer="texture"' not in text:

        continue

    patched = text

    wanted = dict(WANTED)

    if path.name == "biomes.png.import":
        wanted["compress/mode"] = "0"
        wanted["process/fix_alpha_border"] = "false"

    # Left on Detect, a normal map imported headlessly lands as plain colour and its detail is gone.
    if "_normal" in path.name:

        wanted["compress/normal_map"] = "1"

    for key, value in wanted.items():

        patched = re.sub(rf"(?m)^{re.escape(key)}=.*$", f"{key}={value}", patched)

    if patched != text:

        path.write_text(patched)
        print(f"patched {path}")
