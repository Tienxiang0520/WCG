"""Build and optionally publish the existing WCG Azure Functions API. Never exports credentials."""
import argparse
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--publish', action='store_true')
    p.add_argument('--az', default='az')
    p.add_argument('--name', default='wcg-cloud-tienxiang0520')
    p.add_argument('--resource-group', default='wcg-game-rg')
    args = p.parse_args()
    target = ROOT / '.build-tmp/cloud-api-publish'
    subprocess.run(['dotnet', 'restore', 'Wcg.Cloud/Wcg.Cloud.csproj', '--nologo'], cwd=ROOT, check=True)
    subprocess.run(['dotnet', 'test', 'Wcg.Cloud.Tests/Wcg.Cloud.Tests.csproj', '--nologo'], cwd=ROOT, check=True)
    subprocess.run(['dotnet', 'publish', 'Wcg.Cloud/Wcg.Cloud.csproj', '--nologo', '-c', 'Release', '-o', str(target)], cwd=ROOT, check=True)
    for name in ('host.json', 'worker.config.json', 'Wcg.Cloud.dll'):
        if not (target/name).is_file(): raise RuntimeError('Functions package is incomplete')
    package = ROOT / '.build-tmp/cloud-api.zip'
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(target.rglob('*')):
            if path.is_file() and path.suffix != '.pdb':
                if path.name in ('local.settings.json', '.env'): raise RuntimeError('Local settings must never be deployed')
                archive.write(path, path.relative_to(target).as_posix())
    print(f'API package ready: {package.stat().st_size} bytes')
    if not args.publish: return
    # Publish only to an existing Function App, not a newly created site or another App Service.
    app = json.loads(subprocess.check_output([args.az, 'functionapp', 'show', '-g', args.resource_group, '-n', args.name, '-o', 'json'], text=True))
    if 'functionapp' not in app.get('kind', ''): raise RuntimeError('Target is not a Function App')
    properties = app.get('properties', app)
    subprocess.run([args.az, 'functionapp', 'deployment', 'source', 'config-zip', '-g', args.resource_group, '-n', args.name, '--src', str(package), '-o', 'none'], check=True)
    receipt = ROOT / '.runtime/wcg-cloud-api-deployment.json'
    receipt.parent.mkdir(exist_ok=True)
    receipt.write_text(json.dumps({'name': args.name, 'resourceGroup': args.resource_group, 'endpoint': 'https://'+properties['defaultHostName']+'/api', 'packageBytes': package.stat().st_size}, indent=2)+'\n')

if __name__ == '__main__': main()
