#include "nurbsscene.h"

/* No libc dependency: the same source is built as a shared library and a
 * freestanding WebAssembly module. The host supplies every buffer. */
#define NS_MAX_DEGREE 128
#define NS_MAX_DERIVATIVE 32
#define NS_MAX_CONTROLS 10000000
#define NS_EPS 2.2204460492503130808472633361816e-16
static int finite_number(double x) { return __builtin_isfinite(x); }
static double abs_number(double x) { return x < 0 ? -x : x; }
static double safe_div(double a, double b) { return b == 0.0 ? 0.0 : a / b; }
static int min_int(int a, int b) { return a < b ? a : b; }
static void zero(double *p, int n) { for (int i=0;i<n;i++) p[i]=0; }
static double choose(int n, int k) {
 if (k<0 || k>n) return 0;
 if (k>n-k) k=n-k;
 double value=1;
 for(int i=1;i<=k;i++) value=value*(n-k+i)/i;
 return value;
}
static int dimensions(int p, int n) {
 return p>=1 && p<=NS_MAX_DEGREE && n>p && n<=NS_MAX_CONTROLS;
}
static int knots_valid(int p, int n, const double *u) {
 if (!u || !dimensions(p,n)) return NS_ARGUMENT;
 int repeated=1;
 for(int i=0;i<n+p+1;i++) {
  if (!finite_number(u[i])) return NS_KNOTS;
  if (i>0) {
   if(u[i]<u[i-1]) return NS_KNOTS;
   repeated=u[i]==u[i-1]?repeated+1:1;
   if(repeated>p+1) return NS_KNOTS;
  }
 }
 if(!(u[p]<u[n])) return NS_KNOTS;
 return NS_OK;
}
static int points_valid(int count, const double *points) {
 if (!points) return NS_ARGUMENT;
 for (int i=0;i<count;i++) {
  for(int j=0;j<4;j++) if(!finite_number(points[4*i+j])) return NS_WEIGHTS;
  if(!(points[4*i+3]>0)) return NS_WEIGHTS;
 }
 return NS_OK;
}
int ns_abi_version(void) { return 1; }
int ns_curve_workspace(int p, int d) {
 if(p<1 || p>NS_MAX_DEGREE || d<0 || d>NS_MAX_DERIVATIVE) return -1;
 int z=p+1;
 /* basis output + ndu + left/right + two alternating derivative rows
    + homogeneous derivatives; all offsets expressed in doubles. */
 return (d+1)*z + z*z + 4*z + 4*(d+1);
}
int ns_surface_workspace(int p, int q, int du, int dv) {
 if(ns_curve_workspace(p,du)<0 || ns_curve_workspace(q,dv)<0) return -1;
 return (du+1)*(p+1)+(dv+1)*(q+1)
  +(p+1)*(p+1)+4*(p+1)+(q+1)*(q+1)+4*(q+1)
  +4*(du+1)*(dv+1);
}
int ns_validate_curve(int p,int n,const double *u,const double *points) {
 int status=knots_valid(p,n,u);
 return status?status:points_valid(n,points);
}
int ns_find_span(int p,int n,const double *u,double t) {
 if(!u || !dimensions(p,n) || !finite_number(t) || t<u[p] || t>u[n]) return -1;
 if(t==u[n]) {
  int span=n-1;
  while(span>p && u[span]>=u[n]) span--;
  return span;
 }
 int low=p,high=n;
 while(low+1<high) {
  int mid=low+(high-low)/2;
  if(t<u[mid]) high=mid; else low=mid;
 }
 return low;
}
/* Derivative basis computation. Caller has already validated inputs.
   Rows d=0..order, columns j=0..degree for the active span. */
static void basis_core(int p,int span,const double *u,double t,int order,
 double *ders,double *scratch) {
 int z=p+1;
 double *ndu=scratch,*left=ndu+z*z,*right=left+z,*a=right+z;
 zero(ndu,z*z); zero(ders,(order+1)*z); zero(a,2*z);
 ndu[0]=1;
 for(int j=1;j<=p;j++) {
  left[j]=t-u[span+1-j]; right[j]=u[span+j]-t;
  double saved=0;
  for(int r=0;r<j;r++) {
   ndu[j*z+r]=right[r+1]+left[j-r];
   double temp=safe_div(ndu[r*z+j-1],ndu[j*z+r]);
   ndu[r*z+j]=saved+right[r+1]*temp;
   saved=left[j-r]*temp;
  }
  ndu[j*z+j]=saved;
 }
 for(int j=0;j<=p;j++) ders[j]=ndu[j*z+p];
 int limit=min_int(order,p);
 for(int r=0;r<=p;r++) {
  int row0=0,row1=1; a[0]=1;
  for(int k=1;k<=limit;k++) {
   double value=0;
   int rk=r-k,pk=p-k;
   if(r>=k) {
    a[row1*z]=safe_div(a[row0*z],ndu[(pk+1)*z+rk]);
    value=a[row1*z]*ndu[rk*z+pk];
   }
   int j1=rk>=-1?1:-rk;
   int j2=r-1<=pk?k-1:p-r;
   for(int j=j1;j<=j2;j++) {
    a[row1*z+j]=safe_div(a[row0*z+j]-a[row0*z+j-1],ndu[(pk+1)*z+rk+j]);
    value+=a[row1*z+j]*ndu[(rk+j)*z+pk];
   }
   if(r<=pk) {
    a[row1*z+k]=-safe_div(a[row0*z+k-1],ndu[(pk+1)*z+r]);
    value+=a[row1*z+k]*ndu[r*z+pk];
   }
   ders[k*z+r]=value;
   int tmp=row0;row0=row1;row1=tmp;
  }
 }
 double factor=p;
 for(int k=1;k<=limit;k++) {
  for(int j=0;j<=p;j++) ders[k*z+j]*=factor;
  factor*=p-k;
 }
}
int ns_basis(int p,int n,const double *u,double t,int d,double *ders,int cap,
 double *workspace,int workspace_capacity) {
 int status=knots_valid(p,n,u);
 if(status) return status;
 if(d<0 || d>NS_MAX_DERIVATIVE || !ders || !workspace) return NS_ARGUMENT;
 if(cap<(d+1)*(p+1)) return NS_CAPACITY;
 if(workspace_capacity<(p+1)*(p+1)+4*(p+1)) return NS_WORKSPACE;
 int span=ns_find_span(p,n,u,t); if(span<0) return NS_DOMAIN;
 basis_core(p,span,u,t,d,ders,workspace);
 for(int i=0;i<(d+1)*(p+1);i++) if(!finite_number(ders[i])) return NS_NUMERICAL;
 return NS_OK;
}
static int curve_eval_internal(int p,int n,const double *u,const double *points,double t,int d,
 double *out,int cap,double *work,int workcap,int validated) {
 int needed=ns_curve_workspace(p,d);
 if(needed<0 || !out || !work) return NS_ARGUMENT;
 if(cap<3*(d+1)) return NS_CAPACITY;
 if(workcap<needed) return NS_WORKSPACE;
 if(!validated) {int status=ns_validate_curve(p,n,u,points); if(status) return status;}
 int span=ns_find_span(p,n,u,t); if(span<0) return NS_DOMAIN;
 int z=p+1;
 double *basis=work,*scratch=basis+(d+1)*z,*h=scratch+z*z+4*z;
 basis_core(p,span,u,t,d,basis,scratch);
 zero(h,4*(d+1));
 /* Scale all active homogeneous points by a common weight. This preserves
    the curve and reduces overflow/underflow in the rational quotient. */
 double scale=0;
 for(int j=0;j<=p;j++) if(points[4*(span-p+j)+3]>scale) scale=points[4*(span-p+j)+3];
 for(int k=0;k<=d;k++) for(int j=0;j<=p;j++) for(int c=0;c<4;c++)
  h[k*4+c]+=basis[k*z+j]*(points[4*(span-p+j)+c]/scale);
 if(!(h[3]>0) || !finite_number(h[3])) return NS_NUMERICAL;
 for(int k=0;k<=d;k++) for(int c=0;c<3;c++) {
  double value=h[k*4+c];
  for(int i=1;i<=k;i++) value-=choose(k,i)*h[i*4+3]*out[(k-i)*3+c];
  out[k*3+c]=value/h[3];
  if(!finite_number(out[k*3+c])) return NS_NUMERICAL;
 }
 return NS_OK;
}
static int surface_eval_internal(int p,int q,int nu,int nv,const double *u,const double *v,
 const double *points,double tu,double tv,int du,int dv,double *out,int cap,
 double *work,int workcap,int validated) {
 int needed=ns_surface_workspace(p,q,du,dv);
 if(needed<0 || !out || !work || !points) return NS_ARGUMENT;
 if(cap<3*(du+1)*(dv+1)) return NS_CAPACITY;
 if(workcap<needed) return NS_WORKSPACE;
 if(!validated) {
  int status=knots_valid(p,nu,u); if(status) return status;
  status=knots_valid(q,nv,v); if(status) return status;
  if(nu>NS_MAX_CONTROLS/nv) return NS_ARGUMENT;
  status=points_valid(nu*nv,points); if(status) return status;
 }
 int su=ns_find_span(p,nu,u,tu),sv=ns_find_span(q,nv,v,tv);
 if(su<0 || sv<0) return NS_DOMAIN;
 int zu=p+1,zv=q+1,stride=dv+1;
 double *bu=work,*bv=bu+(du+1)*zu,*wsu=bv+(dv+1)*zv;
 double *wsv=wsu+zu*zu+4*zu,*h=wsv+zv*zv+4*zv;
 basis_core(p,su,u,tu,du,bu,wsu);basis_core(q,sv,v,tv,dv,bv,wsv);
 zero(h,4*(du+1)*(dv+1));
 double scale=0;
 for(int i=0;i<=p;i++) for(int j=0;j<=q;j++) {
  double w=points[4*((su-p+i)*nv+sv-q+j)+3];if(w>scale) scale=w;
 }
 for(int k=0;k<=du;k++) for(int l=0;l<=dv;l++)
  for(int i=0;i<=p;i++) for(int j=0;j<=q;j++) for(int c=0;c<4;c++)
   h[4*(k*stride+l)+c]+=bu[k*zu+i]*bv[l*zv+j]
    *(points[4*((su-p+i)*nv+sv-q+j)+c]/scale);
 if(!(h[3]>0) || !finite_number(h[3])) return NS_NUMERICAL;
 for(int k=0;k<=du;k++) for(int l=0;l<=dv;l++) for(int c=0;c<3;c++) {
  double value=h[4*(k*stride+l)+c];
  for(int j=1;j<=l;j++) value-=choose(l,j)*h[4*j+3]*out[3*(k*stride+l-j)+c];
  for(int i=1;i<=k;i++) {
   value-=choose(k,i)*h[4*(i*stride)+3]*out[3*((k-i)*stride+l)+c];
   for(int j=1;j<=l;j++) value-=choose(k,i)*choose(l,j)
    *h[4*(i*stride+j)+3]*out[3*((k-i)*stride+l-j)+c];
  }
  out[3*(k*stride+l)+c]=value/h[3];
  if(!finite_number(out[3*(k*stride+l)+c])) return NS_NUMERICAL;
 }
 return NS_OK;
}
int ns_curve_insert(int p,int n,const double *u,const double *points,double t,
 double *out_u,int ucap,double *out_p,int pcap) {
 int status=ns_validate_curve(p,n,u,points); if(status) return status;
 if(!out_u || !out_p) return NS_ARGUMENT;
 if(ucap<n+p+2 || pcap<4*(n+1)) return NS_CAPACITY;
 if(!finite_number(t) || t<=u[p] || t>=u[n]) return NS_DOMAIN;
 int span=ns_find_span(p,n,u,t),multiplicity=0;
 for(int i=0;i<n+p+1;i++) if(u[i]==t) multiplicity++;
 if(multiplicity>=p) return NS_MULTIPLICITY;
 for(int i=0;i<=span;i++) out_u[i]=u[i];
 out_u[span+1]=t;
 for(int i=span+1;i<n+p+1;i++) out_u[i+1]=u[i];
 for(int i=0;i<=span-p;i++) for(int c=0;c<4;c++) out_p[4*i+c]=points[4*i+c];
 for(int i=span-multiplicity;i<n;i++) for(int c=0;c<4;c++) out_p[4*(i+1)+c]=points[4*i+c];
 for(int i=span-p+1;i<=span-multiplicity;i++) {
  double denominator=u[i+p]-u[i]; if(!(denominator>0)) return NS_NUMERICAL;
  double alpha=(t-u[i])/denominator;
  for(int c=0;c<4;c++) out_p[4*i+c]=(1-alpha)*points[4*(i-1)+c]+alpha*points[4*i+c];
 }
 return NS_OK;
}
int ns_bezier_split(int p,const double *points,double t,double *left,double *right,
 double *work,int cap) {
 if(p<1 || p>NS_MAX_DEGREE || !points || !left || !right || !work) return NS_ARGUMENT;
 if(cap<4*(p+1)) return NS_WORKSPACE;
 if(!finite_number(t) || t<0 || t>1) return NS_DOMAIN;
 int status=points_valid(p+1,points); if(status) return status;
 for(int i=0;i<4*(p+1);i++) work[i]=points[i];
 for(int c=0;c<4;c++) {left[c]=work[c];right[4*p+c]=work[4*p+c];}
 for(int r=1;r<=p;r++) {
  for(int i=0;i<=p-r;i++) for(int c=0;c<4;c++) work[4*i+c]=(1-t)*work[4*i+c]+t*work[4*(i+1)+c];
  for(int c=0;c<4;c++) {left[4*r+c]=work[c];right[4*(p-r)+c]=work[4*(p-r)+c];}
 }
 return NS_OK;
}
int ns_bezier_elevate(int p,const double *points,double *out,int cap) {
 if(p<1 || p>=NS_MAX_DEGREE || !points || !out) return NS_ARGUMENT;
 if(cap<4*(p+2)) return NS_CAPACITY;
 int status=points_valid(p+1,points); if(status) return status;
 for(int c=0;c<4;c++) {out[c]=points[c];out[4*(p+1)+c]=points[4*p+c];}
 for(int i=1;i<=p;i++) {
  double alpha=(double)i/(p+1);
  for(int c=0;c<4;c++) out[4*i+c]=alpha*points[4*(i-1)+c]+(1-alpha)*points[4*i+c];
 }
 return NS_OK;
}
int ns_solve_linear(int n,int m,double *a,double *b,double *scale,int cap) {
 if(n<1 || n>32768 || m<1 || m>128 || !a || !b || !scale) return NS_ARGUMENT;
 if(cap<n) return NS_WORKSPACE;
 for(int i=0;i<n;i++) {
  double s=0;for(int j=0;j<n;j++) {double v=abs_number(a[i*n+j]);if(!finite_number(v))return NS_NUMERICAL;if(v>s)s=v;}
  if(s==0)return NS_NUMERICAL;
  scale[i]=s;
  for(int j=0;j<m;j++)if(!finite_number(b[i*m+j]))return NS_NUMERICAL;
 }
 for(int k=0;k<n;k++) {
  int pivot=k;double best=0;
  for(int i=k;i<n;i++) {double r=abs_number(a[i*n+k])/scale[i];if(r>best){best=r;pivot=i;}}
  if(best<=NS_EPS*n) return NS_NUMERICAL;
  if(pivot!=k) {
   for(int j=0;j<n;j++){double t=a[k*n+j];a[k*n+j]=a[pivot*n+j];a[pivot*n+j]=t;}
   for(int j=0;j<m;j++){double t=b[k*m+j];b[k*m+j]=b[pivot*m+j];b[pivot*m+j]=t;}
   double t=scale[k];scale[k]=scale[pivot];scale[pivot]=t;
  }
  for(int i=k+1;i<n;i++) {
   double f=a[i*n+k]/a[k*n+k];a[i*n+k]=0;
   for(int j=k+1;j<n;j++)a[i*n+j]-=f*a[k*n+j];
   for(int j=0;j<m;j++)b[i*m+j]-=f*b[k*m+j];
  }
 }
 for(int i=n-1;i>=0;i--) for(int c=0;c<m;c++) {
  double value=b[i*m+c];for(int j=i+1;j<n;j++)value-=a[i*n+j]*b[j*m+c];
  b[i*m+c]=value/a[i*n+i];if(!finite_number(b[i*m+c]))return NS_NUMERICAL;
 }
 return NS_OK;
}
double ns_distance_point_segment(double px,double py,double pz,double ax,double ay,
 double az,double bx,double by,double bz) {
 double dx=bx-ax,dy=by-ay,dz=bz-az;
 double divisor=dx*dx+dy*dy+dz*dz;
 double t=divisor>0?((px-ax)*dx+(py-ay)*dy+(pz-az)*dz)/divisor:0;
 if(t<0)t=0;else if(t>1)t=1;
 dx=px-ax-t*dx;dy=py-ay-t*dy;dz=pz-az-t*dz;
 return __builtin_sqrt(dx*dx+dy*dy+dz*dz);
}

int ns_curve_eval(int p,int n,const double *u,const double *points,double t,int d,
 double *out,int cap,double *work,int workcap) {
 return curve_eval_internal(p,n,u,points,t,d,out,cap,work,workcap,0);
}
int ns_surface_eval(int p,int q,int nu,int nv,const double *u,const double *v,
 const double *points,double tu,double tv,int du,int dv,double *out,int cap,
 double *work,int workcap) {
 return surface_eval_internal(p,q,nu,nv,u,v,points,tu,tv,du,dv,out,cap,work,workcap,0);
}
int ns_curve_samples(int p,int n,const double *u,const double *points,
 const double *parameters,int samples,int order,double *out,int cap,
 double *work,int workcap) {
 if(!parameters || samples<0 || samples>1000000 || order<0 || order>32) return NS_ARGUMENT;
 int stride=3*(order+1);
 if(cap<samples*stride) return NS_CAPACITY;
 int status=ns_validate_curve(p,n,u,points);if(status)return status;
 for(int i=0;i<samples;i++) {
  status=curve_eval_internal(p,n,u,points,parameters[i],order,out+i*stride,stride,work,workcap,1);
  if(status)return status;
 }
 return NS_OK;
}
int ns_surface_samples(int p,int q,int nu,int nv,const double *u,const double *v,
 const double *points,const double *parameters_uv,int samples,int du,int dv,
 double *out,int cap,double *work,int workcap) {
 if(!parameters_uv || samples<0 || samples>1000000 || du<0 || du>32 || dv<0 || dv>32) return NS_ARGUMENT;
 int stride=3*(du+1)*(dv+1);
 if(samples>2147483647/stride || cap<samples*stride) return NS_CAPACITY;
 int status=knots_valid(p,nu,u);if(status)return status;
 status=knots_valid(q,nv,v);if(status)return status;
 if(nu>NS_MAX_CONTROLS/nv)return NS_ARGUMENT;
 status=points_valid(nu*nv,points);if(status)return status;
 for(int i=0;i<samples;i++) {
  status=surface_eval_internal(p,q,nu,nv,u,v,points,parameters_uv[2*i],parameters_uv[2*i+1],du,dv,
   out+i*stride,stride,work,workcap,1);
  if(status)return status;
 }
 return NS_OK;
}
