"""HPL-specific static Arabic export; runtime wrapping remains subject to visual QA."""
from collections import Counter
from pathlib import Path
import re, xml.etree.ElementTree as ET
import arabic_reshaper
from bidi.algorithm import get_display

CONTROL=re.compile(r'\$[A-Za-z_][A-Za-z_0-9]*\{[^{}]*\}|\[u\d+\]|%[a-zA-Z]|\{\d+\}')
SHAPER=arabic_reshaper.ArabicReshaper(configuration={'delete_harakat':True,'support_ligatures':True})

def visual_line(logical):
    if not re.search(r'[\u0600-\u06ff]',logical):return logical
    assert not any(0xE000<=ord(c)<=0xF8FF for c in logical)
    protected=re.compile(CONTROL.pattern+r'|\([A-Za-z0-9][A-Za-z0-9 .:/+_\-]*\)')
    tokens=protected.findall(logical)
    placeholder=iter(chr(0xE000+i) for i in range(len(tokens)))
    text=protected.sub(lambda m:next(placeholder),logical)
    text=get_display(SHAPER.reshape(text),base_dir='R')
    for i,token in enumerate(tokens):text=text.replace(chr(0xE000+i),token)
    assert Counter(CONTROL.findall(text))==Counter(CONTROL.findall(logical))
    return text

def advances(font):
    root=ET.parse(font).getroot()
    return {int(c.get('id')):int(c.get('xadvance')) for c in root.find('chars')},int(root.find('common').get('lineHeight'))

def text_width(logical,widths):
    visual=visual_line(CONTROL.sub('MMMM',logical))
    return sum(widths.get(ord(c),widths.get(ord('M'),48)) for c in visual)

def prepare(logical,widths=None,max_width=None):
    lines=[]; added=0
    for explicit in re.split(r'\[br\]|\r\n|\n|\r',logical):
        wrapped=[]
        if widths is not None and max_width is not None:
            current=''
            # Tokens are indivisible words even when they contain spaces.
            words=re.findall(r'\$[A-Za-z_][A-Za-z_0-9]*\{[^{}]*\}|\S+',explicit)
            for word in words:
                candidate=(current+' '+word).strip()
                if current and text_width(candidate,widths)>max_width:
                    wrapped.append(current); current=word; added+=1
                else:current=candidate
            wrapped.append(current)
        else:wrapped=[explicit]
        lines.extend(visual_line(line) for line in wrapped)
    visual='[br]'.join(lines)
    assert Counter(CONTROL.findall(visual))==Counter(CONTROL.findall(logical))
    return visual,added

def hpl_escape(text):
    return ''.join(c if ord(c)<128 else '[u%d]'%ord(c) for c in text)

def self_check():
    text='اضغط $Input{Interact} للفتح (E) 1920x1080'
    visual,n=prepare(text)
    assert '$Input{Interact}' in visual and '1920x1080' in visual and '(E)' in visual and n==0
    assert prepare('نعم[br]لا')[0].count('[br]')==1
    assert '[u' in hpl_escape(visual)
    assert 'مرحبا' not in visual_line('مرحبا')
    assert '<<' in visual_line('<<مرحبا>>')
    assert '[u65533]' in visual_line('لغة [u65533]')
    print('HPL export checks passed')

if __name__=='__main__':self_check()
