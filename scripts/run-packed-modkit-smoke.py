"""Install the locally packed CLI in isolation; prove the Core extraction retained its payload/resources."""
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
root = Path(__file__).resolve().parents[1]
version = ET.parse(root/'Directory.Build.props').getroot().find('.//Version').text
assert (root/'artifacts'/f'Iryomera.ModKit.{version}.nupkg').is_file(), 'Pack the solution first.'
with tempfile.TemporaryDirectory(prefix='iryomera-packed-modkit-') as temporary:
    directory = Path(temporary)
    config = ET.Element('configuration')
    sources = ET.SubElement(config, 'packageSources'); ET.SubElement(sources, 'clear')
    ET.SubElement(sources, 'add', key='local', value=str(root/'artifacts'))
    config_path = directory/'NuGet.Config'; ET.ElementTree(config).write(config_path, encoding='utf-8', xml_declaration=True)
    environment = dict(os.environ); environment['NUGET_PACKAGES'] = str(directory/'packages')
    def run(args): subprocess.run(args, check=True, env=environment, timeout=180)
    run(['dotnet','tool','install','Iryomera.ModKit','--version',version,'--configfile',str(config_path),'--tool-path',str(directory/'tools')])
    executable = directory/'tools'/('iryomera-mod.exe' if os.name == 'nt' else 'iryomera-mod')
    def tool(*args): run([str(executable),*map(str,args)])
    tool('new','--output',directory/'authoring','--namespace','packed.example','--author','Own synthetic package test','--name','Packed example')
    tool('validate','--input',directory/'authoring')
    tool('pack','--input',directory/'authoring','--output',directory/'example.zip')
    tool('inspect','--input',directory/'example.zip')
    tool('unpack','--input',directory/'example.zip','--output',directory/'installed')
    assert (directory/'authoring/content.json').read_bytes() == (directory/'installed/content.json').read_bytes()
    print('IRYOMERA_PACKED_MODKIT_SMOKE_OK: installed CLI, Core resources and exact package roundtrip.')
