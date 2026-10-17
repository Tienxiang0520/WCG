"""Build, verify and (with --publish) deploy the website and the cloud save API together.

One command so the two never drift apart (2026-10-13: an older API only knew 200 cards and
rejected new saves as "驗證未通過"). Order: tests -> site build -> API package -> validation;
with --publish the API goes first (a newer catalog accepts older saves, not the reverse), its
/health must report the same data-file SHA-256 as the site, then the site is uploaded and the
live asset manifest is checked. Without --publish nothing is uploaded.

Run from the repository root with a Python that has azure-storage-blob for the site upload
(or point --blob-python at one), e.g.:
  /workspace/.azvenv/bin/python3 tools/deploy-all.py --publish --az "$(which az)"
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
SITE = ROOT / '.build-tmp/azure-blob-site'
DIST = SITE / 'dist'
# Same keys as the API's /health "data" (Wcg.Cloud/CloudCatalog.cs) and the site's cloud-version.js.
DATA = {'cards': 'cards.json', 'presets': 'preset_decks.json', 'rankedDecks': 'ranked_decks.json'}


def step(title):
    print(f'\n== {title}', flush=True)


def run(args, **kw):
    print('$ ' + ' '.join(str(a) for a in args), flush=True)
    subprocess.run([str(a) for a in args], cwd=ROOT, check=True, **kw)


def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def system_env():
    # Build tools call plain `python3` (Pillow for card art); never inherit the upload-only venv for them.
    env = dict(os.environ)
    venv = env.pop('VIRTUAL_ENV', None)
    if venv:
        env['PATH'] = os.pathsep.join(p for p in env['PATH'].split(os.pathsep) if not p.startswith(venv))
    return env


def get_json(url, timeout=20):
    request = urllib.request.Request(url, headers={'Cache-Control': 'no-cache', 'User-Agent': 'wcg-deploy-all'})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode('utf-8'))


def expected_catalog():
    catalog = {}
    for key, name in DATA.items():
        built, source = DIST / 'data' / name, ROOT / 'WcgWeb/Data' / name
        if sha256(built) != sha256(source):
            raise RuntimeError(f'Site data differs from WcgWeb/Data: {name}')
        catalog[key] = sha256(source)
    return catalog


def wait_for_api(api_url, catalog, timeout):
    deadline = time.monotonic() + timeout
    last = None
    while True:
        try:
            last = get_json(api_url + '/health').get('data')
            if last == catalog:
                return
        except Exception as error:  # cold start or restart in progress
            last = f'unavailable ({type(error).__name__})'
        if time.monotonic() > deadline:
            raise RuntimeError(f'API catalog did not match the site after {timeout}s: {last}')
        time.sleep(10)


def git_state():
    commit = subprocess.run(['git', 'rev-parse', 'HEAD'], cwd=ROOT, capture_output=True, text=True).stdout.strip()
    dirty = bool(subprocess.run(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=ROOT,
                                capture_output=True, text=True).stdout.strip())
    return commit, dirty


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--publish', action='store_true', help='Upload API and site (default: build and validate only)')
    parser.add_argument('--az', default='az', help='Signed-in Azure CLI executable')
    parser.add_argument('--blob-python', default=sys.executable,
                        help='Python with azure-storage-blob for deploy-blob.py (default: this Python)')
    parser.add_argument('--skip-tests', action='store_true', help='Skip WcgTests (the API package still runs Wcg.Cloud.Tests)')
    parser.add_argument('--api-timeout', type=int, default=300, help='Seconds to wait for the new API to report the site catalog')
    parser.add_argument('--blob-arg', action='append', default=[], help='Extra argument passed to deploy-blob.py (repeatable)')
    args = parser.parse_args()
    env = system_env()
    commit, dirty = git_state()
    if dirty:
        print('Warning: tracked files have uncommitted changes; the receipt records this.', flush=True)

    if not args.skip_tests:
        step('Game tests')
        run(['dotnet', 'test', 'WcgTests/WcgTests.csproj', '--nologo'], env=env)
    step('Website build')
    run(['node', 'SoulOath.Static/tools/build-site.mjs', SITE.relative_to(ROOT), '--azure'], env=env)
    run(['node', 'SoulOath.Static/tools/check-azure-output.mjs', DIST.relative_to(ROOT)], env=env)
    catalog = expected_catalog()
    api_url = json.loads((DIST / 'cloud-config.json').read_text())['apiUrl'].rstrip('/')
    print(f'Catalog: cards {catalog["cards"][:12]}…, API {api_url}', flush=True)

    step('Site package validation')
    blob = [args.blob_python, 'SoulOath.Static/tools/deploy-blob.py', DIST.relative_to(ROOT), *args.blob_arg]
    run(blob, env=env)

    step('Cloud save API' + (' (publish)' if args.publish else ' package'))
    run([sys.executable, 'tools/CloudSave/deploy_api.py', *(['--publish', '--az', args.az] if args.publish else [])], env=env)
    if not args.publish:
        print('\nBuild and validation passed. Nothing was uploaded (add --publish).', flush=True)
        return

    step('Waiting for the API to report the same catalog')
    wait_for_api(api_url, catalog, args.api_timeout)
    print('API catalog matches the site.', flush=True)

    step('Website publish')
    record = ROOT / '.runtime/azure-blob-deployment.json'
    run([*blob, '--publish', '--az', args.az, '--record', record], env=env)
    site = json.loads(record.read_text())
    manifest = json.loads((DIST / 'asset-manifest.json').read_text())
    live = get_json(site['endpoint'].rstrip('/') + '/asset-manifest.json')
    if live.get('version') != manifest['version']:
        raise RuntimeError('Live asset manifest differs from this build')
    wait_for_api(api_url, catalog, 60)

    receipt = {'commit': commit, 'dirty': dirty, 'site': site['endpoint'], 'assetVersion': manifest['version'],
               'api': api_url, 'catalog': catalog, 'deployedAt': time.strftime('%Y-%m-%dT%H:%M:%S%z')}
    path = ROOT / '.runtime/wcg-deployment.json'
    path.parent.mkdir(exist_ok=True)
    path.write_text(json.dumps(receipt, indent=2) + '\n')
    print(json.dumps(receipt, indent=2), flush=True)


if __name__ == '__main__':
    main()
