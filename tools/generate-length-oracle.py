"""Independent SciPy QUADPACK oracle for rational curve arc length.
The implementation under test uses neither SciPy nor this generator at runtime.
"""
import json, math
from pathlib import Path
import numpy as np
import scipy
from scipy.interpolate import BSpline
from scipy.integrate import quad

root = Path(__file__).resolve().parents[1]
rng = np.random.default_rng(79321)
cases = []
for degree in range(1, 9):
    for case in range(4):
        count = degree + 2 + case
        knots = np.r_[np.zeros(degree + 1), np.sort(rng.uniform(.05, .95, count-degree-1)), np.ones(degree + 1)]
        weights = np.exp(rng.normal(0, 1.7, count))
        positions = rng.normal(0, 8, (count, 3))
        homogeneous = np.c_[positions * weights[:, None], weights]
        spline = BSpline(knots, homogeneous, degree)
        derivative = spline.derivative()
        def speed(t):
            h = spline(t)
            d = derivative(t)
            return np.linalg.norm((d[:3] - h[:3] * (d[3]/h[3])) / h[3])
        breaks = np.unique(knots)
        length = error = 0.
        for a, b in zip(breaks[:-1], breaks[1:]):
            value, estimate = quad(speed, a, b, epsabs=1e-10, epsrel=1e-12, limit=400)
            length += value
            error += estimate
        cases.append(dict(degree=degree, knots=knots.tolist(), controls=homogeneous.tolist(), length=length, error=error))
path = root/'tests/fixtures/lengths-scipy.json'
path.parent.mkdir(exist_ok=True)
path.write_text(json.dumps(dict(seed=79321, scipy=scipy.__version__, cases=cases), separators=(',', ':'))+'\n')
print(f'Generated {len(cases)} independent length fixtures.')
