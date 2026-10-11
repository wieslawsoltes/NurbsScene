"""Structural checks only; XML/import/path validation is NOT C# compilation."""
import json,re,subprocess,sys,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
errors=[];projects=list((ROOT/'src').rglob('*.csproj'))+list((ROOT/'tests/dotnet').rglob('*.csproj'))
for path in [*projects,ROOT/'Directory.Build.props',ROOT/'NurbsScene.slnx',*(ROOT/'src').rglob('*.xaml')]:
 try:tree=ET.parse(path)
 except ET.ParseError as e:errors.append(f'{path.relative_to(ROOT)}: {e}');continue
 for item in tree.findall('.//ProjectReference'):
  if not(path.parent/item.attrib['Include']).is_file():errors.append('Missing project reference '+str(path.parent/item.attrib['Include']))
solution=ET.parse(ROOT/'NurbsScene.slnx').getroot()
listed=[n.attrib['Path'] for n in solution.findall('Project')]
if len(listed)!=len(set(listed)):errors.append('Duplicate solution project')
for p in projects:
 if p.relative_to(ROOT).as_posix() not in listed:errors.append('Unlisted project '+str(p))
for path in (ROOT/'packages').rglob('*.js'):
 run=subprocess.run(['node','--check',str(path)],capture_output=True,text=True)
 if run.returncode:errors.append(run.stderr)
 for module in re.findall(r'(?:from\s*|import\s*)[\'"](\.[^\'"]+)[\'"]',path.read_text()):
  if not(path.parent/module).is_file():errors.append(f'Missing JS import: {path.relative_to(ROOT)} -> {module}')
for path in (ROOT/'packages').rglob('package.json'):json.loads(path.read_text())
if errors:print('\n'.join(errors),file=sys.stderr);sys.exit(1)
print(f'PASS structural checks: {len(projects)} project files, XAML/XML, ESM syntax/import paths. C# was not compiled.')
