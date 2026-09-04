#!/usr/bin/env python
"""Build the Terra map set from public-domain source imagery and elevation.

Sources are cached under tools/.cache and are not committed; the outputs in game/Assets/Planet are.
Run with: py tools/planet_maps.py [stage ...]

Everything the ground shader samples is laid out on the six faces of a cube rather than on an
equirectangular plate. A quadtree patch already knows which face it is on and where it sits, so a
cube face is a direct lookup with no seam at the date line and no texel crush at the poles - both
of which an equirectangular map has and both of which are visible from low altitude.

The heightfield stays equirectangular because only the simulation reads it, on the CPU, by
interpolation rather than by texture fetch, where neither problem exists.

The albedo deliberately uses the plain Blue Marble Next Generation surface map. The "topography and
bathymetry" variant has hillshading baked into the colour, which would double-shade under our own
directional light.
"""

import gzip
import os
import struct
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
    "night.source.jpg": f"{BASE}/79000/79765/dnb_land_ocean_ice.2012.13500x6750.jpg",
    "cloud.w.png": f"{BASE}/57000/57747/cloud.W.2001210.21600x21600.png",
    "cloud.e.png": f"{BASE}/57000/57747/cloud.E.2001210.21600x21600.png",

    # NOAA ETOPO 2022, 60 arc-second, land and sea floor in one grid, metres, public domain.
    "etopo60.tif": "https://www.ngdc.noaa.gov/mgg/global/relief/ETOPO2022/data/60s/60s_surface_elev_gtif/ETOPO_2022_v1_60s_N90W180_surface.tif",

}

# Face edge in texels. Sampling is coarsest at the middle of a face, where one texel spans
# 2 R / SURFACE_FACE metres - 415 m at this size, against the source's own 371 m at the equator.
SURFACE_FACE = 6144
CLIMATE_FACE = 2048
NIGHT_FACE = 2048

CLOUD_WIDTH = 8192
CLOUD_HEIGHT = 4096

HEIGHT_WIDTH = 10800
HEIGHT_HEIGHT = 5400

# One metre a count over the whole range of the grid, which leaves most of a 16-bit word spare.
HEIGHT_STEP = 1.0
HEIGHT_FLOOR = -11500.0

# Face basis, outward normal then the two axes (s, t) spans. Right cross up is the normal on every
# face, so a patch turned off these is wound the same way whichever face it sits on.
FACES = (

    ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)),
    ((-1.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0)),
    ((0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (1.0, 0.0, 0.0)),
    ((0.0, -1.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0)),
    ((0.0, 0.0, 1.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)),
    ((0.0, 0.0, -1.0), (0.0, 1.0, 0.0), (1.0, 0.0, 0.0)),

)

BAND = 512


def fetch(name):

    path = os.path.join(CACHE, name)

    if os.path.exists(path) and os.path.getsize(path) > 0:
        return path

    os.makedirs(CACHE, exist_ok=True)
    print(f"downloading {name}")

    request = urllib.request.Request(SOURCES[name], headers={"User-Agent": "full-thrust-asset-import"})

    with urllib.request.urlopen(request) as response, open(path + ".part", "wb") as handle:

        while True:

            chunk = response.read(1 << 22)

            if not chunk:
                break

            handle.write(chunk)

    os.replace(path + ".part", path)
    return path


def load(name, mode):

    return Image.open(fetch(name)).convert(mode)


def resample(image, width, height):
    return image.resize((width, height), Image.LANCZOS)


def sample_equirect(source, longitude, latitude):
    """Bilinear lookup into an equirectangular plate, wrapping east-west and clamping at the poles."""

    height, width = source.shape[:2]

    x = (longitude / (2.0 * np.pi) + 0.5) * width - 0.5
    y = (0.5 - latitude / np.pi) * height - 0.5

    x0 = np.floor(x).astype(np.int64)
    y0 = np.clip(np.floor(y).astype(np.int64), 0, height - 1)

    fx = (x - x0).astype(np.float32)[..., None] if source.ndim == 3 else (x - x0).astype(np.float32)
    fy = (y - y0).astype(np.float32)[..., None] if source.ndim == 3 else (y - y0).astype(np.float32)

    x1 = (x0 + 1) % width
    x0 = x0 % width
    y1 = np.clip(y0 + 1, 0, height - 1)

    top = source[y0, x0].astype(np.float32) * (1.0 - fx) + source[y0, x1].astype(np.float32) * fx
    bottom = source[y1, x0].astype(np.float32) * (1.0 - fx) + source[y1, x1].astype(np.float32) * fx

    return top * (1.0 - fy) + bottom * fy


def face_directions(face, size, row, rows):
    """Unit directions for a band of rows of one cube face, row zero at the top (t = +1)."""

    normal, right, up = (np.array(axis, dtype=np.float64) for axis in FACES[face])

    s = (np.arange(size, dtype=np.float64) + 0.5) / size * 2.0 - 1.0
    t = 1.0 - (np.arange(row, row + rows, dtype=np.float64) + 0.5) / size * 2.0

    grid_s, grid_t = np.meshgrid(s, t)

    vector = normal + right * grid_s[..., None] + up * grid_t[..., None]

    return vector / np.linalg.norm(vector, axis=-1, keepdims=True)


def project(source, size, channels):
    """Resample an equirectangular plate onto the six cube faces, a band of rows at a time."""

    for face in range(6):

        out = np.zeros((size, size, channels) if channels > 1 else (size, size), dtype=np.float32)

        for row in range(0, size, BAND):

            rows = min(BAND, size - row)

            direction = face_directions(face, size, row, rows)

            longitude = np.arctan2(direction[..., 1], direction[..., 0])
            latitude = np.arcsin(np.clip(direction[..., 2], -1.0, 1.0))

            out[row:row + rows] = sample_equirect(source, longitude, latitude)

        yield face, out


def elevation_grid():
    """ETOPO at the working resolution, in metres, with the coastline pulled onto the imagery's."""

    print("  reading etopo")

    metres = np.asarray(Image.open(fetch("etopo60.tif")), dtype=np.float32)
    metres = np.asarray(resample(Image.fromarray(metres), HEIGHT_WIDTH, HEIGHT_HEIGHT), dtype=np.float32)

    print("  reading albedo for the coastline")

    colour = np.asarray(resample(load("albedo.jpg", "RGB"), HEIGHT_WIDTH, HEIGHT_HEIGHT), dtype=np.float32) / 255.0

    water = albedo_water(colour)

    # A 60 arc-second post is 1.85 km across, which loses barrier islands, lagoons and most river
    # mouths outright - the Cape the pad stands on among them. The imagery resolves them at a third
    # of that, so within a shallow band either side of sea level the shoreline is taken from the
    # imagery and the grid is nudged to agree with it. Away from that band the grid is the truth.
    band = np.clip((60.0 - np.abs(metres)) / 50.0, 0.0, 1.0)

    return metres + band * (0.5 - water) * 24.0


def albedo_water(colour):
    """Open water off the imagery alone: dark, blue-dominant and unsaturated in the other channels."""

    luminance = colour @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)

    blue = np.clip((colour[..., 2] - colour[..., 0]) / 0.03, 0.0, 1.0)
    dark = 1.0 - np.clip((luminance - 0.26) / 0.14, 0.0, 1.0)

    return blue * dark


def build_heightfield():
    """R16 elevation for the simulation. The renderer builds its meshes off the same numbers."""

    metres = elevation_grid()

    counts = np.clip(np.round((metres - HEIGHT_FLOOR) / HEIGHT_STEP), 0, 65535).astype(np.uint16)

    high = (counts >> 8).astype(np.uint8)
    low = (counts & 0xFF).astype(np.uint8)

    # Deflate finds almost nothing in a raw 16-bit field: the low byte of a metre-quantised height is
    # noise and it sits between every pair of correlated high bytes. Splitting the planes and taking
    # a horizontal difference across each row leaves two smooth planes and halves the file.
    payload = bytearray()

    for plane in (high, low):

        difference = np.empty_like(plane)
        difference[:, 0] = plane[:, 0]
        difference[:, 1:] = plane[:, 1:] - plane[:, :-1]

        payload += difference.tobytes()

    header = struct.pack("<4sIIIdd", b"FTHF", 1, HEIGHT_WIDTH, HEIGHT_HEIGHT, HEIGHT_STEP, HEIGHT_FLOOR)

    path = os.path.join(OUT, "elevation.r16")

    with open(path, "wb") as handle:

        handle.write(header)
        handle.write(gzip.compress(bytes(payload), 6))

    print(f"  {path} {os.path.getsize(path) / 1e6:.1f} MB, {metres.min():.0f} to {metres.max():.0f} m")


def build_surface():
    """Blue Marble on the six faces. The only colour the ground has above detail range."""

    source = np.asarray(load("albedo.jpg", "RGB"), dtype=np.uint8)

    for face, image in project(source, SURFACE_FACE, 3):

        path = os.path.join(OUT, f"surface_{face}.jpg")
        Image.fromarray(np.clip(image, 0, 255).astype(np.uint8), "RGB").save(path, quality=92, subsampling=0, optimize=True)
        print(f"  {path}")


def build_climate():
    """R water, G ice, B vegetation, A bare ground. What the detail materials are blended by."""

    colour = np.asarray(resample(load("albedo.jpg", "RGB"), 10800, 5400), dtype=np.float32) / 255.0

    metres = elevation_grid()

    luminance = colour @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    saturation = colour.max(axis=-1) - colour.min(axis=-1)

    water = np.maximum(albedo_water(colour), np.clip((1.0 - metres) / 3.0, 0.0, 1.0))

    # Bright alone would catch the Sahara; snow and ice are also close to neutral, deserts are not.
    neutral = 1.0 - np.clip((saturation - 0.04) / 0.10, 0.0, 1.0)
    ice = np.clip((luminance - 0.55) / 0.20, 0.0, 1.0) * neutral * (1.0 - water)

    green = np.clip((colour[..., 1] - 0.5 * (colour[..., 0] + colour[..., 2])) / 0.045, 0.0, 1.0)
    vegetation = green * (1.0 - water) * (1.0 - ice)

    bare = np.clip(1.0 - vegetation - ice - water, 0.0, 1.0)

    packed = np.stack([water, ice, vegetation, bare], axis=-1) * 255.0

    for face, image in project(packed, CLIMATE_FACE, 4):

        path = os.path.join(OUT, f"climate_{face}.png")
        Image.fromarray(np.clip(image, 0, 255).astype(np.uint8), "RGBA").save(path, optimize=True)
        print(f"  {path}")


def build_night():
    """City lights, separated from the bluish land plate the source composites them over."""

    colour = np.asarray(resample(load("night.source.jpg", "RGB"), 10800, 5400), dtype=np.float32) / 255.0

    # Only the lights are warm, so red over blue separates them from ice sheets and open ocean.
    warm = np.clip((colour[..., 0] - colour[..., 2] + 0.015) / 0.09, 0.0, 1.0)

    # No gain here. Multiplying up and clipping flattened every city core to a white plateau, which
    # the shader then read as a hard-edged block; the brightness belongs in night_gain instead.
    lights = np.clip(colour * warm[..., None], 0.0, 1.0) * 255.0

    for face, image in project(lights, NIGHT_FACE, 3):

        path = os.path.join(OUT, f"night_{face}.jpg")
        Image.fromarray(np.clip(image, 0, 255).astype(np.uint8), "RGB").save(path, quality=90, optimize=True)
        print(f"  {path}")


def build_clouds():
    """Cloud cover stays equirectangular: the volume is marched by direction, not by patch."""

    half = CLOUD_WIDTH // 2
    margin = 12

    # The source hemispheres are 21600 square each; resampling before the paste keeps peak memory
    # under a gigabyte instead of materialising the full 43200 x 21600 mosaic. The outermost source
    # columns carry a dark border that would resample into a visible meridian, so they are cropped.
    combined = Image.new("L", (CLOUD_WIDTH, CLOUD_HEIGHT))

    for index, name in enumerate(["cloud.w.png", "cloud.e.png"]):

        tile = load(name, "L")
        tile = tile.crop((margin, 0, tile.width - margin, tile.height))

        combined.paste(resample(tile, half, CLOUD_HEIGHT), (index * half, 0))

    pixels = np.asarray(combined, dtype=np.float32)

    fill_cloud_poles(pixels)

    path = os.path.join(OUT, "clouds.jpg")
    Image.fromarray(np.clip(pixels, 0.0, 255.0).astype(np.uint8), "L").save(path, quality=92, optimize=True)
    print(f"  {path}")


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


def main():

    os.makedirs(OUT, exist_ok=True)

    stages = {

        "heightfield": build_heightfield,
        "surface": build_surface,
        "climate": build_climate,
        "night": build_night,
        "clouds": build_clouds,

    }

    wanted = sys.argv[1:] or list(stages)

    for name in wanted:

        print(f"building {name}")
        stages[name]()

    print("done")


if __name__ == "__main__":
    main()
