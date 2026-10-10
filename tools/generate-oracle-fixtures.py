"""Independent SciPy rational-curve oracle for the managed evaluator tests.
Generation does not execute or qualify managed source. Seed, degree coverage and
SciPy provenance are retained in the resulting JSON.
"""
import json,math
from pathlib import Path
import numpy as np
import scipy
from scipy.interpolate import BSpline
root=Path(__file__).resolve().parents[1]
rng=np.random.default_rng(48137)
curves=[]
for degree in range(1,10):
 for case in range(8):
  count=degree+1+case
  knots=np.r_[np.zeros(degree+1),np.sort(rng.uniform(.03,.97,count-degree-1)),np.ones(degree+1)]
  weights=np.exp(rng.normal(0,1,count));controls=np.c_[rng.normal(0,8,(count,3))*weights[:,None],weights]
  spline=BSpline(knots,controls,degree);samples=[]
  for t in [0.,*rng.uniform(.05,.95,3),1.]:
   homogeneous=np.array([spline(t,k) if k<=degree else np.zeros(4) for k in range(5)])
   derivatives=np.zeros((5,3))
   for k in range(5):
    value=homogeneous[k,:3].copy()
    for j in range(1,k+1):value-=math.comb(k,j)*homogeneous[j,3]*derivatives[k-j]
    derivatives[k]=value/homogeneous[0,3]
   samples.append({'t':float(t),'derivatives':derivatives.tolist()})
  curves.append({'degree':degree,'knots':knots.tolist(),'controls':controls.tolist(),'samples':samples})
(root/'tests/fixtures/curves-scipy.json').write_text(json.dumps({'generator':'tools/generate-oracle-fixtures.py','seed':48137,'scipy':scipy.__version__,'curves':curves},separators=(',',':'))+'\n')
print(f'Generated {len(curves)} independent curve fixtures, {sum(len(c["samples"]) for c in curves)} evaluations.')
