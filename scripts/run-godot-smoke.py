"""Test the actual packed net8.0 libraries in a pinned official Godot .NET desktop build."""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import shutil
import stat
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--godot', type=Path, help='Use an existing Godot 4.7.2 .NET executable')
parser.add_argument('--cache', type=Path, default=Path(tempfile.gettempdir())/'iryomera-godot-cache')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
version = '4.7.2'
package_version = ET.parse(root/'Directory.Build.props').getroot().find('.//Version').text
assert (root/'artifacts'/('Iryomera.Content.SDK.'+package_version+'.nupkg')).is_file(), 'Pack the solution first.'
assert (root/'artifacts'/('Iryomera.Localization.'+package_version+'.nupkg')).is_file(), 'Pack the solution first.'

def download(url, target):
    with urllib.request.urlopen(url, timeout=90) as response, target.open('wb') as output:
        shutil.copyfileobj(response, output)

if args.godot:
    executable = args.godot.resolve()
else:
    target = {'Linux':'linux_x86_64', 'Windows':'win64'}.get(platform.system())
    if not target or platform.machine().lower() not in ('x86_64','amd64'):
        raise SystemExit('The pinned smoke download supports Linux/Windows x64; supply --godot for another compatible host.')
    name = 'Godot_v'+version+'-stable_mono_'+target
    cache = args.cache.resolve(); cache.mkdir(parents=True, exist_ok=True)
    archive = cache/(name+'.zip')
    base = 'https://github.com/godotengine/godot-builds/releases/download/'+version+'-stable/'
    sums = cache/('SHA512-SUMS-'+version+'.txt')
    download(base+'SHA512-SUMS.txt', sums)
    expected = next(line.split()[0] for line in sums.read_text().splitlines() if line.split()[-1].lstrip('*') == archive.name)
    if not archive.exists() or hashlib.sha512(archive.read_bytes()).hexdigest() != expected:
        download(base+archive.name, archive)
    if hashlib.sha512(archive.read_bytes()).hexdigest() != expected:
        raise SystemExit('Official Godot archive checksum mismatch.')
    extracted = cache/name
    if extracted.exists(): shutil.rmtree(extracted)
    extracted.mkdir()
    with zipfile.ZipFile(archive) as package:
        if sum(entry.file_size for entry in package.infolist()) > 1024**3:
            raise SystemExit('Unexpectedly large Godot archive.')
        for entry in package.infolist():
            if not (extracted/entry.filename).resolve().is_relative_to(extracted):
                raise SystemExit('Unexpected archive path.')
        package.extractall(extracted)
    candidates = list(extracted.rglob('Godot*'))
    executable = next(p for p in candidates if p.is_file() and (p.name.endswith('.x86_64') if target.startswith('linux') else p.name.endswith('_console.exe')))
    if platform.system() != 'Windows': executable.chmod(executable.stat().st_mode | stat.S_IXUSR)

with tempfile.TemporaryDirectory(prefix='iryomera-godot-smoke-') as temporary:
    project = Path(temporary)/'project'
    shutil.copytree(root/'tests/Iryomera.Godot.Integration', project, ignore=shutil.ignore_patterns('bin','obj','.godot'))
    for name in ['annotations','en','de','diagnostics','diagnostics.en','diagnostics.de','names']:
        shutil.copyfile(root/'examples/synthetic'/(name+'.json'), project/(name+'.json'))
    shutil.copyfile(root/'examples/modkit/example-mod/content.json', project/'mod-content.json')
    for language in ['en','de']:
        shutil.copyfile(root/'examples/modkit/example-mod/localization'/(language+'.json'), project/('mod-'+language+'.json'))
    dotnet = [args.dotnet]
    project_file = project/'Iryomera.Godot.Integration.csproj'
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources'); ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='foundation', value=str(root/'artifacts'))
    ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
    mappings = ET.SubElement(config, 'packageSourceMapping')
    local = ET.SubElement(mappings, 'packageSource', key='foundation'); ET.SubElement(local, 'package', pattern='Iryomera.*')
    public = ET.SubElement(mappings, 'packageSource', key='nuget.org'); ET.SubElement(public, 'package', pattern='*')
    configuration_file = project/'NuGet.Config'; ET.ElementTree(config).write(configuration_file, encoding='utf-8', xml_declaration=True)
    environment = dict(os.environ)
    dotnet_path = Path(shutil.which(args.dotnet) or args.dotnet).resolve().parent
    environment.setdefault('DOTNET_ROOT', str(dotnet_path))
    environment['PATH'] = str(dotnet_path) + os.pathsep + environment.get('PATH', '')
    environment['GODOT_SILENCE_ROOT_WARNING'] = '1'

    subprocess.run(dotnet+['restore',str(project_file),'--configfile',str(configuration_file),
        '-p:FoundationPackageVersion='+package_version,'--packages',str(Path(temporary)/'packages')],check=True,env=environment,timeout=180)
    subprocess.run(dotnet+['build',str(project_file),'-c','Debug','--no-restore','-p:FoundationPackageVersion='+package_version,
        '-p:UseSharedCompilation=false','-m:1','-nr:false'],check=True,env=environment,timeout=180)
    editor = subprocess.run([str(executable),'--headless','--editor','--path',str(project),'--import','--quit'],text=True,capture_output=True,env=environment,timeout=120)
    print(editor.stdout, end=''); print(editor.stderr, end='')
    if editor.returncode != 0 or 'ERROR:' in editor.stdout + editor.stderr:
        raise SystemExit('Godot editor import failed.')
    # The editor/MSBuild can use .NET 10. Only the actual game must prove the .NET 8 minimum.
    game_environment = dict(environment)
    game_environment['DOTNET_ROLL_FORWARD'] = 'Minor'
    result = subprocess.run([str(executable),'--headless','--path',str(project)],text=True,capture_output=True,env=game_environment,timeout=90)
    print(result.stdout, end=''); print(result.stderr, end='')
    if result.returncode != 0 or 'IRYOMERA_GODOT_SMOKE_OK' not in result.stdout or 'ERROR:' in result.stdout + result.stderr:
        raise SystemExit('Godot integration failed (exit '+str(result.returncode)+').')
