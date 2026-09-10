"""Capture rendering regressions on a disposable, rendering-enabled bridge instance."""

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
        with urllib.request.urlopen(url, timeout=25) as response:
            result = json.load(response)
        if result.get('error'):
            raise RuntimeError(result['error'])
        return result

    cases = [
        ('cloud-low', dict(site=300, throttle=0), 0, 90, 100),
        ('cloud-high', dict(site=1800, throttle=0), 0, 90, 100),
        ('ground-close', dict(site=20, throttle=0), 12, 220, 35),
        ('dust', dict(site=24, throttle=1), 8, 270, 75),
        ('crossflow', dict(latitude=28.52, longitude=-80.3, altitude=200,
                          speed=350, throttle=1), 0, 0, 85),
        ('retrograde', dict(latitude=28.52, longitude=-80.3, altitude=200,
                           speed=900, throttle=1, aoa=180), 0, 0, 85),
        ('steam', dict(latitude=28.52, longitude=-80.3, altitude=24,
                      speed=0, throttle=1), 10, 90, 80),
        ('vacuum', dict(altitude=100000, speed=0, throttle=1), 0, 90, 80),
    ]
    results = []
    for name, params, look, bearing, distance in cases:
        params.update(pause='true')
        if 'aoa' not in params:
            params['aim'] = 'up'
        call('control', **params)
        call('camera', look=look, bearing=bearing, distance=distance)
        for attempt in range(20):
            time.sleep(0.5)
            state = call('state')
            if state['terrainWorkerFailures'] or state['forestFailures']:
                raise RuntimeError('Rendering worker failure: ' + json.dumps(state))
            if attempt >= 5 and state['terrainPendingJobs'] == 0:
                break
        else:
            raise RuntimeError('Terrain did not settle for ' + name)
        shot = call('screenshot', path='res://.artifacts/review-' + name + '.png')
        results.append(dict(case=name, gpu=state['renderGpuMs'],
                            terrainFailures=state['terrainWorkerFailures'],
                            forestFailures=state['forestFailures'], path=shot['path']))
        print(json.dumps(results[-1]), flush=True)
    output = Path(__file__).resolve().parent.parent / 'game/.artifacts/review-results.json'
    output.write_text(json.dumps(results, indent=2))


if __name__ == '__main__':
    main()
