"""Opt-in pinned medical ontology tests. Raw and normalized datasets are never published."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
BUILTIN = ROOT / 'tests/Iryomera.MedicalImporter.Integration/Datasets'


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def prepare(case_path, data_directory, download):
    descriptor = json.loads(case_path.read_text(encoding='utf-8'))
    data_directory = data_directory.resolve()
    source = data_directory / descriptor['input']
    if not source.resolve().is_relative_to(data_directory):
        raise ValueError('Dataset input must stay inside the supplied data directory.')
    if not source.exists():
        # Only reviewed built-in ontology descriptors can opt into network acquisition.
        # Custom manifests/BfArM/LOINC/HPOA can never obtain download permission via JSON.
        if not download or case_path.resolve().parent != BUILTIN.resolve() or descriptor['options']['source'] not in ('hpo', 'maxo'):
            raise FileNotFoundError('Supply a legitimately acquired local input: ' + str(source))
        source.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=source.parent, delete=False) as temporary:
            temporary_path = Path(temporary.name)
        try:
            with urllib.request.urlopen(descriptor['options']['sourceUrl'], timeout=90) as response, temporary_path.open('wb') as output:
                shutil.copyfileobj(response, output)
            if sha256(temporary_path) != descriptor['sha256']:
                raise ValueError('Downloaded dataset checksum mismatch.')
            temporary_path.replace(source)
        finally:
            temporary_path.unlink(missing_ok=True)
    if sha256(source) != descriptor['sha256']:
        raise ValueError('Local dataset checksum mismatch: ' + str(source))
    descriptor['input'] = str(source)
    if 'licenseFile' in descriptor:
        license_path = (case_path.parent / descriptor.pop('licenseFile')).resolve()
        descriptor['options']['licenseText'] = license_path.read_text(encoding='utf-8')
    return descriptor


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--case', type=Path, action='append', help='Local descriptor; default is all four pinned ontology cases')
    parser.add_argument('--data-dir', type=Path, required=True, help='Private source-data directory, outside tracked files')
    parser.add_argument('--output', type=Path, default=ROOT/'artifacts/full-datasets')
    parser.add_argument('--download-open-ontologies', action='store_true', help='Download only the reviewed public HPO/MAxO ontology releases; no annotations')
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for case in args.case or sorted(BUILTIN.glob('*.json')):
        descriptor = prepare(case, args.data_dir, args.download_open_ontologies)
        with tempfile.TemporaryDirectory(prefix='iryomera-dataset-case-') as temporary:
            configuration = Path(temporary)/'case.json'
            configuration.write_text(json.dumps(descriptor, ensure_ascii=False), encoding='utf-8')
            subprocess.run([args.dotnet, str(ROOT/'tests/Iryomera.MedicalImporter.Integration/bin/Release/net10.0/Iryomera.MedicalImporter.Integration.dll'),
                            str(configuration), str(args.output/(case.stem+'.json'))], check=True, cwd=ROOT, timeout=600)


if __name__ == '__main__':
    main()
