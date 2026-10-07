from pathlib import Path
import runpy
import shutil
root = Path(__file__).resolve().parents[2]
runpy.run_path(str(root / 'WcgWeb/tools/prepare-card-art.py'))
source = root / 'WcgWeb/wwwroot/card-art'
target = root / 'SoulOath.Static/wwwroot/card-art'
target.mkdir(parents=True, exist_ok=True)
for path in sorted(source.glob('WCG-*.webp')):
    output = target / path.name
    if not output.exists() or output.stat().st_mtime < path.stat().st_mtime:
        shutil.copy2(path, output)
    obsolete = target / path.with_suffix('.png').name
    if obsolete.exists(): obsolete.unlink()
print(f'{len(list(target.glob("WCG-*.webp")))} static card images prepared')
