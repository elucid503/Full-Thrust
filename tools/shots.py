#!/usr/bin/env python
"""Drive the running game's debug bridge through a set of viewpoints and capture each one.

The surface has to look right from orbit, from an airliner's height and from the pad, and the only
way to know whether a change to one has broken another is to take all of them every time.

    py tools/shots.py                    every view, into game/.artifacts/m4
    py tools/shots.py alps pad           only the named views
    py tools/shots.py --tag before       write them as <name>-before.png
"""

import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BRIDGE = "http://localhost:9080"
OUT = ".artifacts/m4"

# The quadtree only subdivides towards the eye, so a jump has to be given frames to page in before
# the shot is worth taking. This is how many state polls it takes to stop growing.
SETTLE = 40


# Depression is measured off the local horizon and bearing off local north, so a view frames the
# same thing wherever on the planet it is taken.
VIEWS = {

    # Cloud deck and limb from a low orbit, which is what the milestone is measured on.
    "orbit": dict(latitude=-25.0, longitude=-72.0, altitude=180_000.0, look=35.0, bearing=270.0, distance=90.0),

    # High enough that the imagery is still doing the work, low enough that relief has to be real.
    "range": dict(latitude=46.0, longitude=7.7, altitude=32_000.0, look=25.0, bearing=90.0, distance=400.0),

    # Where the imagery runs out and the detail materials take over.
    "alps": dict(latitude=46.0, longitude=7.7, altitude=6_000.0, look=14.0, bearing=90.0, distance=300.0),

    # Low over a mountain: the case the whole detail spectrum exists for.
    "valley": dict(latitude=46.2, longitude=7.9, altitude=700.0, look=6.0, bearing=200.0, distance=120.0),

    # Coast, shallows and surf, looking out to sea from over the launch site.
    "cape": dict(latitude=28.52, longitude=-80.62, altitude=1_200.0, look=16.0, bearing=110.0, distance=180.0),

    # Standing on the pad, looking down the range.
    "pad": dict(latitude=28.52, longitude=-80.62, altitude=45.0, look=3.0, bearing=90.0, distance=60.0),

}


def call(route, **query):

    url = f"{BRIDGE}{route}"

    if query:
        url += "?" + urllib.parse.urlencode(query)

    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def settle(rounds=SETTLE):

    last = -1
    steady = 0

    for _ in range(rounds):

        state = call("/state")

        steady = steady + 1 if state["patches"] == last else 0

        if steady >= 5 and state["fps"] > 20:
            return state

        last = state["patches"]

    return call("/state")


def shoot(name, tag):

    view = VIEWS[name]

    call("/control", pause="true", latitude=view["latitude"], longitude=view["longitude"],
         altitude=view["altitude"], speed=0.0)

    call("/camera", look=view["look"], bearing=view["bearing"], distance=view["distance"])

    state = settle()

    suffix = f"-{tag}" if tag else ""

    call("/screenshot", path=f"{OUT}/{name}{suffix}.png")

    print(f"  {name}{suffix}  {state['fps']:>3.0f} fps  {state['renderGpuMs']:5.1f} ms gpu  "
          f"{state['patches']:>5} patches  level {state['patchLevel']:>2}  "
          f"{state['groundAltitude']:>8.0f} m agl")


def main():

    arguments = sys.argv[1:]

    tag = ""

    if "--tag" in arguments:
        index = arguments.index("--tag")
        tag = arguments[index + 1]
        arguments = arguments[:index] + arguments[index + 2:]

    wanted = arguments or list(VIEWS)

    try:
        call("/ping")
    except (urllib.error.URLError, TimeoutError):
        print("no bridge on 9080 - start the game first")
        return 1

    for name in wanted:
        shoot(name, tag)

    return 0


if __name__ == "__main__":
    sys.exit(main())
