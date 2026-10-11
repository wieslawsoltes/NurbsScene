"""Apply one explicitly manifested source patch; no generated files or other paths."""
from pathlib import Path, PurePosixPath
import base64, hashlib, json, lzma, os, subprocess
root=Path.cwd().resolve();staging=root/'.source-import'
plan=json.loads((staging/'plan.json').read_text())
if plan['version']!=3 or os.environ.get('GITHUB_REPOSITORY')!='wieslawsoltes/NurbsScene' or os.environ.get('GITHUB_REF')!='refs/heads/feat/parity-kernel':
    raise SystemExit('Unexpected transfer or target')
def git(*args):return subprocess.check_output(['git',*args],text=True).strip()
def sha(data):return hashlib.sha256(data).hexdigest()
git('merge-base','--is-ancestor',plan['baseCommit'],'HEAD')
if git('diff','--cached','--name-only'):raise ValueError('Index is not clean')
for name,hashes in plan['files'].items():
    p=PurePosixPath(name)
    if p.is_absolute() or '\\' in name or any(x in ('..','.git','.github','.source-import') for x in p.parts):raise ValueError('Unsafe path')
    target=root.joinpath(*p.parts)
    if not target.resolve().is_relative_to(root) or target.is_symlink():raise ValueError('Unsafe target')
    if (sha(target.read_bytes()) if target.exists() else None)!=hashes['before']:raise ValueError('Pre-patch mismatch: '+name)
parts=plan['parts']
if any(Path(name).name!=name or not name.endswith('.b64') for name in parts):raise ValueError('Invalid chunk name')
compressed=base64.b64decode(''.join((staging/'chunks'/name).read_text().strip() for name in parts),validate=True)
if sha(compressed)!=plan['compressedSha256']:raise ValueError('Compressed patch checksum mismatch')
decoder=lzma.LZMADecompressor(memlimit=128*1024*1024)
content=decoder.decompress(compressed,max_length=4*1024*1024)
if not decoder.eof or decoder.unused_data or sha(content)!=plan['patchSha256']:raise ValueError('Patch checksum mismatch')
patch=staging/'changes.patch';patch.write_bytes(content)
git('apply','--check','--index',str(patch))
git('apply','--index',str(patch))
if set(git('diff','--cached','--name-only').splitlines())!=set(plan['files']):raise ValueError('Unexpected patched path')
for name,hashes in plan['files'].items():
    if sha((root/name).read_bytes())!=hashes['after']:raise ValueError('Post-patch mismatch: '+name)
print(json.dumps({'verifiedFiles':len(plan['files']),'sha256':{k:v['after'] for k,v in plan['files'].items()}},indent=2))
