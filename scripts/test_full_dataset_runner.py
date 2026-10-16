"""Offline tests for full-dataset acquisition boundaries; never contacts a network."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('full_datasets', Path(__file__).with_name('run-full-dataset-tests.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class FullDatasetRunnerTests(unittest.TestCase):
    def test_custom_restricted_manifest_cannot_enable_download(self):
        with tempfile.TemporaryDirectory() as temporary:
            case = Path(temporary)/'loinc.json'
            case.write_text(json.dumps({'input':'Loinc.csv', 'options':{'source':'loinc','sourceUrl':'https://example.org/restricted'}}))
            with patch.object(runner.urllib.request, 'urlopen') as download:
                with self.assertRaises(FileNotFoundError):
                    runner.prepare(case, Path(temporary), True)
                download.assert_not_called()

    def test_existing_file_must_match_the_pin(self):
        with tempfile.TemporaryDirectory() as temporary:
            case = Path(temporary)/'case.json'
            case.write_text(json.dumps({'input':'data.json','sha256':'0'*64}))
            (Path(temporary)/'data.json').write_text('synthetic')
            with self.assertRaises(ValueError):
                runner.prepare(case, Path(temporary), False)

    def test_input_path_cannot_escape_private_data_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            case = Path(temporary)/'case.json'
            case.write_text(json.dumps({'input':'../outside.json'}))
            with self.assertRaises(ValueError):
                runner.prepare(case, Path(temporary), True)

    def test_builtin_descriptors_pin_counts_errors_and_normalized_output(self):
        cases = list(runner.BUILTIN.glob('*.json'))
        self.assertEqual(4, len(cases))
        for case in cases:
            value = json.loads(case.read_text())
            self.assertEqual(64, len(value['sha256']))
            self.assertEqual(64, len(value['expectedNormalizedSha256']))
            self.assertGreater(value['expectedTerms'], 0)
            self.assertGreater(value['expectedAssertions'], 0)
            self.assertIsInstance(value['expectedErrors'], dict)
            self.assertTrue((case.parent/value['licenseFile']).is_file())


if __name__ == '__main__':
    unittest.main()
