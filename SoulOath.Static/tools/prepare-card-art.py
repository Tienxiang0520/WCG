from pathlib import Path
from PIL import Image
root = Path(__file__).resolve().parents[2]
source = root / 'LcgWeb/wwwroot/card-art'
target = root / 'SoulOath.Static/wwwroot/card-art'
target.mkdir(parents=True, exist_ok=True)
for path in sorted(source.glob('*.png')):
    output = target / (path.stem + '.webp')
    if not output.exists() or output.stat().st_mtime < path.stat().st_mtime:
        with Image.open(path) as image:
            image.save(output, 'WEBP', quality=88, method=6)
    # These PNG copies were generated for the browser project, not source artwork.
    obsolete = target / path.name
    if obsolete.exists(): obsolete.unlink()
print(f'{len(list(target.glob("*.webp")))} card images prepared')
