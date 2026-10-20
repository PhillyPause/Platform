"""Render the current PhillyPause website approval preview at 1280 × 720.

Revision 5 repairs line attachments, fits robots inside their jars, joins the
instruction cylinder, matches closing-label padding, and crumples one sheet.
The one-second lead-in, synchronized audio/captions and ten-second hold remain.
Full-resolution production exports and director documentation remain deferred.
"""
from pathlib import Path
import argparse
import hashlib
import json
import subprocess
import time

from film import Film, INTRO_SECONDS, END_HOLD_START, END_HOLD_SECONDS, DURATION, FPS, skia
import paths
import revision4_timing as timing


def render(output, work, width=1280, preset='medium', crf=20):
    work = paths.writable(work)
    work.mkdir(exist_ok=True)
    output = paths.writable(output)
    # Built afresh under build/ and checked sample for sample against the approved mix.
    audio, _ = timing.build_soundtrack()
    film = Film('landscape')
    height = round(width * film.H / film.W)
    total = round(DURATION * FPS)
    hold_frame = round(END_HOLD_START * FPS)
    assert (total, hold_frame, total-hold_frame) == (1824, 1584, 240)
    frozen = film.frame(END_HOLD_START, width)
    frozen.save(str(work / 'closing_card.png'), skia.kPNG)
    freeze_bytes = frozen.toarray(colorType=skia.ColorType.kRGBA_8888_ColorType).tobytes()
    freeze_hash = hashlib.sha256(freeze_bytes).hexdigest()
    final_source = film.frame((total-1)/FPS, width)
    assert hashlib.sha256(final_source.toarray(colorType=skia.ColorType.kRGBA_8888_ColorType).tobytes()).hexdigest() == freeze_hash
    commands = [
        'ffmpeg', '-y', '-v', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgba',
        '-s', f'{width}x{height}', '-r', str(FPS), '-i', '-', '-i', str(audio),
        '-map', '0:v:0', '-map', '1:a:0', '-c:v', 'libx264', '-threads', '4',
        '-preset', preset, '-crf', str(crf), '-pix_fmt', 'yuv420p',
        '-frames:v', str(total), '-force_key_frames', f'{INTRO_SECONDS},{END_HOLD_START}',
        # Every frame after the hold's keyframe copies it; see finish.py's mux().
        '-x264-params', f'zones={hold_frame+1},{total-1},q=51',
        '-c:a', 'aac', '-b:a', '192k', '-ar', '48000', '-ac', '2',
        '-t', str(DURATION), '-movflags', '+faststart', '-metadata',
        'title=PhillyPause — revision 5 approval preview', str(output)
    ]
    process = subprocess.Popen(commands, stdin=subprocess.PIPE)
    started = time.monotonic()
    for frame in range(total):
        if frame >= hold_frame:
            raw = freeze_bytes
        else:
            picture = film.frame(frame/FPS, width)
            raw = picture.toarray(colorType=skia.ColorType.kRGBA_8888_ColorType).tobytes()
        process.stdin.write(raw)
        if frame % 120 == 0 or frame == total-1:
            print(f'Frame {frame+1}/{total}; {time.monotonic()-started:.1f}s elapsed', flush=True)
    process.stdin.close()
    if process.wait():
        raise RuntimeError('Website preview encoding failed')
    report = {
        'revision': 5, 'purpose': 'website approval preview', 'file': output.name,
        'width': width, 'height': height, 'fps': FPS, 'frames': total,
        'duration_seconds': DURATION, 'animated_lead_in_seconds': INTRO_SECONDS,
        'story_time_relation': 'output_time = original_story_time + 1.0',
        'freeze_start_seconds': END_HOLD_START, 'frozen_hold_seconds': END_HOLD_SECONDS,
        'frozen_rgba_sha256': freeze_hash, 'source_last_frame_matches_first_frozen_frame': True,
        'audio': str(audio), 'audio_offset_applied_in_wav_seconds': INTRO_SECONDS,
        'closing_cta': 'SHARE THIS VIDEO', 'closing_tagline': None,
        'copyright': '© 2026 PhillyPause', 'qr_payload': 'https://phillypause.org',
        'picture_changes': ['Line endpoints follow the parts they attach to',
                            'Whole robots fit beneath jar domes and inside bases',
                            'Instruction cylinder caps and sides share endpoints',
                            'Closing labels use matching visible padding',
                            'Connected paper folds into a crumpled wad'],
        'encoder': {'video': 'H.264', 'crf': crf, 'preset': preset, 'audio': 'AAC 192 kbps'},
        'bytes': output.stat().st_size,
        'sha256': hashlib.file_digest(output.open('rb'), 'sha256').hexdigest(),
        'render_elapsed_seconds': round(time.monotonic()-started, 3)
    }
    (work/'assembly.json').write_text(json.dumps(report, indent=2)+'\n')
    print('Review preview saved: '+str(output), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--work', type=Path, default=paths.BUILD/'review')
    parser.add_argument('--width', type=int, default=1280)
    parser.add_argument('--preset', default='medium')
    parser.add_argument('--crf', type=int, default=20)
    args = parser.parse_args()
    render(args.output.resolve(), args.work.resolve(), args.width, args.preset, args.crf)
