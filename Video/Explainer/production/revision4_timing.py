"""Build and verify revision 4 audio/caption timing without changing the source mix.

Default: prepend exactly 48,000 silent samples to audio/mix.wav, preserve its
complete PCM payload, pad to 76 seconds, and shift the backed-up revision 3
SRT/VTT cues by 1,000 ms. Inputs are validated before production/output writes.

  python3 production/revision4_timing.py
  python3 production/revision4_timing.py --verify-preview path/to/preview.mp4

The caption source directory is deliberately the revision 3 backup, making
repeat runs idempotent. A relocated source package can supply --caption-source.
Requires ffmpeg/ffprobe; encoded-preview checks additionally require NumPy.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BASE = ROOT.parent
RATE = 48000
CHANNELS = 2
OFFSET_SAMPLES = RATE
OFFSET_MS = 1000
TOTAL_SAMPLES = 76 * RATE
TIMING = re.compile(r'(?m)^(\d{2}:\d{2}:\d{2}[,.]\d{3})([ \t]+-->[ \t]+)(\d{2}:\d{2}:\d{2}[,.]\d{3})([^\r\n]*)')


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


def milliseconds(value):
    h,m,s,ms = map(int,re.split('[:,.]',value))
    return ((h*60+m)*60+s)*1000+ms


def timestamp(value, separator):
    if value < 0:
        raise ValueError('Negative caption time')
    seconds,ms=divmod(value,1000);minutes,seconds=divmod(seconds,60);hours,minutes=divmod(minutes,60)
    return f'{hours:02d}:{minutes:02d}:{seconds:02d}{separator}{ms:03d}'


def shift_caption_text(text, delta_ms):
    def replacement(match):
        return (timestamp(milliseconds(match[1])+delta_ms,match[1][8])+match[2]+
                timestamp(milliseconds(match[3])+delta_ms,match[3][8])+match[4])
    return TIMING.sub(replacement,text)


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


def atomic_bytes(destination, data):
    destination=Path(destination);destination.parent.mkdir(parents=True,exist_ok=True)
    temporary=destination.with_name(destination.name+'.revision4-tmp')
    temporary.write_bytes(data)
    os.replace(temporary,destination)


def build(args):
    original_mix=Path(args.audio_source)
    original_mix_hash=sha256_file(original_mix)
    original_probe=probe_audio(original_mix)
    assert original_probe['codec_name']=='pcm_s24le', 'Keep original 24-bit PCM source format'
    assert int(original_probe['sample_rate'])==RATE and original_probe['channels']==CHANNELS
    original_pcm=pcm24(original_mix)
    assert len(original_pcm)%(CHANNELS*3)==0
    original_samples=len(original_pcm)//(CHANNELS*3)
    assert original_samples+OFFSET_SAMPLES<=TOTAL_SAMPLES, 'Source does not fit requested duration'

    captions={};source_captions={};validation={}
    for extension in ('srt','vtt'):
        path=Path(args.caption_source)/('PhillyPause_Captions.'+extension)
        source_bytes=path.read_bytes();text=source_bytes.decode('utf-8')
        before=cues(text);shifted=shift_caption_text(text,OFFSET_MS);after=cues(shifted)
        assert len(before)==len(after)==31, 'Expected exactly 31 original caption cues'
        assert before[-1]['endMs']==64444, 'Caption source is not the unshifted revision 3 backup'
        assert after[0]['startMs']==1133 and after[-1]['endMs']==65444
        assert shift_caption_text(shifted,-OFFSET_MS)==text, 'Non-timing caption content changed'
        for old,new in zip(before,after):
            assert new['startMs']-old['startMs']==new['endMs']-old['endMs']==OFFSET_MS
            assert old['text']==new['text'] and new['startMs']<new['endMs']
        captions[extension]=shifted.encode('utf-8')
        source_captions[extension]=dict(path=str(path),sha256=sha256_bytes(source_bytes))
        validation[extension]=dict(cues=len(after),firstStartMs=after[0]['startMs'],lastEndMs=after[-1]['endMs'],
            everyCueShiftMs=OFFSET_MS,wordsAndNonTimingBytesUnchanged=True,cueTextSHA256=sha256_bytes('\n\n'.join(c['text'] for c in after).encode()))
    assert cues(captions['srt'].decode())==cues(captions['vtt'].decode()), 'SRT and VTT differ'

    work=Path(args.report).parent/'revision4_audio_timing'
    work.mkdir(parents=True,exist_ok=True)
    staged=work/'mix_revision4.staging.wav'
    audio_filter=f'adelay={OFFSET_SAMPLES}S:all=1,apad=whole_len={TOTAL_SAMPLES},atrim=end_sample={TOTAL_SAMPLES}'
    run(['ffmpeg','-y','-v','error','-i',str(original_mix),'-af',audio_filter,
         '-ar',str(RATE),'-ac',str(CHANNELS),'-c:a','pcm_s24le',str(staged)])
    shifted_probe=probe_audio(staged);shifted_pcm=pcm24(staged)
    byte_offset=OFFSET_SAMPLES*CHANNELS*3;source_end=byte_offset+len(original_pcm)
    assert len(shifted_pcm)==TOTAL_SAMPLES*CHANNELS*3
    assert not any(shifted_pcm[:byte_offset]), 'Lead-in contains nonzero samples'
    assert shifted_pcm[byte_offset:source_end]==original_pcm, 'Original mix PCM changed after shifting'
    assert not any(shifted_pcm[source_end:]), 'Tail padding contains nonzero samples'
    assert int(shifted_probe['duration_ts'])==TOTAL_SAMPLES and shifted_probe['time_base']=='1/48000'
    assert sha256_file(original_mix)==original_mix_hash, 'Original mix changed during build'
    for item in source_captions.values():
        assert sha256_file(item['path'])==item['sha256'], 'Caption backup changed during build'

    # All timing/PCM/caption tests have passed. Publish only the authorized R4 files.
    audio_output=Path(args.audio_output)
    audio_output.parent.mkdir(parents=True,exist_ok=True)
    os.replace(staged,audio_output)
    caption_outputs=[]
    for folder in (ROOT,Path(args.output_dir)):
        for extension,data in captions.items():
            destination=folder/('PhillyPause_Captions.'+extension)
            atomic_bytes(destination,data)
            assert destination.read_bytes()==data
            caption_outputs.append(dict(path=str(destination),sha256=sha256_bytes(data)))
    report=dict(status='PASS',revision=4,checkedAtUTC=datetime.now(timezone.utc).isoformat(),
        scope='One-second audio/caption shift only; no picture, artwork, source mix, or Library identities changed',
        leadIn=dict(durationSeconds=1,samplesPerChannel=OFFSET_SAMPLES,treatment='Digital silence',
            reason='The original mix begins at zero with a gentle onset, and its score starts after initial silence. A silent visual lead-in joins it cleanly while retaining the original mix.'),
        timeline=dict(totalSeconds=76,framesAt24Fps=1824,originalStoryOffsetSeconds=1,finalStillStartSeconds=66,finalStillEndSeconds=76),
        source=dict(audio=str(original_mix),audioFileSHA256=original_mix_hash,audioPCM24SHA256=sha256_bytes(original_pcm),
            audioSamplesPerChannel=original_samples,audioDurationSeconds=original_samples/RATE,captions=source_captions),
        audio=dict(path=str(audio_output),fileSHA256=sha256_file(audio_output),codec='pcm_s24le',sampleRate=RATE,channels=CHANNELS,
            samplesPerChannel=TOTAL_SAMPLES,durationSeconds=76,leadingSilentSamples=OFFSET_SAMPLES,
            originalContentStartSample=OFFSET_SAMPLES,originalContentEndSampleExclusive=OFFSET_SAMPLES+original_samples,
            originalContentStartSeconds=1,originalContentEndSeconds=(OFFSET_SAMPLES+original_samples)/RATE,
            trailingSilentSamples=TOTAL_SAMPLES-OFFSET_SAMPLES-original_samples,
            shiftedOriginalPCM24SHA256=sha256_bytes(shifted_pcm[byte_offset:source_end]),
            completeOriginalMixPreservedBitExactly=True,leadingAndTrailingPaddingAreDigitalSilence=True),
        captions=dict(validation=validation,outputs=caption_outputs,srtAndVttCuesIdentical=True),
        originalMixFileUnchanged=sha256_file(original_mix)==original_mix_hash,
        libraryWritesPerformed=False)
    report_path=Path(args.report);report_path.parent.mkdir(parents=True,exist_ok=True)
    report_path.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


def verify_preview(args):
    import numpy as np
    preview=Path(args.verify_preview);reference=Path(args.audio_output)
    def floats(path):
        return np.frombuffer(run(['ffmpeg','-v','error','-i',str(path),'-map','0:a:0','-vn',
            '-ar','16000','-ac','1','-c:a','pcm_f32le','-f','f32le','-']).stdout,dtype='<f4')
    audio=floats(preview);source=floats(reference);sample_rate=16000;results=[]
    for start,end in [(1,11),(29,39),(55,65.4)]:
        a=audio[round(start*sample_rate):round(end*sample_rate)].astype(np.float64)
        b=source[round(start*sample_rate):round(end*sample_rate)].astype(np.float64)
        assert len(a)==len(b) and len(a)>sample_rate
        a-=a.mean();b-=b.mean()
        fft_size=1<<(len(a)+len(b)-2).bit_length()
        cross=np.fft.irfft(np.fft.rfft(a,fft_size)*np.conj(np.fft.rfft(b,fft_size)),fft_size)
        lags=np.arange(-2000,2001);offset=int(lags[np.argmax(cross[lags%fft_size])])
        if offset>0: aa,bb=a[offset:],b[:-offset]
        elif offset<0: aa,bb=a[:offset],b[-offset:]
        else: aa,bb=a,b
        r=float(np.dot(aa,bb)/np.sqrt(np.dot(aa,aa)*np.dot(bb,bb)))
        results.append(dict(startSeconds=start,endSeconds=end,offsetSamplesAt16kHz=offset,
            offsetMilliseconds=offset/16,correlation=r,passes=abs(offset)<=2 and r>=.985))
    first_900ms=audio[:round(.9*sample_rate)]
    peak=float(np.max(np.abs(first_900ms)))
    peak_db=None if peak==0 else float(20*np.log10(peak))
    probe=probe_audio(preview);duration=float(probe.get('duration',len(audio)/sample_rate))
    passes=all(item['passes'] for item in results) and abs(duration-76)<.025 and (peak==0 or peak_db<=-100)
    report=dict(status='PASS' if passes else 'FAIL',revision=4,scope='Encoded audio timing and silent lead-in',
        checkedAtUTC=datetime.now(timezone.utc).isoformat(),preview=str(preview),previewSHA256=sha256_file(preview),
        reference=str(reference),referenceSHA256=sha256_file(reference),audioDurationSeconds=duration,
        referenceOffsetFromOriginalMixSeconds=1,alignmentWindows=results,
        first900msPeakDBFS=peak_db,first900msDigitalSilence=peak==0,
        offsetConvention='Positive measured offset means encoded audio occurs later than the shifted reference')
    report_path=Path(args.report);report_path.parent.mkdir(parents=True,exist_ok=True)
    report_path.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))
    if not passes: raise SystemExit(1)


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--caption-source',type=Path,default=BASE/'analysis/revision4_backup/production')
    parser.add_argument('--audio-source',type=Path,default=ROOT/'audio/mix.wav')
    parser.add_argument('--audio-output',type=Path,default=ROOT/'audio/mix_revision4.wav')
    parser.add_argument('--output-dir',type=Path,default=BASE/'output')
    parser.add_argument('--verify-preview',type=Path)
    parser.add_argument('--report',type=Path)
    args=parser.parse_args()
    if args.report is None:
        args.report=BASE/'analysis'/('revision4_encoded_audio_verification.json' if args.verify_preview else 'revision4_timing_verification.json')
    if args.verify_preview: verify_preview(args)
    else: build(args)


if __name__=='__main__': main()
