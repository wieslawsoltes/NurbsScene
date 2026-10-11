import {DataTree,Path,matchItems} from './data-tree.js';
export {DataTree,Path,matchItems} from './data-tree.js';
class Signal {#listeners=new Set();subscribe(fn){this.#listeners.add(fn);return()=>this.#listeners.delete(fn);}emit(value){for(const fn of [...this.#listeners])fn(value);}}
let nextId=0;
const id=prefix=>`${prefix}-${Date.now().toString(36)}-${(++nextId).toString(36)}`;
const jsonClone=value=>JSON.parse(JSON.stringify(value));
const deepFreeze=value=>{if(value&&typeof value==='object'){for(const v of Object.values(value))deepFreeze(v);Object.freeze(value);}return value;};
const assertName=name=>{if(typeof name!=='string'||!/^[A-Za-z][\w.-]{0,127}$/.test(name))throw new TypeError(`Invalid identifier: ${name}`);return name;};
export class ComponentRegistry {
 #definitions=new Map();
 register(definition){const key=assertName(definition.id);if(this.#definitions.has(key))throw new Error(`Component ${key} already registered.`);if(typeof definition.solve!=='function')throw new TypeError('Component solve function required.');const normalize=ports=>Object.freeze((ports??[]).map(p=>Object.freeze({access:'item',type:'any',...p,name:assertName(p.name)})));const def=Object.freeze({...definition,inputs:normalize(definition.inputs),outputs:normalize(definition.outputs)});for(const ports of[def.inputs,def.outputs])if(new Set(ports.map(p=>p.name)).size!==ports.length)throw new TypeError('Duplicate port names.');this.#definitions.set(key,def);return this;}
 get(key){const def=this.#definitions.get(key);if(!def)throw new Error(`Missing component implementation: ${key}`);return def;}
 has(key){return this.#definitions.has(key);}
 get definitions(){return[...this.#definitions.values()];}
 search(query='',category=null){const q=query.toLocaleLowerCase();return this.definitions.filter(d=>(!category||d.category===category)&&`${d.id} ${d.name??''} ${d.description??''}`.toLocaleLowerCase().includes(q));}
}
function compatible(source,target){return source==='any'||target==='any'||source===target||(source==='integer'&&target==='number')||(source==='point'&&target==='vector')||(source==='vector'&&target==='point')||(['curve','surface','brep','mesh'].includes(source)&&target==='geometry');}
function validateValue(value,type,name){
 let valid=true;
 if(type==='number')valid=typeof value==='number'&&Number.isFinite(value);
 else if(type==='integer')valid=Number.isSafeInteger(value);
 else if(type==='boolean')valid=typeof value==='boolean';
 else if(type==='text')valid=typeof value==='string';
 else if(type==='point'||type==='vector')valid=Array.isArray(value)&&value.length===3&&value.every(Number.isFinite);
 else if(type==='curve')valid=typeof value?.pointAt==='function'&&typeof value?.degree==='number';
 else if(type==='surface')valid=typeof value?.pointAt==='function'&&typeof value?.degreeU==='number';
 else if(type==='geometry')valid=typeof value?.bounds==='function';
 else if(type==='matrix')valid=Array.isArray(value)&&value.length===16&&value.every(Number.isFinite);
 if(!valid)throw new TypeError(`${name} requires ${type}; received ${typeof value}.`);return value;
}
function abortError(){const error=new Error('Graph evaluation cancelled.');error.name='AbortError';return error;}
export class Graph {
 #nodes=new Map();#wires=new Map();#cache=new Map();#dirty=new Set();#revision=0;#solveGeneration=0;
 constructor(registry,{maxNodes=10000,maxItems=100000}={}){this.registry=registry;this.maxNodes=maxNodes;this.maxItems=maxItems;this.changed=new Signal();this.solved=new Signal();this.groups=[];this.view={x:0,y:0,zoom:1};this.lastResult=null;}
 get revision(){return this.#revision;}get nodes(){return[...this.#nodes.values()];}get wires(){return[...this.#wires.values()];}
 node(nodeId){const node=this.#nodes.get(nodeId);if(!node)throw new Error(`Unknown node ${nodeId}.`);return node;}
 #change(kind,nodeId){this.#revision++;this.changed.emit({kind,nodeId,revision:this.#revision});}
 #invalidate(nodeId){const queue=[nodeId],seen=new Set();while(queue.length){const current=queue.pop();if(seen.has(current))continue;seen.add(current);this.#dirty.add(current);for(const wire of this.#wires.values())if(wire.from.node===current)queue.push(wire.to.node);}}
 addNode(component,{id:nodeId=id('node'),x=0,y=0,parameters={},name=null,preview=true,enabled=true,matching='longest'}={}){
  this.registry.get(component);if(this.#nodes.size>=this.maxNodes)throw new RangeError('Node count exceeds graph budget.');if(this.#nodes.has(nodeId))throw new Error(`Duplicate node ${nodeId}.`);if(!Number.isFinite(x)||!Number.isFinite(y))throw new TypeError('Node coordinates must be finite.');
  const node=Object.freeze({id:nodeId,component,x,y,name,preview:!!preview,enabled:!!enabled,matching,parameters:deepFreeze(jsonClone(parameters))});this.#nodes.set(nodeId,node);this.#invalidate(nodeId);this.#change('add',nodeId);return node;
 }
 updateNode(nodeId,patch){const previous=this.node(nodeId);if(patch.component&&patch.component!==previous.component)throw new Error('Cannot replace component type in place.');if(patch.id&&patch.id!==nodeId)throw new Error('Cannot replace node identity.');const next=Object.freeze({...previous,...patch,id:nodeId,component:previous.component,parameters:deepFreeze(jsonClone(patch.parameters??previous.parameters))});if(!Number.isFinite(next.x)||!Number.isFinite(next.y))throw new TypeError('Node coordinates must be finite.');this.#nodes.set(nodeId,next);if('parameters'in patch||'enabled'in patch||'matching'in patch)this.#invalidate(nodeId);this.#change('update',nodeId);return next;}
 removeNode(nodeId){this.node(nodeId);for(const wire of [...this.#wires.values()])if(wire.from.node===nodeId||wire.to.node===nodeId)this.disconnect(wire.id);this.#nodes.delete(nodeId);this.#cache.delete(nodeId);this.#dirty.delete(nodeId);this.groups=this.groups.map(g=>({...g,nodes:g.nodes.filter(n=>n!==nodeId)}));this.#change('remove',nodeId);}
 #wouldCycle(from,to){if(from===to)return true;const stack=[to],seen=new Set();while(stack.length){const n=stack.pop();if(n===from)return true;if(seen.has(n))continue;seen.add(n);for(const w of this.#wires.values())if(w.from.node===n)stack.push(w.to.node);}return false;}
 connect(fromNode,fromPort,toNode,toPort,{id:wireId=id('wire'),replace=false,transform='none'}={}){
  const a=this.node(fromNode),b=this.node(toNode),source=this.registry.get(a.component).outputs.find(p=>p.name===fromPort),target=this.registry.get(b.component).inputs.find(p=>p.name===toPort);
  if(!source||!target)throw new Error('Connection references a missing port.');if(!compatible(source.type,target.type))throw new TypeError(`Incompatible connection: ${source.type} → ${target.type}.`);if(this.#wouldCycle(fromNode,toNode))throw new Error('Connection would create a cycle. Feedback requires an explicit state component.');if(this.#wires.has(wireId))throw new Error(`Duplicate wire ${wireId}.`);
  if(!['none','flatten','graft','simplify'].includes(transform))throw new TypeError('Unsupported wire data transform.');
  if(replace)for(const w of[...this.#wires.values()])if(w.to.node===toNode&&w.to.port===toPort)this.disconnect(w.id);
  if(this.wires.some(w=>w.from.node===fromNode&&w.from.port===fromPort&&w.to.node===toNode&&w.to.port===toPort))throw new Error('Duplicate connection.');
  const wire=Object.freeze({id:wireId,from:Object.freeze({node:fromNode,port:fromPort}),to:Object.freeze({node:toNode,port:toPort}),transform});this.#wires.set(wireId,wire);this.#invalidate(toNode);this.#change('connect',toNode);return wire;
 }
 disconnect(wireId){const wire=this.#wires.get(wireId);if(!wire)throw new Error(`Unknown wire ${wireId}.`);this.#wires.delete(wireId);this.#invalidate(wire.to.node);this.#change('disconnect',wire.to.node);}
 setWireTransform(wireId,transform){const w=this.#wires.get(wireId);if(!w)throw new Error('Unknown wire.');if(!['none','flatten','graft','simplify'].includes(transform))throw new TypeError('Unsupported data transform.');this.#wires.set(wireId,Object.freeze({...w,transform}));this.#invalidate(w.to.node);this.#change('transform',w.to.node);}
 addGroup(nodeIds,{name='Group',color='#dfd4e9',id:groupId=id('group')}={}){for(const n of nodeIds)this.node(n);const g=Object.freeze({id:groupId,name,color,nodes:Object.freeze([...new Set(nodeIds)])});this.groups.push(g);this.#change('group',null);return g;}
 #topological(nodes,wires){const degrees=new Map([...nodes.keys()].map(k=>[k,0])),out=new Map();for(const w of wires){degrees.set(w.to.node,degrees.get(w.to.node)+1);if(!out.has(w.from.node))out.set(w.from.node,[]);out.get(w.from.node).push(w.to.node);}const ready=[...degrees].filter(([,d])=>d===0).map(([k])=>k),order=[];let cursor=0;while(cursor<ready.length){const current=ready[cursor++];order.push(current);for(const dependent of out.get(current)??[]){const d=degrees.get(dependent)-1;degrees.set(dependent,d);if(d===0)ready.push(dependent);}}if(order.length!==nodes.size)throw new Error('Graph contains a cycle.');return order;}
 async solve({signal=null,context={}}={}){
  const generation=++this.#solveGeneration,revision=this.#revision,nodes=new Map(this.#nodes),wires=this.wires,dirty=new Set(this.#dirty),order=this.#topological(nodes,wires),results=new Map(),start=performance.now();let evaluated=0,cached=0;
  const cancelled=()=>signal?.aborted||generation!==this.#solveGeneration;
  for(const nodeId of order){if(cancelled())throw abortError();const node=nodes.get(nodeId),cachedResult=this.#cache.get(nodeId);if(!dirty.has(nodeId)&&cachedResult){results.set(nodeId,cachedResult);cached++;continue;}
   const def=this.registry.get(node.component),nodeStart=performance.now();let result;
   try{
    if(!node.enabled){result={outputs:Object.fromEntries(def.outputs.map(p=>[p.name,DataTree.empty()])),status:'disabled',messages:[]};}
    else {
     const inputs={};for(const port of def.inputs){const incoming=wires.filter(w=>w.to.node===nodeId&&w.to.port===port.name);let tree=DataTree.empty();
      for(const wire of incoming){const upstream=results.get(wire.from.node);if(!upstream||upstream.status==='error'||upstream.status==='blocked')throw Object.assign(new Error(`Upstream component ${wire.from.node} failed.`),{blocked:true});let value=upstream.outputs[wire.from.port]??DataTree.empty();if(wire.transform!=='none')value=value[wire.transform]();tree=tree.merge(value);}
      if(!incoming.length){const defaults=node.parameters.inputs??{};const value=Object.hasOwn(defaults,port.name)?defaults[port.name]:port.default;if(value!==undefined)tree=port.access==='list'?DataTree.list(Array.isArray(value)?value:[value]):DataTree.item(value);else if(port.optional)tree=DataTree.empty();else throw new Error(`Required input ${port.name} is not connected.`);}
      if(port.type!=='any')for(const value of tree.allItems)validateValue(value,port.type,port.name);inputs[port.name]=tree;
     }
     result={outputs:await this.#evaluateDefinition(def,node,inputs,{...context,signal,cancelled,maxItems:this.maxItems}),status:'ok',messages:[]};
    }
   }catch(error){if(error.name==='AbortError')throw error;result={outputs:Object.fromEntries(def.outputs.map(p=>[p.name,DataTree.empty()])),status:error.blocked?'blocked':'error',messages:[String(error.message??error)]};}
   result=Object.freeze({...result,durationMs:performance.now()-nodeStart});results.set(nodeId,result);evaluated++;
  }
  if(cancelled())throw abortError();const stale=revision!==this.#revision;
  const result=Object.freeze({revision,stale,nodes:results,durationMs:performance.now()-start,evaluated,cached,errors:[...results].filter(([,r])=>r.status==='error'||r.status==='blocked').map(([nodeId,r])=>({nodeId,...r}))});
  if(!stale){this.#cache=results;this.#dirty.clear();this.lastResult=result;this.solved.emit(result);}return result;
 }
 async #evaluateDefinition(def,node,inputs,context){
  const outputBranches=Object.fromEntries(def.outputs.map(p=>[p.name,new Map()]));let outputCount=0;
  const add=(port,path,value)=>{if(value instanceof DataTree){for(const branch of value.branches){if(!outputBranches[port.name].has(branch.path.key))outputBranches[port.name].set(branch.path.key,{path:branch.path,items:[]});for(const item of branch.items)add(port,branch.path,item);}return;}validateValue(value,port.type,port.name);const branches=outputBranches[port.name];let b=branches.get(path.key);if(!b){b={path,items:[]};branches.set(path.key,b);}b.items.push(value);if(++outputCount>context.maxItems)throw new RangeError('Component output exceeds item budget.');};
  const paths=new Map();for(const port of def.inputs)if(port.access!=='tree')for(const b of inputs[port.name].branches)paths.set(b.path.key,b.path);if(!paths.size)paths.set('0',new Path([0]));
  const onlyTrees=def.inputs.every(p=>p.access==='tree');const pathsToSolve=onlyTrees?[new Path([0])]:[...paths.values()].sort(Path.compare);
  for(const path of pathsToSolve){const arrays=[],itemPorts=[],fixed={};let missing=false;
   for(const port of def.inputs){const tree=inputs[port.name];if(port.access==='tree'){fixed[port.name]=tree;continue;}const items=tree.get(path)??(tree.branchCount===1?tree.branches[0].items:[]);if(port.access==='list')fixed[port.name]=items;else{if(!items.length&&port.optional){fixed[port.name]=undefined;continue;}if(!items.length)missing=true;itemPorts.push(port);arrays.push(items);}}
   if(missing)continue;const matches=matchItems(arrays,node.matching,context.maxItems);
   for(const row of matches){if(context.cancelled())throw abortError();const values={...fixed};itemPorts.forEach((p,i)=>values[p.name]=row[i]);const solved=await def.solve(values,node.parameters,{...context,path,node});if(!solved||typeof solved!=='object')throw new TypeError('Component must return an output object.');for(const port of def.outputs){if(!Object.hasOwn(solved,port.name))throw new Error(`Component did not produce ${port.name}.`);const value=solved[port.name];if(port.access==='list'&&!(value instanceof DataTree)){if(!Array.isArray(value))throw new TypeError(`Output ${port.name} must be a list.`);if(!outputBranches[port.name].has(path.key))outputBranches[port.name].set(path.key,{path,items:[]});for(const item of value)add(port,path,item);}else add(port,path,value);}}
  }
  return Object.fromEntries(def.outputs.map(p=>[p.name,new DataTree(outputBranches[p.name].values())]));
 }
 cancel(){this.#solveGeneration++;}
 invalidateAll(){for(const id of this.#nodes.keys())this.#dirty.add(id);}
 toJSON(){return{format:'nurbsscene.graph',version:1,nodes:this.nodes,wires:this.wires,groups:this.groups,view:this.view};}
 static fromJSON(registry,value,options={}){if(!value||value.format!=='nurbsscene.graph'||value.version!==1||!Array.isArray(value.nodes)||!Array.isArray(value.wires))throw new TypeError('Unsupported graph format.');const graph=new Graph(registry,options);for(const n of value.nodes)graph.addNode(n.component,n);for(const w of value.wires)graph.connect(w.from.node,w.from.port,w.to.node,w.to.port,w);for(const g of value.groups??[])graph.addGroup(g.nodes,g);if(value.view&&[value.view.x,value.view.y,value.view.zoom].every(Number.isFinite)&&value.view.zoom>0)graph.view={...value.view};return graph;}
}
export class GraphHistory {
 constructor(graph,{capacity=100}={}){this.graph=graph;this.capacity=capacity;this.undoStack=[];this.redoStack=[];}
 transaction(name,action){const before=jsonClone(this.graph.toJSON());try{const result=action(this.graph);if(result&&typeof result.then==='function')throw new TypeError('Graph transactions must be synchronous.');this.undoStack.push({name,before,after:jsonClone(this.graph.toJSON())});if(this.undoStack.length>this.capacity)this.undoStack.shift();this.redoStack=[];return result;}catch(error){this.graph=Graph.fromJSON(this.graph.registry,before);throw error;}}
 undo(){const entry=this.undoStack.pop();if(!entry)return false;this.redoStack.push(entry);this.graph=Graph.fromJSON(this.graph.registry,entry.before);return true;}
 redo(){const entry=this.redoStack.pop();if(!entry)return false;this.undoStack.push(entry);this.graph=Graph.fromJSON(this.graph.registry,entry.after);return true;}
}
