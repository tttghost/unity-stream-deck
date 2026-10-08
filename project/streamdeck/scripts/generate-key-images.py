"""Extract Unity 6 icons and reserve space for Stream Deck's native titles.
Run in a Python environment with UnityPy and Pillow installed.
"""
import argparse
import json
from pathlib import Path

import UnityPy
from PIL import Image, ImageOps, ImageSequence

PROJECT = Path(__file__).resolve().parents[1]
PLUGIN = PROJECT / 'com.tttghost.stream-deck-unity.sdPlugin'
MAPPING = {
    'play': 'd_PlayButton', 'pause': 'd_PauseButton', 'stop': 'd_StopButton',
    'build': 'd_BuildProfile Icon', 'window': 'd_UIBuilder',
    'method': 'd_cs Script Icon', 'menu': 'd__Menu',
    'console': 'd_UnityEditor.ConsoleWindow', 'clear': 'd_clear',
    'scene': 'd_SceneAsset Icon',
}


def fit(source, size, box, top):
    """Contain the native aspect ratio; never stretch narrow or tall icons."""
    result = Image.new('RGBA', (size, size))
    bounds = source.getchannel('A').getbbox()
    if bounds:
        source = source.crop(bounds)
    icon = ImageOps.contain(source, (box, box), Image.Resampling.LANCZOS)
    result.alpha_composite(icon, ((size - icon.width) // 2, top + (box - icon.height) // 2))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--resources', type=Path, default=Path(
        '/Applications/Unity/Hub/Editor/6000.2.10f1/Unity.app/Contents/Resources/unity editor resources'))
    args = parser.parse_args()
    environment = UnityPy.load(str(args.resources))
    textures = {}
    for obj in environment.objects:
        if obj.type.name == 'Texture2D':
            data = obj.read()
            textures[data.m_Name] = data
    keys = PLUGIN / 'imgs/keys'
    icons = PLUGIN / 'imgs/unity-editor'
    keys.mkdir(exist_ok=True)
    report = {}
    for name, asset in MAPPING.items():
        candidates = [data for key, data in textures.items() if key == asset or key.startswith(asset + '@')]
        native = max(candidates, key=lambda data: data.m_Width * data.m_Height)
        original = native.image.convert('RGBA')
        assert len(original.getcolors(original.width * original.height)) > 1, asset
        assert min(original.size) >= 32, f'Low resolution source: {asset}'
        report[name] = {'source': native.m_Name, 'size': list(original.size)}
        for scale, suffix in [(1, ''), (2, '@2x')]:
            fit(original, 72 * scale, 64 * scale, 4 * scale).save(icons / f'{name}{suffix}.png')
            # Center the visible icon at (36, 36); keep the bottom label clear.
            fit(original, 72 * scale, 28 * scale, 22 * scale).save(keys / f'{name}{suffix}.png')
            if name == 'window':
                # Keep the same center with a smaller icon for two title lines.
                fit(original, 72 * scale, 18 * scale, 27 * scale).save(keys / f'editor-status{suffix}.png')
    # User-approved photo/copy vectors use separate artwork instead of Unity's video/document icons.
    for name in ['camera', 'clipboard']:
        from xml.etree import ElementTree as ET
        root = ET.fromstring((PLUGIN / f'imgs/ui/{name}.svg').read_text())
        artwork = ''.join(ET.tostring(child, encoding='unicode') for child in root)
        (keys / f'{name}.svg').write_text(
            f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 72 72">'
            f'<g transform="translate(22 22) scale(0.875)">{artwork}</g></svg>\n')
        report[name] = {'source': f'imgs/ui/{name}.svg', 'format': 'vector'}
    for name in ['build-working', 'build-success', 'build-failed']:
        with Image.open(PLUGIN / f'imgs/actions/{name}.gif') as source:
            durations, frames = [], []
            for frame in ImageSequence.Iterator(source):
                durations.append(frame.info.get('duration', 100))
                canvas = Image.new('RGBA', (144, 144), 'black')
                canvas.alpha_composite(fit(frame.convert('RGBA'), 144, 56, 44))
                frames.append(canvas.convert('RGB'))
            frames[0].save(keys / f'{name}.gif', save_all=True, append_images=frames[1:],
                           duration=durations, loop=source.info.get('loop', 0), disposal=2, optimize=False)
    (PROJECT / 'scripts/key-image-sources.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f'Generated {len(report)} icons, title-safe key images and 3 status animations.')


if __name__ == '__main__':
    main()
