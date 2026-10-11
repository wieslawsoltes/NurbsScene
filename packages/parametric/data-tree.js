/** Persistent, ordered data trees; no dependency on DOM or a geometry kernel. */
export class Path {
 #indices;
 constructor(indices=[0]){if(!Array.isArray(indices)||indices.length>64||!indices.every(n=>Number.isInteger(n)&&n>=0&&n<=2147483647))throw new TypeError('A path contains at most 64 nonnegative 32-bit indices.');this.#indices=Object.freeze([...indices]);this.key=this.#indices.join(';');Object.freeze(this);}
 get indices(){return this.#indices;}get length(){return this.#indices.length;}
 append(index){return new Path([...this.#indices,index]);}
 toString(){return`{${this.key}}`;}
 equals(other){return other instanceof Path&&this.key===other.key;}
 static parse(text){if(typeof text!=='string'||!/^\{(?:\d+(?:;\d+)*)?\}$/.test(text))throw new TypeError('Expected a path such as {0;2;1}.');return new Path(text.slice(1,-1)?text.slice(1,-1).split(';').map(Number):[]);}
 static compare(a,b){for(let i=0;i<Math.min(a.length,b.length);i++){const d=a.indices[i]-b.indices[i];if(d)return d;}return a.length-b.length;}
}
function immutableValue(value){if(Array.isArray(value))return Object.freeze(value.map(immutableValue));return value;}
export class DataTree {
 #branches;#index;
 constructor(branches=[]){const index=new Map();let itemCount=0;for(const branch of branches){const path=branch.path instanceof Path?branch.path:new Path(branch.path),key=path.key;if(index.has(key))throw new TypeError(`Duplicate branch ${path}.`);const items=Object.freeze(Array.from(branch.items,immutableValue));itemCount+=items.length;if(itemCount>1000000)throw new RangeError('Data tree exceeds 1,000,000 items.');index.set(key,Object.freeze({path,items}));}this.#branches=Object.freeze([...index.values()].sort((a,b)=>Path.compare(a.path,b.path)));this.#index=index;this.itemCount=itemCount;Object.freeze(this);}
 get branchCount(){return this.#branches.length;}get branches(){return this.#branches;}
 get(path){const key=path instanceof Path?path.key:Array.isArray(path)?path.join(';'):path;return this.#index.get(key)?.items;}
 get allItems(){return this.#branches.flatMap(b=>b.items);}
 static item(value,path=[0]){return new DataTree([{path,items:[value]}]);}
 static list(values,path=[0]){return new DataTree([{path,items:values}]);}
 static empty(){return new DataTree();}
 map(fn){return new DataTree(this.#branches.map(b=>({path:b.path,items:b.items.map((item,i)=>fn(item,b.path,i))})));}
 flatten(path=[0]){return DataTree.list(this.allItems,path);}
 graft(){return new DataTree(this.#branches.flatMap(b=>b.items.map((item,i)=>({path:b.path.append(i),items:[item]}))));}
 simplify(){if(!this.branchCount)return this;let common=Math.min(...this.#branches.map(b=>Math.max(0,b.path.length-1)));for(let i=0;i<common;i++)if(!this.#branches.every(b=>b.path.indices[i]===this.#branches[0].path.indices[i])){common=i;break;}return new DataTree(this.#branches.map(b=>({path:new Path(b.path.indices.slice(common)),items:b.items})));}
 reverse(){return new DataTree(this.#branches.map(b=>({path:b.path,items:[...b.items].reverse()})));}
 merge(...trees){const map=new Map();for(const tree of[this,...trees])for(const b of tree.branches){let found=map.get(b.path.key);if(!found){found={path:b.path,items:[]};map.set(b.path.key,found);}found.items.push(...b.items);}return new DataTree(map.values());}
 partition(size){if(!Number.isInteger(size)||size<1)throw new RangeError('Partition size must be a positive integer.');return new DataTree(this.#branches.flatMap(b=>{const parts=[];for(let i=0;i<b.items.length;i+=size)parts.push({path:b.path.append(i/size),items:b.items.slice(i,i+size)});return parts;}));}
 toJSON(encode=x=>x){return this.#branches.map(b=>({path:b.path.indices,items:b.items.map(encode)}));}
 static fromJSON(value,decode=x=>x){if(!Array.isArray(value))throw new TypeError('Tree branch array required.');return new DataTree(value.map(b=>({path:new Path(b.path),items:b.items.map(decode)})));}
}
/** List matching is explicit; unlike Grasshopper, branch matching uses exact
 * paths plus broadcasting of a single branch. No guessed path remapping. */
export function matchItems(arrays,mode='longest',budget=100000){
 if(arrays.some(a=>!a.length))return[];
 if(!arrays.length)return[[]];
 if(mode==='cross'){let rows=[[]];for(const values of arrays){if(rows.length*values.length>budget)throw new RangeError('Cross-reference exceeds the item budget.');rows=rows.flatMap(row=>values.map(v=>[...row,v]));}return rows;}
 if(mode!=='longest'&&mode!=='shortest')throw new TypeError('Matching must be longest, shortest or cross.');
 const count=(mode==='longest'?Math.max:Math.min)(...arrays.map(a=>a.length));if(count>budget)throw new RangeError('Matching exceeds the item budget.');return Array.from({length:count},(_,i)=>arrays.map(values=>values[Math.min(i,values.length-1)]));
}
