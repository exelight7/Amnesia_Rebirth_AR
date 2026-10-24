"""Build HPL-compatible XML BMFonts. Never writes to the game directory."""
from pathlib import Path
import argparse
import hashlib
import json
import unicodedata
import xml.etree.ElementTree as ET

from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
SOURCE = ROOT / 'source' / 'IBMPlexSansArabic-Regular.ttf'
SIZES = (10, 24, 28, 32, 36, 40, 56, 72, 96)


def build(size, destination, codepoints):
    name = f'arabic_{size}'
    # HPL measures glyphs relative to lineHeight; fit the font's 1500-unit metrics.
    raster_size = max(1, int(size / 1.5))
    font = ImageFont.truetype(str(SOURCE), raster_size, layout_engine=ImageFont.Layout.BASIC)
    ascent, descent = font.getmetrics()
    while ascent + descent > size and raster_size > 1:
        raster_size -= 1
        font = ImageFont.truetype(str(SOURCE), raster_size, layout_engine=ImageFont.Layout.BASIC)
        ascent, descent = font.getmetrics()
    baseline = ascent
    side = 1024
    atlas = Image.new('RGBA', (side, side), (255, 255, 255, 0))
    draw = ImageDraw.Draw(atlas)
    root = ET.Element('font')
    ET.SubElement(root, 'info', face='Amnesia Arabic', size=str(raster_size), bold='0', italic='0', charset='', unicode='1', stretchH='100', smooth='1', aa='1', padding='0,0,0,0', spacing='2,2', outline='0')
    common = ET.SubElement(root, 'common', lineHeight=str(size), base=str(baseline), scaleW=str(side), scaleH=str(side), pages='1', packed='0', alphaChnl='0', redChnl='4', greenChnl='4', blueChnl='4')
    pages = ET.SubElement(root, 'pages')
    chars = ET.SubElement(root, 'chars', count=str(len(codepoints)))
    x = y = 2
    row_height = 0
    page = 0
    artifacts = []

    def save_page():
        filename = f'{name}_{page}.dds'
        target = destination / filename
        atlas.save(target)
        with Image.open(target) as reloaded:
            assert reloaded.size == atlas.size
            assert reloaded.convert('RGBA').tobytes() == atlas.tobytes(), 'DDS pixels changed'
        ET.SubElement(pages, 'page', id=str(page), file=filename)
        artifacts.append(target)

    for cp in codepoints:
        letter = chr(cp)
        left, top, right, bottom = font.getbbox(letter, anchor='ls')
        width, height = max(0, right - left), max(0, bottom - top)
        assert width + 4 < side and height + 4 < side
        if x + width + 2 > side:
            x, y, row_height = 2, y + row_height + 2, 0
        if y + height + 2 > side:
            save_page()
            page += 1
            atlas = Image.new('RGBA', (side, side), (255, 255, 255, 0))
            draw = ImageDraw.Draw(atlas)
            x = y = 2
            row_height = 0
        if width and height:
            draw.text((x - left, y - top), letter, font=font, anchor='ls', fill=(255, 255, 255, 255))
        ET.SubElement(chars, 'char', id=str(cp), x=str(x), y=str(y), width=str(width), height=str(height), xoffset=str(left), yoffset=str(baseline + top), xadvance=str(round(font.getlength(letter))), page=str(page), chnl='15')
        x += width + 2
        row_height = max(row_height, height)
    save_page()
    common.set('pages', str(page + 1))
    target = destination / f'{name}.fnt'
    ET.indent(root, space='  ')
    ET.ElementTree(root).write(target, encoding='utf-8', xml_declaration=True)
    artifacts.append(target)
    parsed = ET.parse(target).getroot()
    assert len(parsed.findall('chars/char')) == len(codepoints)
    assert {int(c.get('id')) for c in parsed.findall('chars/char')} == set(codepoints)
    return {'line_height': size, 'raster_size': raster_size, 'baseline': baseline, 'pages': page + 1, 'glyphs': len(codepoints)}, artifacts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'generated')
    args = parser.parse_args()
    destination = args.output.resolve()
    if not destination.is_relative_to(ROOT):
        raise SystemExit('Output must remain inside 05_fonts')
    destination.mkdir(parents=True, exist_ok=True)
    cmap = TTFont(str(SOURCE)).getBestCmap()
    codepoints = sorted(cp for cp in cmap if cp <= 0xFFFF and not unicodedata.category(chr(cp)).startswith('C'))
    assert all(cp in codepoints for cp in range(32, 127)), 'ASCII coverage incomplete'
    assert all(cp in codepoints for cp in range(0xFEF5, 0xFEFD)), 'Lam-alef forms missing'
    report = {'source_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(), 'codepoints': codepoints, 'fonts': {}, 'sha256': {}}
    for size in SIZES:
        info, files = build(size, destination, codepoints)
        report['fonts'][f'arabic_{size}'] = info
        for file in files:
            report['sha256'][file.name] = hashlib.sha256(file.read_bytes()).hexdigest()
    (destination / 'font_manifest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report['fonts'], indent=2))


if __name__ == '__main__':
    main()
