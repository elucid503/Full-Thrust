#!/usr/bin/env python3
"""Build the hull's PBR set from an ambientCG material, repainted white.

Nothing in the catalogue is a white aerospace livery, and the ones that are already white are also
flat - their normal and roughness maps carry almost no variation, so a hull wearing them renders as
smooth plastic. Brushed steel has the surface history we want (brush grain, scuffing, patchy
roughness) and only the wrong colour, so the albedo is taken down to luminance and stretched back
up into a paint range. Normal and roughness are used exactly as shipped: they are the detail.

    tools/hull_texture.py [AssetId] [resolution]
"""

import io
import pathlib
import sys
import urllib.request
import zipfile

import numpy as np
from PIL import Image

TARGET = pathlib.Path(__file__).resolve().parent.parent / "game" / "Assets" / "Vessel"

# Painted metal is a dielectric and never reaches full white, so the top of the range is left short
# of 255 - a livery that clips has nothing left to shade with.
PAINT_LOW = 203.0
PAINT_HIGH = 253.0


def whiten(image):

    luminance = np.asarray(image.convert("RGB"), dtype=np.float32).mean(axis=2)

    # Percentiles rather than min/max, so one dark speck cannot flatten the whole range.
    low, high = np.percentile(luminance, 2), np.percentile(luminance, 98)

    level = np.clip((luminance - low) / max(high - low, 1e-6), 0.0, 1.0)

    return Image.fromarray(np.clip(PAINT_LOW + level * (PAINT_HIGH - PAINT_LOW), 0, 255).astype(np.uint8)).convert("RGB")


def build(asset, resolution):

    url = f"https://ambientcg.com/get?file={asset}_{resolution}-JPG.zip"

    # The CDN behind the redirect turns away the default urllib agent outright.
    request = urllib.request.Request(url, headers={"User-Agent": "full-thrust-asset-import"})

    with urllib.request.urlopen(request) as response:

        archive = zipfile.ZipFile(io.BytesIO(response.read()))

    def read(kind):

        return Image.open(io.BytesIO(archive.read(f"{asset}_{resolution}-JPG_{kind}.jpg")))

    for kind, name, repaint in (("Color", "hull_color", True), ("NormalGL", "hull_normal", False), ("Roughness", "hull_roughness", False)):

        image = read(kind)
        image = whiten(image) if repaint else image.convert("RGB")

        path = TARGET / f"{name}.jpg"

        image.save(path, quality=94, subsampling=0)

        print(f"{asset} {kind} -> {path} {image.size}")


if __name__ == "__main__":

    build(sys.argv[1] if len(sys.argv) > 1 else "Metal010", sys.argv[2] if len(sys.argv) > 2 else "2K")
