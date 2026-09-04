#!/usr/bin/env python
"""Fly the vehicle from the pad to a stable orbit through the debug bridge, and photograph it.

This is the milestone's headline claim and the only way to check it is to fly it. The script does
nothing a pilot could not: full throttle, the ascent hold, stage when the first stage is dry, and
shut down once the periapsis is up. Everything else is the simulation's own.

    py tools/ascent.py            fly it, at five times real time
    py tools/ascent.py 1          fly it at real time
"""

import json
import sys
import time
import urllib.parse
import urllib.request

BRIDGE = "http://localhost:9080"
OUT = ".artifacts/m4"

# Periapsis that counts as orbit: clear of the air with room to spare, under an apoapsis that is
# not much above it. Left at full throttle the Meridian has enough in the tank to leave the planet
# entirely, so the shutdown is part of flying the ascent rather than an afterthought.
ORBIT_PERIAPSIS = 78_000.0
TARGET_APOAPSIS = 115_000.0

TIMEOUT_SECONDS = 900.0

# The bridge answers on the main thread, so polling it hard is polling the frame rate away.
POLL = 0.2


def call(route, **query):

    url = f"{BRIDGE}{route}"

    if query:
        url += "?" + urllib.parse.urlencode(query)

    with urllib.request.urlopen(url, timeout=60) as response:
        return json.load(response)


def shoot(name):
    call("/screenshot", path=f"{OUT}/{name}.png")


def main():

    scale = float(sys.argv[1]) if len(sys.argv) > 1 else 5.0

    call("/control", restart="1")

    # The scene reloads, so give the bridge a moment to come back up on the other side of it.
    for _ in range(60):

        try:
            state = call("/state")

            if state.get("missionTime", 1.0) < 0.5 and state.get("clamped"):
                break

        except Exception:
            pass

    call("/control", timescale=scale, pause="false")
    call("/camera", look=8.0, bearing=200.0, distance=55.0)

    call("/control", throttle=1.0, hold="Ascent")

    marks = {"liftoff": False, "tower": False, "maxq": False, "staged": False, "trimmed": False}

    peak_pressure = 0.0
    started = time.time()

    while time.time() - started < TIMEOUT_SECONDS:

        time.sleep(POLL)

        state = call("/state")

        height = state["groundAltitude"]
        pressure = state.get("dynamicPressure", 0.0)

        if not marks["liftoff"] and height > 4.0:

            marks["liftoff"] = True
            print(f"  liftoff at T+{state['missionTime']:.1f} s")
            shoot("ascent-liftoff")

        if marks["liftoff"] and not marks["tower"] and height > 120.0:

            marks["tower"] = True
            call("/camera", look=14.0, bearing=200.0, distance=110.0)
            shoot("ascent-tower")

        if pressure > peak_pressure:

            peak_pressure = pressure

        elif marks["liftoff"] and not marks["maxq"] and peak_pressure > 8000.0:

            marks["maxq"] = True
            print(f"  max q {peak_pressure / 1000.0:.1f} kPa at {state['altitude'] / 1000.0:.1f} km")
            shoot("ascent-maxq")

        if state["canSeparate"] and state["stage"] == "Zenith" and state["fuelMass"] < 40.0:

            call("/control", stage="1")
            call("/control", throttle=1.0, hold="Ascent")

            marks["staged"] = True

            print(f"  staged at T+{state['missionTime']:.1f} s, {state['altitude'] / 1000.0:.1f} km, {state['speed']:.0f} m/s")

            call("/camera", look=0.0, bearing=200.0, distance=60.0)
            shoot("ascent-staging")

        if state["fate"] != "Flying":

            print(f"  lost the vehicle: {state['fate']} at {state['altitude'] / 1000.0:.1f} km")
            shoot("ascent-lost")
            return 1

        apoapsis = state["apoapsis"] if isinstance(state["apoapsis"], (int, float)) else float("inf")

        # Once the apoapsis is where it belongs the rest of the burn only has to lift the other
        # side of the orbit, so the lever comes back and the nose goes on the track.
        if not marks["trimmed"] and apoapsis > TARGET_APOAPSIS:

            marks["trimmed"] = True

            call("/control", throttle=0.3, hold="Prograde")

            print(f"  apoapsis reached at T+{state['missionTime']:.1f} s, trimming on prograde")

        if state["periapsis"] > ORBIT_PERIAPSIS:

            call("/control", throttle=0.0, hold="Prograde", timescale=1.0)

            print(f"  orbit at T+{state['missionTime']:.1f} s: "
                  f"{state['periapsis'] / 1000.0:.1f} x {apoapsis / 1000.0:.1f} km, "
                  f"inclination {state['inclination'] * 57.2958:.1f} deg, "
                  f"{state['deltaV']:.0f} m/s left in the Meridian")

            call("/camera", look=25.0, bearing=200.0, distance=90.0)
            shoot("ascent-orbit")

            return 0

    print("  timed out short of orbit")
    shoot("ascent-timeout")

    return 1


if __name__ == "__main__":
    sys.exit(main())
