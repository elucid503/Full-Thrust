#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SOURCE="${1:-$ROOT/tools/.cache/terrain-materials}"
SOURCE="$(cd "$SOURCE" && pwd)"
mkdir -p "$ROOT/game/.artifacts"
WORK="$(mktemp -d "$ROOT/game/.artifacts/terrain-pack.XXXXXX")"
trap 'rm -f -- "$WORK/pack.gd"; rmdir -- "$WORK"' EXIT

cat > "$WORK/pack.gd" <<'GDSCRIPT'
extends SceneTree

func _initialize():
    var source = OS.get_cmdline_user_args()[0]
    for material in [["Rock030", "rock"], ["Ground037", "soil"]]:
        var archive = ZIPReader.new()
        if archive.open(source.path_join(material[0] + "_2K-JPG.zip")) != OK:
            quit(1)
            return
        var channels = []
        for channel in ["Displacement", "AmbientOcclusion", "Roughness"]:
            var plate = Image.new()
            if plate.load_jpg_from_buffer(archive.read_file(material[0] + "_2K-JPG_" + channel + ".jpg")) != OK:
                quit(1)
                return
            plate.resize(1024, 1024, Image.INTERPOLATE_LANCZOS)
            plate.convert(Image.FORMAT_L8)
            channels.append(plate.get_data())
        archive.close()
        var pixels = PackedByteArray()
        pixels.resize(1024 * 1024 * 3)
        for pixel in 1024 * 1024:
            for channel in 3:
                pixels[pixel * 3 + channel] = channels[channel][pixel]
        var packed = Image.create_from_data(1024, 1024, false, Image.FORMAT_RGB8, pixels)
        var output = "res://Assets/Planet/" + material[1] + "_detail.png"
        if packed.save_png(output) != OK:
            quit(1)
            return
        print(output)
    quit()
GDSCRIPT

bash "$ROOT/tools/godot.sh" --headless --script "res://.artifacts/$(basename "$WORK")/pack.gd" -- "$SOURCE"
