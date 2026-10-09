"""Deployment boundary tests: public data only, complete preload, valid routes."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('deploy_blob', Path(__file__).with_name('deploy-blob.py'))
deploy = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = deploy
spec.loader.exec_module(deploy)


class BlobPlanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.write('index.html', b'<base href="/"><title>WCG</title>')
        self.write('asset-worker.js', b'/* offline worker */')
        self.write('_framework/runtime.hash123.wasm', b'\x00asm')
        self.write('data/cards.json', json.dumps([{'Id': f'WCG-{i:03}'} for i in range(199)]).encode())
        for i in range(199):
            self.write(f'card-art/WCG-{i:03}.webp', b'test-art')
        for route in deploy.ROUTES:
            self.write(f'{route}/index.html', (self.root / 'index.html').read_bytes())
        self.manifest()

    def write(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)

    def manifest(self):
        assets = []
        for path in sorted(self.root.rglob('*')):
            name = path.relative_to(self.root).as_posix()
            if path.is_file() and path.suffix in {'.wasm', '.json', '.webp'} and name != 'asset-manifest.json':
                assets.append({'path': name, 'size': path.stat().st_size, 'sha256': deploy.digest(path)})
        version = hashlib.sha256(json.dumps(assets, separators=(',', ':')).encode()).hexdigest()
        self.write('asset-manifest.json', json.dumps({'version': version, 'assets': assets}).encode())

    def test_mime_routes_and_publication_order(self):
        _, entries = deploy.plan(self.root)
        wasm = next(e for e in entries if e.name.endswith('.wasm'))
        self.assertEqual(wasm.content_type, 'application/wasm')
        aliases = [e for e in entries if e.name in deploy.ROUTES]
        self.assertEqual(len(aliases), len(deploy.ROUTES))
        self.assertTrue(all(e.content_type.startswith('text/html') for e in aliases))
        self.assertEqual(entries[-1].name, 'index.html')
        self.assertLess(max(i for i, e in enumerate(entries) if e.phase == 0),
                        next(i for i, e in enumerate(entries) if e.name == 'asset-manifest.json'))

    def test_player_data_rejected_before_upload_even_outside_manifest(self):
        for name in ('.runtime/azure-cli-config/token.json', 'data/player_profile.json',
                     'data/ranked.json', '玩家存檔備份/save.json', '.env'):
            with self.subTest(name=name):
                self.write(name, b'private')
                with self.assertRaisesRegex(ValueError, 'Local-only'):
                    deploy.plan(self.root)
                (self.root / name).unlink()
                # Remove only the empty fixture directory, not project data.
                parent = (self.root / name).parent
                while parent != self.root and not list(parent.iterdir()):
                    parent.rmdir()
                    parent = parent.parent

    def test_corrupt_artwork_stops_plan(self):
        self.write('card-art/WCG-001.webp', b'changed')
        with self.assertRaisesRegex(ValueError, 'Missing or damaged'):
            deploy.plan(self.root)

    def test_unlisted_game_asset_stops_plan(self):
        self.write('unlisted.js', b'new game code')
        with self.assertRaisesRegex(ValueError, 'cover every'):
            deploy.plan(self.root)

    def test_symlink_to_private_file_stops_plan(self):
        (self.root / 'linked.json').symlink_to(self.root / 'data/cards.json')
        with self.assertRaisesRegex(ValueError, 'Symlink'):
            deploy.plan(self.root)

    def test_inconsistent_route_stops_plan(self):
        self.write('cards/index.html', b'outdated')
        with self.assertRaisesRegex(ValueError, 'Route entry differs'):
            deploy.plan(self.root)

    def test_wrong_manifest_version_stops_plan(self):
        path = self.root / 'asset-manifest.json'
        value = json.loads(path.read_text())
        value['version'] = '0' * 64
        path.write_text(json.dumps(value))
        with self.assertRaisesRegex(ValueError, 'version does not match'):
            deploy.plan(self.root)

    def test_unused_host_configs_and_compressed_copies_omitted_licenses_kept(self):
        self.write('staticwebapp.config.json', b'{}')
        self.write('_framework/runtime.hash123.wasm.br', b'compressed')
        self.write('battle/audio/licenses/test.txt', b'CC0 license')
        _, entries = deploy.plan(self.root)
        names = {e.name for e in entries}
        self.assertNotIn('staticwebapp.config.json', names)
        self.assertNotIn('_framework/runtime.hash123.wasm.br', names)
        self.assertIn('battle/audio/licenses/test.txt', names)


if __name__ == '__main__':
    unittest.main()
