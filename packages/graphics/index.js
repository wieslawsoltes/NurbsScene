import{WebGpuRenderer}from'./webgpu.js';import{WebGl2Renderer}from'./webgl2.js';
export{Camera,viewportLayout}from'./camera.js';export{RenderScene}from'./scene.js';export{buildDisplayMesh}from'./mesh.js';export{TriangleBvh,rayTriangle,pickCurve}from'./picking.js';export{WebGpuRenderer,WebGl2Renderer};
export async function createRenderer(canvas,{backend='auto',onLost=()=>{}}={}){
 if(!['auto','webgpu','webgl2'].includes(backend))throw new TypeError('Renderer must be auto, webgpu or webgl2.');
 const failures=[];
 if(backend!=='webgl2')try{return{renderer:await WebGpuRenderer.create(canvas,{onLost}),canvas,failures};}catch(error){failures.push(`WebGPU: ${error.message}`);if(backend==='webgpu')throw error;const replacement=canvas.cloneNode(false);canvas.replaceWith(replacement);canvas=replacement;}
 try{return{renderer:await WebGl2Renderer.create(canvas,{onLost}),canvas,failures};}catch(error){failures.push(`WebGL2: ${error.message}`);throw new AggregateError(failures.map(message=>new Error(message)),'No GPU renderer could be initialized. '+failures.join(' '));}
}
