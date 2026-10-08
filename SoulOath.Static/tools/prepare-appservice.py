"""Package the verified static game for Windows App Service's IIS host."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
source = root / '.build-tmp/azure-site/dist'
subprocess.run(['node', str(root / 'SoulOath.Static/tools/check-azure-output.mjs'), str(source)], check=True)
config = root / 'deployment/azure/appservice-web.config'
ET.parse(config)
output = root / '.build-tmp/azure-appservice.zip'
with ZipFile(output, 'w', compression=ZIP_DEFLATED, compresslevel=6) as archive:
    for path in sorted(source.rglob('*')):
        if path.is_file() and path.name != 'staticwebapp.config.json':
            archive.write(path, path.relative_to(source).as_posix())
    archive.write(config, 'web.config')
with ZipFile(output) as archive:
    assert 'index.html' in archive.namelist() and 'web.config' in archive.namelist()
    assert len([n for n in archive.namelist() if n.startswith('card-art/') and n.endswith('.webp')]) == 199
    assert archive.testzip() is None
print(f'Windows App Service package ready: {output}, {output.stat().st_size / 1_000_000:.1f} MB')
