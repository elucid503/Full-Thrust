"""Bake compact procedural controls from HYBMAP 2020 IGBP land cover.

Source: https://zenodo.org/records/6717123 (Zhu et al., HYBMAP).
The source is a classification, never a colour photograph. RGBA stores vegetation,
tree cover, aridity, and permanent ice; mixtures are averaged after classification.
"""

import hashlib
from pathlib import Path
import urllib.request

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools/.cache/hybmap-2020.tif"
URL = "https://zenodo.org/records/6717123/files/HYBMAP_IGBP_2020_LC.tif?download=1"
MD5 = "81c91d1f1d3884431569cda5181f7f30"

# IGBP: water, five forests, two shrublands, two savannas, grass, wetland,
# crops, urban, crop mosaic, snow, barren, water. Values are authored cover fractions.
CONTROLS = np.array([
    [0.45, 0.05, 0.10, 0.00],
    [0.85, 0.90, 0.05, 0.00],
    [0.98, 1.00, 0.00, 0.00],
    [0.80, 0.80, 0.12, 0.00],
    [0.90, 0.90, 0.05, 0.00],
    [0.90, 0.90, 0.05, 0.00],
    [0.48, 0.10, 0.45, 0.00],
    [0.22, 0.02, 0.75, 0.00],
    [0.65, 0.38, 0.32, 0.00],
    [0.55, 0.12, 0.48, 0.00],
    [0.72, 0.00, 0.25, 0.00],
    [0.85, 0.18, 0.00, 0.00],
    [0.75, 0.00, 0.20, 0.00],
    [0.16, 0.00, 0.30, 0.00],
    [0.75, 0.16, 0.20, 0.00],
    [0.00, 0.00, 0.00, 1.00],
    [0.02, 0.00, 1.00, 0.00],
    [0.45, 0.05, 0.10, 0.00],
], dtype=np.float32)


def main():
    SOURCE.parent.mkdir(parents=True, exist_ok=True)
    if not SOURCE.exists():
        temporary = SOURCE.with_suffix(".part")
        urllib.request.urlretrieve(URL, temporary)
        if hashlib.md5(temporary.read_bytes()).hexdigest() != MD5:
            raise ValueError("Downloaded HYBMAP checksum differs from the pinned Zenodo record")
        temporary.replace(SOURCE)
    if hashlib.md5(SOURCE.read_bytes()).hexdigest() != MD5:
        raise ValueError("HYBMAP checksum differs from the pinned Zenodo record")

    Image.MAX_IMAGE_PIXELS = None
    with Image.open(SOURCE) as survey:
        if survey.size != (43200, 18000):
            raise ValueError("Expected 30 arc-second HYBMAP from 90N to 60S")
        # Nearest sampling preserves category IDs; only cover fractions may be averaged.
        classes = np.array(survey.resize((7200, 3000), Image.Resampling.NEAREST))
    if not np.isin(classes, list(range(18)) + [127]).all():
        raise ValueError("Unknown HYBMAP class")
    classes[classes == 127] = 17
    globe = np.full((3600, 7200), 15, dtype=np.uint8)
    globe[:3000] = classes
    pixels = np.round(CONTROLS[globe] * 255).astype(np.uint8)
    # Alpha is ice data, so never let Pillow premultiply the other channels by it.
    image = Image.merge("RGBA", [Image.fromarray(pixels[..., channel]).resize(
        (4096, 2048), Image.Resampling.BOX) for channel in range(4)])
    destination = ROOT / "game/Assets/Planet/biomes.png"
    image.save(destination, optimize=True)
    print(f"{destination}: {destination.stat().st_size / 1e6:.2f} MB")


if __name__ == "__main__":
    main()
