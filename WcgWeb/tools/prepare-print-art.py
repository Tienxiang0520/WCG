from pathlib import Path
from PIL import Image

project = Path(__file__).resolve().parents[1]
target = project / 'wwwroot/print-art'
target.mkdir(parents=True, exist_ok=True)
for source in sorted((project / 'wwwroot/card-art').glob('*.png')):
    output = target / (source.stem + '.jpg')
    if output.exists() and output.stat().st_mtime >= source.stat().st_mtime:
        continue
    with Image.open(source) as image:
        image = image.convert('RGB')
        image.thumbnail((700, 700), Image.Resampling.LANCZOS)
        image.save(output, 'JPEG', quality=85, optimize=True, progressive=True)
print(f'{len(list(target.glob("*.jpg")))} print images prepared (about 300 dpi at card width)')
