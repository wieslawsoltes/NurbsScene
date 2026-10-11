"""One-time manifest-verified recovery of previously transferred source.
The first transport is a known incomplete compressed prefix, not an executable
archive. Only the explicitly listed complete tar records may be recovered.
Every destination byte is independently SHA256-checked before any file is written.
"""
from pathlib import Path, PurePosixPath
import base64, hashlib, io, json, lzma, os, subprocess, tarfile

root = Path.cwd().resolve()
staging = root / '.source-import'
plan = json.loads((staging / 'plan.json').read_text())
if plan['version'] != 2:
    raise SystemExit('Unsupported transfer manifest')
if os.environ.get('GITHUB_REPOSITORY') != 'wieslawsoltes/NurbsScene' or os.environ.get('GITHUB_REF') != 'refs/heads/feat/parity-kernel':
    raise SystemExit('Unexpected source target')

def git(*args):
    return subprocess.check_output(['git', *args], text=True).strip()

def digest(data):
    return hashlib.sha256(data).hexdigest()

git('merge-base', '--is-ancestor', plan['baseCommit'], 'HEAD')
if git('diff', '--cached', '--name-only'):
    raise ValueError('Expected a clean index')

def destination(name):
    p = PurePosixPath(name)
    if p.is_absolute() or '\\' in name or any(x in ('..', '.', '.git', '.github', '.source-import') for x in p.parts):
        raise ValueError('Unsafe source path: ' + name)
    target = root.joinpath(*p.parts)
    if not target.resolve().is_relative_to(root):
        raise ValueError('Path outside checkout')
    check = target
    while check != root:
        if check.is_symlink():
            raise ValueError('Refusing symlink target')
        check = check.parent
    if target.exists():
        raise ValueError('Refusing to replace existing source: ' + name)
    return target

def transport(spec):
    pieces = []
    for name in spec['parts']:
        if Path(name).name != name or not name.endswith('.b64'):
            raise ValueError('Invalid chunk name')
        pieces.append((staging / 'chunks' / name).read_text().strip())
    data = base64.b64decode(''.join(pieces), validate=True)
    if digest(data) != spec['sha256']:
        raise ValueError('Compressed transport checksum mismatch')
    return data

prepared = {}
def retain(member, data, name, expected):
    if not member.isfile() or len(data) != member.size or member.size > 2 * 1024 * 1024:
        raise ValueError('Invalid source member')
    if name in prepared or digest(data) != expected:
        raise ValueError('Duplicate source or SHA256 mismatch: ' + name)
    prepared[name] = (destination(name), data, member.mode)

prefix = plan['prefix']
decoder = lzma.LZMADecompressor(memlimit=128 * 1024 * 1024)
raw = decoder.decompress(transport(prefix), max_length=8 * 1024 * 1024)
if decoder.eof or digest(raw) != prefix['decodedSha256']:
    raise ValueError('Unexpected recovered prefix bytes')
# Parse only the known complete records, never the truncated following record.
with tarfile.open(fileobj=io.BytesIO(raw), mode='r:') as archive:
    seen = set()
    for _ in range(len(prefix['files'])):
        member = archive.next()
        if member is None or member.name not in prefix['files'] or member.name in seen:
            raise ValueError('Prefix record not listed in manifest')
        seen.add(member.name)
        if not member.isfile():
            raise ValueError('Only regular source files are accepted')
        metadata = prefix['files'][member.name]
        retain(member, archive.extractfile(member).read(), metadata['path'], metadata['sha256'])
    if seen != set(prefix['files']):
        raise ValueError('Incomplete prefix source set')

remaining = plan['archive']
with tarfile.open(fileobj=io.BytesIO(transport(remaining)), mode='r:xz') as archive:
    members = archive.getmembers()
    if len(members) != len(remaining['files']) or sum(m.size for m in members) > 8 * 1024 * 1024:
        raise ValueError('Unexpected tail member count or size')
    for member in members:
        if not member.isfile() or member.name not in remaining['files']:
            raise ValueError('Unlisted tail source file')
        retain(member, archive.extractfile(member).read(), member.name, remaining['files'][member.name])

# Everything above is read-only. Only verified, independently checked source
# reaches the working tree, never build outputs, tokens or runner files.
for name, (target, data, mode) in prepared.items():
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    target.chmod(0o755 if mode & 0o111 else 0o644)
    git('add', '--', name)
if set(git('diff', '--cached', '--name-only').splitlines()) != set(prepared):
    raise ValueError('Unexpected staged paths')
for name, (target, data, _) in prepared.items():
    if digest(target.read_bytes()) != digest(data):
        raise ValueError('Final source mismatch: ' + name)
print(json.dumps({'verifiedFiles': len(prepared), 'sha256': {name: digest(data) for name, (_, data, _) in prepared.items()}}, indent=2))
