"""Capture environmental rendering cases from a disposable running game instance."""

import argparse
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bridge", default="http://localhost:9081")
    args = parser.parse_args()

    def call(route, **parameters):
        url = args.bridge + "/" + route + "?" + urllib.parse.urlencode(parameters)
        with urllib.request.urlopen(url, timeout=30) as response:
            result = json.load(response)
        if result.get("error"):
            raise RuntimeError(result["error"])
        return result

    cases = [
        ("ocean-steam", 28.52, -80.3, 40, 1, 12, 110, 90),
        ("cloud-interaction", 46, 7.7, 1800, 1, 8, 90, 150),
        ("terrain-close", 36.2, -112.3, 60, 0, 25, 200, 90),
        ("terrain-6km", 46, 7.7, 6000, 0, 25, 90, 300),
        ("terrain-24km", 46, 7.7, 24000, 0, 35, 90, 100),
        ("terrain-26km", 46, 7.7, 26000, 0, 35, 90, 100),
        ("orbit", -25, -72, 180000, 0, 35, 270, 90),
    ]
    results = []
    call("control", pause="true")
    for name, latitude, longitude, altitude, throttle, look, bearing, distance in cases:
        call("control", latitude=latitude, longitude=longitude, altitude=altitude,
             speed=0, throttle=throttle, aim="up", pause="true")
        call("camera", look=look, bearing=bearing, distance=distance)
        for attempt in range(20):
            time.sleep(0.5)
            state = call("state")
            if state["terrainWorkerFailures"] or state["forestFailures"]:
                raise RuntimeError("Environment worker failed: " + json.dumps(state))
            if attempt >= 5 and state["terrainPendingJobs"] == 0:
                break
        screenshot = call("screenshot", path="res://.artifacts/" + name + ".png")
        results.append({"case": name, "screenshot": screenshot["path"],
                        "gpu_ms": state["renderGpuMs"], "fps": state["fps"],
                        "patches": state["patches"]})
        print(json.dumps(results[-1]), flush=True)
    output = Path(__file__).resolve().parent.parent / "game/.artifacts/environment-checks.json"
    output.write_text(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
