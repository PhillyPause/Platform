#!/usr/bin/env python3
"""Mux the finished film and make website/social captioned exports.

Run after film.py has rendered the three picture files in renders/.
Requires ffmpeg with libx264 and libass. All paths are local to this package.
"""
from __future__ import annotations
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import re
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
DURATION = 1619 / 24

def run(cmd):
    subprocess.run(cmd, check=True)

def ass_time(seconds):
    cs = round(seconds * 100)
    h, cs = divmod(cs, 360000)
    m, cs = divmod(cs, 6000)
    s, cs = divmod(cs, 100)
    return f'{h}:{m:02}:{s:02}.{cs:02}'

def parse_srt(path):
    result = []
    for block in re.split(r'\n\s*\n', path.read_text().strip()):
        lines = block.splitlines()
        times = re.findall(r'(\d+):(\d+):(\d+),(\d+)', lines[1])
        a, b = [int(h)*3600 + int(m)*60 + int(s) + int(ms)/1000 for h,m,s,ms in times]
        result.append((a, b, '\\N'.join(lines[2:])))
    return result

def write_ass(fmt, out):
    width,height,size,margin = {
        'web': (1280,720,32,38),
        'vertical': (1080,1920,46,190),
        'square': (1080,1080,42,60),
    }[fmt]
    header = f'''[Script Info]
Title: PhillyPause — verbatim supplied narration
ScriptType: v4.00+
PlayResX: {width}
PlayResY: {height}
WrapStyle: 2
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,DejaVu Sans,{size},&H00FFFFFF,&H00FFFFFF,&H7012110F,&H7012110F,0,0,0,0,100,100,0,0,3,7,0,2,65,65,{margin},1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
'''
    rows=[]
    for a,b,caption in parse_srt(ROOT/'PhillyPause_Captions.srt'):
        # The vertical isolation counter occupies the bottom safe region.
        # Put these three one-line captions above the cabinet's printed sign.
        override = r'{\an8\pos(540,75)}' if fmt=='vertical' and 19.4 < a < 25.1 else ''
        # Keep the TEST placard fully visible while the narration stresses TODAY'S.
        if 36.0 < a < 38.0:
            override = r'{\an8\pos(%d,%d)}' % (width//2,round(height*.055))
        rows.append(f'Dialogue: 0,{ass_time(a)},{ass_time(b)},Default,,0,0,0,,{override}{caption}')
    out.write_text(header+'\n'.join(rows)+'\n')

def export_all(output, jobs=3, only=None):
    output.mkdir(parents=True,exist_ok=True)
    subs=ROOT/'captions'
    subs.mkdir(exist_ok=True)
    for fmt in ('web','vertical','square'):
        write_ass(fmt,subs/f'{fmt}.ass')
    for ext in ('srt','vtt'):
        shutil.copy2(ROOT/f'PhillyPause_Captions.{ext}', output/f'PhillyPause_Captions.{ext}')
    specifications = [
        ('PhillyPause_Master_1080p.mp4','landscape_picture.mp4',None,False,False),
        ('PhillyPause_Master_ProRes422.mov','landscape_picture_prores.mov',None,False,True),
        ('PhillyPause_Web_720p.mp4','landscape_picture.mp4','web',False,False),
        ('PhillyPause_Web_720p_Captioned.mp4','landscape_picture.mp4','web',True,False),
        ('PhillyPause_Vertical_1080x1920.mp4','vertical_picture.mp4','vertical',True,False),
        ('PhillyPause_Square_1080x1080.mp4','square_picture.mp4','square',True,False),
    ]
    if only:
        specifications=[s for s in specifications if s[0] in only]
    def export(spec):
        filename,picture,fmt,burn,prores=spec
        cmd=['ffmpeg','-y','-v','error','-i',str(ROOT/'renders'/picture),'-i',str(ROOT/'audio/mix.wav'),'-map','0:v:0','-map','1:a:0']
        if fmt is None:
            cmd += ['-c:v','copy']
        else:
            filters=[]
            if fmt=='web': filters.append('scale=1280:720:flags=lanczos')
            if burn:
                # Run from ROOT to keep filter paths free of platform-specific escaping.
                filters.append(f"ass=captions/{fmt}.ass:fontsdir=assets/fonts")
            cmd += ['-vf',','.join(filters),'-c:v','libx264','-threads','4','-preset','medium','-crf','20' if fmt=='web' else '18','-pix_fmt','yuv420p']
        if prores:
            cmd += ['-c:a','pcm_s24le']
        else:
            cmd += ['-c:a','aac','-b:a','192k']
        cmd += ['-ar','48000','-ac','2','-t',f'{DURATION:.9f}','-movflags','+faststart','-metadata','title=PhillyPause — A lightning rod for the AI age',str(output/filename)]
        subprocess.run(cmd,cwd=ROOT,check=True)
        return {'file':filename,'bytes':(output/filename).stat().st_size}
    with ThreadPoolExecutor(max_workers=jobs) as pool:
        for result in pool.map(export,specifications):
            print(json.dumps(result),flush=True)
    run(['ffmpeg','-y','-v','error','-ss','66','-i',str(output/'PhillyPause_Web_720p.mp4'),'-frames:v','1','-q:v','2',str(output/'PhillyPause_Poster.jpg')])

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--output-dir',type=Path,default=ROOT/'exports')
    parser.add_argument('--jobs',type=int,default=3)
    parser.add_argument('--only',nargs='+',help='Optional exact export filename(s) to rebuild.')
    args=parser.parse_args()
    export_all(args.output_dir.resolve(),args.jobs,args.only)
