#!/usr/bin/env python3
"""Bundle this repository's deliberately limited ESM syntax for offline validation.
No network access and no security-policy changes. Not a general JavaScript bundler.
The browser still requests actual GPU contexts and reports capability failures.
"""
from pathlib import Path
import re,json,base64
ROOT=Path(__file__).resolve().parent.parent
seen=set();pieces=[]
imports=re.compile(r'\bimport\s*(?:\{([^}]+)\}\s*from\s*)?[\'\"]([^\'\"]+)[\'\"];')
reexports=re.compile(r'\bexport\s*\{([^}]+)\}\s*from\s*[\'\"]([^\'\"]+)[\'\"];')
namedexports=re.compile(r'\bexport\s*\{([^}]+)\};')
def names(text):
 return [n.strip() for n in text.split(',') if n.strip()]
def pairs(text):
 out=[]
 for x in names(text):
  n=x.split(' as ');out.append((n[0],n[-1]))
 return out
def resolve_module(parent,specifier):
 if specifier.startswith('@nurbsscene/'):
  return ROOT/'packages'/specifier.removeprefix('@nurbsscene/')
 return parent/specifier

def module(path):
 path=path.resolve();key=path.relative_to(ROOT).as_posix()
 if key in seen:return key
 seen.add(key);source=path.read_text();exports={};pre=[]
 def import_match(m):
  other=module(resolve_module(path.parent,m.group(2)))
  if m.group(1):return 'const {'+','.join(a if a==b else a+':'+b for a,b in pairs(m.group(1)))+'}=__modules['+json.dumps(other)+'];'
  return ''
 source=imports.sub(import_match,source)
 def rex(m):
  other=module(resolve_module(path.parent,m.group(2)))
  for a,b in pairs(m.group(1)):exports[b]='__modules['+json.dumps(other)+']['+json.dumps(a)+']'
  return ''
 source=reexports.sub(rex,source)
 def nex(m):
  for a,b in pairs(m.group(1)):exports[b]=a
  return ''
 source=namedexports.sub(nex,source)
 def declare(m):
  exports[m.group(3)]=m.group(3)
  return (m.group(1) or '')+m.group(2)+' '+m.group(3)
 source=re.sub(r'\bexport\s+(async\s+)?(class|function|const|let)\s+(\w+)',declare,source)
 source=source.replace('import.meta.url',json.dumps('https://offline.invalid/'+key))
 if re.search(r'^\s*(?:import|export)\b',source,re.M):raise ValueError('Unsupported ESM syntax in '+key)
 pieces.append('__modules['+json.dumps(key)+']=(()=>{\n'+source+'\nreturn {'+','.join(json.dumps(k)+':'+v for k,v in exports.items())+'};\n})();')
 return key
module(ROOT/'validation-harness/app.js')
wasm=base64.b64encode((ROOT/'packages/geometry/dist/nurbsscene.wasm').read_bytes()).decode()
script='const __modules=Object.create(null);\nglobalThis.NURBSSCENE_OFFLINE_WASM=Uint8Array.from(atob('+json.dumps(wasm)+'),c=>c.charCodeAt(0));\n'+'\n'.join(pieces)
html=(ROOT/'validation-harness/index.html').read_text()
html=re.sub(r'<link rel="stylesheet" href="([^"]+)">',lambda m:'<style>'+(ROOT/'validation-harness'/m.group(1)).resolve().read_text()+'</style>',html)
html=html.replace('<script type="module" src="./app.js"></script>','<script>'+script.replace('</script','<\\/script')+'</script>')
html=html.replace('<title>NurbsScene — browser module validation workbench</title>','<title>NurbsScene — offline browser module validation</title>')
(ROOT/'validation-harness/offline.html').write_text(html)
print(f'Bundled {len(pieces)} modules; {len(html):,} characters. GPU code is unchanged.')
