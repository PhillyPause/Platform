#!/usr/bin/env python3
"""Finish the film: picture, soundtrack and captions, muxed into delivery files.

From Video/Explainer, with the virtualenv:

  .venv/Scripts/python production/finish.py                  every delivery file, into build/exports/
  .venv/Scripts/python production/finish.py --only PhillyPause_Web_720p.mp4
  .venv/Scripts/python production/finish.py --draft          the same at quarter size, into build/draft/

Builds the soundtrack and caption tracks (revision4_timing.py), writes the
burned-in caption layouts, renders each picture afresh with film.py, muxes,
then checks every finished file: 1,824 frames, 76 seconds, audio matching the
soundtrack, a moving lead-in and a still closing card from 66 seconds on.
finish_report.json records the result; any failed check exits nonzero.
Everything is written under production/build/; see paths.py.
Requires ffmpeg with libx264 and libass.
"""
from __future__ import annotations
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

from film import FPS, INTRO_SECONDS, END_HOLD_START, DURATION
import paths
import revision4_timing as timing

ROOT = paths.ROOT
FRAMES = round(DURATION * FPS)
TITLE = 'PhillyPause'
DRAFT_DIVISOR = 4
ASPECT = {'landscape': (16, 9), 'vertical': (9, 16), 'square': (1, 1)}
# File: picture format, delivery width, burned-in caption layout, and video
# codec: an x264 CRF, or the rendered picture's own H.264 or ProRes stream.
EXPORTS = {
    'PhillyPause_Master_1080p.mp4': ('landscape', 1920, None, 'copy'),
    'PhillyPause_Master_ProRes422.mov': ('landscape', 1920, None, 'prores'),
    'PhillyPause_Web_720p.mp4': ('landscape', 1280, None, 20),
    'PhillyPause_Web_720p_Captioned.mp4': ('landscape', 1280, 'web', 20),
    'PhillyPause_Vertical_1080x1920.mp4': ('vertical', 1080, None, 18),
    'PhillyPause_Vertical_1080x1920_Captioned.mp4': ('vertical', 1080, 'vertical', 18),
    # The LG StanbyME 2's panel turned upright. The caption layout scales with the picture.
    'PhillyPause_Vertical_1440x2560.mp4': ('vertical', 1440, None, 18),
    'PhillyPause_Vertical_1440x2560_Captioned.mp4': ('vertical', 1440, 'vertical', 18),
    'PhillyPause_Square_1080x1080.mp4': ('square', 1080, 'square', 18),
}
POSTER_SOURCE = 'PhillyPause_Web_720p.mp4'
# Burned-in caption layout: script resolution, font size, bottom margin.
LAYOUTS = {
    'web': (1280, 720, 32, 38),
    'vertical': (1080, 1920, 46, 190),
    'square': (1080, 1080, 42, 60),
}


def run(cmd):
    subprocess.run(cmd, check=True)


def filter_path(path):
    """An absolute path as an ffmpeg filter option: forward slashes, drive colon escaped, quoted."""
    text = Path(path).resolve().as_posix().replace(':', r'\:').replace("'", r"'\''")
    return f"'{text}'"


def ass_time(ms):
    cs = (ms + 5) // 10
    h, cs = divmod(cs, 360000)
    m, cs = divmod(cs, 6000)
    s, cs = divmod(cs, 100)
    return f'{h}:{m:02}:{s:02}.{cs:02}'


def write_ass(layout):
    width, height, size, margin = LAYOUTS[layout]
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
    rows = []
    for cue in timing.playback_cues():
        # The placements below were set on the story clock, against scene timings in film.STARTS.
        story = cue['startMs'] / 1000 - INTRO_SECONDS
        # The vertical isolation counter occupies the bottom safe region.
        # Put these three one-line captions above the cabinet's printed sign.
        override = r'{\an8\pos(540,75)}' if layout == 'vertical' and 19.4 < story < 25.1 else ''
        # Keep the TEST placard fully visible while the narration stresses TODAY'S.
        if 36.0 < story < 38.0:
            override = r'{\an8\pos(%d,%d)}' % (width // 2, round(height * .055))
        text = cue['text'].replace('\n', r'\N')
        rows.append(f"Dialogue: 0,{ass_time(cue['startMs'])},{ass_time(cue['endMs'])},Default,,0,0,0,,{override}{text}")
    out = paths.output('captions', f'{layout}.ass')
    out.write_bytes((header + '\n'.join(rows) + '\n').encode('utf-8'))
    return out


def render(fmt, width, prores):
    """Render one silent picture afresh with film.py, in its own process."""
    picture = paths.output('renders', f'{fmt}_{width}.mp4')
    cmd = [sys.executable, str(ROOT / 'film.py'), '--format', fmt, '--width', str(width), '--output', str(picture)]
    if prores:
        cmd += ['--prores', str(picture.with_suffix('.mov'))]
    run(cmd)
    return picture


def mux(name, picture, soundtrack, folder, layouts):
    fmt, width, layout, video = EXPORTS[name]
    out = paths.writable(folder / name)
    source = picture.with_suffix('.mov') if video == 'prores' else picture
    cmd = ['ffmpeg', '-y', '-v', 'error', '-i', str(source), '-i', str(soundtrack), '-map', '0:v:0', '-map', '1:a:0']
    if video in ('copy', 'prores'):
        cmd += ['-c:v', 'copy']
    else:
        if layout:
            cmd += ['-vf', f'ass=filename={filter_path(layouts[layout])}:fontsdir={filter_path(ROOT / "assets/fonts")}']
        # The hold opens on a keyframe and every later frame copies it: at QP 51 x264 codes no
        # residual. Left to CRF, it spends the hold's first frames sharpening the card.
        hold = round(END_HOLD_START * FPS)
        cmd += ['-c:v', 'libx264', '-threads', '4', '-preset', 'medium', '-crf', str(video), '-pix_fmt', 'yuv420p',
                '-force_key_frames', f'{INTRO_SECONDS},{END_HOLD_START}', '-x264-params', f'zones={hold + 1},{FRAMES - 1},q=51']
    cmd += ['-c:a', 'pcm_s24le'] if video == 'prores' else ['-c:a', 'aac', '-b:a', '192k']
    cmd += ['-ar', '48000', '-ac', '2', '-t', f'{DURATION:.9f}', '-movflags', '+faststart',
            '-metadata', f'title={TITLE}', str(out)]
    run(cmd)
    return out


def stills(path):
    """Every stretch of half a second or more in which the picture does not change, as [start, end] seconds."""
    log = subprocess.run(['ffmpeg', '-hide_banner', '-nostats', '-v', 'info', '-i', str(path), '-map', '0:v:0',
                          '-vf', 'freezedetect=n=0.001:d=0.5', '-f', 'null', '-'],
                         check=True, capture_output=True, text=True).stderr
    starts = [float(x) for x in re.findall(r'freeze_start: ([\d.]+)', log)]
    ends = [float(x) for x in re.findall(r'freeze_end: ([\d.]+)', log)]
    return [[start, ends[i] if i < len(ends) else DURATION] for i, start in enumerate(starts)]


def hold_drift(path, size):
    """The most any pixel changes, in 8-bit levels, between the first frame of the hold and later ones.

    A picture straight from film.py shows 2, which is codec noise; a finished re-encode, 0.
    An encoder still refining the card after the motion before it shows 35 or more for the
    first few frames, which is why every encode keys at 66 s and mux() freezes the rest.
    """
    import numpy as np
    first = round(END_HOLD_START * FPS)
    numbers = [first, first + 1, first + 2, first + 3, first + 6, first + 12, first + 48, FRAMES - 1]
    expression = '+'.join(f'eq(n\\,{n})' for n in numbers)
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', str(path), '-map', '0:v:0', '-vf', f'select={expression}',
                          '-fps_mode', 'passthrough', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'],
                         check=True, capture_output=True).stdout
    frames = np.frombuffer(raw, np.uint8).reshape(len(numbers), size[1], size[0], 3).astype(np.int16)
    return int(np.abs(frames[1:] - frames[0]).max())


def check(path, size, soundtrack):
    probe = json.loads(subprocess.run(['ffprobe', '-v', 'error', '-show_streams', '-show_format', '-of', 'json', str(path)],
                                      check=True, capture_output=True).stdout)
    video = next(s for s in probe['streams'] if s['codec_type'] == 'video')
    duration = float(probe['format']['duration'])
    still = stills(path)
    tolerance = .5 / FPS
    sound = timing.check_audio(path, soundtrack)
    result = {
        'frames': int(video['nb_frames']), 'size': [video['width'], video['height']],
        'frameRate': video['r_frame_rate'], 'durationSeconds': duration,
        'title': probe['format'].get('tags', {}).get('title'), 'stillIntervals': still,
        'holdDriftLevels': hold_drift(path, size), 'audio': sound,
    }
    result['checks'] = {
        'frames': result['frames'] == FRAMES and result['frameRate'] == f'{FPS}/1',
        'size': result['size'] == list(size),
        'duration': abs(duration - DURATION) <= 1 / FPS,
        'title': result['title'] == TITLE,
        'audioMatchesSoundtrack': sound['status'] == 'PASS',
        'leadInMoves': not any(start < INTRO_SECONDS for start, _ in still),
        'closingCardStillFrom66': result['holdDriftLevels'] <= 4 and any(
            start <= END_HOLD_START + tolerance and end >= DURATION - tolerance for start, end in still),
    }
    result['passes'] = all(result['checks'].values())
    return result


def sha256(path):
    with open(path, 'rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def finish(names, draft=False, jobs=3):
    folder = paths.BUILD / ('draft' if draft else 'exports')
    divisor = DRAFT_DIVISOR if draft else 1
    soundtrack, audio = timing.build_soundtrack()
    captions, caption_report = timing.build_captions()
    layouts = {layout: write_ass(layout) for layout in LAYOUTS}
    # One render per picture format and size; ProRes rides along with the H.264 pass.
    renders = {}
    for name in names:
        fmt, width, _, video = EXPORTS[name]
        key = (fmt, width // divisor)
        renders[key] = renders.get(key, False) or video == 'prores'
    # Sequential on purpose: film.py's artwork cache is shared and not safe for concurrent writers.
    pictures = {key: render(*key, prores) for key, prores in renders.items()}

    def deliver(name):
        fmt, width, _, _ = EXPORTS[name]
        width //= divisor
        out = mux(name, pictures[(fmt, width)], soundtrack, folder, layouts)
        w, h = ASPECT[fmt]
        return out, check(out, (width, width * h // w), soundtrack)

    with ThreadPoolExecutor(max_workers=jobs) as pool:
        delivered = list(pool.map(deliver, names))
    extras = []
    for path in captions.values():
        shutil.copyfile(path, paths.writable(folder / path.name))
        extras.append(folder / path.name)
    if POSTER_SOURCE in names:
        poster = paths.writable(folder / 'PhillyPause_Poster.jpg')
        run(['ffmpeg', '-y', '-v', 'error', '-ss', str(END_HOLD_START), '-i', str(folder / POSTER_SOURCE),
             '-frames:v', '1', '-q:v', '2', str(poster)])
        extras.append(poster)
    report = {
        'status': 'PASS' if all(result['passes'] for _, result in delivered) else 'FAIL',
        'draft': draft, 'title': TITLE,
        'timeline': {'fps': FPS, 'frames': FRAMES, 'durationSeconds': DURATION, 'leadInSeconds': INTRO_SECONDS,
                     'finalStillStartSeconds': END_HOLD_START},
        'soundtrack': audio, 'captions': caption_report,
        'exports': [{'file': out.name, 'bytes': out.stat().st_size, 'sha256': sha256(out), **result}
                    for out, result in delivered],
        'extras': [{'file': p.name, 'bytes': p.stat().st_size, 'sha256': sha256(p)} for p in extras],
    }
    paths.writable(folder / 'finish_report.json').write_text(json.dumps(report, indent=2) + '\n',
                                                              encoding='utf-8', newline='\n')
    for item in report['exports']:
        failed = [k for k, v in item['checks'].items() if not v]
        print(json.dumps({'file': item['file'], 'bytes': item['bytes'], 'failed': failed}), flush=True)
    print(f'{report["status"]}: {folder / "finish_report.json"}', flush=True)
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--only', nargs='+', choices=list(EXPORTS), metavar='FILE',
                        help='exact export filename(s) to build: ' + ', '.join(EXPORTS))
    parser.add_argument('--draft', action='store_true',
                        help=f'render at 1/{DRAFT_DIVISOR} size into build/draft/, for checking sync and captions')
    parser.add_argument('--jobs', type=int, default=3)
    args = parser.parse_args()
    names = [name for name in EXPORTS if not args.only or name in args.only]
    if finish(names, args.draft, args.jobs)['status'] != 'PASS':
        raise SystemExit(1)
