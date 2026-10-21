"""Build the 76-second soundtrack and caption tracks from their narration-time sources.

The approved mix (audio/mix.wav) and the caption cues measured from the
narration (audio_alignment.json) both run on the story clock, whose zero is
the start of the supplied narration. The film opens with INTRO_SECONDS of
moving picture before story time zero, so sound and captions are delayed by
it, once, here and nowhere else:

  build/audio/mix_revision4.wav             48,000 silent samples, mix.wav bit for bit, silence to 76 s
  build/captions/PhillyPause_Captions.srt   YouTube
  build/captions/PhillyPause_Captions.vtt   website
  build/reports/revision4_timing.json

Each output must reproduce the approved revision 4 file it stands for, which
stays in production/ untouched: audio/mix_revision4.wav sample for sample (the
WAV header carries ffmpeg's version, so files are compared by PCM), and the
caption files byte for byte with line endings folded. The narration itself is
located in the new mix by cross-correlation and must sit exactly INTRO_SECONDS
in. Any failure stops the build. Nothing is written outside build/; see paths.py.

  python production/revision4_timing.py
  python production/revision4_timing.py --verify-preview path/to/finished.mp4

Requires ffmpeg/ffprobe and NumPy.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
from pathlib import Path

import paths
from film import INTRO_SECONDS, END_HOLD_START, DURATION

ROOT = paths.ROOT
RATE = 48000
CHANNELS = 2
SAMPLE_BYTES = CHANNELS*3
OFFSET_SAMPLES = round(INTRO_SECONDS*RATE)
OFFSET_MS = round(INTRO_SECONDS*1000)
TOTAL_SAMPLES = round(DURATION*RATE)
SOURCE_MIX = ROOT/'audio/mix.wav'
NARRATION = ROOT/'audio/source_narration.mp3'
ALIGNMENT = ROOT/'audio_alignment.json'
APPROVED_MIX = ROOT/'audio/mix_revision4.wav'
APPROVED_CAPTIONS = {extension: ROOT/('PhillyPause_Captions.'+extension) for extension in ('srt','vtt')}
TIMING = re.compile(r'(?m)^(\d{2}:\d{2}:\d{2}[,.]\d{3})([ \t]+-->[ \t]+)(\d{2}:\d{2}:\d{2}[,.]\d{3})([^\r\n]*)')
ANALYSIS_RATE = 16000


def sha256_bytes(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    return sha256_bytes(Path(path).read_bytes())


def run(arguments):
    return subprocess.run(arguments, check=True, capture_output=True)


def probe_audio(path):
    info = json.loads(run(['ffprobe','-v','error','-select_streams','a:0',
        '-show_entries','stream=codec_name,sample_rate,channels,duration,duration_ts,time_base,start_time',
        '-of','json',str(path)]).stdout)
    if not info['streams']:
        raise ValueError('No audio stream in ' + str(path))
    return info['streams'][0]


def pcm24(path):
    return run(['ffmpeg','-v','error','-i',str(path),'-map','0:a:0','-vn',
        '-ar',str(RATE),'-ac',str(CHANNELS),'-c:a','pcm_s24le','-f','s24le','-']).stdout


def mono(path):
    """Audio as float64 mono at ANALYSIS_RATE, for locating one recording inside another."""
    import numpy as np
    return np.frombuffer(run(['ffmpeg','-v','error','-i',str(path),'-map','0:a:0','-vn',
        '-ar',str(ANALYSIS_RATE),'-ac','1','-c:a','pcm_f32le','-f','f32le','-']).stdout,dtype='<f4').astype(np.float64)


def locate(needle, haystack, at, search):
    """Find needle in haystack near sample `at`: (best offset from `at` in samples, correlation there)."""
    import numpy as np
    low=max(0,at-search);region=haystack[low:at+len(needle)+search]
    a=needle-needle.mean()
    size=1<<(len(region)+len(a)).bit_length()
    cross=np.fft.irfft(np.fft.rfft(region,size)*np.conj(np.fft.rfft(a,size)),size)[:len(region)-len(a)+1]
    k=int(np.argmax(cross));b=region[k:k+len(a)]-region[k:k+len(a)].mean()
    return low+k-at,float(np.dot(a,b)/np.sqrt(np.dot(a,a)*np.dot(b,b)))


def milliseconds(value):
    h,m,s,ms = map(int,re.split('[:,.]',value))
    return ((h*60+m)*60+s)*1000+ms


def timestamp(value, separator):
    if value < 0:
        raise ValueError('Negative caption time')
    seconds,ms=divmod(value,1000);minutes,seconds=divmod(seconds,60);hours,minutes=divmod(minutes,60)
    return f'{hours:02d}:{minutes:02d}:{seconds:02d}{separator}{ms:03d}'


def cues(text):
    lines=text.splitlines();result=[]
    for i,line in enumerate(lines):
        match=TIMING.match(line)
        if not match:
            continue
        content=[]
        for following in lines[i+1:]:
            if not following.strip(): break
            content.append(following)
        result.append(dict(startMs=milliseconds(match[1]),endMs=milliseconds(match[3]),text='\n'.join(content)))
    return result


def narration_cues():
    """Caption cues on the story clock, in milliseconds, as measured from the narration."""
    data=json.loads(ALIGNMENT.read_text(encoding='utf-8'))
    return [dict(startMs=round(c['start']*1000),endMs=round(c['end']*1000),text=c['text']) for c in data['captions']]


def playback_cues():
    """The same cues on the film's clock. The only place the lead-in offset is applied to captions."""
    return [dict(c,startMs=c['startMs']+OFFSET_MS,endMs=c['endMs']+OFFSET_MS) for c in narration_cues()]


def caption_text(track, separator):
    return '\n'.join(f"{i}\n{timestamp(c['startMs'],separator)} --> {timestamp(c['endMs'],separator)}\n{c['text']}\n"
                     for i,c in enumerate(track,1))


def atomic_bytes(destination, data):
    destination=paths.writable(destination)
    temporary=destination.with_name(destination.name+'.tmp')
    temporary.write_bytes(data)
    os.replace(temporary,destination)


def build_captions():
    """Write the SRT and VTT tracks under BUILD; stop if either differs from its approved file."""
    track=playback_cues()
    for previous,cue in zip([None]+track,track):
        if not (OFFSET_MS<=cue['startMs']<cue['endMs']<=END_HOLD_START*1000):
            raise ValueError(f'Caption {cue["text"]!r} falls outside the narration window: {cue}')
        if previous and previous['endMs']>cue['startMs']:
            raise ValueError(f'Caption {cue["text"]!r} overlaps the one before it')
    texts=dict(srt=caption_text(track,','),vtt='WEBVTT\n\n'+caption_text(track,'.'))
    assert cues(texts['srt'])==cues(texts['vtt'])==[dict(c) for c in track], 'SRT, VTT and cues disagree'
    outputs={}
    for extension,text in texts.items():
        data=text.encode('utf-8');approved=APPROVED_CAPTIONS[extension]
        destination=paths.output('captions',approved.name)
        atomic_bytes(destination,data)
        if data!=paths.text_bytes(approved):
            raise ValueError(f'{destination} differs from the approved {approved.name}. If audio_alignment.json '
                             'was edited on purpose, review the new track and approve it by replacing that file.')
        outputs[extension]=destination
    return outputs,dict(cues=len(track),firstStartMs=track[0]['startMs'],lastEndMs=track[-1]['endMs'],
        offsetMs=OFFSET_MS,source=paths.portable(ALIGNMENT),sourceSHA256=sha256_file(ALIGNMENT),
        files={extension:dict(path=paths.portable(path),sha256=sha256_file(path),
            matchesApproved=paths.portable(APPROVED_CAPTIONS[extension])) for extension,path in outputs.items()})


def narration_offsets(soundtrack):
    """Where the supplied narration sits in the soundtrack, measured, in three windows."""
    voice=mono(NARRATION);mix=mono(soundtrack);result=[]
    for start,end in [(1,11),(29,39),(50,60)]:
        at=start*ANALYSIS_RATE
        offset,r=locate(voice[at:end*ANALYSIS_RATE],mix,at+round(INTRO_SECONDS*ANALYSIS_RATE),ANALYSIS_RATE//2)
        result.append(dict(storySeconds=[start,end],offsetSeconds=INTRO_SECONDS+offset/ANALYSIS_RATE,correlation=round(r,4),
                           passes=offset==0 and r>=.95))
    return result


def build_soundtrack():
    """Write the 76-second mix under BUILD, proven to be the approved one sample for sample."""
    probe=probe_audio(SOURCE_MIX)
    if (probe['codec_name'],int(probe['sample_rate']),probe['channels'])!=('pcm_s24le',RATE,CHANNELS):
        raise ValueError(f'{SOURCE_MIX.name} must be 48 kHz stereo 24-bit PCM; it is {probe}')
    source=pcm24(SOURCE_MIX)
    samples=len(source)//SAMPLE_BYTES
    if len(source)%SAMPLE_BYTES or OFFSET_SAMPLES+samples>TOTAL_SAMPLES:
        raise ValueError(f'{SOURCE_MIX.name} does not fit a {DURATION:g}-second film after a {INTRO_SECONDS:g}-second lead-in')
    destination=paths.output('audio','mix_revision4.wav')
    staged=destination.with_name('mix_revision4.staging.wav')
    audio_filter=f'adelay={OFFSET_SAMPLES}S:all=1,apad=whole_len={TOTAL_SAMPLES},atrim=end_sample={TOTAL_SAMPLES}'
    run(['ffmpeg','-y','-v','error','-i',str(SOURCE_MIX),'-af',audio_filter,'-ar',str(RATE),'-ac',str(CHANNELS),
         '-c:a','pcm_s24le','-fflags','+bitexact',str(staged)])
    shifted=pcm24(staged);start=OFFSET_SAMPLES*SAMPLE_BYTES;end=start+len(source)
    checks=dict(samplesPerChannel=len(shifted)==TOTAL_SAMPLES*SAMPLE_BYTES,
        leadInIsDigitalSilence=not any(shifted[:start]),
        sourceMixBitExactAfterLeadIn=shifted[start:end]==source,
        tailIsDigitalSilence=not any(shifted[end:]),
        matchesApprovedMix=shifted==pcm24(APPROVED_MIX))
    narration=narration_offsets(staged)
    checks['narrationStartsAfterLeadIn']=all(item['passes'] for item in narration)
    if not all(checks.values()):
        raise ValueError(f'{staged} failed {[k for k,v in checks.items() if not v]}. Nothing was published.')
    os.replace(staged,destination)
    return destination,dict(path=paths.portable(destination),fileSHA256=sha256_file(destination),
        pcm24SHA256=sha256_bytes(shifted),codec='pcm_s24le',sampleRate=RATE,channels=CHANNELS,
        samplesPerChannel=TOTAL_SAMPLES,durationSeconds=DURATION,leadInSamples=OFFSET_SAMPLES,
        source=dict(path=paths.portable(SOURCE_MIX),fileSHA256=sha256_file(SOURCE_MIX),pcm24SHA256=sha256_bytes(source),
            samplesPerChannel=samples,durationSeconds=samples/RATE),
        sourceContentSeconds=[INTRO_SECONDS,(OFFSET_SAMPLES+samples)/RATE],
        approved=dict(path=paths.portable(APPROVED_MIX),fileSHA256=sha256_file(APPROVED_MIX)),
        narration=narration,checks=checks)


def write_report(name, report):
    path=paths.output('reports',name)
    path.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
    return path


def build():
    soundtrack,audio=build_soundtrack()
    captions,caption_report=build_captions()
    report=dict(status='PASS',revision=4,
        timeline=dict(totalSeconds=DURATION,leadInSeconds=INTRO_SECONDS,finalStillStartSeconds=END_HOLD_START,
            storyToPlaybackSeconds=INTRO_SECONDS),
        audio=audio,captions=caption_report)
    write_report('revision4_timing.json',report)
    return report


def check_audio(preview, reference):
    """Compare a finished file's audio with the soundtrack it was muxed from."""
    import numpy as np
    audio=mono(preview);source=mono(reference);results=[]
    for start,end in [(1,11),(29,39),(55,65.4)]:
        at=round(start*ANALYSIS_RATE)
        offset,r=locate(source[at:round(end*ANALYSIS_RATE)],audio,at,2000)
        results.append(dict(startSeconds=start,endSeconds=end,offsetSamplesAt16kHz=offset,
            offsetMilliseconds=offset*1000/ANALYSIS_RATE,correlation=r,passes=abs(offset)<=2 and r>=.985))
    lead_in=audio[:round(.9*INTRO_SECONDS*ANALYSIS_RATE)]
    peak=float(np.max(np.abs(lead_in)))
    peak_db=None if peak==0 else float(20*np.log10(peak))
    duration=float(probe_audio(preview).get('duration',len(audio)/ANALYSIS_RATE))
    passes=all(item['passes'] for item in results) and abs(duration-DURATION)<.025 and (peak==0 or peak_db<=-100)
    return dict(status='PASS' if passes else 'FAIL',audioDurationSeconds=duration,
        alignmentWindows=results,leadInPeakDBFS=peak_db,leadInDigitalSilence=peak==0,
        offsetConvention='Positive offset means the file\'s audio is later than the soundtrack')


def verify_preview(preview):
    soundtrack,_=build_soundtrack()
    report=dict(check_audio(preview,soundtrack),revision=4,preview=str(preview),previewSHA256=sha256_file(preview),
        reference=paths.portable(soundtrack),referenceSHA256=sha256_file(soundtrack))
    write_report('revision4_preview_'+Path(preview).stem+'.json',report)
    return report


def main():
    parser=argparse.ArgumentParser(description=__doc__,formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--verify-preview',type=Path,help='check a finished file\'s audio against the soundtrack instead')
    args=parser.parse_args()
    report=verify_preview(args.verify_preview.resolve()) if args.verify_preview else build()
    print(json.dumps(report,indent=2))
    if report['status']!='PASS': raise SystemExit(1)


if __name__=='__main__': main()
