import{NurbsCurve,NurbsSurface,V3}from'@nurbsscene/geometry/index.js';
function edgesFromPolyline(positions){const count=positions.length/3-1,out=new Float32Array(Math.max(0,count)*6);for(let i=0;i<count;i++){out.set(positions.slice(i*3,i*3+3),i*6);out.set(positions.slice((i+1)*3,(i+2)*3),i*6+3);}return out;}
export function buildDisplayMesh(geometry,{segments=36,curveTolerance=.035,isocurves=5,origin=[0,0,0]}={}){
 const relative=positions=>{const data=Float32Array.from(positions,(v,i)=>v-origin[i%3]);if(data.some(v=>!Number.isFinite(v)))throw new RangeError('Geometry exceeds float32 display range. Rebase nearer the model.');return data;};
 if(geometry instanceof NurbsCurve){const result=geometry.tessellate({tolerance:curveTolerance});if(!result.toleranceMet)throw new RangeError('Curve display tessellation did not meet the requested chord tolerance.');const positions=relative(result.positions);return{vertices:new Float32Array(),indices:new Uint32Array(),lines:edgesFromPolyline(positions),positions:result.positions,normals:new Float64Array(),uv:null,bounds:geometry.bounds(),source:geometry,triangleCount:0,chordalBound:result.maxChordalBound};}
 if(!(geometry instanceof NurbsSurface))throw new TypeError('Unsupported geometry for display tessellation.');
 const mesh=geometry.tessellate({segmentsU:segments,segmentsV:segments}),count=mesh.positions.length/3,vertices=new Float32Array(count*6);for(let i=0;i<count;i++){for(let j=0;j<3;j++)vertices[6*i+j]=mesh.positions[3*i+j]-origin[j];vertices.set(mesh.normals.slice(i*3,i*3+3),i*6+3);}
 if(vertices.some(v=>!Number.isFinite(v)))throw new RangeError('Surface exceeds finite float32 display range. Rebase nearer the model.');
 const lines=[],rows=segments+1,positions=relative(mesh.positions),levels=[...new Set([0,segments,...Array.from({length:isocurves},(_,i)=>Math.round((i+1)*segments/(isocurves+1)))])];
 const line=(a,b)=>lines.push(...positions.slice(a*3,a*3+3),...positions.slice(b*3,b*3+3));
 for(const i of levels)for(let j=0;j<segments;j++){line(i*rows+j,i*rows+j+1);line(j*rows+i,(j+1)*rows+i);}
 return{vertices,indices:mesh.indices,lines:Float32Array.from(lines),positions:mesh.positions,normals:mesh.normals,uv:mesh.uv,bounds:geometry.bounds(),source:geometry,triangleCount:mesh.indices.length/3};
}
export function gridLines(origin=[0,0,0],extent=100,step=5){const fine=[],major=[],xAxis=[],yAxis=[];const push=(target,a,b)=>target.push(...V3.sub(a,origin),...V3.sub(b,origin));for(let i=-extent;i<=extent;i+=step){if(i===0)continue;const target=i%(step*5)===0?major:fine;push(target,[i,-extent,0],[i,extent,0]);push(target,[-extent,i,0],[extent,i,0]);}push(xAxis,[-extent,0,0],[extent,0,0]);push(yAxis,[0,-extent,0],[0,extent,0]);return[{lines:Float32Array.from(fine),color:[.55,.55,.55,.45]},{lines:Float32Array.from(major),color:[.44,.44,.44,.55]},{lines:Float32Array.from(xAxis),color:[.68,.18,.16,.8]},{lines:Float32Array.from(yAxis),color:[.18,.48,.18,.8]}];}

export function constructionGrids(origin=[0,0,0]){
 const groups=[];
 for(const [plane,views] of [['xy',['Top','Perspective']],['xz',['Front']],['yz',['Right']]]){
  for(const group of gridLines([0,0,0])){
   const positions=new Float32Array(group.lines.length);
   for(let i=0;i<group.lines.length;i+=3){const x=group.lines[i],y=group.lines[i+1];const p=plane==='xy'?[x,y,0]:plane==='xz'?[x,0,y]:[0,x,y];positions.set(p.map((v,a)=>v-origin[a]),i);}
   groups.push({...group,lines:positions,views});
  }
 }
 return groups;
}
