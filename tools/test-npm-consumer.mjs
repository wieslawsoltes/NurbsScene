/** Test tarballs in a clean consumer directory, not workspace symlinks. */
import {mkdtempSync, readdirSync, writeFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {resolve, dirname, join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {spawnSync} from 'node:child_process';
const root=resolve(dirname(fileURLToPath(import.meta.url)), '..');
const archives=resolve(process.argv[2]??join(root,'artifacts/npm'));
const files=readdirSync(archives).filter(name=>name.endsWith('.tgz')).map(name=>join(archives,name));
if(files.length!==6)throw new Error('Expected six independently packaged archives.');
const consumer=mkdtempSync(join(tmpdir(),'nurbsscene-consumer-'));
function run(command,args){const r=spawnSync(command,args,{cwd:consumer,stdio:'inherit'});if(r.status!==0)throw new Error(`${command} failed (${r.status}).`);}
try{
 writeFileSync(join(consumer,'package.json'),JSON.stringify({name:'nurbsscene-consumer-test',version:'1.0.0',private:true,type:'module'}));
 run('npm',['install','--offline','--ignore-scripts','--no-audit','--no-fund',...files]);
 writeFileSync(join(consumer,'consumer.mjs'),String.raw`
import assert from 'node:assert/strict';
import {readFile,realpath} from 'node:fs/promises';
import {createGeometryKernel} from '@nurbsscene/geometry';
import {Graph} from '@nurbsscene/parametric';
import {createStandardRegistry} from '@nurbsscene/parametric/components';
import {Document} from '@nurbsscene/model';
import {serializeWorkspace,deserializeWorkspace,exportObj} from '@nurbsscene/io';
import {RenderScene} from '@nurbsscene/graphics';
const kernel=await createGeometryKernel(await readFile(new URL(import.meta.resolve('@nurbsscene/geometry/kernel.wasm'))));
const registry=createStandardRegistry(kernel),graph=new Graph(registry);
assert.equal(registry.definitions.length,102);
const number=graph.addNode('Number',{parameters:{value:7}}),circle=graph.addNode('Circle');
graph.connect(number.id,'N',circle.id,'Radius');
const result=await graph.solve();assert.equal(result.errors.length,0);
const curve=result.nodes.get(circle.id).outputs.C.allItems[0];
assert.ok(Math.abs(curve.pointAt(0)[0]-7)<1e-12);
assert.ok(Math.abs(curve.pointAt(.25)[1]-7)<1e-12);
const document=new Document();document.add(curve,{name:'Consumer circle'});
const restored=deserializeWorkspace(kernel,registry,serializeWorkspace(document,graph));
assert.equal(restored.document.objects.length,1);
assert.equal((await restored.graph.solve()).errors.length,0);
assert.ok(exportObj(document.objects).includes('\nl '));
const scene=new RenderScene();scene.setItems(document.objects);
assert.equal(scene.meshes.size,1);
const controls=new URL(import.meta.resolve('@nurbsscene/controls'));
assert.match(await readFile(controls,'utf8'),/customElements|export/);
assert.ok((await readFile(new URL(import.meta.resolve('@nurbsscene/controls/styles.css')))).length>100);
for(const name of ['geometry','parametric','model','io','graphics','controls']){
 const path=await realpath(new URL(import.meta.resolve('@nurbsscene/'+name)));
 assert.ok(path.startsWith(process.cwd()),'Package must resolve inside the isolated consumer.');
}
console.log('PASS: six standalone tarballs resolve inside an isolated consumer.');
console.log('PASS: WASM exact curve, 102-component registry, typed graph, document round trip, display meshing and OBJ export.');
console.log('Controls assets resolved; DOM and GPU execution are not claimed by this Node test.');
`);
 run(process.execPath,['consumer.mjs']);
}finally{rmSync(consumer,{recursive:true,force:true});}
