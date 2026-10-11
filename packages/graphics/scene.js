import{V3}from'@nurbsscene/geometry/math.js';import{buildDisplayMesh,constructionGrids}from'./mesh.js';import{TriangleBvh,pickCurve}from'./picking.js';
export class RenderScene {
 constructor(){this.items=[];this.origin=[0,0,0];this.meshes=new Map();this.grid=constructionGrids(this.origin);this.revision=0;this.quality={segments:32,curveTolerance:.035,isocurves:4};this.clipPlane=null;this.mode='Shaded';this.showGrid=true;this.showIsocurves=true;this.selected=new Set();}
 // Prepare all display data before publishing a new scene revision. A meshing or
 // float32-range failure must not leave half of the previous scene destroyed.
 #prepare(items,origin,quality,force=false){
  if(!Array.isArray(items)||items.length>100000)throw new RangeError('Invalid render item budget.');
  const next=items.filter(i=>i.visible!==false).map(i=>Object.freeze({...i,...(i.color?{color:Object.freeze([...i.color])}:{})})),meshes=new Map();
  for(const item of next){
   if(!item.geometry)throw new TypeError('Render item requires exact geometry.');
   const id=item.geometry.id,previous=this.meshes.get(id);
   if(meshes.has(id))continue;
   if(!force&&previous?.source===item.geometry){meshes.set(id,previous);continue;}
   const mesh=buildDisplayMesh(item.geometry,{...quality,origin});
   mesh.bvh=mesh.indices.length?new TriangleBvh(mesh.positions,mesh.indices):null;meshes.set(id,mesh);
  }
  return{items:Object.freeze(next),meshes};
 }
 setItems(items){
  const before=new Set(this.items.map(i=>i.geometry.id)),next=this.#prepare(items,this.origin,this.quality),after=new Set(next.items.map(i=>i.geometry.id));
  this.items=next.items;this.meshes=next.meshes;this.revision++;
  return{added:[...after].filter(id=>!before.has(id)),removed:[...before].filter(id=>!after.has(id))};
 }
 rebase(origin){
  if(!V3.finite(origin))throw new TypeError('Invalid scene origin.');
  const next=this.#prepare([...this.items],origin,this.quality,true),grid=constructionGrids(origin);
  if(grid.some(g=>g.lines.some(v=>!Number.isFinite(v))))throw new RangeError('Rebased grid exceeds float32 display range.');
  this.origin=[...origin];this.items=next.items;this.meshes=next.meshes;this.grid=grid;this.revision++;
 }
 setQuality(quality){
  const candidate={...this.quality,...quality};
  if(!Number.isInteger(candidate.segments)||candidate.segments<1||candidate.segments>999||!Number.isInteger(candidate.isocurves)||candidate.isocurves<0||candidate.isocurves>1000||!(candidate.curveTolerance>0)||!Number.isFinite(candidate.curveTolerance))throw new RangeError('Invalid display quality.');
  const next=this.#prepare([...this.items],this.origin,candidate,true);
  this.quality=candidate;this.items=next.items;this.meshes=next.meshes;this.revision++;
 }
 bounds(){const min=[Infinity,Infinity,Infinity],max=[-Infinity,-Infinity,-Infinity];for(const item of this.items){const b=item.geometry.bounds();for(let a=0;a<3;a++){min[a]=Math.min(min[a],b.min[a]);max[a]=Math.max(max[a],b.max[a]);}}return{min,max};}
 pick(camera,x,y,width,height){const ray=camera.ray(x,y,width,height);let best=null;for(const item of this.items){if(item.locked)continue;const mesh=this.meshes.get(item.geometry.id),hit=mesh.bvh?mesh.bvh.intersect(ray):pickCurve(camera,x,y,width,height,mesh);if(hit&&(!best||hit.distance<best.distance)){if(this.clipPlane&&V3.dot(this.clipPlane.slice(0,3),hit.point)+this.clipPlane[3]<0)continue;best={...hit,item};}}return best;}
 drawItems(){return this.items.map(item=>({item,mesh:this.meshes.get(item.geometry.id),selected:this.selected.has(item.id)}));}
}
