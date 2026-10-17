"""List (default) or delete blobs in the static site's $web container that the current build no longer uses.

deploy-blob.py keeps old files so open tabs can finish their own version; fingerprinted framework
files and renamed assets therefore pile up. Run this some time after a deploy (players already in
a game have every file cached by the asset worker; only a tab still downloading an older version
would need to refresh). Safety:
  * dry run by default: prints what would be deleted and the bytes saved;
  * refuses unless the live asset-manifest.json is exactly this build's (prune only right after
    deploying the same dist folder);
  * never deletes anything the current build publishes (deploy-blob.py plan(), incl. route aliases).
Example:
  /workspace/.azvenv/bin/python3 SoulOath.Static/tools/prune-blobs.py .build-tmp/azure-blob-site/dist --az "$(which az)"
  ... --delete   # after reviewing the list
"""
import argparse
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location('deploy_blob', Path(__file__).with_name('deploy-blob.py'))
deploy = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = deploy
spec.loader.exec_module(deploy)


def obsolete(remote_names, entries):
    """Remote blob names not published by the current build, sorted."""
    current = {e.name for e in entries}
    return sorted(name for name in remote_names if name not in current)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('source', type=Path, help='The dist folder that is currently deployed')
    parser.add_argument('--delete', action='store_true', help='Actually delete (default: list only)')
    parser.add_argument('--account', default='wcggametienxiang0520')
    parser.add_argument('--resource-group', default='wcg-game-rg')
    parser.add_argument('--az', default='az', help='Signed-in Azure CLI executable')
    args = parser.parse_args()
    _, entries = deploy.plan(args.source)
    from azure.storage.blob import BlobServiceClient
    account = deploy.az_json(args.az, ['storage', 'account', 'show', '--name', args.account, '--resource-group', args.resource_group])
    key = deploy.az_json(args.az, ['storage', 'account', 'keys', 'list', '--account-name', args.account,
                                   '--resource-group', args.resource_group])[0]['value']
    container = BlobServiceClient(account['primaryEndpoints']['blob'], credential=key).get_container_client('$web')
    manifest = next(e for e in entries if e.name == 'asset-manifest.json')
    if container.get_blob_client('asset-manifest.json').get_blob_properties().metadata.get('sha256') != manifest.sha256:
        raise SystemExit('The live site is not this build; deploy this dist first (nothing deleted).')
    remote = {b.name: b.size for b in container.list_blobs()}
    stale = obsolete(remote, entries)
    for name in stale:
        print(f'{"delete" if args.delete else "would delete"} {name} ({remote[name]} bytes)')
    print(f'{len(stale)} obsolete blobs, {sum(remote[n] for n in stale)/1e6:.1f} MB'
          + ('' if args.delete else ' (dry run; add --delete to remove)'), flush=True)
    if args.delete:
        for name in stale:
            container.delete_blob(name)
        print('Deleted.', flush=True)


if __name__ == '__main__':
    main()
