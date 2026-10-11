import {V3,M4,boundsOf,clamp,linspace,finiteNumber} from './math.js';
export {V3,M4,boundsOf,clamp,linspace} from './math.js';
const messages=['OK','Invalid argument or supported degree/order limit exceeded','Invalid nondecreasing knot vector','Control points must be finite with positive weights','Parameter outside the active domain','Insufficient caller workspace','Numerically singular or non-finite result','Knot multiplicity exceeds degree','Operation is not supported for this representation','Insufficient output capacity'];
export class GeometryError extends Error {
 constructor(status,operation){super(`${operation}: ${messages[status]??'Kernel failure'}`);this.name='GeometryError';this.status=status;this.operation=operation;}
}
const check=(status,operation)=>{if(status!==0)throw new GeometryError(status,operation);};
function checkedArray(values,name){const a=Float64Array.from(values);if(!a.every(Number.isFinite))throw new TypeError(`${name} must contain finite numbers.`);return a;}
function homogeneous(points){return Float64Array.from(points.flatMap(p=>{if(!Array.isArray(p)||p.length<3||p.length>4)throw new TypeError('A control point is [x,y,z,weight?].');const w=p[3]??1;if(!p.every(Number.isFinite)||!(w>0))throw new TypeError('Control coordinates must be finite and weight positive.');return[p[0]*w,p[1]*w,p[2]*w,w];}));}
function euclidean(h){return Array.from({length:h.length/4},(_,i)=>[h[i*4]/h[i*4+3],h[i*4+1]/h[i*4+3],h[i*4+2]/h[i*4+3],h[i*4+3]]);}
const copy3=(a,i)=>[a[i],a[i+1],a[i+2]];
let geometrySerial=0;
/** One synchronous, caller-owned workspace arena per kernel instance. */
export class GeometryKernel {
 constructor(instance){this.wasm=instance.exports;this.memory=this.wasm.memory;this.base=Number(this.wasm.__heap_base.value);this.offset=this.base;if(this.wasm.ns_abi_version()!==1)throw new Error('NurbsScene kernel ABI mismatch.');}
 frame(fn){const mark=this.offset;try{return fn();}finally{this.offset=mark;}}
 alloc(length,values){if(!Number.isSafeInteger(length)||length<0)throw new RangeError('Invalid allocation.');const address=(this.offset+7)&~7,end=address+length*8;if(end>268435456)throw new RangeError('Kernel arena exceeds the 256 MiB limit.');if(end>this.memory.buffer.byteLength)this.memory.grow(Math.ceil((end-this.memory.buffer.byteLength)/65536));this.offset=end;if(values)new Float64Array(this.memory.buffer,address,length).set(values);return address;}
 read(address,count){return new Float64Array(this.memory.buffer,address,count).slice();}
 createCurve({degree,knots,points,homogeneous:alreadyHomogeneous=false}){
  const u=checkedArray(knots,'Knots'),h=alreadyHomogeneous?checkedArray(points,'Control points'):homogeneous(points);
  if(!Number.isInteger(degree)||degree<1||degree>128||h.length%4||h.length/4<=degree||u.length!==h.length/4+degree+1)throw new TypeError('Invalid NURBS curve dimensions (degree 1..128).');
  this.frame(()=>check(this.wasm.ns_validate_curve(degree,h.length/4,this.alloc(u.length,u),this.alloc(h.length,h)),'Create curve'));
  return new NurbsCurve(this,degree,u,h);
 }
 createSurface({degreeU,degreeV,countU,countV,knotsU,knotsV,points,homogeneous:alreadyHomogeneous=false}){
  const u=checkedArray(knotsU,'U knots'),v=checkedArray(knotsV,'V knots'),h=alreadyHomogeneous?checkedArray(points,'Control points'):homogeneous(points);
  if(![degreeU,degreeV,countU,countV].every(Number.isInteger)||degreeU<1||degreeV<1||degreeU>128||degreeV>128||countU<=degreeU||countV<=degreeV||countU*countV>1000000||h.length!==countU*countV*4||u.length!==countU+degreeU+1||v.length!==countV+degreeV+1)throw new TypeError('Invalid NURBS surface dimensions.');
  const surface=new NurbsSurface(this,degreeU,degreeV,countU,countV,u,v,h);
  surface.evaluate(u[degreeU],v[degreeV],0,0);return surface;
 }
 curveEvaluate(curve,parameters,order=0){
  if(!Number.isInteger(order)||order<0||order>32)throw new RangeError('Derivative order must be 0..32.');
  if(parameters.length>1000000)throw new RangeError('Too many samples.');
  const {degree,u,h}=curve._data(),count=h.length/4;
  return this.frame(()=>{const ku=this.alloc(u.length,u),cp=this.alloc(h.length,h),ts=this.alloc(parameters.length,parameters),n=parameters.length*3*(order+1),out=this.alloc(n),wc=this.wasm.ns_curve_workspace(degree,order),ws=this.alloc(wc);
   check(this.wasm.ns_curve_samples(degree,count,ku,cp,ts,parameters.length,order,out,n,ws,wc),'Evaluate curve');return this.read(out,n);});
 }
 surfaceEvaluate(surface,parameters,du=0,dv=0){
  if(![du,dv].every(x=>Number.isInteger(x)&&x>=0&&x<=32)||parameters.length%2||parameters.length/2>1000000)throw new RangeError('Invalid surface derivative order or sample count.');
  const{p,q,nu,nv,u,v,h}=surface._data();
  return this.frame(()=>{const ku=this.alloc(u.length,u),kv=this.alloc(v.length,v),cp=this.alloc(h.length,h),ts=this.alloc(parameters.length,parameters),n=parameters.length/2*3*(du+1)*(dv+1),out=this.alloc(n),wc=this.wasm.ns_surface_workspace(p,q,du,dv),ws=this.alloc(wc);
   check(this.wasm.ns_surface_samples(p,q,nu,nv,ku,kv,cp,ts,parameters.length/2,du,dv,out,n,ws,wc),'Evaluate surface');return this.read(out,n);});
 }
 insert(curve,t){const{degree:p,u,h}=curve._data(),n=h.length/4;return this.frame(()=>{const ip=this.alloc(h.length,h),iu=this.alloc(u.length,u),ou=this.alloc(u.length+1),op=this.alloc(h.length+4);check(this.wasm.ns_curve_insert(p,n,iu,ip,t,ou,u.length+1,op,h.length+4),'Insert knot');return this.createCurve({degree:p,knots:this.read(ou,u.length+1),points:this.read(op,h.length+4),homogeneous:true});});}
 splitBezier(degree,h,t){return this.frame(()=>{const cp=this.alloc(h.length,h),left=this.alloc(h.length),right=this.alloc(h.length),ws=this.alloc(h.length);check(this.wasm.ns_bezier_split(degree,cp,t,left,right,ws,h.length),'Bezier subdivision');return[this.read(left,h.length),this.read(right,h.length)];});}
 elevateBezier(degree,h){return this.frame(()=>{const cp=this.alloc(h.length,h),out=this.alloc(h.length+4);check(this.wasm.ns_bezier_elevate(degree,cp,out,h.length+4),'Bezier degree elevation');return this.read(out,h.length+4);});}
 basis(degree,count,knots,t,order=0){return this.frame(()=>{const u=this.alloc(knots.length,knots),n=(order+1)*(degree+1),out=this.alloc(n),wc=(degree+1)**2+4*(degree+1),ws=this.alloc(wc);check(this.wasm.ns_basis(degree,count,u,t,order,out,n,ws,wc),'Evaluate basis');return{span:this.wasm.ns_find_span(degree,count,u,t),values:this.read(out,n)};});}
 solveLinear(matrix,rightHandSides,columns=1){const n=Math.sqrt(matrix.length);if(!Number.isInteger(n)||n<1||n>32768||!Number.isInteger(columns)||columns<1||columns>128||rightHandSides.length!==n*columns)throw new RangeError('Invalid linear system dimensions.');return this.frame(()=>{const a=this.alloc(matrix.length,matrix),b=this.alloc(rightHandSides.length,rightHandSides),ws=this.alloc(n);check(this.wasm.ns_solve_linear(n,columns,a,b,ws,n),'Solve linear system');return this.read(b,rightHandSides.length);});}
 line(a,b){return this.createCurve({degree:1,knots:[0,0,1,1],points:[a,b]});}
 polyline(points){if(points.length<2)throw new RangeError('A polyline needs at least two points.');return this.createCurve({degree:1,knots:[0,...points.map((_,i)=>i),points.length-1],points});}
 arc(center,radius,start=0,sweep=Math.PI*2,axisX=[1,0,0],axisY=[0,1,0]){
  finiteNumber(radius,'Radius');finiteNumber(start,'Start angle');finiteNumber(sweep,'Sweep');if(radius<=0||Math.abs(sweep)<1e-14||Math.abs(sweep)>2*Math.PI+1e-12)throw new RangeError('Arc requires positive radius and a nonzero sweep of at most one turn.');
  if(!V3.finite(center))throw new TypeError('Invalid center.');const x=V3.unit(axisX),y=V3.unit(axisY);if(Math.abs(V3.dot(x,y))>1e-10)throw new RangeError('Arc axes must be orthogonal.');
  const segments=Math.ceil(Math.abs(sweep)/(Math.PI/2)),step=sweep/segments,points=[],knots=[0,0,0];
  const point=(a,f=1,w=1)=>[...V3.add(center,V3.scale(V3.add(V3.scale(x,Math.cos(a)),V3.scale(y,Math.sin(a))),radius*f)),w];
  points.push(point(start));for(let i=0;i<segments;i++){const a=start+i*step,b=a+step,w=Math.cos(step/2);points.push(point((a+b)/2,1/w,w),point(b));if(i<segments-1)knots.push((i+1)/segments,(i+1)/segments);}
  knots.push(1,1,1);return this.createCurve({degree:2,knots,points});
 }
 circle(center,radius,axisX=[1,0,0],axisY=[0,1,0]){return this.arc(center,radius,0,2*Math.PI,axisX,axisY);}
 ellipse(center,radiusX,radiusY){if(!(radiusX>0)||!(radiusY>0)||!Number.isFinite(radiusX)||!Number.isFinite(radiusY))throw new RangeError('Ellipse radii must be positive.');const c=this.circle([0,0,0],1);return c.transform(M4.multiply(M4.translation(center),M4.scale([radiusX,radiusY,1])));}
 interpolate(points,degree=3,parameters=null){
  if(points.length<2||points.length>4096)throw new RangeError('Interpolation requires 2..4096 points.');degree=Math.min(degree,points.length-1);
  if(!Number.isInteger(degree)||degree<1||degree>128||!points.every(V3.finite))throw new TypeError('Invalid interpolation input.');
  const n=points.length;let ts=parameters?Array.from(parameters):[0];
  if(!parameters){for(let i=1;i<n;i++)ts.push(ts[i-1]+V3.distance(points[i],points[i-1]));if(!(ts[n-1]>0))throw new RangeError('Interpolation points coincide.');ts=ts.map(x=>x/ts[n-1]);}
  if(ts.length!==n||ts.some((x,i)=>!Number.isFinite(x)||(i>0&&x<=ts[i-1])))throw new RangeError('Interpolation parameters must increase strictly; duplicate consecutive points need removal.');
  const u=new Array(degree+1).fill(ts[0]);for(let j=1;j<n-degree;j++){let sum=0;for(let i=j;i<j+degree;i++)sum+=ts[i];u.push(sum/degree);}u.push(...new Array(degree+1).fill(ts[n-1]));
  const a=new Float64Array(n*n);for(let row=0;row<n;row++){const{span,values}=this.basis(degree,n,u,ts[row]);for(let j=0;j<=degree;j++)a[row*n+span-degree+j]=values[j];}
  const solved=this.solveLinear(a,points.flat(),3),cp=Array.from({length:n},(_,i)=>copy3(solved,i*3));return this.createCurve({degree,knots:u,points:cp});
 }
 extrude(curve,vector){if(!V3.finite(vector)||V3.length(vector)<1e-14)throw new RangeError('Extrusion vector must be nonzero.');const d=curve._data(),points=[];for(const p of euclidean(d.h))points.push(p,[...V3.add(p,vector),p[3]]);return this.createSurface({degreeU:d.degree,degreeV:1,countU:d.h.length/4,countV:2,knotsU:d.u,knotsV:[0,0,1,1],points});}
 revolve(curve,origin=[0,0,0],axis=[0,0,1],sweep=2*Math.PI){
  const z=V3.unit(axis),reference=Math.abs(z[0])<.8?[1,0,0]:[0,1,0],x=V3.unit(V3.cross(reference,z)),y=V3.cross(z,x),arc=this.arc([0,0,0],1,0,sweep),ad=arc._data(),angles=euclidean(ad.h),d=curve._data(),points=[];
  for(const p of euclidean(d.h)){const r=V3.sub(p,origin),a=V3.dot(r,x),b=V3.dot(r,y),h=V3.dot(r,z);for(const c of angles){const radial=V3.add(V3.scale(x,a*c[0]-b*c[1]),V3.scale(y,a*c[1]+b*c[0]));points.push([...V3.add(origin,V3.add(radial,V3.scale(z,h))),p[3]*c[3]]);}}
  return this.createSurface({degreeU:d.degree,degreeV:ad.degree,countU:d.h.length/4,countV:ad.h.length/4,knotsU:d.u,knotsV:ad.u,points});
 }
 ruled(first,second){const[a,b]=compatibleCurves([first,second]);const da=a._data(),db=b._data(),h=[];for(let i=0;i<da.h.length/4;i++)h.push(...da.h.slice(i*4,i*4+4),...db.h.slice(i*4,i*4+4));return this.createSurface({degreeU:da.degree,degreeV:1,countU:da.h.length/4,countV:2,knotsU:da.u,knotsV:[0,0,1,1],points:h,homogeneous:true});}
 loft(curves,degreeV=3){
  if(curves.length<2||curves.length>128)throw new RangeError('Loft requires 2..128 section curves.');const compatible=compatibleCurves(curves),ds=compatible.map(c=>c._data()),nu=ds[0].h.length/4,nv=curves.length,q=Math.min(degreeV,nv-1),ts=linspace(0,1,nv);
  const v=new Array(q+1).fill(0);for(let j=1;j<nv-q;j++){let sum=0;for(let i=j;i<j+q;i++)sum+=ts[i];v.push(sum/q);}v.push(...new Array(q+1).fill(1));
  const a=new Float64Array(nv*nv);for(let row=0;row<nv;row++){const{span,values}=this.basis(q,nv,v,ts[row]);for(let j=0;j<=q;j++)a[row*nv+span-q+j]=values[j];}
  const h=new Float64Array(nu*nv*4);for(let u=0;u<nu;u++){const rhs=ds.flatMap(d=>Array.from(d.h.slice(u*4,u*4+4))),solved=this.solveLinear(a,rhs,4);h.set(solved,u*nv*4);}
  return this.createSurface({degreeU:ds[0].degree,degreeV:q,countU:nu,countV:nv,knotsU:ds[0].u,knotsV:v,points:h,homogeneous:true});
 }
 fromJSON(value){if(!value||typeof value!=='object')throw new TypeError('Geometry object expected.');if(value.type==='NurbsCurve')return this.createCurve(value);if(value.type==='NurbsSurface')return this.createSurface(value);throw new TypeError(`Unsupported geometry type: ${value.type}`);}
}
export async function createGeometryKernel(source=new URL('./dist/nurbsscene.wasm',import.meta.url)){
 const bytes=(source instanceof URL||typeof source==='string')?await(await fetch(source)).arrayBuffer():source;
 const result=await WebAssembly.instantiate(bytes,{});return new GeometryKernel(result.instance??result);
}
export class NurbsCurve {
 #kernel;#degree;#u;#h;
 constructor(kernel,degree,u,h){this.#kernel=kernel;this.#degree=degree;this.#u=u;this.#h=h;this.id=`curve-${++geometrySerial}`;Object.freeze(this);}
 get kernel(){return this.#kernel;}get degree(){return this.#degree;}get controlCount(){return this.#h.length/4;}get domain(){return[this.#u[this.#degree],this.#u[this.controlCount]];}get knots(){return this.#u.slice();}get controlPoints(){return euclidean(this.#h);}
 // Copies protect immutable geometry from clients and cached render resources.
 _data(){return{degree:this.#degree,u:this.#u.slice(),h:this.#h.slice()};}
 evaluate(t,order=0){return this.#kernel.curveEvaluate(this,[finiteNumber(t,'Parameter')],order);}
 pointAt(t){return Array.from(this.evaluate(t));}
 sample(parameters,order=0){return this.#kernel.curveEvaluate(this,parameters,order);}
 tangentAt(t){return V3.unit(copy3(this.evaluate(t,1),3));}
 curvatureAt(t){const d=this.evaluate(t,2),v=copy3(d,3),a=copy3(d,6),speed=V3.length(v);if(speed<=1e-14)throw new GeometryError(6,'Curve curvature at singular tangent');return V3.length(V3.cross(v,a))/(speed**3);}
 bounds(){return boundsOf(this.controlPoints.map(p=>p.slice(0,3)));}
 insertKnot(t,times=1){if(!Number.isInteger(times)||times<0||times>128)throw new RangeError('Invalid insertion count.');let c=this;for(let i=0;i<times;i++)c=this.#kernel.insert(c,t);return c;}
 reverse(){const[a,b]=this.domain;return this.#kernel.createCurve({degree:this.degree,knots:Array.from(this.#u).reverse().map(t=>a+b-t),points:this.controlPoints.reverse()});}
 reparameterize(a=0,b=1){finiteNumber(a);finiteNumber(b);if(a>=b)throw new RangeError('Domain must increase.');const[u,v]=this.domain;return this.#kernel.createCurve({degree:this.degree,knots:Array.from(this.#u,t=>a+(t-u)*(b-a)/(v-u)),points:this.#h,homogeneous:true});}
 transform(matrix){if(matrix.length!==16||!matrix.every(Number.isFinite))throw new TypeError('Expected finite 4×4 matrix.');const out=new Float64Array(this.#h.length);for(let i=0;i<this.controlCount;i++)for(let r=0;r<4;r++)for(let c=0;c<4;c++)out[4*i+r]+=matrix[c*4+r]*this.#h[4*i+c];return this.#kernel.createCurve({degree:this.degree,knots:this.#u,points:out,homogeneous:true});}
 split(t){
  const[a,b]=this.domain;if(!Number.isFinite(t)||t<=a||t>=b)throw new RangeError('Split parameter must be strictly inside the domain.');
  let c=this;let multiplicity=Array.from(this.#u).filter(u=>u===t).length;if(multiplicity>this.degree)throw new GeometryError(8,'Split at full discontinuity');c=c.insertKnot(t,this.degree-multiplicity);
  const d=c._data(),last=Array.from(d.u).lastIndexOf(t),shared=last-this.degree,leftH=d.h.slice(0,(shared+1)*4),rightH=d.h.slice(shared*4),leftU=[...d.u.slice(0,last+1),t],rightU=[t,...d.u.slice(last-this.degree+1)];
  return[this.#kernel.createCurve({degree:this.degree,knots:leftU,points:leftH,homogeneous:true}),this.#kernel.createCurve({degree:this.degree,knots:rightU,points:rightH,homogeneous:true})];
 }
 trim(a,b){const[u,v]=this.domain;if(a<u||b>v||!(a<b))throw new RangeError('Invalid trimming interval.');let c=this;if(a>u)c=c.split(a)[1];if(b<v)c=c.split(b)[0];return c;}
 bezierSegments(){
  const[a,b]=this.domain,p=this.degree;
  if(!Array.from(this.#u.slice(0,p+1)).every(t=>t===a)||!Array.from(this.#u.slice(-p-1)).every(t=>t===b))throw new GeometryError(8,'Bezier decomposition requires clamped endpoints');
  const internal=[...new Set(this.#u)].filter(t=>t>a&&t<b);let c=this;
  for(const t of internal){const s=Array.from(c.#u).filter(x=>x===t).length;if(s>p)throw new GeometryError(8,'Disconnected curve decomposition');c=c.insertKnot(t,p-s);}
  const boundaries=[a,...internal,b],h=c.#h;return boundaries.slice(0,-1).map((t,i)=>({degree:p,domain:[t,boundaries[i+1]],homogeneous:h.slice(i*p*4,(i*p+p+1)*4)}));
 }
 elevateDegree(times=1){
  if(!Number.isInteger(times)||times<0||this.degree+times>128)throw new RangeError('Invalid degree elevation count.');let curve=this;
  for(let step=0;step<times;step++){const segments=curve.bezierSegments(),q=curve.degree+1,h=[],u=new Array(q+1).fill(segments[0].domain[0]);for(let i=0;i<segments.length;i++){const cp=this.#kernel.elevateBezier(curve.degree,segments[i].homogeneous);h.push(...cp.slice(i===0?0:4));if(i<segments.length-1)u.push(...new Array(q).fill(segments[i].domain[1]));}u.push(...new Array(q+1).fill(segments.at(-1).domain[1]));curve=this.#kernel.createCurve({degree:q,knots:u,points:h,homogeneous:true});}
  return curve;
 }
 tessellate({tolerance=.01,maxDepth=24,maxPoints=200000}={}){
  if(!(tolerance>0)||!Number.isFinite(tolerance)||!Number.isInteger(maxDepth)||maxDepth<1||maxDepth>40)throw new RangeError('Invalid tessellation controls.');
  const positions=[],parameters=[];let maxBound=0,unresolved=0;
  const distance=(p,a,b)=>{const d=V3.sub(b,a),l=V3.dot(d,d),t=l>0?clamp(V3.dot(V3.sub(p,a),d)/l,0,1):0;return V3.distance(p,V3.add(a,V3.scale(d,t)));};
  const visit=(h,a,b,depth)=>{const cp=euclidean(h).map(p=>p.slice(0,3)),first=cp[0],last=cp.at(-1),bound=Math.max(...cp.map(p=>distance(p,first,last)));
   if(bound<=tolerance||depth>=maxDepth){if(bound>tolerance)unresolved++;maxBound=Math.max(maxBound,bound);positions.push(last);parameters.push(b);if(positions.length>maxPoints)throw new RangeError('Curve tessellation exceeds point budget.');return;}
   const[left,right]=this.#kernel.splitBezier(this.degree,h,.5),mid=(a+b)/2;visit(left,a,mid,depth+1);visit(right,mid,b,depth+1);};
  for(const segment of this.bezierSegments()){if(!positions.length){positions.push(euclidean(segment.homogeneous)[0].slice(0,3));parameters.push(segment.domain[0]);}visit(segment.homogeneous,...segment.domain,0);}
  return{positions:Float64Array.from(positions.flat()),parameters:Float64Array.from(parameters),maxChordalBound:maxBound,toleranceMet:unresolved===0,unresolved};
 }
 length({absoluteTolerance=1e-8,relativeTolerance=1e-9,maxDepth=20}={}){
  const xs=[.09501250983763744,.2816035507792589,.4580167776572274,.6178762444026438,.755404408355003,.8656312023878318,.9445750230732326,.9894009349916499],ws=[.1894506104550685,.1826034150449236,.1691565193950025,.1495959888165767,.1246289712555339,.09515851168249278,.06225352393864789,.027152459411754095];
  const integrate=(a,b)=>{const mid=(a+b)/2,half=(b-a)/2,ts=xs.flatMap(x=>[mid-half*x,mid+half*x]),values=this.sample(ts,1);let sum=0;for(let i=0;i<xs.length;i++)sum+=ws[i]*(Math.hypot(...values.slice(i*12+3,i*12+6))+Math.hypot(...values.slice(i*12+9,i*12+12)));return sum*half;};
  let estimatedError=0,converged=true,evaluations=0;const[a,b]=this.domain;
  const recurse=(u,v,whole,depth)=>{evaluations+=32;const mid=(u+v)/2,left=integrate(u,mid),right=integrate(mid,v),error=Math.abs(left+right-whole),tol=absoluteTolerance*(v-u)/(b-a)+relativeTolerance*Math.abs(left+right);if(error<=tol||depth>=maxDepth){estimatedError+=error;if(error>tol)converged=false;return left+right;}return recurse(u,mid,left,depth+1)+recurse(mid,v,right,depth+1);};
  const spans=[...new Set(this.#u)].filter(t=>t>=a&&t<=b);let value=0;for(let i=0;i<spans.length-1;i++){evaluations+=16;value+=recurse(spans[i],spans[i+1],integrate(spans[i],spans[i+1]),0);}return{value,estimatedError,converged,evaluations};
 }
 closestPoint(point,{seedsPerSpan=12,maxIterations=32,tolerance=1e-10}={}){
  if(!V3.finite(point))throw new TypeError('Expected finite 3D point.');const[a,b]=this.domain,spans=[...new Set(this.#u)].filter(t=>t>=a&&t<=b);let best={parameter:a,point:this.pointAt(a),distance:Infinity,converged:false};
  const consider=t=>{const p=this.pointAt(t),distance=V3.distance(point,p);if(distance<best.distance)best={parameter:t,point:p,distance,converged:false};};
  for(let s=0;s<spans.length-1;s++){const lo=spans[s],hi=spans[s+1];consider(lo);consider(hi);
   for(let seed=0;seed<=seedsPerSpan;seed++){let t=lo+(hi-lo)*seed/seedsPerSpan,converged=false;for(let it=0;it<maxIterations;it++){const d=this.evaluate(t,2),r=V3.sub(copy3(d,0),point),v=copy3(d,3),acc=copy3(d,6),f=V3.dot(r,v),df=V3.dot(v,v)+V3.dot(r,acc);if(Math.abs(df)<1e-25)break;const next=clamp(t-f/df,lo,hi);if(Math.abs(next-t)<=tolerance*(1+Math.abs(t))){t=next;converged=true;break;}let candidate=next;for(let bt=0;bt<12&&V3.distance(this.pointAt(candidate),point)>V3.length(r);bt++)candidate=(candidate+t)/2;t=candidate;}
    const p=this.pointAt(t),distance=V3.distance(point,p);if(distance<best.distance)best={parameter:t,point:p,distance,converged};}}
  return{...best,globalMinimumCertified:false};
 }
 toJSON(){return{type:'NurbsCurve',degree:this.degree,knots:Array.from(this.#u),points:this.controlPoints};}
}
export class NurbsSurface {
 #kernel;#p;#q;#nu;#nv;#u;#v;#h;
 constructor(kernel,p,q,nu,nv,u,v,h){this.#kernel=kernel;this.#p=p;this.#q=q;this.#nu=nu;this.#nv=nv;this.#u=u;this.#v=v;this.#h=h;this.id=`surface-${++geometrySerial}`;Object.freeze(this);}
 get kernel(){return this.#kernel;}get degreeU(){return this.#p;}get degreeV(){return this.#q;}get countU(){return this.#nu;}get countV(){return this.#nv;}get domainU(){return[this.#u[this.#p],this.#u[this.#nu]];}get domainV(){return[this.#v[this.#q],this.#v[this.#nv]];}get controlPoints(){return euclidean(this.#h);}
 _data(){return{p:this.#p,q:this.#q,nu:this.#nu,nv:this.#nv,u:this.#u.slice(),v:this.#v.slice(),h:this.#h.slice()};}
 evaluate(u,v,du=0,dv=0){return this.#kernel.surfaceEvaluate(this,[u,v],du,dv);}
 pointAt(u,v){return Array.from(this.evaluate(u,v));}
 normalAt(u,v){const d=this.evaluate(u,v,1,1);return V3.unit(V3.cross(copy3(d,6),copy3(d,3)));}
 curvatureAt(u,v){const d=this.evaluate(u,v,2,2),su=copy3(d,9),sv=copy3(d,3),n=V3.unit(V3.cross(su,sv)),suu=copy3(d,18),suv=copy3(d,12),svv=copy3(d,6),E=V3.dot(su,su),F=V3.dot(su,sv),G=V3.dot(sv,sv),e=V3.dot(n,suu),f=V3.dot(n,suv),g=V3.dot(n,svv),den=E*G-F*F;
  if(Math.abs(den)<1e-25)throw new GeometryError(6,'Surface curvature at singular parameter');const gaussian=(e*g-f*f)/den,mean=(E*g-2*F*f+G*e)/(2*den),discriminant=Math.sqrt(Math.max(0,mean*mean-gaussian));return{gaussian,mean,principal:[mean+discriminant,mean-discriminant],normal:n};}
 isoCurve(direction,parameter){const alongU=direction==='u';if(!alongU&&direction!=='v')throw new TypeError('Direction is u or v.');const degree=alongU?this.#q:this.#p,count=alongU?this.#nv:this.#nu,knots=alongU?this.#v:this.#u,{span,values}=this.#kernel.basis(degree,count,knots,parameter),outCount=alongU?this.#nu:this.#nv,h=new Float64Array(outCount*4);for(let i=0;i<outCount;i++)for(let j=0;j<=degree;j++){const index=alongU?i*this.#nv+span-degree+j:(span-degree+j)*this.#nv+i;for(let c=0;c<4;c++)h[4*i+c]+=values[j]*this.#h[4*index+c];}return this.#kernel.createCurve({degree:alongU?this.#p:this.#q,knots:alongU?this.#u:this.#v,points:h,homogeneous:true});}
 transform(matrix){if(matrix.length!==16||!matrix.every(Number.isFinite))throw new TypeError('Expected finite 4×4 matrix.');const h=new Float64Array(this.#h.length);for(let i=0;i<this.#nu*this.#nv;i++)for(let r=0;r<4;r++)for(let c=0;c<4;c++)h[4*i+r]+=matrix[4*c+r]*this.#h[4*i+c];return this.#kernel.createSurface({degreeU:this.#p,degreeV:this.#q,countU:this.#nu,countV:this.#nv,knotsU:this.#u,knotsV:this.#v,points:h,homogeneous:true});}
 reverse(direction){const alongU=direction==='u';if(!alongU&&direction!=='v')throw new TypeError('Direction is u or v.');const h=new Float64Array(this.#h.length);for(let i=0;i<this.#nu;i++)for(let j=0;j<this.#nv;j++){const source=((alongU?this.#nu-1-i:i)*this.#nv+(alongU?j:this.#nv-1-j))*4;h.set(this.#h.slice(source,source+4),(i*this.#nv+j)*4);}const domain=alongU?this.domainU:this.domainV,reverse=knots=>Array.from(knots).reverse().map(t=>domain[0]+domain[1]-t);return this.#kernel.createSurface({degreeU:this.#p,degreeV:this.#q,countU:this.#nu,countV:this.#nv,knotsU:alongU?reverse(this.#u):this.#u,knotsV:alongU?this.#v:reverse(this.#v),points:h,homogeneous:true});}
 transpose(){const h=new Float64Array(this.#h.length);for(let i=0;i<this.#nu;i++)for(let j=0;j<this.#nv;j++)h.set(this.#h.slice((i*this.#nv+j)*4,(i*this.#nv+j+1)*4),(j*this.#nu+i)*4);return this.#kernel.createSurface({degreeU:this.#q,degreeV:this.#p,countU:this.#nv,countV:this.#nu,knotsU:this.#v,knotsV:this.#u,points:h,homogeneous:true});}
 bounds(){return boundsOf(this.controlPoints.map(p=>p.slice(0,3)));}
 tessellate({segmentsU=40,segmentsV=40}={}){
  if(![segmentsU,segmentsV].every(n=>Number.isInteger(n)&&n>=1&&n<=1000)||(segmentsU+1)*(segmentsV+1)>1000000)throw new RangeError('Surface grid exceeds the 1,000,000 vertex budget.');
  const us=linspace(...this.domainU,segmentsU+1),vs=linspace(...this.domainV,segmentsV+1),uv=new Float64Array(us.length*vs.length*2);let at=0;for(const u of us)for(const v of vs){uv[at++]=u;uv[at++]=v;}
  const d=this.#kernel.surfaceEvaluate(this,uv,1,1),count=us.length*vs.length,positions=new Float64Array(count*3),normals=new Float64Array(count*3),indices=new Uint32Array(segmentsU*segmentsV*6);let degeneracies=0;
  for(let i=0;i<count;i++){positions.set(d.slice(i*12,i*12+3),i*3);const normal=V3.cross(copy3(d,i*12+6),copy3(d,i*12+3)),length=V3.length(normal);if(length>1e-20)normals.set(V3.scale(normal,1/length),i*3);else degeneracies++;}
  at=0;for(let i=0;i<segmentsU;i++)for(let j=0;j<segmentsV;j++){const a=i*vs.length+j,b=a+vs.length,c=b+1,e=a+1;indices.set([a,b,c,a,c,e],at);at+=6;}
  // Singular poles have no unique parameter normal. Accumulate adjacent face
  // normals rather than inventing a world-axis normal.
  if(degeneracies){for(let k=0;k<indices.length;k+=3){const ids=indices.slice(k,k+3),a=copy3(positions,ids[0]*3),b=copy3(positions,ids[1]*3),c=copy3(positions,ids[2]*3),n=V3.cross(V3.sub(b,a),V3.sub(c,a));for(const id of ids)if(V3.length(copy3(d,id*12+3))<1e-18||V3.length(copy3(d,id*12+6))<1e-18){for(let j=0;j<3;j++)normals[id*3+j]+=n[j];}}for(let i=0;i<count;i++){const n=copy3(normals,i*3),l=V3.length(n);if(l>1e-25)normals.set(V3.scale(n,1/l),i*3);}}
  return{positions,normals,indices,uv,segmentsU,segmentsV,degenerateNormals:degeneracies,source:this.id,authoritative:false};
 }
 toJSON(){return{type:'NurbsSurface',degreeU:this.#p,degreeV:this.#q,countU:this.#nu,countV:this.#nv,knotsU:Array.from(this.#u),knotsV:Array.from(this.#v),points:this.controlPoints};}
}
export function compatibleCurves(curves){
 if(!curves.length||!curves.every(c=>c instanceof NurbsCurve))throw new TypeError('NURBS curves required.');const degree=Math.max(...curves.map(c=>c.degree));let result=curves.map(c=>c.reparameterize().elevateDegree(degree-c.degree));const multiplicities=new Map();for(const c of result){const own=new Map();for(const t of c.knots)if(t>0&&t<1)own.set(t,(own.get(t)??0)+1);for(const[t,n]of own)multiplicities.set(t,Math.max(n,multiplicities.get(t)??0));}
 result=result.map(c=>{for(const[t,n]of [...multiplicities].sort((a,b)=>a[0]-b[0])){const current=Array.from(c.knots).filter(x=>x===t).length;c=c.insertKnot(t,n-current);}return c;});return result;
}
