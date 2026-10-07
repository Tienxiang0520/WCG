"""Prepare disposable WebP delivery files from the preserved PNG artwork."""
from pathlib import Path
from PIL import Image

folder = Path(__file__).resolve().parents[1] / 'wwwroot/card-art'
for source in sorted(folder.glob('WCG-*.png')):
    target = source.with_suffix('.webp')
    if not target.exists() or target.stat().st_mtime < source.stat().st_mtime:
        with Image.open(source) as image:
            image.convert('RGB').save(target, 'WEBP', quality=88, method=6)
print(f'{len(list(folder.glob("WCG-*.webp")))} card images prepared')
