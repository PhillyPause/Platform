"""Exact QR modules printed in the film's ink with a transparent paper field.

The 4-module quiet zone remains part of the 660-pixel canvas. Composite this
asset only over light, unobstructed parchment; never crop that empty margin.
"""
from pathlib import Path
import hashlib,json
from PIL import Image,ImageDraw

HERE=Path(__file__).resolve().parent
SOURCE=HERE/'phillypause_qr.png'
OUTPUT=HERE/'phillypause_qr_parchment.png'
INK=(41,38,31)

def generate():
    # Preserve the previously verified exact module matrix and mask. Sampling
    # happens at native module centers; no antialiasing or resampling is used.
    original=Image.open(SOURCE).convert('RGB')
    assert original.size==(660,660)
    image=Image.new('RGBA',(660,660),INK+(0,))
    draw=ImageDraw.Draw(image)
    for row in range(33):
        for col in range(33):
            pixel=original.getpixel((col*20+10,row*20+10))
            assert pixel in ((0,0,0),(255,255,255))
            if pixel==(0,0,0):
                draw.rectangle((col*20,row*20,col*20+19,row*20+19),fill=INK+(255,))
    assert image.getchannel('A').crop((0,0,660,80)).getbbox() is None
    assert image.getchannel('A').crop((0,580,660,660)).getbbox() is None
    assert image.getchannel('A').crop((0,0,80,660)).getbbox() is None
    assert image.getchannel('A').crop((580,0,660,660)).getbbox() is None
    temporary=OUTPUT.with_suffix('.tmp.png')
    image.save(temporary);temporary.replace(OUTPUT)
    metadata={'payload':'https://phillypause.org','source_matrix':SOURCE.name,
      'source_sha256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
      'image_sha256':hashlib.sha256(OUTPUT.read_bytes()).hexdigest(),
      'image':OUTPUT.name,'width':660,'height':660,'mode':'RGBA',
      'ink':'#29261f','background':'transparent; the closing-card parchment shows through',
      'symbol_modules':25,'quiet_zone_modules':4,'pixels_per_module':20,
      'module_matrix_unchanged':True}
    OUTPUT.with_suffix('.json').write_text(json.dumps(metadata,indent=2)+'\n')
    print(json.dumps(metadata))

if __name__=='__main__':generate()
