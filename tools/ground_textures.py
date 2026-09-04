#!/usr/bin/env python
"""Build the ground's detail materials from ambientCG, the launch complex's, and a wave normal.

The surface imagery is 415 m a texel, so from anywhere under about forty kilometres it is a flat
colour field. These two materials are what stands in below that: they carry no colour of their own -
the photograph is the colour - only the luminance variance and the normals that a texel of imagery
cannot hold. That is why they are picked on the standard deviation of their normal map rather than
on how much they look like ground, and why the albedo is reduced to a grey.

The complex's materials are taken the other way round: an apron is concrete and reads as concrete,
so its colour is kept rather than reduced. Its steelwork is the hull's own painted set, tinted -
there is no reason to carry a second white-painted-metal texture for it.

    py tools/ground_textures.py                 build every material
    py tools/ground_textures.py ground          only the detail pair and the wave normal
    py tools/ground_textures.py pad             only the complex's surfaces
    py tools/ground_textures.py audit A B C     print the detail in each candidate and stop

The wave normal is generated rather than downloaded. A water normal map is band-limited noise and
nothing photographic improves on one that is exactly tileable.
"""

import io
import os
import sys
import urllib.request
import zipfile

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "game", "Assets", "Planet")

RESOLUTION = "1K"
SIZE = 1024

# Broken ground at the scale of a boulder field, and packed earth at the scale of a footpath. The
# first stands in on anything steep, the second on anything flat, and between them they cover every
# surface the imagery hands over to them.
ROCK = "Rock030"
SOIL = "Ground037"

# The apron the complex stands on, and the road that reaches it.
CONCRETE = "Concrete016"
ASPHALT = "Asphalt014"

WAVE_SIZE = 512

# Longest swell the wave normal carries, in cells of its own tile. Everything under it is octaves.
WAVE_SCALE = 6
WAVE_OCTAVES = 5
WAVE_RELIEF = 2.4


def download(asset):

    url = f"https://ambientcg.com/get?file={asset}_{RESOLUTION}-JPG.zip"

    # The CDN behind the redirect turns away the default urllib agent outright.
    request = urllib.request.Request(url, headers={"User-Agent": "full-thrust-asset-import"})

    with urllib.request.urlopen(request) as response:

        return zipfile.ZipFile(io.BytesIO(response.read()))


def read(archive, asset, kind):

    return Image.open(io.BytesIO(archive.read(f"{asset}_{RESOLUTION}-JPG_{kind}.jpg")))


def audit(assets):

    for asset in assets:

        try:

            archive = download(asset)

            normal = np.asarray(read(archive, asset, "NormalGL").convert("RGB"), dtype=np.float32)
            colour = np.asarray(read(archive, asset, "Color").convert("RGB"), dtype=np.float32)

            print(f"{asset:16s} normal sigma {normal[..., :2].std():6.2f}   colour sigma {colour.std():6.2f}")

        except Exception as failure:

            print(f"{asset:16s} unavailable: {failure}")


def build_material(asset, name):

    archive = download(asset)

    colour = read(archive, asset, "Color").convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
    normal = read(archive, asset, "NormalGL").convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)

    # Centred on a mid grey rather than reduced to one: the shader takes the luminance as a
    # modulation of the photograph and a fraction of the hue as the material the ground is made of,
    # and a greyed material can only ever supply the first of those. Centring is what lets it be a
    # modulation at all - a material that averaged dark would darken every surface it landed on.
    pixels = np.asarray(colour, dtype=np.float32)

    grey = pixels.mean(axis=2, keepdims=True)

    low, high = np.percentile(grey, 2), np.percentile(grey, 98)
    level = np.clip((grey - low) / max(high - low, 1e-6), 0.0, 1.0)

    hue = pixels / np.maximum(grey, 1.0)

    balanced = np.clip(hue * (64.0 + level * 127.0), 0, 255).astype(np.uint8)

    Image.fromarray(balanced, "RGB").save(os.path.join(OUT, f"{name}_colour.jpg"), quality=92, subsampling=0)
    normal.save(os.path.join(OUT, f"{name}_normal.jpg"), quality=94, subsampling=0)

    print(f"  {asset} -> {name}, normal sigma {np.asarray(normal, dtype=np.float32)[..., :2].std():.2f}")


def build_surface(asset, name):
    """A material kept as it arrived, for a surface that is meant to read as what it is."""

    archive = download(asset)

    colour = read(archive, asset, "Color").convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)
    normal = read(archive, asset, "NormalGL").convert("RGB").resize((SIZE, SIZE), Image.LANCZOS)

    colour.save(os.path.join(OUT, f"{name}_colour.jpg"), quality=92, subsampling=0)
    normal.save(os.path.join(OUT, f"{name}_normal.jpg"), quality=94, subsampling=0)

    print(f"  {asset} -> {name}")


def tileable_noise(size, cells, octaves):
    """Value noise on a lattice that divides the tile, so every octave wraps exactly."""

    field = np.zeros((size, size), dtype=np.float32)

    amplitude = 1.0
    total = 0.0

    generator = np.random.default_rng(20260904)

    for octave in range(octaves):

        step = cells * (2 ** octave)

        lattice = generator.random((step, step), dtype=np.float32)

        # Wrapping the lattice before the resize is what makes the result tile: the interpolation
        # that lands on the last cell reads the first one back rather than clamping to the edge.
        wrapped = np.concatenate([lattice, lattice[:, :1]], axis=1)
        wrapped = np.concatenate([wrapped, wrapped[:1, :]], axis=0)

        grown = np.asarray(Image.fromarray(wrapped, "F").resize((size + 1, size + 1), Image.BICUBIC), dtype=np.float32)

        field += grown[:size, :size] * amplitude

        total += amplitude
        amplitude *= 0.55

    return field / total


def build_wave():

    height = tileable_noise(WAVE_SIZE, WAVE_SCALE, WAVE_OCTAVES) * WAVE_RELIEF

    east = np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)
    north = np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)

    normal = np.stack([-east, north, np.full_like(height, 2.0 / WAVE_SIZE * 40.0)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)

    packed = np.clip((normal * 0.5 + 0.5) * 255.0, 0, 255).astype(np.uint8)

    path = os.path.join(OUT, "wave_normal.png")

    Image.fromarray(packed, "RGB").save(path, optimize=True)

    print(f"  {path}")


def main():

    if len(sys.argv) > 1 and sys.argv[1] == "audit":

        audit(sys.argv[2:])

        return

    os.makedirs(OUT, exist_ok=True)

    wanted = sys.argv[1:] or ["ground", "pad"]

    if "ground" in wanted:

        print("building detail materials")

        build_material(ROCK, "rock")
        build_material(SOIL, "soil")

        print("building wave normal")

        build_wave()

    if "pad" in wanted:

        print("building complex surfaces")

        build_surface(CONCRETE, "pad_concrete")
        build_surface(ASPHALT, "pad_asphalt")

    print("done")


if __name__ == "__main__":
    main()
