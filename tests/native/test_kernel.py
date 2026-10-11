"""Numerical implementation tests against SciPy and independent rational recurrences.
Run: python -m unittest discover -s tests/native -v
The tests execute the compiled C kernel, not a Python replacement.
"""
import ctypes as C
import math
import pathlib
import unittest
import numpy as np
from scipy.interpolate import BSpline

ROOT = pathlib.Path(__file__).resolve().parents[2]
LIB = C.CDLL(str(ROOT / 'artifacts/native/libnurbsscene_kernel.so'))
P = C.POINTER(C.c_double)
LIB.ns_validate_curve.argtypes = [C.c_int, C.c_int, P, P]
LIB.ns_curve_workspace.argtypes = [C.c_int, C.c_int]
LIB.ns_surface_workspace.argtypes = [C.c_int, C.c_int, C.c_int, C.c_int]
LIB.ns_curve_eval.argtypes = [C.c_int,C.c_int,P,P,C.c_double,C.c_int,P,C.c_int,P,C.c_int]
LIB.ns_surface_eval.argtypes = [C.c_int,C.c_int,C.c_int,C.c_int,P,P,P,C.c_double,C.c_double,C.c_int,C.c_int,P,C.c_int,P,C.c_int]
LIB.ns_curve_insert.argtypes = [C.c_int,C.c_int,P,P,C.c_double,P,C.c_int,P,C.c_int]
LIB.ns_bezier_split.argtypes = [C.c_int,P,C.c_double,P,P,P,C.c_int]
LIB.ns_bezier_elevate.argtypes = [C.c_int,P,P,C.c_int]
LIB.ns_solve_linear.argtypes = [C.c_int,C.c_int,P,P,P,C.c_int]
LIB.ns_basis.argtypes = [C.c_int,C.c_int,P,C.c_double,C.c_int,P,C.c_int,P,C.c_int]

def arr(a): return np.ascontiguousarray(a, dtype=np.float64)
def ptr(a): return a.ctypes.data_as(P)
def evaluate(p,u,points,t,order=2):
    u,points=arr(u),arr(points)
    out=np.zeros((order+1,3)); work=np.zeros(LIB.ns_curve_workspace(p,order))
    status=LIB.ns_curve_eval(p,len(points),ptr(u),ptr(points),t,order,ptr(out),out.size,ptr(work),work.size)
    if status: raise RuntimeError(status)
    return out

def ref_curve(p,u,points,t,order):
    spline=BSpline(u,points,p)
    h=np.array([spline(t,k) if k<=p else np.zeros(4) for k in range(order+1)])
    out=np.zeros((order+1,3))
    for k in range(order+1):
        v=h[k,:3].copy()
        for i in range(1,k+1): v-=math.comb(k,i)*h[i,3]*out[k-i]
        out[k]=v/h[0,3]
    return out

class NumericalKernelTests(unittest.TestCase):
    def test_abi_and_capacities(self):
        self.assertEqual(LIB.ns_abi_version(),1)
        for p in [1,2,3,7,64,128]: self.assertGreater(LIB.ns_curve_workspace(p,2),0)
        self.assertEqual(LIB.ns_curve_workspace(129,2),-1)
        self.assertEqual(LIB.ns_curve_workspace(2,33),-1)

    def test_random_rational_derivatives_against_scipy(self):
        rng=np.random.default_rng(48291)
        for degree in range(1,10):
            for fixture in range(8):
                n=degree+1+fixture
                u=np.r_[np.zeros(degree+1),np.sort(rng.uniform(.03,.97,n-degree-1)),np.ones(degree+1)]
                w=np.exp(rng.normal(0,1,n)); xyz=rng.normal(0,8,(n,3))
                points=np.c_[xyz*w[:,None],w]
                for t in np.r_[0.,rng.uniform(0,1,8),1.]:
                    actual=evaluate(degree,u,points,float(t),4)
                    expected=ref_curve(degree,u,points,t,4)
                    np.testing.assert_allclose(actual,expected,rtol=4e-10,atol=1e-7)

    def test_partition_unity_and_derivative_sum(self):
        for degree in [1,2,3,4,7,16]:
            count=degree+6
            u=arr(np.r_[np.zeros(degree+1),[.15,.35,.4,.6,.83],np.ones(degree+1)])
            for t in [0.,.01,.15,.35,.4,.55,.83,.999,1.]:
                out=np.zeros((4,degree+1));work=np.zeros((degree+1)**2+4*(degree+1))
                status=LIB.ns_basis(degree,count,ptr(u),t,3,ptr(out),out.size,ptr(work),work.size)
                self.assertEqual(status,0)
                np.testing.assert_allclose(out.sum(axis=1),[1,0,0,0],atol=2e-7)

    def test_circle_position_tangent_curvature(self):
        w=math.sqrt(.5)
        points=arr([[1,0,0,1],[w,w,0,w],[0,1,0,1]])
        u=arr([0,0,0,1,1,1])
        for t in np.linspace(0,1,81):
            p,d1,d2=evaluate(2,u,points,float(t))
            self.assertAlmostEqual(np.linalg.norm(p),1,places=13)
            self.assertAlmostEqual(np.dot(p,d1),0,places=12)
            self.assertAlmostEqual(np.linalg.norm(np.cross(d1,d2))/np.linalg.norm(d1)**3,1,places=12)

    def test_extreme_common_weight_scale(self):
        u=arr([0,0,0,1,1,1]); base=arr([[1,0,0,1],[.7,.7,.2,.7],[0,1,0,1]])
        expected=evaluate(2,u,base,.372,4)
        for scale in [1e-290,1e-200,1e200,1e290]:
            np.testing.assert_allclose(evaluate(2,u,base*scale,.372,4),expected,rtol=2e-13,atol=1e-12)

    def test_internal_knot_one_sided_derivatives(self):
        p=3;u=arr([0,0,0,0,.5,.5,.5,1,1,1,1]);points=arr([[i,i*i%7,i%2,1] for i in range(7)])
        for t in [0,.5,1]:np.testing.assert_allclose(evaluate(p,u,points,t),ref_curve(p,u,points,t,2),atol=1e-10)

    def test_nonclamped_domain(self):
        p=3;u=arr(range(11));points=arr([[i,i*i,0,1] for i in range(7)])
        for t in [3,3.75,5,6.999,7]:np.testing.assert_allclose(evaluate(p,u,points,t,3),ref_curve(p,u,points,t,3),atol=1e-9)

    def test_knot_insertion_preserves_geometry_and_derivatives(self):
        rng=np.random.default_rng(9931)
        for p in range(1,9):
            n=p+5;u=arr(np.r_[np.zeros(p+1),[.21,.48,.6,.82],np.ones(p+1)])
            w=rng.uniform(.2,3,n);points=arr(np.c_[rng.normal(size=(n,3))*w[:,None],w])
            for t in [.1,.21,.48,.73]:
                if p==1 and t in u:continue
                new_u=np.zeros(len(u)+1);new_p=np.zeros((n+1,4))
                status=LIB.ns_curve_insert(p,n,ptr(u),ptr(points),t,ptr(new_u),new_u.size,ptr(new_p),new_p.size)
                self.assertEqual(status,0)
                for x in np.linspace(.001,.999,31):
                    np.testing.assert_allclose(evaluate(p,new_u,new_p,float(x)),evaluate(p,u,points,float(x)),rtol=3e-10,atol=5e-9)

    def test_bezier_subdivision_and_elevation(self):
        rng=np.random.default_rng(432)
        for p in range(1,13):
            w=rng.uniform(.3,2,p+1);points=arr(np.c_[rng.normal(size=(p+1,3))*w[:,None],w])
            u=arr([0]*(p+1)+[1]*(p+1));left=np.zeros_like(points);right=np.zeros_like(points);work=np.zeros(points.size)
            status=LIB.ns_bezier_split(p,ptr(points),.37,ptr(left),ptr(right),ptr(work),work.size)
            self.assertEqual(status,0)
            elevated=np.zeros((p+2,4));status=LIB.ns_bezier_elevate(p,ptr(points),ptr(elevated),elevated.size)
            self.assertEqual(status,0)
            ue=arr([0]*(p+2)+[1]*(p+2))
            for x in np.linspace(0,1,21):
                np.testing.assert_allclose(evaluate(p,u,left,float(x),0),evaluate(p,u,points,float(x*.37),0),atol=2e-13)
                np.testing.assert_allclose(evaluate(p,u,right,float(x),0),evaluate(p,u,points,float(.37+x*.63),0),atol=2e-13)
                np.testing.assert_allclose(evaluate(p+1,ue,elevated,float(x),2),evaluate(p,u,points,float(x),2),rtol=2e-11,atol=3e-10)

    def test_tensor_surface_mixed_derivatives(self):
        rng=np.random.default_rng(94)
        for p,q in [(1,1),(2,3),(4,2),(5,5)]:
            nu,nv=p+3,q+4
            u=arr([0]*(p+1)+[.3,.7]+[1]*(p+1));v=arr([0]*(q+1)+[.2,.4,.8]+[1]*(q+1))
            w=rng.uniform(.4,2,(nu,nv));points=arr(np.concatenate([rng.normal(size=(nu,nv,3))*w[:,:,None],w[:,:,None]],axis=2))
            for tu,tv in [(0,0),(.23,.61),(.3,.8),(1,1)]:
                du=dv=2;out=np.zeros((3,3,3));work=np.zeros(LIB.ns_surface_workspace(p,q,du,dv))
                status=LIB.ns_surface_eval(p,q,nu,nv,ptr(u),ptr(v),ptr(points),tu,tv,du,dv,ptr(out),out.size,ptr(work),work.size)
                self.assertEqual(status,0)
                h=np.zeros((3,3,4))
                for k in range(3):
                    intermediate=BSpline(u,points,p,axis=0)(tu,k) if k<=p else np.zeros((nv,4))
                    for l in range(3):h[k,l]=BSpline(v,intermediate,q)(tv,l) if l<=q else np.zeros(4)
                expected=np.zeros((3,3,3))
                for k in range(3):
                    for l in range(3):
                        value=h[k,l,:3].copy()
                        for j in range(1,l+1):value-=math.comb(l,j)*h[0,j,3]*expected[k,l-j]
                        for i in range(1,k+1):
                            value-=math.comb(k,i)*h[i,0,3]*expected[k-i,l]
                            for j in range(1,l+1):value-=math.comb(k,i)*math.comb(l,j)*h[i,j,3]*expected[k-i,l-j]
                        expected[k,l]=value/h[0,0,3]
                np.testing.assert_allclose(out,expected,rtol=3e-10,atol=1e-6)

    def test_scaled_pivot_linear_solver(self):
        rng=np.random.default_rng(102)
        for n in [1,2,7,25,80]:
            a=rng.normal(size=(n,n));a+=n*np.eye(n);x=rng.normal(size=(n,4));b=arr(a@x)
            a=arr(a);work=np.zeros(n)
            self.assertEqual(LIB.ns_solve_linear(n,4,ptr(a),ptr(b),ptr(work),n),0)
            np.testing.assert_allclose(b,x,atol=1e-12)
        a=arr([[1,2],[2,4]]);b=arr([[3],[6]]);work=np.zeros(2)
        self.assertEqual(LIB.ns_solve_linear(2,1,ptr(a),ptr(b),ptr(work),2),6)

    def test_error_status_and_output_guards(self):
        p=2;u=arr([0,0,0,1,1,1]);points=arr([[0,0,0,1],[1,2,0,1],[2,0,0,1]])
        out=np.full(11,777.);work=np.zeros(LIB.ns_curve_workspace(2,2))
        def run(t=0.3,cap=9,wcap=None,knots=u,pts=points):
            return LIB.ns_curve_eval(p,3,ptr(knots),ptr(pts),t,2,ptr(out),cap,ptr(work),len(work) if wcap is None else wcap)
        self.assertEqual(run(t=-.1),4);self.assertEqual(run(t=float('nan')),4)
        self.assertEqual(run(cap=8),9);self.assertEqual(run(wcap=1),5)
        self.assertTrue(np.all(out==777))
        wrong=points.copy();wrong[1,3]=0;self.assertEqual(run(pts=wrong),3)
        wrong=u.copy();wrong[2]=2;self.assertEqual(run(knots=wrong),2)
        self.assertEqual(run(),0);np.testing.assert_equal(out[9:],[777,777])

if __name__=='__main__':unittest.main()
