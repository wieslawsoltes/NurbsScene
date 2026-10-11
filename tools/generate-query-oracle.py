"""Independent SciPy closest-point reference, not a certified global oracle.
Minimizes every one of 128 subintervals per knot span, not just apparent
sampled minima: narrow near-knot minima can lie before the first sample.
Endpoints are explicitly compared. Seed/versions retained.
"""
from pathlib import Path
import json
import numpy as np
import scipy
from scipy.interpolate import BSpline
from scipy.optimize import minimize_scalar
root=Path(__file__).resolve().parents[1]
source=json.loads((root/'tests/fixtures/lengths-scipy.json').read_text())
rng=np.random.default_rng(93182)
cases=[]
for c in source['cases']:
    spline=BSpline(c['knots'],np.array(c['controls']),c['degree'])
    def xyz(t):
        h=spline(t);return h[:3]/h[3]
    queries=[]
    for _ in range(3):
        point=rng.normal(0,12,3)
        def squared(t):
            d=xyz(t)-point;return float(d@d)
        candidates=[(squared(0),0.),(squared(1),1.)]
        boundaries=np.unique(c['knots'])
        for a,b in zip(boundaries[:-1],boundaries[1:]):
            ts=np.linspace(a,b,129);values=[squared(t) for t in ts]
            candidates.extend([(values[0],float(a)),(values[-1],float(b))])
            for j in range(128):
                optimum=minimize_scalar(squared,bounds=(ts[j],ts[j+1]),method='bounded',options={'xatol':1e-14})
                if not optimum.success:raise RuntimeError(optimum.message)
                candidates.append((optimum.fun,optimum.x))
        distance2,t=min(candidates)
        queries.append(dict(point=point.tolist(),parameter=float(t),distance=float(np.sqrt(distance2))))
    cases.append(dict(degree=c['degree'],knots=c['knots'],controls=c['controls'],queries=queries))
(root/'tests/fixtures/queries-scipy.json').write_text(json.dumps(dict(seed=93182,scipy=scipy.__version__,cases=cases),separators=(',',':'))+'\n')
print(f'Generated {len(cases)*3} independent SciPy query references.')
