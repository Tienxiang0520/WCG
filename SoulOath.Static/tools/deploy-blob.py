"""Validate and publish public WCG assets to an existing Azure Storage static site.

No credentials or player data are written to the deployment package. Azure CLI
must already be signed in; its account key is used only in this process's memory.
Old assets are retained so open game tabs can finish using their own version.

Transfer compression: Blob static websites do not negotiate encodings, so text-like
files (WASM, JS, CSS, JSON, ICU data, HTML) are stored pre-compressed with a fixed
Content-Encoding (Brotli by default; every current major browser decodes it over
HTTPS, and `--encoding gzip` is the universal fallback). Browsers decode before
scripts see the body, so the asset manifest sizes and SHA-256 values (and the
preloader's integrity checks) always refer to the original, uncompressed bytes.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import gzip
import re
import shutil
import subprocess

ROUTES = ('battle', 'cards', 'deckbuilder', 'ranked', 'rules', 'settings', 'tutorial', 'legacy', 'not-found', 'auth/callback')
CACHE_CONTROL = 'no-cache'
MIME = {
    '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8',
    '.wasm': 'application/wasm', '.dat': 'application/octet-stream',
    '.blat': 'application/octet-stream', '.webp': 'image/webp', '.png': 'image/png',
    '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
    '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav',
    '.woff': 'font/woff', '.woff2': 'font/woff2', '.ttf': 'font/ttf',
    '.txt': 'text/plain; charset=utf-8', '.md': 'text/plain; charset=utf-8',
}
PRIVATE = {'.git', '.openai', '.runtime', 'node_modules', '玩家存檔備份',
           'custom_decks.json', 'ranked.json', 'player_profile.json'}
OMIT = {'staticwebapp.config.json', 'web.config'}
# Already-compressed media (webp/jpg/png/ogg/mp3/woff2) gain nothing from transfer encoding.
COMPRESSIBLE = {'.html', '.js', '.css', '.json', '.wasm', '.dat', '.blat', '.svg', '.ico',
                '.ttf', '.wav', '.txt', '.md'}
ENCODINGS = ('br', 'gzip', 'identity')
# Keep an encoded payload only when it saves at least this share of the bytes.
MIN_SAVING = 0.1


@dataclass(frozen=True)
class Entry:
    name: str
    source: Path
    sha256: str
    size: int
    content_type: str
    phase: int
    # Bytes actually stored in Blob: the source itself, or its pre-compressed payload.
    upload: Path = None
    upload_size: int = 0
    encoding: str = ''

    @property
    def payload(self):
        return self.upload or self.source

    @property
    def payload_size(self):
        return self.upload_size if self.upload else self.size


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


# Current playable catalog used by the build (not balance-before-* era snapshots).
CATALOG_PATH = Path(__file__).resolve().parents[2] / 'WcgWeb' / 'Data' / 'cards.json'


def catalog_card_count(path=CATALOG_PATH):
    cards = json.loads(Path(path).read_text())
    ids = {card.get('Id') or card.get('id') for card in cards}
    if not ids or None in ids:
        raise ValueError('Current card catalog is missing card ids')
    return len(ids)


def public_path(name):
    parts = name.split('/')
    if any(p in PRIVATE or p.startswith('.') or p.startswith('.env') for p in parts):
        raise ValueError(f'Local-only data in public package: {name}')
    if not re.fullmatch(r'[A-Za-z0-9_][A-Za-z0-9_./-]*', name) or '..' in parts:
        raise ValueError(f'Unsafe public path: {name}')


def plan(source):
    source = source.resolve()
    files = {}
    # Reject private files and symlinks even when they are absent from the manifest.
    for path in sorted(source.rglob('*')):
        name = path.relative_to(source).as_posix()
        if path.is_symlink():
            raise ValueError(f'Symlink in public package: {name}')
        public_path(name)
        if path.is_dir():
            continue
        if not path.is_file():
            raise ValueError(f'Unsupported entry: {name}')
        if name in OMIT or re.search(r'\.(?:gz|br|map|pdb)$', name):
            continue
        if path.suffix not in MIME:
            raise ValueError(f'Unknown public file type: {name}')
        if path.suffix in {'.txt', '.md'} and not name.startswith('battle/audio/'):
            raise ValueError(f'Unexpected documentation in public package: {name}')
        files[name] = path

    manifest = json.loads(files['asset-manifest.json'].read_text())
    assets = manifest['assets']
    if not re.fullmatch(r'[a-f0-9]{64}', manifest['version']):
        raise ValueError('Invalid manifest version')
    if hashlib.sha256(json.dumps(assets, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest() != manifest['version']:
        raise ValueError('Manifest version does not match its contents')
    seen = set()
    for asset in assets:
        name = asset['path']
        public_path(name)
        if name in seen:
            raise ValueError(f'Duplicate preload asset: {name}')
        seen.add(name)
        path = files.get(name)
        if path is None or path.stat().st_size != asset['size'] or digest(path) != asset['sha256']:
            raise ValueError(f'Missing or damaged preload asset: {name}')
    excluded = {'index.html', 'asset-manifest.json', 'asset-worker.js'} | {f'{r}/index.html' for r in ROUTES}
    excluded |= {name for name in files if name.startswith('battle/audio/') and Path(name).suffix in {'.md', '.txt'}}
    if set(files) - excluded != seen:
        raise ValueError('Preload manifest must cover every game asset')
    cards = json.loads(files['data/cards.json'].read_text())
    expected = catalog_card_count()
    if len(cards) != expected:
        raise ValueError(f'Expected {expected} cards from current catalog')
    for card in cards:
        if f"card-art/{card.get('Id', card.get('id'))}.webp" not in seen:
            raise ValueError('Missing card artwork')
    index = files['index.html']
    if '<base href="/"' not in index.read_text():
        raise ValueError('Blob site requires root-relative navigation')
    if 'asset-worker.js' not in files:
        raise ValueError('Missing offline asset worker')
    entries = []
    for name, path in files.items():
        phase = 2 if name.endswith('index.html') else 1 if name == 'asset-manifest.json' else 0
        entries.append(Entry(name, path, digest(path), path.stat().st_size, MIME[path.suffix], phase))
    for route in ROUTES:
        route_index = files[f'{route}/index.html']
        if route != 'auth/callback' and route_index.read_bytes() != index.read_bytes():
            raise ValueError(f'Route entry differs from homepage: {route}')
        # Blob names can coexist with virtual folders. Exact aliases make /cards
        # work without relying on a host-specific rewrite or a 404 fallback.
        entries.append(Entry(route, route_index, digest(route_index), route_index.stat().st_size, MIME['.html'], 2))
    return manifest['version'], sorted(entries, key=lambda e: (e.phase, e.name == 'index.html', e.name))


def encode(entries, encoding, cache):
    """Attach verified pre-compressed payloads to compressible entries."""
    if encoding not in ENCODINGS:
        raise ValueError(f'Unknown encoding: {encoding}')
    if encoding == 'identity':
        return entries
    cache = Path(cache).resolve()
    suffix = '.br' if encoding == 'br' else '.gz'
    # Content-addressed by the source hash, so route aliases share one payload.
    targets = {}
    for entry in entries:
        if entry.source.suffix in COMPRESSIBLE and entry.size >= 256:
            targets.setdefault(entry.sha256, (entry.source, cache / encoding / (entry.sha256 + suffix)))
    if encoding == 'gzip':
        for source, output in targets.values():
            data = source.read_bytes()
            if output.exists():
                try:
                    if gzip.decompress(output.read_bytes()) == data:
                        continue
                except (OSError, EOFError):
                    pass
            packed = gzip.compress(data, compresslevel=9, mtime=0)
            if gzip.decompress(packed) != data:
                raise RuntimeError(f'gzip round trip failed: {source}')
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(packed)
    else:
        node = shutil.which('node')
        if not node:
            raise RuntimeError('Brotli encoding needs Node.js (or use --encoding gzip)')
        jobs = [{'source': str(s), 'output': str(o)} for s, o in targets.values()]
        result = subprocess.run([node, str(Path(__file__).with_name('brotli-encode.mjs'))],
                                input=json.dumps(jobs), capture_output=True, text=True)
        if result.returncode:
            raise RuntimeError(f'Brotli encoding failed: {result.stderr.strip()[-500:]}')
    encoded = []
    for entry in entries:
        target = targets.get(entry.sha256)
        if target and entry.source.suffix in COMPRESSIBLE and entry.size >= 256:
            size = target[1].stat().st_size
            if size <= entry.size * (1 - MIN_SAVING):
                entry = Entry(entry.name, entry.source, entry.sha256, entry.size, entry.content_type,
                              entry.phase, target[1], size, encoding)
        encoded.append(entry)
    return encoded


def stage(entries, directory):
    """Write the exact blobs and headers that publish would store, for local emulation."""
    directory = Path(directory).resolve()
    if directory.exists():
        # Only ever replace an earlier stage (or an empty folder), never other data.
        if any(directory.iterdir()) and not (directory / 'headers.json').is_file():
            raise ValueError(f'Stage folder is not empty and is not an earlier stage: {directory}')
        shutil.rmtree(directory)
    headers = {}
    for entry in entries:
        # Route aliases are extension-less blobs next to same-named virtual folders;
        # keep the headers map authoritative instead of reproducing that on disk.
        target = directory / 'blobs' / entry.name.replace('/', '%2F')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(entry.payload, target)
        headers[entry.name] = {'Content-Type': entry.content_type, 'Cache-Control': CACHE_CONTROL,
                               **({'Content-Encoding': entry.encoding} if entry.encoding else {})}
    (directory / 'headers.json').write_text(json.dumps(headers, indent=1, ensure_ascii=False))
    return directory


def az_json(az, args):
    result = subprocess.run([az, *args, '--only-show-errors', '--output', 'json'], capture_output=True, text=True)
    if result.returncode:
        # Never echo credential-returning CLI output, including partial output.
        raise RuntimeError(f'Azure CLI request failed (exit {result.returncode}): {args[0]}')
    return json.loads(result.stdout)


def matches(remote, entry):
    return (remote.metadata.get('sha256') == entry.sha256 and remote.size == entry.payload_size
            and remote.content_settings.content_type == entry.content_type
            and remote.content_settings.cache_control == CACHE_CONTROL
            and (remote.content_settings.content_encoding or '') == entry.encoding)


def publish(args, version, entries):
    from azure.storage.blob import BlobServiceClient, ContentSettings
    account = az_json(args.az, ['storage', 'account', 'show', '--name', args.account,
                              '--resource-group', args.resource_group])
    if (account['kind'] != 'StorageV2' or account['sku']['name'] != 'Standard_LRS'
            or account.get('accessTier') != 'Hot' or account.get('isHnsEnabled', False)
            or not account.get('enableHttpsTrafficOnly') or account.get('minimumTlsVersion') != 'TLS1_2'):
        raise ValueError('Expected HTTPS/TLS1.2, Hot LRS StorageV2 without hierarchical namespace')
    # Existing Azure management permission can obtain a key without granting any
    # additional role. This key is not logged, persisted, or passed on a command line.
    key = az_json(args.az, ['storage', 'account', 'keys', 'list', '--account-name', args.account,
                          '--resource-group', args.resource_group])[0]['value']
    service = BlobServiceClient(account['primaryEndpoints']['blob'], credential=key,
                                connection_timeout=60, read_timeout=60, retry_total=4,
                                max_single_put_size=4 * 1024 * 1024, max_block_size=1024 * 1024)
    service.set_service_properties(static_website={'enabled': True, 'index_document': 'index.html',
                                                   'error_document404_path': 'not-found/index.html'})
    container = service.get_container_client('$web')
    existing = {b.name: b for b in container.list_blobs(include=['metadata'])}
    changed = [e for e in entries if e.name not in existing or not matches(existing[e.name], e)]
    print(f'Upload {len(changed)}/{len(entries)} files, {sum(e.payload_size for e in changed)/1e6:.1f} MB', flush=True)

    def upload(entry):
        with entry.payload.open('rb') as stream:
            container.upload_blob(entry.name, stream, length=entry.payload_size, overwrite=True,
                                  metadata={'sha256': entry.sha256},
                                  content_settings=ContentSettings(content_type=entry.content_type,
                                                                   cache_control=CACHE_CONTROL,
                                                                   content_encoding=entry.encoding or None),
                                  max_concurrency=1)

    def verify(entry):
        if not matches(container.get_blob_client(entry.name).get_blob_properties(), entry):
            raise RuntimeError(f'Remote asset verification failed: {entry.name}')

    def verify_all(selected):
        with ThreadPoolExecutor(max_workers=args.workers) as pool:
            for future in as_completed([pool.submit(verify, e) for e in selected]):
                future.result()

    # Assets must all succeed before publishing a new manifest or HTML entry.
    for phase in (0, 1, 2):
        selected = [e for e in changed if e.phase == phase and e.name != 'index.html']
        failures = []
        with ThreadPoolExecutor(max_workers=args.workers) as pool:
            futures = {pool.submit(upload, e): e for e in selected}
            for i, future in enumerate(as_completed(futures), 1):
                try:
                    future.result()
                except Exception:
                    failures.append(futures[future].name)
                    print(f'Upload failed; rerun to resume: {futures[future].name}', flush=True)
                if i % 50 == 0 or i == len(selected):
                    print(f'Phase {phase}: {i}/{len(selected)}', flush=True)
        if failures:
            raise RuntimeError(f'{len(failures)} uploads failed; entry pages were not published. Rerun to resume.')
        if phase == 0:
            verify_all([e for e in entries if e.phase == 0])
    for entry in changed:
        if entry.name == 'index.html':
            upload(entry)
    verify_all(entries)
    # The SDK returns stored bytes as-is (no decoding), so compare with the uploaded payload.
    if container.download_blob('asset-manifest.json').readall() != next(e.payload.read_bytes() for e in entries if e.name == 'asset-manifest.json'):
        raise RuntimeError('Published manifest differs from local build')
    endpoint = account['primaryEndpoints']['web']
    record = {'account': args.account, 'endpoint': endpoint, 'assetVersion': version,
              'files': len(entries), 'bytes': sum(e.size for e in entries),
              'encoding': args.encoding, 'transferBytes': sum(e.payload_size for e in entries)}
    if args.record:
        record_path = Path(args.record)
        record_path.parent.mkdir(parents=True, exist_ok=True)
        temporary = record_path.with_suffix('.tmp')
        temporary.write_text(json.dumps(record, indent=2) + '\n')
        temporary.replace(record_path)
    print(json.dumps(record, indent=2), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--publish', action='store_true', help='Upload after validation (default: validate only)')
    parser.add_argument('--account', default='wcggametienxiang0520')
    parser.add_argument('--resource-group', default='wcg-game-rg')
    parser.add_argument('--az', default='az', help='Signed-in Azure CLI executable')
    parser.add_argument('--workers', type=int, choices=range(1, 9), default=6)
    parser.add_argument('--record', help='Local deployment receipt; contains no credentials')
    parser.add_argument('--encoding', choices=ENCODINGS, default='br',
                        help='Transfer encoding stored for compressible files (default: br)')
    parser.add_argument('--encoded-cache', type=Path,
                        help='Where compressed payloads are kept between runs (default: next to the source folder)')
    parser.add_argument('--stage', type=Path,
                        help='Also write the exact blobs and headers to this folder for local emulation (serve-static.mjs --blob-stage)')
    args = parser.parse_args()
    version, entries = plan(args.source)
    cache = args.encoded_cache or args.source.resolve().parent / 'encoded-cache'
    if cache.resolve().is_relative_to(args.source.resolve()):
        raise ValueError('Encoded cache must be outside the public package')
    entries = encode(entries, args.encoding, cache)
    total = sum(e.size for e in entries)
    transfer = sum(e.payload_size for e in entries)
    print(f'Validated: {len(entries)} files, {total/1e6:.1f} MB, version {version}', flush=True)
    print(f'Transfer ({args.encoding}): {transfer/1e6:.1f} MB, {sum(1 for e in entries if e.encoding)} files encoded', flush=True)
    if args.stage:
        print(f'Staged blobs: {stage(entries, args.stage)}', flush=True)
    if args.publish:
        publish(args, version, entries)


if __name__ == '__main__':
    main()
