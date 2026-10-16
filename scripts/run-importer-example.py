"""Run the real CLI against our synthetic MIT fixtures; never use for clinical approval."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet', help='dotnet executable')
parser.add_argument('--tool', help='Optional installed iryomera-medical executable instead of dotnet run')
parser.add_argument('--output', type=Path, help='New demo directory; default is a temporary directory')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
fixtures = root / 'tests/Iryomera.MedicalImporter.Tests/Fixtures'
base = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix='iryomera-synthetic-import-'))
if args.output:
    base.mkdir(parents=True, exist_ok=False)
workspace = base / 'workspace.json'
command = [args.tool] if args.tool else [args.dotnet, 'run', '--project', str(root/'src/Iryomera.MedicalImporter'), '-c', 'Release', '--no-build', '--']

def run(*arguments):
    subprocess.run(command + list(arguments), cwd=root, check=True)

run('sources', 'list')
for source, filename in [('hpo','hpo.json'), ('hpo-annotations','hpoa.tsv'), ('maxo','maxo.obo'), ('maxo-annotations','maxo-annotations.tsv')]:
    run('import', source, '--input', str(fixtures/filename), '--source-version', 'synthetic-1',
        '--source-url', 'https://example.org/iryomera/synthetic/'+source,
        '--license', 'MIT', '--license-file', str(fixtures/'LICENSE.txt'),
        '--attribution', 'Iryomera own synthetic fixtures; not medical knowledge.',
        '--imported-at', '2026-01-01T00:00:00Z', '--namespace', 'synthetic_import', '--workspace', str(workspace))
run('validate', '--workspace', str(workspace))
run('matches', '--workspace', str(workspace))
for batch in json.loads(workspace.read_text())['batches']:
    run('review', '--batch', batch['id'], '--all', '--status', 'approved', '--reviewer', 'Synthetic demo reviewer',
        '--at', '2026-01-02T00:00:00Z', '--note', 'Fictional software fixture only; no clinical approval.', '--workspace', str(workspace))
    run('rights', '--batch', batch['id'], '--decision', 'permitted', '--reviewer', 'Synthetic demo rights reviewer',
        '--at', '2026-01-02T00:00:00Z', '--note', 'Own MIT-licensed synthetic fixtures only.', '--workspace', str(workspace))
run('export', '--workspace', str(workspace), '--output', str(base/'release'), '--require-languages', 'en')
print('Synthetic demo workspace:', workspace)
print('Validated synthetic release:', base/'release')
