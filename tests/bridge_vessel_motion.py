"""Run against a fresh game with its bridge on localhost:9080; stages and flies the test craft."""

import json
import math
import time
import urllib.parse
import urllib.request


def call(route, **parameters):
    query = urllib.parse.urlencode(parameters)
    with urllib.request.urlopen(f"http://localhost:9080/{route}?{query}", timeout=15) as response:
        result = json.load(response)
    assert not result.get("error"), result
    return result


def key(code, press="tap"):
    call("key", code=code, press=press)
    time.sleep(0.04)


def relative_state():
    state = call("state")
    assert len(state["debris"]) == 1, "Expected both test vessels to survive"
    other = state["debris"][0]
    return state["missionTime"], other["relativePosition"], other["relativeVelocity"]


def sample_motion(label, duration=2.0, initial=None, burning=()):
    state = call("state")
    vessels = {state["vessel"]: state, state["debris"][0]["name"]: state["debris"][0]}
    for name in burning:
        assert vessels[name]["thrust"] + vessels[name]["rcsThrust"] > 0, f"{name} is not producing thrust"
    previous = initial or relative_state()
    end = previous[0] + duration
    errors = []
    intervals = []
    deadline = time.monotonic() + 30
    while previous[0] < end:
        assert time.monotonic() < deadline, "Simulation stopped advancing"
        current = relative_state()
        dt = current[0] - previous[0]
        if dt <= 0:
            continue
        residual = [
            current[1][axis] - previous[1][axis]
            - 0.5 * (previous[2][axis] + current[2][axis]) * dt
            for axis in range(3)
        ]
        error = math.sqrt(sum(component * component for component in residual))
        # The tolerance allows integration error while rejecting the former metre-scale time jumps.
        assert error < 0.01, f"{label}: position jumped {error:.6f} m over {dt:.6f} s"
        errors.append(error)
        intervals.append(dt)
        previous = current
    assert len(errors) >= 15, "Too few samples to verify continuous relative motion"
    print(f"PASS {label}: {len(errors)} samples, max residual {max(errors):.6f} m, "
          f"frame intervals {min(intervals):.4f}-{max(intervals):.4f} s", flush=True)
    return previous


def main():
    state = call("state")
    assert state["vesselCount"] == 1 and state["canSeparate"], "Start a fresh flight before this test"
    call("control", pause="true", hold="Off", rcs="false", throttle=0)
    key("Space")
    key("Bracketright")
    assert call("state")["vesselIndex"] == 1
    key("Bracketleft")
    assert call("state")["vesselIndex"] == 0
    key("Bracketright")
    call("control", throttle=0.35, pause="false")
    before_cutoff = sample_motion("stage burns away after switching back", burning=("Meridian",))
    key("X")
    before_ignition = sample_motion("burn-to-coast transition", initial=before_cutoff)
    key("Z")
    sample_motion("coast-to-burn transition", initial=before_ignition, burning=("Meridian",))
    key("Bracketleft")
    assert call("state")["vesselIndex"] == 0
    sample_motion("unselected stage burns past coasting capsule", burning=("Meridian",))
    key("R")
    key("H", "down")
    sample_motion("stage burn alongside capsule RCS translation", burning=("Meridian", "Aegis"))
    key("H", "up")
    key("Bracketright")
    sample_motion("selected stage accelerates away", burning=("Meridian",))
    key("X")
    call("control", pause="true")


if __name__ == "__main__":
    main()
