/** Produce independently installable, versioned npm archives without publishing. */
import {spawnSync} from 'node:child_process';
import {mkdirSync,readdirSync} from 'node:fs';
import {resolve,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const output=resolve(process.argv[2]??resolve(root,'artifacts/npm'));
mkdirSync(output,{recursive:true});
for(const name of readdirSync(resolve(root,'packages'))){
 const result=spawnSync('npm',['pack','--ignore-scripts','--offline','--pack-destination',output],{cwd:resolve(root,'packages',name),stdio:'inherit'});
 if(result.status!==0)process.exit(result.status??1);
}
