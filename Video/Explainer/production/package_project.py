#!/usr/bin/env python3
"""Make a portable source archive after rendering, finishing and preflight.

Excludes dependencies, temporary renders and all copyrighted reference stills.
Reads finish.py's exports from build/exports/ and writes the archive under
build/; see paths.py.
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

import paths

ROOT=paths.ROOT

def portable(value):
    if isinstance(value,dict):return {k:portable(v) for k,v in value.items()}
    if isinstance(value,list):return [portable(v) for v in value]
    if isinstance(value,str):return value.replace(str(ROOT)+'/', '')
    return value

def build(destination,exports):
    rootfiles=[
      'README.txt','Asset_Sources.txt','Production_Notes.txt',
      'Animation_Brief.md','AE_README.txt','Build_PhillyPause_AfterEffects.jsx',
      'film.py','art.py','transitions.py','audio_score.py','finish.py','package_project.py',
      'requirements.txt','master_timeline.json','audio_alignment.json',
      'PhillyPause_Captions.srt','PhillyPause_Captions.vtt',
      'Export_Validation.json','AE_Preflight.json','Transition_Design.json',
      'QR_Validation.json','QR_Final_Validation.json',
    ]
    selected=[ROOT/p for p in rootfiles]
    for sub in ('assets','audio','captions'):
      for p in (ROOT/sub).rglob('*'):
        if not p.is_file():continue
        parts=p.relative_to(ROOT).parts
        if any(x.startswith('.') or x in ('__pycache__','qa') for x in parts):continue
        if p.name=='metadata_2002695523.json':continue
        selected.append(p)
    missing=[str(p.relative_to(ROOT)) for p in selected if not p.is_file()]
    if missing:raise FileNotFoundError('Missing package inputs: '+', '.join(missing))
    manifest=[];destination=paths.writable(destination)
    prefix='PhillyPause_Production/'
    with zipfile.ZipFile(destination,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True) as archive:
      for p in sorted(set(selected)):
        relative=p.relative_to(ROOT).as_posix()
        data=p.read_bytes()
        if p.suffix=='.json' and p.name!='master_timeline.json':
          data=(json.dumps(portable(json.loads(data)),ensure_ascii=False,indent=2)+'\n').encode()
        archive.writestr(prefix+relative,data)
        manifest.append({'path':relative,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
      for name in ('PhillyPause_Web_720p.mp4','PhillyPause_Poster.jpg'):
        p=exports/name;data=p.read_bytes();relative='reference_movie/'+name
        archive.writestr(prefix+relative,data,compress_type=zipfile.ZIP_STORED)
        manifest.append({'path':relative,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
      archive.writestr(prefix+'Package_Manifest.json',json.dumps({'files':manifest,'note':'Copyrighted reference-film stills and runtime dependencies are excluded. All historical source TIFFs/JPEGs are retained.'},indent=2)+'\n')
    with zipfile.ZipFile(destination) as archive:
      failure=archive.testzip()
      if failure:raise IOError('Archive CRC failure: '+failure)
      names=archive.namelist()
      assert not any('/reference/' in n or '/python_libs/' in n for n in names)
    print(json.dumps({'file':str(destination),'bytes':destination.stat().st_size,'files':len(manifest)+1,'crc_check':'passed'}),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--exports',type=Path,default=paths.BUILD/'exports')
    parser.add_argument('--output',type=Path,default=paths.BUILD/'PhillyPause_Production_Files.zip')
    args=parser.parse_args()
    build(args.output.resolve(),args.exports.resolve())
