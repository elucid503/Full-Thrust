#!/usr/bin/env python3
"""Generate the vessel's detail parts with Meshy and write them to game/Assets/Vessel.

Parts with a real reference photograph go through image-to-3D, which reproduces actual hardware
far more faithfully than any text prompt; the rest go through text-to-3D as preview then refine.
Task ids are cached in tools/.cache/meshy.json, so a re-run resumes and never pays twice.
"""

import json
import pathlib
import sys
import time
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
CACHE = ROOT / "tools" / ".cache" / "meshy.json"
REFERENCE = ROOT / "tools" / ".cache" / "reference"
OUT = ROOT / "game" / "Assets" / "Vessel"

TEXT_API = "https://api.meshy.ai/openapi/v2/text-to-3d"
IMAGE_API = "https://api.meshy.ai/openapi/v1/image-to-3d"

# Wikimedia refuses the default urllib agent outright.
AGENT = "FullThrust/0.1 (personal project; contact elucid@duck.com)"

# Public-domain NASA photographs of the real hardware each part is modelled on.
REFERENCES = {

    "engine": "https://upload.wikimedia.org/wikipedia/commons/9/95/RL-10_rocket_engine.jpg",
    "rcs": "https://upload.wikimedia.org/wikipedia/commons/3/38/Apollo_RCS_quad.jpg",

}

PROMPTS = {

    "avionics": (

        "spacecraft avionics module, rectangular machined aluminium housing with milled cooling "
        "ribs, circular military connectors on the face, a bundled cable harness with lacing, "
        "white thermal paint over bare metal, real flight hardware, single part, no stand"

    ),

}


def key():

    for line in (ROOT / ".env").read_text().splitlines():

        if line.startswith("MESHY_KEY="):

            return line.split("=", 1)[1].strip()

    sys.exit("MESHY_KEY missing from .env")


def fetch(url, headers=None):

    return urllib.request.urlopen(urllib.request.Request(url, headers=headers or {"User-Agent": AGENT}))


def call(path, token, payload=None):

    request = urllib.request.Request(
        path,
        data=json.dumps(payload).encode() if payload else None,
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
        method="POST" if payload else "GET",
    )

    with urllib.request.urlopen(request) as response:

        return json.load(response)


def wait(api, task, token):

    while True:

        state = call(f"{api}/{task}", token)
        status = state["status"]

        if status == "SUCCEEDED":

            return state

        if status in ("FAILED", "CANCELED"):

            sys.exit(f"{task}: {status} {state.get('task_error')}")

        print(f"  {task} {status} {state.get('progress', 0)}%", flush=True)
        time.sleep(15)


def data_uri(url):

    REFERENCE.mkdir(parents=True, exist_ok=True)

    local = REFERENCE / url.rsplit("/", 1)[-1]

    if not local.exists():

        with fetch(url) as response:

            local.write_bytes(response.read())

    import base64

    return "data:image/jpeg;base64," + base64.b64encode(local.read_bytes()).decode()


def from_image(name, url, token, entry):

    if "image" not in entry:

        entry["image"] = call(IMAGE_API, token, {

            "image_url": data_uri(url),
            "ai_model": "meshy-5",
            "topology": "triangle",
            "target_polycount": 40000,
            "should_remesh": True,
            "should_texture": True,
            "enable_pbr": True,
            "symmetry_mode": "auto",

        })["result"]

        save(entry)

    print(f"{name}: image-to-3d {entry['image']}", flush=True)

    return wait(IMAGE_API, entry["image"], token)


def from_text(name, prompt, token, entry):

    if "preview" not in entry:

        entry["preview"] = call(TEXT_API, token, {

            "mode": "preview",
            "prompt": prompt,
            "art_style": "realistic",
            "ai_model": "meshy-5",
            "topology": "triangle",
            "target_polycount": 40000,
            "should_remesh": True,
            "symmetry_mode": "auto",

        })["result"]

        save(entry)

    print(f"{name}: preview {entry['preview']}", flush=True)
    wait(TEXT_API, entry["preview"], token)

    if "refine" not in entry:

        entry["refine"] = call(TEXT_API, token, {

            "mode": "refine",
            "preview_task_id": entry["preview"],
            "enable_pbr": True,

        })["result"]

        save(entry)

    print(f"{name}: refine {entry['refine']}", flush=True)

    return wait(TEXT_API, entry["refine"], token)


_cache = {}


def save(_entry=None):

    CACHE.parent.mkdir(parents=True, exist_ok=True)
    CACHE.write_text(json.dumps(_cache, indent=2))


def main():

    global _cache

    token = key()

    OUT.mkdir(parents=True, exist_ok=True)

    _cache = json.loads(CACHE.read_text()) if CACHE.exists() else {}

    for name in list(REFERENCES) + list(PROMPTS):

        entry = _cache.setdefault(name, {})

        if name in REFERENCES:

            state = from_image(name, REFERENCES[name], token, entry)

        else:

            state = from_text(name, PROMPTS[name], token, entry)

        with fetch(state["model_urls"]["glb"]) as response:

            (OUT / f"{name}.glb").write_bytes(response.read())

        print(f"{name}: wrote {OUT / (name + '.glb')}", flush=True)


if __name__ == "__main__":

    main()
