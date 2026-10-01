"""Import the provided Android app icon without changing compiled resource IDs."""
import hashlib
import json
from pathlib import Path
import shutil

from PIL import Image

ROOT = Path(__file__).resolve().parent
APK = ROOT.parent / 'DurangoBrasilApk'


def main():
    source = ROOT.parent / 'icon.png'
    pinned = ROOT / 'branding/app-icon.png'
    shutil.copy2(source, pinned)
    records = []
    with Image.open(pinned) as image:
        image = image.convert('RGBA')
        if image.width != image.height:
            raise ValueError('O ícone deve ser quadrado para preservar suas proporções.')
        for current in sorted((APK / 'res').rglob('app_icon*.png')):
            with Image.open(current) as old:
                dimensions = old.size
            resized = image.resize(dimensions, Image.Resampling.LANCZOS)
            relative = current.relative_to(APK)
            target = ROOT / 'branding/resources' / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            resized.save(target)
            shutil.copy2(target, current)
            records.append({'path': relative.as_posix(), 'dimensions': list(dimensions),
                            'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    if len(records) != 30:
        raise ValueError('Quantidade inesperada de recursos de ícone: ' + str(len(records)))
    manifest = {'source': 'branding/app-icon.png', 'source_sha256': hashlib.sha256(pinned.read_bytes()).hexdigest(),
                'files': records}
    (ROOT / 'branding/app-icon-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Ícone brasileiro importado em 30 variantes, de 36 a 192 pixels.')


if __name__ == '__main__':
    main()
