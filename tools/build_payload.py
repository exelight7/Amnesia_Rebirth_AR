"""Build redistributable Arabic payload without reading or bundling the game."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys
import zipfile
from export_text import prepare, hpl_escape, advances, CONTROL

ROOT = Path(__file__).resolve().parents[1]


def main():
    subprocess.run([sys.executable, str(ROOT/'fonts/build_fonts.py')], check=True)
    source = json.loads((ROOT/'localization/arabic.json').read_text(encoding='utf-8'))
    assert len(source) == 5008 and [r['id'] for r in source] == list(range(5008))
    widths, _ = advances(ROOT/'fonts/generated/arabic_72.fnt')
    entries = []
    for row in source:
        text = row['arabic']
        assert not any(0xE000 <= ord(c) <= 0xF8FF for c in text)
        assert not re.search(r'[\u200e\u200f\u202a-\u202e\u2066-\u2069]', text)
        limit = 2580 if row['category'].startswith('Voices_') or row['category'] == 'LoadScreen' else 2016
        visual, _ = prepare(text, widths, limit) if row['file'] != 'config/Credits.cfg' and ('[br]' in text or len(text)>100) else prepare(text)
        missing = {ord(c) for c in CONTROL.sub('',visual).replace('[br]','') if ord(c)>32 and ord(c) not in widths}
        assert not missing, (row['id'],missing)
        entries.append({k:row[k] for k in ('id','file','category','key')} | {'visual':hpl_escape(visual)})
    assets = {}
    for path in sorted((ROOT/'fonts/generated').iterdir()):
        if path.suffix in ('.fnt','.dds'): assets['fonts/'+path.name] = path.read_bytes()
    assets['fonts/OFL-IBM.txt'] = (ROOT/'fonts/source/OFL.txt').read_bytes()
    manifest = dict(version='1.0.0',entries=entries,binaries=json.loads((ROOT/'localization/native-patches.json').read_text()),
                    assets=[dict(path=p,sha256=hashlib.sha256(b).hexdigest()) for p,b in assets.items()])
    out = ROOT/'build'; out.mkdir(exist_ok=True)
    with zipfile.ZipFile(out/'payload.zip','w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        z.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False,separators=(',',':')))
        for path,data in assets.items(): z.writestr('assets/'+path,data)
    print(f'Payload: {len(entries)} entries; {len(assets)} font/license files; {(out/"payload.zip").stat().st_size:,} bytes')


if __name__ == '__main__':
    main()
