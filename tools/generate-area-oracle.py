"""Independent SciPy tensor-spline / high-order Gauss area-centroid references.
No NurbsScene evaluator, tessellation, cache or adaptive algorithm is used.
Two quadrature orders must agree before a reference is accepted; not a certificate.
"""
from pathlib import Path
import json
import numpy as np
import scipy
from scipy.interpolate import BSpline
from numpy.polynomial.legendre import leggauss

root = Path(__file__).resolve().parents[1]
rng = np.random.default_rng(81715)
cases = []
for index in range(12):
    p, q = 2 + index % 4, 1 + index % 3
    nu, nv = p + 3, q + 3
    ku = np.r_[[0.] * (p + 1), [.21, .79], [1.] * (p + 1)]
    kv = np.r_[[0.] * (q + 1), [.13, .65], [1.] * (q + 1)]
    xyz = np.empty((nu, nv, 3))
    for i in range(nu):
        for j in range(nv):
            xyz[i, j] = [i*2., j*3., rng.normal(0, .7)]
    weights = rng.uniform(.7, 1.6, (nu, nv))
    controls = np.concatenate((xyz * weights[:, :, None], weights[:, :, None]), axis=2)
    def measure(order):
        nodes, quadrature_weights = leggauss(order)
        total = np.zeros(4)
        for u0, u1 in zip(np.unique(ku)[:-1], np.unique(ku)[1:]):
            u = (u0 + u1)/2 + nodes*(u1-u0)/2
            bu = BSpline(ku, np.eye(nu), p)(u)
            du = BSpline(ku, np.eye(nu), p)(u, 1)
            for v0, v1 in zip(np.unique(kv)[:-1], np.unique(kv)[1:]):
                v = (v0+v1)/2 + nodes*(v1-v0)/2
                bv = BSpline(kv, np.eye(nv), q)(v)
                dv = BSpline(kv, np.eye(nv), q)(v, 1)
                h = np.einsum('ai,ijc,bj->abc', bu, controls, bv)
                hu = np.einsum('ai,ijc,bj->abc', du, controls, bv)
                hv = np.einsum('ai,ijc,bj->abc', bu, controls, dv)
                point = h[:, :, :3] / h[:, :, 3, None]
                a = (hu[:, :, :3]-point*hu[:, :, 3, None]) / h[:, :, 3, None]
                b = (hv[:, :, :3]-point*hv[:, :, 3, None]) / h[:, :, 3, None]
                density = np.linalg.norm(np.cross(a, b), axis=2)
                weighted = density * np.outer(quadrature_weights, quadrature_weights)*(u1-u0)*(v1-v0)/4
                total[0] += weighted.sum()
                total[1:] += (point*weighted[:, :, None]).sum(axis=(0, 1))
        return np.r_[total[0], total[1:]/total[0]]
    a, b = measure(32), measure(48)
    if not np.allclose(a, b, rtol=1e-11, atol=1e-11):
        raise RuntimeError(f'Independent rules disagree for fixture {index}: {a}, {b}')
    cases.append(dict(degreeU=p, degreeV=q, countU=nu, countV=nv,
                      knotsU=ku.tolist(), knotsV=kv.tolist(), controls=controls.reshape(-1, 4).tolist(),
                      area=float(b[0]), centroid=b[1:].tolist()))
output = root/'tests/fixtures/areas-scipy.json'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(dict(seed=81715, scipy=scipy.__version__, orders=[32,48], cases=cases), separators=(',', ':'))+'\n')
print(f'Generated {len(cases)} independent surface area/centroid references.')
