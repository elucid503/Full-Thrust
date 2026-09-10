"""Verify persistent exhaust/water response through a disposable rendering bridge."""

import argparse
import json
import time
import urllib.parse
import urllib.request
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bridge', default='http://localhost:9082')
    args = parser.parse_args()

    def call(route, **params):
        url = args.bridge.rstrip('/') + '/' + route + '?' + urllib.parse.urlencode(params)
        with urllib.request.urlopen(url, timeout=30) as response:
            data = json.load(response)
        if data.get('error'):
            raise RuntimeError(data['error'])
        return data

    rows = []

    def capture(name):
        state = call('state')
        for key in ('surfaceFailures', 'terrainWorkerFailures', 'forestFailures'):
            if state[key]:
                raise RuntimeError(key + ': ' + str(state[key]))
        call('screenshot', path='res://.artifacts/flow-' + name + '.png')
        row = {key: state[key] for key in ('surfaceParcels', 'surfaceWaveHeight', 'surfaceMs',
                                          'renderGpuMs', 'frameP95Ms', 'surfaceFailures')}
        row['case'] = name
        rows.append(row)
        print(json.dumps(row), flush=True)
        return state

    call('control', pause='true', latitude=28.52, longitude=-80.3,
         altitude=24, speed=0, throttle=0, aim='up')
    call('camera', look=25, bearing=230, distance=65)
    time.sleep(2)
    capture('rest')
    call('control', throttle=1)
    time.sleep(0.5)
    capture('ignition')
    time.sleep(2)
    burn = capture('burn')
    assert burn['surfaceParcels'] > 0 and burn['surfaceWaveHeight'] > 0.05
    call('control', throttle=0)
    time.sleep(0.5)
    shutdown = capture('shutdown-half')
    assert shutdown['surfaceParcels'] > 0 and shutdown['surfaceWaveHeight'] > 0.01
    time.sleep(2.5)
    capture('shutdown-three')
    time.sleep(12)
    settled = capture('settled')
    assert settled['surfaceParcels'] == 0, 'Spray did not finish settling'
    assert settled['surfaceWaveHeight'] < shutdown['surfaceWaveHeight'], 'Water did not relax'
    output = Path(__file__).resolve().parent.parent / 'game/.artifacts/flow-sequence.json'
    output.write_text(json.dumps(rows, indent=2))


if __name__ == '__main__':
    main()
