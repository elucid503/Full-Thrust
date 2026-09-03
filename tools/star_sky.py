#!/usr/bin/env python
"""Bake the real night sky from the Yale Bright Star Catalogue (CDS V/50).

Every star in game/Assets/Sky/stars.png is a catalogued one: right ascension and declination give
its position, the V magnitude its brightness and the B-V colour index its colour. The result is an
equirectangular map in the simulation's Z-polar inertial frame, sampled by the sky shader.

Run with: py tools/star_sky.py
"""

import gzip
import math
import os
import urllib.request

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "tools", ".cache")
OUT = os.path.join(ROOT, "game", "Assets", "Sky", "stars.png")

SOURCE = "https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz"

# Fixed-width J2000 fields, from the catalogue's own ReadMe, converted to zero-based slices.
RIGHT_ASCENSION = (75, 83)
DECLINATION = (83, 90)
MAGNITUDE = (102, 107)
COLOUR_INDEX = (109, 114)

WIDTH = 8192
HEIGHT = 4096

LIMITING_MAGNITUDE = 6.5

# Raw flux across the catalogue spans a factor of about 1600, far more than a display can hold, so
# the stored value is a compressed stop of it. The shader multiplies straight through.
COMPRESSION = 0.38

SPREAD = 0.8
MAX_SPREAD = 48.0


def fetch():

    path = os.path.join(CACHE, "bsc5.dat")

    if os.path.exists(path):

        return path

    os.makedirs(CACHE, exist_ok=True)

    with urllib.request.urlopen(SOURCE) as response:

        data = gzip.decompress(response.read())

    with open(path, "wb") as handle:

        handle.write(data)

    return path


def temperature(colour_index):

    """Ballesteros' relation between B-V and effective temperature."""
    return 4600.0 * (1.0 / (0.92 * colour_index + 1.7) + 1.0 / (0.92 * colour_index + 0.62))


def blackbody(kelvin):

    """Tanner Helland's piecewise fit to blackbody chromaticity, normalised to its brightest channel."""
    scaled = min(max(kelvin, 1500.0), 40000.0) / 100.0

    if scaled <= 66.0:

        red = 255.0
        green = 99.4708025861 * math.log(scaled) - 161.1195681661

    else:

        red = 329.698727446 * (scaled - 60.0) ** -0.1332047592
        green = 288.1221695283 * (scaled - 60.0) ** -0.0755148492

    if scaled >= 66.0:

        blue = 255.0

    elif scaled <= 19.0:

        blue = 0.0

    else:

        blue = 138.5177312231 * math.log(scaled - 10.0) - 305.0447927307

    channels = [min(max(value, 0.0), 255.0) for value in (red, green, blue)]

    return np.array(channels, dtype=np.float64) / max(channels)


def read_catalogue():

    stars = []

    with open(fetch(), "r", encoding="latin-1") as handle:

        for line in handle:

            ascension = line[slice(*RIGHT_ASCENSION)]
            declination = line[slice(*DECLINATION)]
            magnitude = line[slice(*MAGNITUDE)].strip()

            # Novae and other entries the catalogue keeps without a position or a magnitude.
            if not ascension.strip() or not magnitude:

                continue

            hours = float(ascension[0:2]) + float(ascension[2:4]) / 60.0 + float(ascension[4:8]) / 3600.0

            degrees = float(declination[1:3]) + float(declination[3:5]) / 60.0 + float(declination[5:7]) / 3600.0

            if declination[0] == "-":

                degrees = -degrees

            colour_index = line[slice(*COLOUR_INDEX)].strip()

            stars.append((
                math.radians(hours * 15.0),
                math.radians(degrees),
                float(magnitude),
                blackbody(temperature(float(colour_index) if colour_index else 0.65)),
            ))

    return stars


def splat(canvas, ascension, declination, colour, flux):

    """Lay one star down as a small round Gaussian, widened in longitude to stay round on the sphere."""

    u = (ascension / (2.0 * math.pi)) * WIDTH
    v = (0.5 - declination / math.pi) * HEIGHT

    spread_v = SPREAD
    spread_u = min(SPREAD / max(math.cos(declination), 1e-3), MAX_SPREAD)

    reach_u = int(math.ceil(spread_u * 3.0))
    reach_v = int(math.ceil(spread_v * 3.0))

    columns = np.arange(int(u) - reach_u, int(u) + reach_u + 1)
    rows = np.arange(max(int(v) - reach_v, 0), min(int(v) + reach_v + 1, HEIGHT))

    if rows.size == 0:

        return

    weights = np.exp(-((columns - u) ** 2) / (2.0 * spread_u * spread_u))[None, :] * np.exp(-((rows - v) ** 2) / (2.0 * spread_v * spread_v))[:, None]

    total = weights.sum()

    if total <= 0.0:

        return

    patch = (weights / total)[:, :, None] * colour[None, None, :] * flux

    np.add.at(canvas, (rows[:, None], np.mod(columns, WIDTH)[None, :]), patch)


def main():

    os.makedirs(os.path.dirname(OUT), exist_ok=True)

    stars = read_catalogue()

    canvas = np.zeros((HEIGHT, WIDTH, 3), dtype=np.float32)

    for ascension, declination, magnitude, colour in stars:

        splat(canvas, ascension, declination, colour, (10.0 ** (-0.4 * (magnitude - LIMITING_MAGNITUDE))) ** COMPRESSION)

    # Scaling on the brightest pixel rather than the brightest star spends the whole 8-bit range on
    # the run of faint stars, which is where the quantisation would otherwise show.
    canvas /= canvas.max()

    Image.fromarray((np.clip(canvas, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGB").save(OUT, optimize=True)

    print(f"{len(stars)} stars baked into {OUT}")


if __name__ == "__main__":

    main()
