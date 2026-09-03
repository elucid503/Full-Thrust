#!/usr/bin/env python3
"""Convert KSP part models (.mu) into GLB for game/Assets/Vessel.

KSP ships Unity-derived binary .mu models and DXT-compressed .dds textures; Godot reads neither.
The binary format layer is taniwha's io_object_mu, cloned into tools/.cache on first run - only its
mu.py is used, and that module is plain Python with no Blender dependency, so the whole conversion
is one reproducible script rather than a hand-driven Blender session.

  python tools/ksp_import.py --list                       what the game currently uses
  python tools/ksp_import.py engine rcs --install         convert, then reimport in Godot
  python tools/ksp_import.py --preview engine             also write a shaded PNG

Each part is one entry in PARTS. A KSP model carries every variant its part switcher can show plus
colliders, attachment nodes and effect emitters, so `drop` prunes subtrees by name and `keep`
restricts the export to one variant. Colliders and effects need no rules of their own: they carry
no renderer, and that is the test used to decide whether geometry is drawn at all.

Choosing a part is the slow half, so it runs without editing this file. `--probe` prints a model's
hierarchy, materials and triangle counts, which is where the `keep` and `drop` names come from, and
`--try` converts any model straight into tools/.cache with a preview and its measured dimensions:

  python tools/ksp_import.py --probe ReStock/Assets/Coupling/restock-engineplate-25-1.mu
  python tools/ksp_import.py --try ReStock/Assets/Coupling/restock-engineplate-25-1.mu \\
      --keep Boattail-25-Metal --shell barrel

Several `--try` runs in one command are previewed onto a single contact sheet for comparison.
"""

import argparse
import os
import pathlib
import re
import subprocess
import sys

import numpy as np
import trimesh
from PIL import Image, ImageDraw

ROOT = pathlib.Path(__file__).resolve().parent.parent
CACHE = ROOT / "tools" / ".cache"
ADDON = CACHE / "io_object_mu"
ADDON_URL = "https://github.com/taniwha/io_object_mu.git"

OUT = ROOT / "game" / "Assets" / "Vessel"

DEFAULT_GAMEDATA = "C:/Program Files (x86)/Steam/steamapps/common/Kerbal Space Program/GameData"

TEXTURE_SUFFIXES = (".dds", ".png", ".tga", ".jpg")

# Materials whose shader only ever draws an effect billboard: engine glow discs, RCS jets, flares.
EFFECT_SHADERS = ("particles", "unlit", "translucent")


class Part:

    def __init__(self, model, keep=(), drop=(), metallic=1.0, gloss=1.0, emissive=False, shell=None):

        self.model = model
        self.keep = tuple(keep)
        self.drop = tuple(drop)

        # A shell is a skin for a mould-line band rather than a part in its own right, so it is
        # exported at unit radius and unit length for the renderer to stretch onto the stations.
        # A "barrel" takes its radius from the modal station, a "cone" from the widest one, because
        # a taper spreads its radii evenly and has no modal station worth the name.
        self.shell = shell

        # Specular-workflow art has no metalness channel, so it is derived from the specular level
        # in the albedo's alpha. These two scale that derivation; they are the only tuning knobs.
        self.metallic = metallic
        self.gloss = gloss

        self.emissive = emissive


PARTS = {

    # A compact powerhead over a long regeneratively-cooled bell, in the proportions a 180 kN
    # vacuum stage wants. Its siblings under Erebus are the mount ring, boattail and shrouds,
    # all of which belong to the stage that carries it, not to the engine.
    "engine": Part(

        model="CryoEngines/Assets/Engine/cryoengine-erebus-1.mu",

        keep=("B_Erebus_Common",),

    ),

    # The RV-105 quad. `4Mask` is a depth mask that fakes hollow nozzles in KSP's forward
    # renderer; drawn normally it is four solid caps across the throats.
    "rcs": Part(

        model="ReStock/Assets/Control/restock-rcs-block-1.mu",

        drop=("4Mask",),

    ),

    # The 2.5 m barrel, whose 2.98 length-to-diameter ratio is within three percent of the mould
    # line's own tank band, so its panel lines and stringers land undistorted. Its four siblings
    # are the same cylinder painted for other liveries, stacked in the same space.
    "tank": Part(

        model="ReStock/Assets/FuelTank/restock-fueltank-25-1.mu",

        keep=("25TankLargeWhite",),

        shell="barrel",

    ),

    # The thrust structure the engine hangs in. This is an engine plate's boattail, which is the
    # part KSP uses for exactly this job, so it already carries the stringers and the vent cutouts.
    "skirt": Part(

        model="ReStock/Assets/Coupling/restock-engineplate-25-1.mu",

        keep=("Boattail-25-Metal",),

        shell="barrel",

    ),

    # A two-crew capsule stands in for the fairing. `Mk2PodDark` is the same hull in the other
    # livery, and the flag decal is an alpha-blended plane that only reads right in KSP's renderer.
    "nose": Part(

        model="ReStock/Assets/Command/restock-mk2-pod.mu",

        keep=("Mk2PodWhite",),

        shell="cone",

    ),

}


def gamedata():

    path = pathlib.Path(os.environ.get("KSP_GAMEDATA", DEFAULT_GAMEDATA))

    if not path.is_dir():
        sys.exit(f"KSP GameData not found at {path}; set KSP_GAMEDATA")

    return path


def mu_module():

    if not ADDON.is_dir():

        CACHE.mkdir(parents=True, exist_ok=True)

        print(f"cloning {ADDON_URL}")
        subprocess.run(["git", "clone", "--depth", "1", ADDON_URL, str(ADDON)], check=True)

    sys.path.insert(0, str(ADDON))

    import mu

    return mu


# mu.py already rebases Unity's left-handed Y-up onto a right-handed Z-up frame and reverses the
# triangle winding to match. Only the Z-up to Y-up quarter turn is left, and that is a pure
# rotation, so the winding it hands over stays correct.
ZUP_TO_YUP = np.array([

    [1.0, 0.0, 0.0, 0.0],
    [0.0, 0.0, 1.0, 0.0],
    [0.0, -1.0, 0.0, 0.0],
    [0.0, 0.0, 0.0, 1.0],

])


def local_matrix(transform):

    matrix = trimesh.transformations.quaternion_matrix(transform.localRotation)

    matrix = matrix @ np.diag(list(transform.localScale) + [1.0])
    matrix[:3, 3] = transform.localPosition

    return matrix


def collect(node, matrix, part, kept, out):

    if node.transform.name in part.drop:
        return

    matrix = matrix @ local_matrix(node.transform)

    kept = kept or not part.keep or node.transform.name in part.keep

    mesh = getattr(node, "shared_mesh", None)
    renderer = getattr(node, "renderer", None)

    if kept and mesh is not None and renderer is not None:
        out.append((matrix, mesh, renderer.materials))

    for child in node.children:
        collect(child, matrix, part, kept, out)


def find_texture(name, model_path, root):

    stem = pathlib.Path(name).stem

    for directory in (model_path.parent, *model_path.parents):

        for suffix in TEXTURE_SUFFIXES:

            candidate = directory / (stem + suffix)

            if candidate.is_file():
                return candidate

        if directory == root:
            break

    matches = sorted(root.rglob(stem + ".*"))

    return matches[0] if matches else None


def load_maps(material, textures, model_path, root, part):
    """KSP art is specular-workflow: albedo RGB with the specular level in its alpha, and a DXT5nm
    normal map with X hidden in the alpha channel. Rebuild both as glTF metallic-roughness."""

    def texture(slot):

        entry = material.textureProperties.get(slot)

        if entry is None:
            return None

        path = find_texture(textures[entry.index].name, model_path, root)

        if path is None:

            print(f"  missing texture {textures[entry.index].name}")

            return None

        return Image.open(path).convert("RGBA")

    main = texture("_MainTex")

    if main is None:
        return None, None, None, None

    pixels = np.asarray(main, dtype=np.float32) / 255.0

    albedo = Image.fromarray((pixels[:, :, :3] * 255.0).astype(np.uint8), "RGB")

    specular = pixels[:, :, 3]
    shininess = float(material.floatProperties3.get("_Shininess", 0.3))

    metallic = np.clip(specular * part.metallic, 0.0, 1.0)
    roughness = np.clip(1.0 - specular * (0.45 + shininess) * part.gloss, 0.05, 1.0)

    packed = np.zeros(pixels.shape[:2] + (3,), dtype=np.uint8)
    packed[:, :, 1] = (roughness * 255.0).astype(np.uint8)
    packed[:, :, 2] = (metallic * 255.0).astype(np.uint8)

    rough_metal = Image.fromarray(packed, "RGB")

    normal = None
    bump = texture("_BumpMap")

    if bump is not None:

        source = np.asarray(bump, dtype=np.float32) / 255.0

        x = source[:, :, 3] * 2.0 - 1.0
        y = source[:, :, 1] * 2.0 - 1.0
        z = np.sqrt(np.clip(1.0 - x * x - y * y, 0.0, 1.0))

        stacked = np.stack([x, y, z], axis=-1) * 0.5 + 0.5

        normal = Image.fromarray((stacked * 255.0).astype(np.uint8), "RGB")

    emissive = texture("_Emissive") if part.emissive else None

    if emissive is not None:
        emissive = emissive.convert("RGB")

    return albedo, rough_metal, normal, emissive


def fit(points, mode):
    """Centre and scale that take a body of revolution to unit radius and unit length. The renderer
    then stretches it onto a band of the hull's own mould line, so the source part's dimensions never
    have to be copied into the game as constants that could drift from the stations behind them."""

    # Radii are meaningless until the axis of revolution is on the origin, and a .mu prefab arrives
    # parked wherever it sat in the scene it was authored in.
    axis = (points[:, [0, 2]].min(axis=0) + points[:, [0, 2]].max(axis=0)) * 0.5

    radius = np.hypot(points[:, 0] - axis[0], points[:, 2] - axis[1])

    low = float(points[:, 1].min())
    high = float(points[:, 1].max())

    if mode == "cone":

        peak = float(radius.max())

        # Seat a taper on its widest ring, not on its lowest vertex. A capsule's recessed floor hangs
        # below that ring, and fitting the bounding box instead lifts the hull clear of whatever it
        # stands on, leaving an open ring of daylight at the joint.
        low = float(np.median(points[radius > peak * 0.98, 1]))

    else:

        # The modal radius is the barrel itself. A mean or median is dragged outwards by end flanges
        # and by the conduit run that stands proud of it.
        counts, edges = np.histogram(radius, bins=64)
        peak = float(edges[counts.argmax()] + edges[counts.argmax() + 1]) * 0.5

    centre = np.array([axis[0], (low + high) * 0.5, axis[1]])

    return centre, np.array([1.0 / peak, 1.0 / (high - low), 1.0 / peak])


def build(name, part, root):

    mu = mu_module()

    model_path = root / part.model

    if not model_path.is_file():
        sys.exit(f"{name}: model not found at {model_path}")

    model = mu.Mu()

    if not model.read(str(model_path)):
        sys.exit(f"{name}: {model_path} is not a readable .mu")

    pieces = []

    collect(model.obj, ZUP_TO_YUP, part, False, pieces)

    groups = {}

    for matrix, mesh, materials in pieces:

        for index, faces in enumerate(mesh.submeshes):

            slot = materials[index] if index < len(materials) else materials[0]

            if any(tag in model.materials[slot].shaderName.lower() for tag in EFFECT_SHADERS):
                continue

            groups.setdefault(slot, []).append((matrix, mesh, faces))

    if not groups:
        sys.exit(f"{name}: nothing to export - every submesh was dropped")

    surfaces = []

    for slot, entries in groups.items():

        vertices = []
        indices = []
        uvs = []
        normals = []

        base = 0

        for matrix, mesh, faces in entries:

            points = trimesh.transformations.transform_points(np.asarray(mesh.verts, dtype=np.float64), matrix)

            direction = np.asarray(mesh.normals, dtype=np.float64) if mesh.normals else np.zeros_like(points)
            direction = direction @ matrix[:3, :3].T

            # Unity samples textures from the bottom left, glTF from the top left.
            uv = np.asarray(mesh.uvs, dtype=np.float64) if mesh.uvs else np.zeros((len(points), 2))
            uv = np.column_stack([uv[:, 0], 1.0 - uv[:, 1]])

            vertices.append(points)
            normals.append(direction)
            uvs.append(uv)
            indices.append(np.asarray(faces, dtype=np.int64) + base)

            base += len(points)

        vertices = np.vstack(vertices)
        normals = np.vstack(normals)
        uvs = np.vstack(uvs)
        indices = np.vstack(indices)

        facing = np.cross(vertices[indices[:, 1]] - vertices[indices[:, 0]], vertices[indices[:, 2]] - vertices[indices[:, 0]])

        # A face of zero area has no direction, and one left in place turns every normal that later
        # gets rebuilt from it into a NaN.
        solid = np.linalg.norm(facing, axis=1) > 1e-12

        indices = indices[solid]
        facing = facing[solid]

        # Check the winding against the authored normals rather than reasoning about handedness:
        # a silently inverted hull renders solid and plausible, and reads as a lighting bug.
        if np.einsum("ij,ij->i", facing, normals[indices].sum(axis=1)).sum() < 0.0:

            print(f"  {model.materials[slot].name}: winding inverted, flipped")

            indices = indices[:, ::-1]

        surfaces.append((slot, vertices, indices, uvs, normals))

    # A .mu holds a prefab still parked wherever it sat in the scene it was authored in.
    points = np.vstack([surface[1] for surface in surfaces])

    if part.shell:

        centre, scale = fit(points, part.shell)

    else:

        centre, scale = (points.min(axis=0) + points.max(axis=0)) * 0.5, np.ones(3)

    scene = trimesh.Scene()

    for slot, vertices, indices, uvs, normals in surfaces:

        material = model.materials[slot]

        albedo, rough_metal, normal, emissive = load_maps(material, model.textures, model_path, root, part)

        # Normals of a non-uniform scale follow the inverse transpose, not the scale itself.
        normals = normals / scale
        normals = normals / np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-12)

        visual = trimesh.visual.TextureVisuals(

            uv=uvs,

            material=trimesh.visual.material.PBRMaterial(

                name=material.name,

                baseColorTexture=albedo,
                metallicRoughnessTexture=rough_metal,
                normalTexture=normal,
                emissiveTexture=emissive,

                metallicFactor=1.0,
                roughnessFactor=1.0,

            ),

        )

        piece = trimesh.Trimesh(

            vertices=(vertices - centre) * scale,
            faces=indices,

            vertex_normals=normals,
            visual=visual,

            process=False,

        )

        scene.add_geometry(piece, geom_name=material.name)

    OUT.mkdir(parents=True, exist_ok=True)

    target = OUT / (name + ".glb")

    target.write_bytes(scene.export(file_type="glb"))

    triangles = sum(len(geometry.faces) for geometry in scene.geometry.values())

    span = float(points[:, 1].max() - points[:, 1].min())
    barrel = 1.0 / fit(points, part.shell or "barrel")[1][0]

    # Length, shell radius and their ratio are what a band on the mould line has to be matched against.
    print(f"{name}: {triangles} tris, {len(scene.geometry)} materials, "
          f"length {span:.3f}, radius {barrel:.3f}, ratio {span / (2.0 * barrel):.3f} -> {target.relative_to(ROOT)}")

    return scene


def preview(scene, path, size=520):
    """A flat-shaded orthographic z-buffer, three quarters on. Enough to judge a silhouette without
    a GPU, a window, or a round trip through the engine."""

    vertices = []
    faces = []

    base = 0

    for geometry in scene.geometry.values():

        vertices.append(geometry.vertices)
        faces.append(geometry.faces + base)

        base += len(geometry.vertices)

    vertices = np.vstack(vertices)
    faces = np.vstack(faces)

    facing = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    facing = facing / np.maximum(np.linalg.norm(facing, axis=1, keepdims=True), 1e-12)

    yaw = np.radians(35.0)
    pitch = np.radians(18.0)

    right = np.array([np.cos(yaw), 0.0, -np.sin(yaw)])
    up = np.array([-np.sin(yaw) * np.sin(pitch), np.cos(pitch), -np.cos(yaw) * np.sin(pitch)])
    view = np.cross(right, up)

    axes = np.stack([right, up, view], axis=1)

    points = vertices @ axes

    low = points.min(axis=0)
    high = points.max(axis=0)

    span = max(high[0] - low[0], high[1] - low[1]) * 1.06
    centre = (high + low) * 0.5

    screen = np.empty((len(points), 3))
    screen[:, 0] = (points[:, 0] - centre[0]) / span * size + size * 0.5
    screen[:, 1] = size * 0.5 - (points[:, 1] - centre[1]) / span * size
    screen[:, 2] = points[:, 2]

    # Over the camera's shoulder, high and to the right; smaller view components are nearer.
    light = right * 0.42 + up * 0.66 - view * 0.62

    shade = np.clip(facing @ (light / np.linalg.norm(light)), 0.0, 1.0)
    shade = 0.12 + 0.88 * shade ** 0.65

    depth = np.full((size, size), np.inf)
    image = np.zeros((size, size), dtype=np.float64)

    for face, value in zip(faces, shade):

        tri = screen[face]

        x0, y0 = np.floor(tri[:, :2].min(axis=0)).astype(int)
        x1, y1 = np.ceil(tri[:, :2].max(axis=0)).astype(int)

        x0, y0 = max(x0, 0), max(y0, 0)
        x1, y1 = min(x1, size - 1), min(y1, size - 1)

        if x1 < x0 or y1 < y0:
            continue

        ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]

        ax, ay = tri[0, 0], tri[0, 1]
        bx, by = tri[1, 0], tri[1, 1]
        cx, cy = tri[2, 0], tri[2, 1]

        area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax)

        if abs(area) < 1e-9:
            continue

        w0 = ((bx - ax) * (ys - ay) - (by - ay) * (xs - ax)) / area
        w1 = ((xs - ax) * (cy - ay) - (ys - ay) * (cx - ax)) / area

        inside = (w0 >= 0.0) & (w1 >= 0.0) & (w0 + w1 <= 1.0)

        if not inside.any():
            continue

        z = tri[0, 2] + w1 * (tri[1, 2] - tri[0, 2]) + w0 * (tri[2, 2] - tri[0, 2])

        window = depth[y0:y1 + 1, x0:x1 + 1]
        mask = inside & (z < window)

        window[mask] = z[mask]
        image[y0:y1 + 1, x0:x1 + 1][mask] = value

    Image.fromarray((np.clip(image, 0.0, 1.0) * 255.0).astype(np.uint8), "L").save(path)

    print(f"  preview {path.relative_to(ROOT)}")


def probe(model_path):
    """Print a model's hierarchy, materials and triangle counts. Every `keep` and `drop` name in
    PARTS was read off one of these; guessing them from a part's in-game name does not work."""

    mu = mu_module()

    model = mu.Mu()

    if not model.read(str(model_path)):
        sys.exit(f"{model_path} is not a readable .mu")

    print(model_path.name)

    for index, material in enumerate(model.materials):
        print(f"  material {index}: {material.name}  [{material.shaderName}]")

    def walk(node, depth):

        mesh = getattr(node, "shared_mesh", None)
        renderer = getattr(node, "renderer", None)

        drawn = mesh is not None and renderer is not None
        count = sum(len(faces) for faces in mesh.submeshes) if drawn else 0

        note = f"  [{count} tris, material {list(renderer.materials)}]" if drawn else ""

        print("  " * (depth + 1) + node.transform.name + note)

        for child in node.children:
            walk(child, depth + 1)

    walk(model.obj, 0)


def sheet(names, path):
    """One contact sheet, so a run of candidates can be compared side by side instead of opened
    one at a time."""

    tiles = [Image.open(CACHE / f"preview-{name}.png") for name in names]

    size = max(tile.width for tile in tiles)

    columns = min(len(names), 4)
    rows = (len(names) + columns - 1) // columns

    canvas = Image.new("L", (size * columns, (size + 20) * rows), 0)
    pen = ImageDraw.Draw(canvas)

    for index, (name, tile) in enumerate(zip(names, tiles)):

        x = (index % columns) * size
        y = (index // columns) * (size + 20)

        canvas.paste(tile, (x, y + 20))
        pen.text((x + 6, y + 5), name, fill=255)

    canvas.save(path)

    print(f"  sheet {path.relative_to(ROOT)}")


def godot_binary():
    """godot.sh owns the engine path, so read its default out rather than keeping a second copy.
    Running the engine through the script instead would mean picking a bash, and the one on PATH
    here is a WSL build that cannot execute a Windows executable."""

    if "GODOT" in os.environ:
        return os.environ["GODOT"]

    text = (ROOT / "tools" / "godot.sh").read_text()

    found = re.search(r'GODOT="\$\{GODOT:-(.+?)\}"', text)

    if found is None:
        sys.exit("could not read the engine path out of tools/godot.sh; set GODOT")

    return found.group(1)


def install():
    """Godot's headless importer skips 3D texture detection, so new art lands uncompressed unless
    it is imported, patched and imported again. Three commands that are never useful apart."""

    project = str(ROOT / "game")

    reimport = [godot_binary(), "--path", project, "--headless", "--import"]

    subprocess.run(reimport, check=True, stdout=subprocess.DEVNULL)
    subprocess.run([sys.executable, str(ROOT / "tools" / "import_fix.py"), str(OUT)], check=True)
    subprocess.run(reimport, check=True, stdout=subprocess.DEVNULL)

    print("reimported")


def main():

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)

    parser.add_argument("parts", nargs="*", help="part names from the PARTS table")

    parser.add_argument("--list", action="store_true", help="show the known parts and exit")
    parser.add_argument("--preview", action="store_true", help="also write a shaded PNG into tools/.cache")
    parser.add_argument("--install", action="store_true", help="reimport in Godot afterwards, both passes")

    parser.add_argument("--probe", metavar="MODEL", nargs="+", help="print a model's hierarchy and exit")
    parser.add_argument("--try", dest="attempt", metavar="MODEL", nargs="+", help="convert a model into tools/.cache")

    parser.add_argument("--keep", default="", help="--try only: comma-separated sub-objects to keep")
    parser.add_argument("--drop", default="", help="--try only: comma-separated sub-objects to prune")
    parser.add_argument("--shell", choices=("barrel", "cone"), help="--try only: export at unit radius and length")

    args = parser.parse_args()

    if args.list:

        for name, part in PARTS.items():
            print(f"{name:12s} {part.model}")

        return

    root = gamedata()

    CACHE.mkdir(parents=True, exist_ok=True)

    if args.probe:

        for model in args.probe:
            probe(root / model)

        return

    global OUT

    names = args.parts or list(PARTS)
    table = PARTS

    if args.attempt:

        # Candidates stay out of the game's asset directory until one of them is chosen.
        OUT = CACHE / "cand"

        table = {

            pathlib.Path(model).stem: Part(

                model=model,

                keep=[name for name in args.keep.split(",") if name],
                drop=[name for name in args.drop.split(",") if name],

                shell=args.shell,

            )

            for model in args.attempt

        }

        names = list(table)

    for name in names:

        if name not in table:
            sys.exit(f"unknown part {name}; try --list")

        scene = build(name, table[name], root)

        if args.preview or args.attempt:
            preview(scene, CACHE / f"preview-{name}.png")

    if len(names) > 1 and (args.preview or args.attempt):
        sheet(names, CACHE / "sheet.png")

    if args.install:
        install()


if __name__ == "__main__":
    main()
