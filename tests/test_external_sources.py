"""Release safety checks: sources must exist and match before runtime clearance."""
from pathlib import Path
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
import zipfile


TOOL = Path(__file__).resolve().parents[1] / 'tools/ExternalSources.py'


class SourceDistributionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not TOOL.exists():
            return
        spec = importlib.util.spec_from_file_location('external_sources', TOOL)
        cls.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.module)

    def module_required(self):
        self.assertTrue(TOOL.exists(), 'External source packaging and verification is not implemented')
        return self.module

    def fixture(self, root):
        package = root / 'baseline'
        package.mkdir()
        payload = {
            'Tools/bin/mpv.exe': b'unchanged player',
            'Tools/bin/node.exe': b'unchanged JS runtime',
            'Tools/licenses/example.txt': b'original license',
            'Tools/provenance/example.json': b'{}',
            'Tools/THIRD_PARTY_NOTICES.txt': b'original notices',
            'Tools/Sources/README.txt': b'old source instructions',
            'README.txt': b'mod README',
        }
        for name, data in payload.items():
            path = package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        with zipfile.ZipFile(package / 'Tools/Sources/example.zip', 'w') as z:
            z.writestr('source.c', 'complete original source')
        files = self.module.records(package)
        lock = dict(public_redistribution_cleared=True, tools=[dict(tool='mpv', version='fixture')],
                    source_closure='fixture with full sources', files=[f for f in files if f['path'].startswith('Tools/')])
        (package / 'Tools/bundled-tools.json').write_text(json.dumps(lock), encoding='utf-8')
        manifest = root / 'baseline.json'
        manifest.write_text(json.dumps(dict(version='fixture', public_redistribution_cleared=True,
                                           files=self.module.records(package))), encoding='utf-8')
        return package, manifest

    def test_prepare_preserves_binaries_and_archives_without_granting_clearance(self):
        tool = self.module_required()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, manifest = self.fixture(root)
            out = root / 'candidate'
            tool.prepare(source, manifest, out, 'owner/source-repo', 'tools-fixture')
            self.assertEqual((source/'Tools/bin/mpv.exe').read_bytes(), (out/'package/Tools/bin/mpv.exe').read_bytes())
            self.assertEqual((source/'Tools/Sources/example.zip').read_bytes(), (out/'release/example.zip').read_bytes())
            self.assertFalse((out/'package/Tools/Sources/example.zip').exists())
            self.assertFalse(json.loads((out/'package-manifest.json').read_text(encoding='utf-8'))['public_redistribution_cleared'])
            index = json.loads((out/'release/source-manifest.json').read_text(encoding='utf-8'))
            self.assertTrue(all('/releases/download/tools-fixture/' in a['url'] for a in index['assets']))

    def test_failed_download_never_clears_draft(self):
        tool = self.module_required()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, manifest = self.fixture(root)
            out = root/'candidate'
            tool.prepare(source, manifest, out, 'owner/source-repo', 'tools-fixture')
            def unavailable(*args, **kwargs):
                raise OSError('source is not publicly available')
            with self.assertRaises(OSError):
                tool.verify_and_finalize(out, opener=unavailable)
            self.assertFalse(json.loads((out/'package-manifest.json').read_text(encoding='utf-8'))['public_redistribution_cleared'])
            self.assertFalse((out/'release.zip').exists())

    def test_corruption_or_oversize_is_rejected(self):
        tool = self.module_required()
        expected = dict(bytes=3, sha256=hashlib.sha256(b'abc').hexdigest())
        self.assertEqual(tool.verify_stream(io.BytesIO(b'abc'), expected), expected['sha256'])
        for payload in (b'abd', b'abcd', b'ab'):
            with self.assertRaises(ValueError):
                tool.verify_stream(io.BytesIO(payload), expected)

    def test_corrupt_remote_asset_leaves_clearance_false(self):
        tool = self.module_required()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, manifest = self.fixture(root)
            out = root/'candidate'
            tool.prepare(source, manifest, out, 'owner/source-repo', 'tools-fixture')
            def corrupted_response(request, **kwargs):
                name = request.full_url.rsplit('/', 1)[-1]
                payload = (out/'release'/name).read_bytes()
                return io.BytesIO(payload if name == 'source-manifest.json' else b'x'*len(payload))
            with self.assertRaises(ValueError):
                tool.verify_and_finalize(out, opener=corrupted_response)
            self.assertFalse(json.loads((out/'package-manifest.json').read_text(encoding='utf-8'))['public_redistribution_cleared'])
            self.assertFalse((out/'release.zip').exists())

    def test_modified_runtime_is_rejected_before_any_download(self):
        tool = self.module_required()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, manifest = self.fixture(root)
            out = root/'candidate'
            tool.prepare(source, manifest, out, 'owner/source-repo', 'tools-fixture')
            (out/'package/Tools/bin/mpv.exe').write_bytes(b'changed')
            def should_not_download(*args, **kwargs):
                self.fail('Tampered runtime must be rejected before network access')
            with self.assertRaises(ValueError):
                tool.verify_and_finalize(out, opener=should_not_download)

    def test_only_complete_verified_sources_allow_final_package(self):
        tool = self.module_required()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, manifest = self.fixture(root)
            out = root/'candidate'
            tool.prepare(source, manifest, out, 'owner/source-repo', 'tools-fixture')
            def local_response(request, **kwargs):
                name = request.full_url.rsplit('/', 1)[-1]
                return io.BytesIO((out/'release'/name).read_bytes())
            tool.verify_and_finalize(out, opener=local_response)
            result = json.loads((out/'package-manifest.json').read_text(encoding='utf-8'))
            self.assertTrue(result['public_redistribution_cleared'])
            self.assertEqual(result['archive_sha256'], tool.digest(out/'release.zip'))
            self.assertTrue(json.loads((out/'source-verification.json').read_text(encoding='utf-8'))['all_assets_verified'])
            self.assertEqual((source/'Tools/bin/node.exe').read_bytes(), (out/'package/Tools/bin/node.exe').read_bytes())


if __name__ == '__main__':
    unittest.main()
