"""Sound, captions and finishing: timing, isolation and synchronisation.

The approved narration, soundtrack and caption files are pinned by hash. The
real commands run in fresh processes with PHILLYPAUSE_BUILD pointed at a
temporary folder:

  revision4_timing.py                         the soundtrack and SRT/VTT
  audio_score.py                              the score and foley stems
  finish.py --draft --only <the two web files>  a full-length 320 x 180 film, clean and captioned

Every file under Video/Explainer must be byte-identical afterwards. The
finished film is then checked independently of finish.py's own report:
frames, duration, audio against the approved mix, where the narration falls,
and burned captions appearing exactly when the SRT says. Run from
Video/Explainer:

  .venv/Scripts/python -m unittest discover -s production/tests -v
"""
from __future__ import annotations
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

PRODUCTION=Path(__file__).resolve().parents[1]
PROJECT=PRODUCTION.parent
SKIP={'.venv','__pycache__','.pytest_cache'}
SCRATCH=tempfile.TemporaryDirectory(prefix='phillypause-finish-')
WEB=['PhillyPause_Web_720p.mp4','PhillyPause_Web_720p_Captioned.mp4']
OBSOLETE='lightning rod for the AI age'
# The approved sources, as recovered for revision 5. Text files hash with CRLF folded to LF.
APPROVED={
    'audio/source_narration.mp3':'750795c9706b1adda1bcdaf5f7c81daaf8e3f60234eb2b2a7c23ec848f946e5a',
    'audio/PhillyPauseSiteHeroVideo.mp3':'750795c9706b1adda1bcdaf5f7c81daaf8e3f60234eb2b2a7c23ec848f946e5a',
    'audio/mix.wav':'691420ef91d070ba12676c33854ea4a10b9359b851ecb9ead7a0be5228565480',
    'audio/mix_revision4.wav':'f39f13bc8e781085cda894c5bd2926ae9101d6561cf127445e72b83f53de41e4',
    'audio/voice.wav':'ec1ab77ba6b97d26ce724aaa49e54a9b7bf56a10409ea9ba10d222d4da282022',
    'audio/music.wav':'c83247556da2d70107ebe3cc9c409c587d2c18e073265b052a485c1189e82079',
    'audio/foley.wav':'f546ae365e19db7052c02e556fdc63461b9befe481c9924d47800886f9365b5c',
    'audio/score_metadata.json':'c33474711734719dce63562eb667bc11cf489fec3f821ff414a2e0dd45f76683',
    'audio_alignment.json':'d2dbecdbf8a6230d46ac50f9bda09c40f7b108a8902ce4ff72e54453f2c43119',
    'PhillyPause_Captions.srt':'fb0be56e85b6de71fe1d67c24ec15d3988a58dbed289bd2a9b4531fe76817b92',
    'PhillyPause_Captions.vtt':'1adfaa3be2bf5b55bb3775b848b2d6de2653fecd4b634b30ec52bb163fe63b3d',
}
MODULES=('paths','revision4_timing','finish','audio_score','film','art','transitions','paper_crumple')
_previous_build=os.environ.get('PHILLYPAUSE_BUILD')


def setUpModule():
    # In-process imports resolve paths.BUILD once; keep even that away from production/build.
    os.environ['PHILLYPAUSE_BUILD']=str(Path(SCRATCH.name)/'unit')
    sys.path.insert(0,str(PRODUCTION))


def tearDownModule():
    # Leave the interpreter as found, so another test module's paths.BUILD is its own.
    for name in MODULES:sys.modules.pop(name,None)
    sys.path.remove(str(PRODUCTION))
    if _previous_build is None:os.environ.pop('PHILLYPAUSE_BUILD',None)
    else:os.environ['PHILLYPAUSE_BUILD']=_previous_build
    SCRATCH.cleanup()


def tree(root):
    """sha256 of every file under root, except virtualenvs and bytecode."""
    digest={}
    for folder,dirs,files in os.walk(root):
        dirs[:]=[d for d in dirs if d not in SKIP]
        for name in files:
            path=Path(folder)/name
            digest[path.relative_to(root).as_posix()]=hashlib.sha256(path.read_bytes()).hexdigest()
    return digest


def command(build,*args):
    env=dict(os.environ,PHILLYPAUSE_BUILD=str(build),PYTHONHASHSEED='0')
    result=subprocess.run([sys.executable,*map(str,args)],cwd=PROJECT,env=env,capture_output=True,text=True,timeout=3600)
    if result.returncode:raise AssertionError(f'{args} failed:\n{result.stdout[-4000:]}{result.stderr[-4000:]}')
    return result


def probe(path):
    return json.loads(subprocess.run(['ffprobe','-v','error','-show_streams','-show_format','-of','json',str(path)],
                                     check=True,capture_output=True).stdout)


def frames(path,numbers,size):
    """Decoded RGB frames by index, in the order asked."""
    expression='+'.join(f'eq(n\\,{n})' for n in sorted(set(numbers)))
    raw=subprocess.run(['ffmpeg','-v','error','-i',str(path),'-map','0:v:0','-vf',f'select={expression}',
                        '-fps_mode','passthrough','-f','rawvideo','-pix_fmt','rgb24','-'],check=True,capture_output=True).stdout
    width,height=size
    decoded=dict(zip(sorted(set(numbers)),np.frombuffer(raw,np.uint8).reshape(-1,height,width,3).astype(np.int16)))
    return [decoded[n] for n in numbers]


class Timeline(unittest.TestCase):
    def test_constants(self):
        import film,finish,revision4_timing as timing
        self.assertEqual((film.FPS,film.INTRO_SECONDS,film.END_HOLD_START,film.END_HOLD_SECONDS,film.DURATION),(24,1.0,66.0,10.0,76.0))
        self.assertEqual((finish.FRAMES,round(film.END_HOLD_START*film.FPS)),(1824,1584))
        self.assertEqual((timing.OFFSET_SAMPLES,timing.TOTAL_SAMPLES,timing.OFFSET_MS),(48000,76*48000,1000))

    def test_picture_lead_in_moves_and_closing_card_holds(self):
        import film
        f=film.Film('landscape')
        def raw(t):return f.frame(t,192).toarray().tobytes()
        lead=[raw(n/24) for n in (0,6,12,18,23)]
        self.assertEqual(len(set(lead)),len(lead),'the one-second lead-in must move')
        hold={raw(t) for t in (66,68.5,71,73.5,1823/24)}
        self.assertEqual(len(hold),1,'the closing card must be still from 66 s to the end')


class AfterEffectsTrace(unittest.TestCase):
    def test_sound_is_on_the_film_clock(self):
        import film
        audio={a['file']:a for a in film.TRACE_AUDIO}
        for name in audio:self.assertIn(name,APPROVED,'the AE timeline uses only approved sound')
        self.assertEqual((audio['audio/mix_revision4.wav']['startTime'],audio['audio/mix_revision4.wav'].get('enabled',True)),(0,True),
                         'the 76-second mix already carries the lead-in')
        self.assertNotIn('audio/mix.wav',audio,'the story-clock mix would play a second early')
        for name in ('audio/voice.wav','audio/music.wav','audio/foley.wav'):
            self.assertEqual((audio[name]['startTime'],audio[name]['enabled']),(film.INTRO_SECONDS,False))

    def test_markers_are_the_story_beats(self):
        import film
        jsx=(PRODUCTION/'Build_PhillyPause_AfterEffects.jsx').read_text(encoding='utf-8')
        beats=[float(s) for s in re.findall(r'^\s*\[(\d+\.\d+), "\d\d - ',jsx,re.M)]
        self.assertEqual(beats,film.STARTS[:-1],'markers are story-clock beats, shifted by leadInSeconds in the builder')


class Captions(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import revision4_timing as timing
        cls.timing=timing
        cls.narration=timing.narration_cues()
        cls.playback=timing.playback_cues()
        cls.alignment=json.loads((PRODUCTION/'audio_alignment.json').read_text(encoding='utf-8'))

    def test_lead_in_offset_applied_exactly_once(self):
        self.assertEqual(len(self.playback),31)
        for story,film in zip(self.narration,self.playback):
            self.assertEqual((film['startMs']-story['startMs'],film['endMs']-story['endMs'],film['text']),(1000,1000,story['text']))
        self.assertEqual((self.narration[0]['startMs'],self.playback[0]['startMs']),(133,1133))
        self.assertEqual(self.playback[-1]['endMs'],65444)

    def test_cues_follow_the_narrated_words(self):
        words=self.alignment['words']
        for cue,source in zip(self.narration,self.alignment['captions']):
            first,last=words[source['first_word_index']],words[source['last_word_index']]
            with self.subTest(cue=cue['text']):
                self.assertEqual(cue['startMs'],round(first['start']*1000),'a caption starts on its first spoken word')
                self.assertTrue(-50<=cue['endMs']-round(last['end']*1000)<=600,'and ends with its last')

    def test_cues_fit_the_film(self):
        import film
        for before,cue in zip(self.playback,self.playback[1:]):self.assertLessEqual(before['endMs'],cue['startMs'])
        self.assertGreaterEqual(self.playback[0]['startMs'],film.INTRO_SECONDS*1000)
        self.assertLessEqual(self.playback[-1]['endMs'],film.END_HOLD_START*1000)

    def test_approved_tracks_are_the_shifted_narration(self):
        import paths
        t=self.timing
        srt=paths.text_bytes(PRODUCTION/'PhillyPause_Captions.srt').decode('utf-8')
        vtt=paths.text_bytes(PRODUCTION/'PhillyPause_Captions.vtt').decode('utf-8')
        self.assertEqual(t.caption_text(self.playback,','),srt)
        self.assertEqual('WEBVTT\n\n'+t.caption_text(self.playback,'.'),vtt)
        self.assertEqual(t.cues(srt),t.cues(vtt))
        self.assertEqual(t.cues(srt),self.playback)


class Approved(unittest.TestCase):
    def test_sources_match_their_pins(self):
        import paths
        for name,expected in APPROVED.items():
            path=PRODUCTION/name
            actual=paths.text_sha256(path) if path.suffix in ('.srt','.vtt','.json') else hashlib.sha256(path.read_bytes()).hexdigest()
            with self.subTest(name=name):self.assertEqual(actual,expected)

    def test_no_recovery_folders_or_obsolete_tagline(self):
        # The recovery archive's analysis/ and output/ folders were never restored; nothing may need them.
        for script in PRODUCTION.glob('*.py'):
            text=script.read_text(encoding='utf-8')
            with self.subTest(script=script.name):
                self.assertNotRegex(text,r'revision4_backup|[\'"/]analysis\b|[\'"/]output[\'"/]')
                self.assertNotIn(OBSOLETE.lower(),text.lower())


class Guard(unittest.TestCase):
    def test_finishing_refuses_source_destinations(self):
        import paths,finish,revision4_timing as timing,audio_score
        before=tree(PRODUCTION/'audio'),paths.text_bytes(PRODUCTION/'PhillyPause_Captions.srt')
        with self.assertRaises(PermissionError):timing.atomic_bytes(PRODUCTION/'PhillyPause_Captions.srt',b'')
        with self.assertRaises(PermissionError):finish.mux(WEB[0],Path('picture.mp4'),Path('sound.wav'),PRODUCTION,{})
        with self.assertRaises(PermissionError):audio_score.write_wav(PRODUCTION/'audio/music.wav',np.zeros((4,2)))
        self.assertEqual((tree(PRODUCTION/'audio'),paths.text_bytes(PRODUCTION/'PhillyPause_Captions.srt')),before)


class Finishing(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import paths,revision4_timing as timing,finish
        cls.paths,cls.timing,cls.finish=paths,timing,finish
        cls.build=Path(SCRATCH.name)/'build'
        cls.before=tree(PROJECT)
        command(cls.build,PRODUCTION/'revision4_timing.py')
        cls.first=tree(cls.build)
        command(cls.build,PRODUCTION/'audio_score.py')
        command(cls.build,PRODUCTION/'finish.py','--draft','--only',*WEB)
        cls.after=tree(PROJECT)
        cls.last=tree(cls.build)
        cls.draft=cls.build/'draft'
        cls.report=json.loads((cls.draft/'finish_report.json').read_text(encoding='utf-8'))

    def test_sources_untouched(self):
        changed=sorted(k for k in self.before.keys()&self.after.keys() if self.before[k]!=self.after[k])
        added=sorted(self.after.keys()-self.before.keys())
        removed=sorted(self.before.keys()-self.after.keys())
        self.assertEqual(dict(changed=changed,added=added,removed=removed),dict(changed=[],added=[],removed=[]))

    def test_everything_lands_in_build(self):
        expected={'audio/mix_revision4.wav','captions/PhillyPause_Captions.srt','captions/PhillyPause_Captions.vtt',
                  'captions/web.ass','captions/vertical.ass','captions/square.ass','reports/revision4_timing.json',
                  'audio/score/music.wav','audio/score/foley.wav','audio/score/score_metadata.json',
                  'renders/landscape_320.mp4','draft/finish_report.json','draft/PhillyPause_Poster.jpg',
                  'draft/PhillyPause_Captions.srt','draft/PhillyPause_Captions.vtt',*('draft/'+name for name in WEB)}
        self.assertLessEqual(expected,set(self.last))
        self.assertFalse([k for k in self.last if k.endswith('.tmp') or 'staging' in k])

    def test_rebuild_is_identical(self):
        for name in ('audio/mix_revision4.wav','captions/PhillyPause_Captions.srt','captions/PhillyPause_Captions.vtt'):
            with self.subTest(name=name):self.assertEqual(self.first[name],self.last[name])

    def test_soundtrack_is_the_approved_mix(self):
        t=self.timing
        built=t.pcm24(self.build/'audio/mix_revision4.wav')
        source=t.pcm24(PRODUCTION/'audio/mix.wav')
        lead=t.OFFSET_SAMPLES*t.SAMPLE_BYTES
        self.assertEqual(len(built),t.TOTAL_SAMPLES*t.SAMPLE_BYTES)
        self.assertFalse(any(built[:lead]),'one second of digital silence first')
        self.assertTrue(built[lead:lead+len(source)]==source,'then the approved mix, bit for bit')
        self.assertFalse(any(built[lead+len(source):]),'then silence to 76 s')
        self.assertTrue(built==t.pcm24(PRODUCTION/'audio/mix_revision4.wav'),'the approved 76-second mix, sample for sample')

    def test_score_regenerates_the_approved_stems(self):
        for stem in ('music','foley'):
            with self.subTest(stem=stem):
                self.assertTrue(self.timing.pcm24(self.build/f'audio/score/{stem}.wav')==self.timing.pcm24(PRODUCTION/f'audio/{stem}.wav'))

    def test_burned_layouts_are_the_r3_layouts_shifted_once(self):
        def shifted(line):
            def later(m):
                cs=((int(m[1])*60+int(m[2]))*60+int(m[3]))*100+int(m[4])+100
                h,cs=divmod(cs,360000);mi,cs=divmod(cs,6000);s,cs=divmod(cs,100)
                return f'{h}:{mi:02}:{s:02}.{cs:02}'
            return re.sub(r'(\d+):(\d\d):(\d\d)\.(\d\d)',later,line) if line.startswith('Dialogue:') else line
        for layout in ('web','vertical','square'):
            old=(PRODUCTION/'captions'/f'{layout}.ass').read_text(encoding='utf-8').splitlines()
            new=(self.build/'captions'/f'{layout}.ass').read_text(encoding='utf-8').splitlines()
            with self.subTest(layout=layout):self.assertEqual([shifted(line) for line in old],new)

    def test_finish_report_passes(self):
        self.assertEqual(self.report['status'],'PASS')
        self.assertEqual([e['file'] for e in self.report['exports']],WEB)
        for name in ('PhillyPause_Captions.srt','PhillyPause_Captions.vtt'):
            self.assertEqual(self.paths.text_bytes(self.draft/name),self.paths.text_bytes(PRODUCTION/name))

    def test_finished_film_timing(self):
        for name in WEB:
            info=probe(self.draft/name)
            video=next(s for s in info['streams'] if s['codec_type']=='video')
            audio=next(s for s in info['streams'] if s['codec_type']=='audio')
            with self.subTest(name=name):
                self.assertEqual((int(video['nb_frames']),video['r_frame_rate'],video['width'],video['height']),(1824,'24/1',320,180))
                self.assertAlmostEqual(float(info['format']['duration']),76,delta=1/24)
                self.assertAlmostEqual(float(audio['duration']),76,delta=.025)
                self.assertEqual((audio['codec_name'],audio['sample_rate'],audio['channels']),('aac','48000',2))
                title=info['format'].get('tags',{}).get('title','')
                self.assertEqual(title,'PhillyPause')
                self.assertNotIn(OBSOLETE.lower(),json.dumps(info).lower())

    def test_finished_audio_is_in_sync(self):
        t=self.timing;rate=t.ANALYSIS_RATE
        approved=t.mono(PRODUCTION/'audio/mix_revision4.wav');voice=t.mono(PRODUCTION/'audio/source_narration.mp3')
        for name in WEB:
            heard=t.mono(self.draft/name)
            for start,end in [(2,12),(30,40),(54,64)]:
                with self.subTest(name=name,window=start):
                    offset,r=t.locate(approved[start*rate:end*rate],heard,start*rate,rate//4)
                    self.assertLessEqual(abs(offset),2);self.assertGreater(r,.985)
                    # The narration, measured against the supplied recording itself: one second in.
                    story=start-1
                    offset,r=t.locate(voice[story*rate:(end-1)*rate],heard,start*rate,rate//2)
                    self.assertLessEqual(abs(offset),2);self.assertGreater(r,.9)
            self.assertEqual(float(np.max(np.abs(heard[:round(.9*rate)]))),0.0,'the lead-in is silent')

    def test_picture_holds_and_moves(self):
        clean=self.draft/WEB[0]
        lead=frames(clean,[0,6,12,18,23],(320,180))
        for a,b in zip(lead,lead[1:]):self.assertGreater(np.abs(a-b).mean(),.05,'the lead-in moves')
        hold=frames(clean,[1584,1585,1586,1588,1650,1720,1823],(320,180))
        # Codec noise is 2 or 3 levels. Without a keyframe at 66 s the encoder spends
        # the first frames of the hold refining the card, and pixels move by 35 or more.
        for later in hold[1:]:self.assertLessEqual(np.abs(hold[0]-later).max(),4,'the closing card is still')
        self.assertGreater(np.abs(frames(clean,[1500],(320,180))[0]-hold[0]).mean(),.05)

    def test_burned_captions_follow_the_srt(self):
        cues=self.timing.cues(self.paths.text_bytes(PRODUCTION/'PhillyPause_Captions.srt').decode('utf-8'))
        def frame(ms):return int(ms*24//1000)
        shown=[frame((c['startMs']+c['endMs'])//2) for c in cues]
        # Frames with no caption: before the first, in every gap of at least a quarter-second, and in the hold.
        hidden=[0,12,frame(cues[0]['startMs'])-2,1584,1823]
        hidden+=[frame((a['endMs']+b['startMs'])//2) for a,b in zip(cues,cues[1:]) if b['startMs']-a['endMs']>=250]
        numbers=shown+hidden
        clean=frames(self.draft/WEB[0],numbers,(320,180));captioned=frames(self.draft/WEB[1],numbers,(320,180))
        # Pixels the caption changes. At 320 x 180 the shortest, MY job., changes about 55; no caption, 0 or 1.
        ink=[int((np.abs(a-b).max(axis=2)>48).sum()) for a,b in zip(clean,captioned)]
        for n,count in zip(shown,ink[:len(shown)]):
            with self.subTest(frame=n):self.assertGreater(count,25,'a caption is on screen during its cue')
        for n,count in zip(hidden,ink[len(shown):]):
            with self.subTest(frame=n):self.assertLess(count,10,'and nowhere else')


if __name__=='__main__':
    unittest.main()
