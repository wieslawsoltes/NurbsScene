"""One-time checksum-verified source transfer for this repository branch.
Only explicitly manifested paths are modified. No archive code is executed.
"""
from pathlib import Path, PurePosixPath
import base64,hashlib,io,json,os,subprocess,tarfile
root=Path.cwd(); staging=root/'.source-import'
plan=json.loads((staging/'plan.json').read_text())
if plan['version']!=1:raise SystemExit('Unsupported transfer format')
if os.environ.get('GITHUB_REPOSITORY')!='wieslawsoltes/NurbsScene' or os.environ.get('GITHUB_REF')!='refs/heads/feat/parity-kernel':raise SystemExit('Unexpected publication target')
sha=lambda b:hashlib.sha256(b).hexdigest()
def git(*args):return subprocess.check_output(['git',*args],text=True).strip()
git('merge-base','--is-ancestor',plan['baseCommit'],'HEAD')
def path(name):
 p=PurePosixPath(name)
 if p.is_absolute() or any(part in ('..','.git','.github','.source-import') for part in p.parts) or '\\' in name:raise ValueError('Unsafe source path: '+name)
 target=root.joinpath(*p.parts)
 if any(parent.is_symlink() for parent in [target,*target.parents] if parent!=root.parent):raise ValueError('Symlink source path')
 if not target.resolve().is_relative_to(root):raise ValueError('Path outside checkout')
 return target
changed=plan.get('changedFiles',{});new=plan.get('newFiles',{})
if set(changed)&set(new):raise ValueError('Duplicate source targets')
for name,hashes in changed.items():
 p=path(name);actual=sha(p.read_bytes()) if p.is_file() else None
 if actual!=hashes['before']:raise ValueError('Pre-patch source mismatch: '+name)
if changed:
 patch=(staging/'changes.patch').read_bytes()
 if sha(patch)!=plan['patchSha256']:raise ValueError('Patch hash mismatch')
 git('apply','--check','--index',str(staging/'changes.patch'))
 git('apply','--index',str(staging/'changes.patch'))
 if set(git('diff','--cached','--name-only').splitlines())!=set(changed):raise ValueError('Patch changed unlisted paths')
if new:
 parts=sorted((staging/'chunks').glob('*.b64'))
 if len(parts)!=plan['archiveParts']:raise ValueError('Incomplete source archive')
 archive=base64.b64decode(''.join(p.read_text().strip() for p in parts),validate=True)
 if sha(archive)!=plan['archiveSha256']:raise ValueError('Archive checksum mismatch')
 prepared={}
 with tarfile.open(fileobj=io.BytesIO(archive),mode='r:xz') as tar:
  members=tar.getmembers()
  if len(members)!=len(new) or sum(m.size for m in members)>8*1024*1024:raise ValueError('Archive size/member budget')
  for member in members:
   if not member.isfile() or member.name not in new or member.name in prepared:raise ValueError('Unlisted archive member')
   p=path(member.name)
   if p.exists():raise ValueError('Refusing to replace existing source: '+member.name)
   data=tar.extractfile(member).read()
   if sha(data)!=new[member.name]:raise ValueError('Member checksum mismatch: '+member.name)
   prepared[member.name]=(p,data,member.mode)
 for name,(p,data,mode) in prepared.items():
  p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data);p.chmod(0o755 if mode&0o111 else 0o644)
  git('add','--',name)
for name,hashes in changed.items():
 if sha(path(name).read_bytes())!=hashes['after']:raise ValueError('Post-patch mismatch: '+name)
expected={**new,**{p:h['after'] for p,h in changed.items()}}
for name,digest in expected.items():
 if sha(path(name).read_bytes())!=digest:raise ValueError('Final source mismatch: '+name)
# Do not add untracked compiler outputs, local runner files or credentials.
if set(git('diff','--cached','--name-only').splitlines())!=set(expected):raise ValueError('Unexpected staged paths')
print(json.dumps({'verifiedFiles':len(expected),'sha256':expected},indent=2))
