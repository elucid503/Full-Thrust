#!/usr/bin/env python
"""Build the Terra texture set from NASA Blue Marble source imagery.

Sources are cached under tools/.cache and are not committed; the 8K outputs in
game/Assets/Planet are. Run with: py tools/planet_maps.py

The albedo deliberately uses the plain Blue Marble Next Generation surface map.
The "topography and bathymetry" variant has hillshading baked into the colour,
which would double-shade under our own directional light.
"""

import os
import sys
import urllib.request

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "tools", ".cache")
OUT = os.path.join(ROOT, "game", "Assets", "Planet")

BASE = "https://eoimages.gsfc.nasa.gov/images/imagerecords"

SOURCES = {
    "albedo.jpg": f"{BASE}/74000/74092/world.200407.3x21600x10800.jpg",
    "elevation.png": f"{BASE}/73000/73934/gebco_08_rev_elev_21600x10800.png",
    "night.jpg": f"{BASE}/79000/79765/dnb_land_ocean_ice.2012.13500x6750.jpg",
    "cloud.w.png": f"{BASE}/57000/57747/cloud.W.2001210.21600x21600.png",
    "cloud.e.png": f"{BASE}/57000/57747/cloud.E.2001210.21600x21600.png",
}

WIDTH = 8192
HEIGHT = 4096


def fetch(name, url):
    path = os.path.join(CACHE, name)

    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path

    os.makedirs(CACHE, exist_ok=True)
    print(f"downloading {name}")

    with urllib.request.urlopen(url) as response, open(path + ".part", "wb") as handle:
        while True:
            chunk = response.read(1 << 20)
            if not chunk:
                break
            handle.write(chunk)

    os.replace(path + ".part", path)
    return path


def load(name, mode):
    image = Image.open(fetch(name, SOURCES[name]))

    # Halve JPEG decode where the source is still above the target width; saves ~1 GB of peak RSS.
    if image.format == "JPEG" and image.width >= WIDTH * 2:
        image.draft(mode, (image.width // 2, image.height // 2))

    return image.convert(mode)


def resample(image, width=WIDTH, height=HEIGHT):
    return image.resize((width, height), Image.LANCZOS)


def build_albedo():
    resample(load("albedo.jpg", "RGB")).save(os.path.join(OUT, "albedo.jpg"), quality=94, subsampling=0, optimize=True)


def build_night():
    colour = np.asarray(resample(load("night.jpg", "RGB")), dtype=np.float32) / 255.0

    # The source composites city lights over a bluish land-and-ice plate that would otherwise glow.
    # Only the lights are warm, so red over blue separates them from ice sheets and open ocean.
    warm = np.clip((colour[..., 0] - colour[..., 2] + 0.015) / 0.09, 0.0, 1.0)

    lights = np.clip(colour * warm[..., None] * 1.6, 0.0, 1.0)

    Image.fromarray((lights * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "night.jpg"), quality=92, optimize=True)


def build_clouds():
    half = WIDTH // 2
    margin = 12

    # The source hemispheres are 21600 square each; resampling before the paste keeps peak memory
    # under a gigabyte instead of materialising the full 43200 x 21600 mosaic. The outermost source
    # columns carry a dark border that would resample into a visible meridian, so they are cropped.
    combined = Image.new("L", (WIDTH, HEIGHT))

    for index, name in enumerate(["cloud.w.png", "cloud.e.png"]):
        tile = load(name, "L")
        tile = tile.crop((margin, 0, tile.width - margin, tile.height))

        combined.paste(resample(tile, half, HEIGHT), (index * half, 0))

    pixels = np.asarray(combined, dtype=np.float32)

    fill_cloud_poles(pixels)

    Image.fromarray(np.clip(pixels, 0.0, 255.0).astype(np.uint8), "L").save(os.path.join(OUT, "clouds.jpg"), quality=92, optimize=True)


def fill_cloud_poles(pixels):
    """The MODIS composite carries no data above 83N or below 73S; those rows arrive black.

    Replicating the nearest covered row would draw streaks converging on the pole, so the fill fades
    that row into its own zonal mean, which reads as the uniform polar overcast that is actually there.
    """

    rows = pixels.mean(axis=1)

    # Coverage fades out over a couple of degrees rather than ending on one row, so the last row with
    # a representative amount of cloud in it is the honest edge, not the last row that is non-black.
    covered = np.nonzero(rows > 0.35 * float(rows.max()))[0]

    for edge, gap in ((covered[0], range(covered[0] - 1, -1, -1)), (covered[-1], range(covered[-1] + 1, pixels.shape[0]))):
        source = pixels[edge]
        mean = float(source.mean())

        span = float(len(gap))

        for step, row in enumerate(gap):
            blend = min(1.0, (step + 1) / (span * 0.6))

            pixels[row] = source * (1.0 - blend) + mean * blend


def build_terrain():
    """R = elevation, G = water mask, B = ice mask."""

    elevation = np.asarray(resample(load("elevation.png", "L")), dtype=np.float32) / 255.0

    colour = np.asarray(resample(load("albedo.jpg", "RGB")), dtype=np.float32) / 255.0
    luminance = colour @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

    # Sea level alone is not enough: river basins and salt flats also read as zero elevation.
    # Open water is additionally dark and blue-dominant, which no lowland or ice sheet is.
    low = 1.0 - np.clip(elevation / (3.0 / 255.0), 0.0, 1.0)
    blue = np.clip((colour[..., 2] - colour[..., 0]) / 0.02, 0.0, 1.0)
    dark = 1.0 - np.clip((luminance - 0.30) / 0.15, 0.0, 1.0)

    water = low * blue * dark

    # Bright alone would catch the Sahara; snow and ice are also close to neutral, deserts are not.
    saturation = colour.max(axis=-1) - colour.min(axis=-1)
    neutral = 1.0 - np.clip((saturation - 0.04) / 0.10, 0.0, 1.0)

    ice = np.clip((luminance - 0.55) / 0.20, 0.0, 1.0) * neutral * (1.0 - water)

    packed = np.stack([elevation, water, ice], axis=-1)

    Image.fromarray((packed * 255.0 + 0.5).astype(np.uint8), "RGB").save(os.path.join(OUT, "terrain.png"), optimize=True)


def main():
    os.makedirs(OUT, exist_ok=True)

    stages = {
        "albedo": build_albedo,
        "night": build_night,
        "clouds": build_clouds,
        "terrain": build_terrain,
    }

    wanted = sys.argv[1:] or list(stages)

    for name in wanted:
        print(f"building {name}")
        stages[name]()

    print("done")


if __name__ == "__main__":
    main()
