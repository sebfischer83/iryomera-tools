"""Real offline CLI round trip with own synthetic data/assets, no game runtime."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet',default='dotnet')
args=parser.parse_args()
cli=[args.dotnet,str(root/'src/Iryomera.ModKit/bin/Release/net10.0/Iryomera.ModKit.dll')]
def run(*arguments):subprocess.run(cli+list(map(str,arguments)),check=True,cwd=root)
with tempfile.TemporaryDirectory(prefix='iryomera-modkit-example-') as temporary:
 temporary=Path(temporary); project=temporary/'authoring'; archive=temporary/'example.zip'; second=temporary/'repeat.zip'; unpacked=temporary/'unpacked'
 run('new','--output',project,'--namespace','example.mod','--author','Own synthetic fixture author','--name','Synthetic CLI example')
 run('validate','--input',project,'--game-version','0.2.0')
 replacement=temporary/'en.json'; english=json.loads((project/'localization/en.json').read_text(encoding='utf-8')); english['entries']['example.mod.mod.name']='Edited synthetic CLI example';replacement.write_text(json.dumps(english),encoding='utf-8')
 run('edit','--input',project,'--path','localization/en.json','--replacement',replacement)
 run('pack','--input',project,'--output',archive)
 run('pack','--input',project,'--output',second)
 assert hashlib.sha256(archive.read_bytes()).digest()==hashlib.sha256(second.read_bytes()).digest()
 run('inspect','--input',archive)
 run('unpack','--input',archive,'--output',unpacked)
 run('validate','--input',unpacked)
 for item in json.loads((project/'mod.json').read_text(encoding='utf-8'))['files']:
  assert (project/item['path']).read_bytes()==(unpacked/item['path']).read_bytes()
 with zipfile.ZipFile(archive) as zipped:
  assert 'checksums.json' in zipped.namelist()
 print('IRYOMERA_MODKIT_SMOKE_OK: new/edit/validate/pack/inspect/unpack, en/de, deterministic ZIP and exact declared bytes.')
