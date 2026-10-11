"""Copy only actual dotnet-published Uno files. Never deploy the JS harness as Uno."""
from pathlib import Path
import argparse,shutil
p=argparse.ArgumentParser();p.add_argument('--source',default='src/NurbsScene.App/bin/Release/net10.0-browserwasm/publish');p.add_argument('--output',default='artifacts/site');a=p.parse_args()
source=Path(a.source);candidates=[source/'wwwroot',source]
root=next((d for d in candidates if (d/'index.html').is_file() and any(d.rglob('*.wasm'))),None)
if root is None:raise SystemExit('No actual Uno index.html + WebAssembly publication found. Build is required; no harness substitution.')
dest=Path(a.output)
if dest.resolve()==root.resolve() or root.resolve() in dest.resolve().parents:raise SystemExit('Output must be separate from publish source.')
if dest.exists():shutil.rmtree(dest)
shutil.copytree(root,dest);(dest/'.nojekyll').touch()
print(f'Staged actual Uno publication: {root} -> {dest}')
