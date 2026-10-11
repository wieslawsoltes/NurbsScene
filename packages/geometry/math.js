/** Double-precision geometry helpers. Matrices use column-major storage. */
export const V3 = Object.freeze({
 add:(a,b)=>[a[0]+b[0],a[1]+b[1],a[2]+b[2]],
 sub:(a,b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]],
 scale:(a,s)=>[a[0]*s,a[1]*s,a[2]*s],
 dot:(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2],
 cross:(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]],
 length:a=>Math.hypot(...a),
 distance:(a,b)=>Math.hypot(a[0]-b[0],a[1]-b[1],a[2]-b[2]),
 unit(a){const n=Math.hypot(...a);if(!Number.isFinite(n)||n<=1e-300)throw new RangeError('Cannot normalize a zero or invalid vector.');return a.map(x=>x/n);},
 lerp:(a,b,t)=>a.map((x,i)=>x+(b[i]-x)*t),
 finite:a=>Array.isArray(a)&&a.length===3&&a.every(Number.isFinite),
});
export const M4 = Object.freeze({
 identity:()=>[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],
 translation:([x,y,z])=>[1,0,0,0,0,1,0,0,0,0,1,0,x,y,z,1],
 scale:([x,y,z])=>[x,0,0,0,0,y,0,0,0,0,z,0,0,0,0,1],
 multiply(a,b){const r=new Array(16).fill(0);for(let c=0;c<4;c++)for(let row=0;row<4;row++)for(let k=0;k<4;k++)r[c*4+row]+=a[k*4+row]*b[c*4+k];return r;},
 point(m,p){const w=m[3]*p[0]+m[7]*p[1]+m[11]*p[2]+m[15];if(!Number.isFinite(w)||Math.abs(w)<1e-300)throw new RangeError('Projective point at infinity.');return[0,1,2].map(i=>(m[i]*p[0]+m[4+i]*p[1]+m[8+i]*p[2]+m[12+i])/w);},
 vector:(m,p)=>[0,1,2].map(i=>m[i]*p[0]+m[4+i]*p[1]+m[8+i]*p[2]),
 rotation(axis,angle,origin=[0,0,0]){const[x,y,z]=V3.unit(axis),c=Math.cos(angle),s=Math.sin(angle),t=1-c;
 const r=[t*x*x+c,t*x*y+s*z,t*x*z-s*y,0,t*x*y-s*z,t*y*y+c,t*y*z+s*x,0,t*x*z+s*y,t*y*z-s*x,t*z*z+c,0,0,0,0,1];
 return M4.multiply(M4.translation(origin),M4.multiply(r,M4.translation(V3.scale(origin,-1))));},
 lookAt(eye,target,up){const z=V3.unit(V3.sub(eye,target)),x=V3.unit(V3.cross(up,z)),y=V3.cross(z,x);
 return[x[0],y[0],z[0],0,x[1],y[1],z[1],0,x[2],y[2],z[2],0,-V3.dot(x,eye),-V3.dot(y,eye),-V3.dot(z,eye),1];},
 perspective(fov,aspect,near,far,zeroToOne=false){const f=1/Math.tan(fov/2),nf=1/(near-far);return[f/aspect,0,0,0,0,f,0,0,0,0,(zeroToOne?far:far+near)*nf,-1,0,0,(zeroToOne?1:2)*far*near*nf,0];},
 orthographic(left,right,bottom,top,near,far,zeroToOne=false){return[2/(right-left),0,0,0,0,2/(top-bottom),0,0,0,0,(zeroToOne?1:2)/(near-far),0,-(right+left)/(right-left),-(top+bottom)/(top-bottom),(zeroToOne?near:far+near)/(near-far),1];},
 inverse(m){
  const a=Array.from({length:4},(_,r)=>Array.from({length:8},(_,c)=>c<4?m[c*4+r]:(c-4===r?1:0)));
  for(let k=0;k<4;k++){let p=k;for(let r=k+1;r<4;r++)if(Math.abs(a[r][k])>Math.abs(a[p][k]))p=r;if(Math.abs(a[p][k])<1e-30)throw new RangeError('Singular matrix.');[a[k],a[p]]=[a[p],a[k]];let d=a[k][k];a[k]=a[k].map(x=>x/d);for(let r=0;r<4;r++)if(r!==k){const f=a[r][k];for(let c=0;c<8;c++)a[r][c]-=f*a[k][c];}}
  return Array.from({length:16},(_,i)=>a[i%4][4+Math.floor(i/4)]);
 },
});
export function boundsOf(points){const min=[Infinity,Infinity,Infinity],max=[-Infinity,-Infinity,-Infinity];for(const p of points){for(let i=0;i<3;i++){min[i]=Math.min(min[i],p[i]);max[i]=Math.max(max[i],p[i]);}}return{min,max,center:V3.scale(V3.add(min,max),.5),size:V3.sub(max,min)};}
export function clamp(x,a,b){return Math.max(a,Math.min(b,x));}
export function linspace(a,b,n){if(!Number.isInteger(n)||n<2||n>1000000)throw new RangeError('Sample count must be 2..1,000,000.');return Float64Array.from({length:n},(_,i)=>i===n-1?b:a+(b-a)*i/(n-1));}
export function finiteNumber(x,name='Value'){if(typeof x!=='number'||!Number.isFinite(x))throw new TypeError(`${name} must be finite.`);return x;}
