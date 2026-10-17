"""Prepare disposable WebP delivery files from the preserved PNG artwork.

Screen art only: the largest on-screen use is the card preview (about 340 CSS px of
art width) and the battle detail dialog (about 450 CSS px), so 1000 px wide covers
2x-3x high-DPI screens. Print art is produced separately by prepare-print-art.py.
"""
import json
from pathlib import Path
from PIL import Image

MAX_WIDTH = 1000
QUALITY = 80
SETTINGS = {'max_width': MAX_WIDTH, 'quality': QUALITY, 'method': 6}

folder = Path(__file__).resolve().parents[1] / 'wwwroot/card-art'
stamp = folder / '.webp-settings.json'
current = json.dumps(SETTINGS, sort_keys=True)
# Regenerate everything when the encoder settings change; otherwise only stale files.
rebuild = not stamp.exists() or stamp.read_text(encoding='utf-8') != current
for source in sorted(folder.glob('WCG-*.png')):
    target = source.with_suffix('.webp')
    if rebuild or not target.exists() or target.stat().st_mtime < source.stat().st_mtime:
        with Image.open(source) as image:
            image = image.convert('RGB')
            if image.width > MAX_WIDTH:
                height = round(image.height * MAX_WIDTH / image.width)
                image = image.resize((MAX_WIDTH, height), Image.Resampling.LANCZOS)
            image.save(target, 'WEBP', quality=QUALITY, method=6)
stamp.write_text(current, encoding='utf-8')
print(f'{len(list(folder.glob("WCG-*.webp")))} card images prepared ({MAX_WIDTH}px wide, WebP q{QUALITY})')
