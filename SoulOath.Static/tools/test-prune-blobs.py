"""prune-blobs.py never treats anything the current build publishes as obsolete."""
import importlib.util
from pathlib import Path
import sys
import unittest

here = Path(__file__).resolve().parent
sys.path.insert(0, str(here))
spec = importlib.util.spec_from_file_location('test_deploy_blob', here / 'test-deploy-blob.py')
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)
prune_spec = importlib.util.spec_from_file_location('prune_blobs', here / 'prune-blobs.py')
prune = importlib.util.module_from_spec(prune_spec)
prune_spec.loader.exec_module(prune)


class PruneTests(fixtures.BlobPlanTests):
    def test_only_blobs_outside_the_current_build_are_obsolete(self):
        _, entries = prune.deploy.plan(self.root)
        remote = [e.name for e in entries] + ['_framework/runtime.oldhash.wasm', 'card-art/WCG-999.webp']
        self.assertEqual(prune.obsolete(remote, entries), ['_framework/runtime.oldhash.wasm', 'card-art/WCG-999.webp'])
        # Route aliases (extension-less blobs) belong to the build and are never obsolete.
        self.assertIn('cards', remote)
        self.assertNotIn('cards', prune.obsolete(remote, entries))


if __name__ == '__main__':
    unittest.main(defaultTest='PruneTests')
