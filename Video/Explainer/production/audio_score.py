#!/usr/bin/env python3
"""Original score and foley for PhillyPause's 2026 explainer.

All notes, arrangements, oscillator models, noise and timing are generated here.
No recorded samples, third-party compositions, model-generated music or voices.
The timbres are deliberately synthetic evocations of small acoustic instruments,
not claimed recordings of an orchestra. The supplied narration is read only to
make a very gentle, slow level envelope; its audio is never added to either stem.

Run: python audio_score.py
Requires Python, numpy, scipy and ffmpeg. Outputs 48 kHz stereo 24-bit PCM WAV.
"""
from __future__ import annotations

import json
import math
from pathlib import Path
import subprocess

import numpy as np
from scipy.ndimage import uniform_filter1d
from scipy.signal import butter, sosfilt

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "audio"
OUT.mkdir(parents=True, exist_ok=True)
SR = 48_000
DURATION = 67.5
N = round(DURATION * SR)
RNG = np.random.default_rng(1760_2026)
MUSIC = np.zeros((N, 2), dtype=np.float64)
FOLEY = np.zeros((N, 2), dtype=np.float64)
EVENTS: list[dict] = []


def hz(note: float) -> float:
    return 440.0 * 2 ** ((note - 69) / 12)


def attack_release(t: np.ndarray, length: float, attack=.025, release=.09) -> np.ndarray:
    a = np.clip(t / attack, 0, 1)
    a = np.sin(a * np.pi / 2) ** 2
    r = np.clip((length - t) / release, 0, 1)
    return a * np.sin(r * np.pi / 2) ** 2


def normalize(x: np.ndarray) -> np.ndarray:
    peak = np.max(np.abs(x))
    return x / peak if peak > 0 else x


def deposit(stem: np.ndarray, signal: np.ndarray, at: float, gain: float, pan=0.0) -> None:
    start = round(at * SR)
    if start >= N:
        return
    left = max(0, -start)
    start = max(0, start)
    count = min(len(signal) - left, N - start)
    if count <= 0:
        return
    angle = (np.clip(pan, -1, 1) + 1) * np.pi / 4
    stereo_gain = np.array([np.cos(angle), np.sin(angle)]) * gain
    stem[start:start + count] += signal[left:left + count, None] * stereo_gain


def musicbox(note: float, length: float, velocity: float) -> np.ndarray:
    t = np.arange(round(SR * length)) / SR
    f = hz(note)
    # Tuned tine with a few non-harmonic, short-lived overtones.
    ratios = [1.0, 2.003, 3.011, 4.17, 5.43]
    amplitudes = [1.0, .32, .13, .055, .021]
    decay = [.62, .29, .17, .095, .055]
    x = np.zeros_like(t)
    for r, a, d in zip(ratios, amplitudes, decay):
        x += a * np.sin(2 * np.pi * f * r * t) * np.exp(-t / d)
    x *= attack_release(t, length, .0035, .065)
    return normalize(x) * velocity


def pizzicato(note: float, length: float, velocity: float) -> np.ndarray:
    t = np.arange(round(SR * length)) / SR
    f = hz(note)
    x = np.zeros_like(t)
    # A band-limited plucked string. Higher modes decay faster than its body.
    for k in range(1, 14):
        a = np.sin(k * np.pi * .22) / (k ** 1.22)
        detune = np.sqrt(1 + .000035 * k * k)
        x += a * np.sin(2 * np.pi * f * k * detune * t) * np.exp(-t * (4.6 + .86 * k))
    body = .105 * np.sin(2 * np.pi * 182 * t) * np.exp(-t / .045)
    x = (x + body) * attack_release(t, length, .005, .055)
    return normalize(x) * velocity


def clarinet(note: float, length: float, velocity: float) -> np.ndarray:
    t = np.arange(round(SR * length)) / SR
    f = hz(note)
    vibrato = .0024 * np.sin(2 * np.pi * 4.8 * t) * np.clip(t / .32, 0, 1)
    phase = 2 * np.pi * np.cumsum(f * (1 + vibrato)) / SR
    x = np.zeros_like(t)
    for k, a in [(1, 1), (3, .28), (5, .09), (7, .025), (2, .025)]:
        x += a * np.sin(k * phase)
    # A tiny filtered air component, deliberately below the reed tone.
    breath = sosfilt(butter(2, [800, 3700], fs=SR, btype="band", output="sos"), RNG.normal(size=len(t)))
    x += .011 * breath
    envelope = attack_release(t, length, min(.16, length * .24), min(.19, length * .28))
    envelope *= .96 + .04 * np.sin(2 * np.pi * .9 * t)
    return normalize(x * envelope) * velocity


def accordion(note: float, length: float, velocity: float) -> np.ndarray:
    t = np.arange(round(SR * length)) / SR
    f = hz(note)
    x = np.zeros_like(t)
    # Two gently mistuned reeds, darker and quieter than the music-box melody.
    for k in range(1, 9):
        a = 1 / k ** 2.05
        x += a * (.65 * np.sin(2 * np.pi * f * k * t) + .35 * np.sin(2 * np.pi * (f * 1.0015) * k * t))
    x *= attack_release(t, length, .18, .24)
    x *= .96 + .04 * np.sin(2 * np.pi * .65 * t)
    return normalize(x) * velocity


INSTRUMENTS = {"box": musicbox, "pizz": pizzicato, "reed": clarinet, "bellows": accordion}


def note(kind: str, pitch: float, at: float, length: float, velocity: float, pan=0.0) -> None:
    sig = INSTRUMENTS[kind](pitch, length, velocity)
    deposit(MUSIC, sig, at, 1.0, pan)
    EVENTS.append({"stem": "music", "instrument": kind, "midi": pitch, "time": round(at, 4), "length": length})


CHORDS = {
    "dm": [50, 57, 62, 65, 69],
    "dm6": [50, 57, 62, 65, 71],
    "bb": [46, 53, 58, 62, 65],
    "bbmaj": [46, 53, 58, 62, 69],
    "a": [45, 52, 57, 61, 67],
    "asus": [45, 52, 57, 62, 67],
    "am": [45, 52, 57, 60, 64],
    "gm": [43, 50, 55, 58, 62],
    "f": [41, 48, 53, 57, 60],
    "c": [48, 55, 60, 64, 67],
    "d": [50, 57, 62, 66, 69],
    "g": [43, 50, 55, 59, 64],
}


def compose() -> None:
    """An original, spare waltz: D-minor curiosity -> uneasy clockwork -> D-major resolve."""
    quarter = .625
    bar = 3 * quarter
    changes = ["dm", "dm6", "bbmaj", "asus", "dm", "f", "c", "a",
               "dm", "bb", "gm", "a", "dm", "dm6", "bbmaj", "am",
               "gm", "asus", "am", "a", "dm", "gm", "bb", "a",
               "f", "c", "f", "a"]
    # Four individually authored 2-bar melodic cells, deliberately broken by rests.
    cells = [
        [(0, 74, .39), (1.50, 77, .33), (2.00, 76, .39)],
        [(.50, 69, .43), (1.75, 72, .30), (2.50, 74, .31)],
        [(.25, 77, .31), (1.25, 74, .48), (2.50, 69, .32)],
        [(.75, 76, .35), (1.50, 73, .37), (2.50, 69, .31)],
        [(0, 74, .42), (1.25, 81, .31), (2.25, 77, .38)],
        [(.50, 76, .43), (1.75, 72, .34)],
        [(.25, 70, .39), (1.50, 74, .36), (2.25, 77, .31)],
        [(.75, 73, .46), (2.00, 76, .48)],
    ]
    for b, chord_name in enumerate(changes):
        start = .12 + b * bar
        chord = CHORDS[chord_name]
        soft = .66 if 19.0 < start < 35.9 else .80
        note("pizz", chord[0], start, .70, .105 * soft, -.24)
        if b % 3 != 2:
            note("pizz", chord[1], start + 1.34 * quarter, .55, .070 * soft, -.11)
        # Accordion breath every other bar, never a continuous held drone.
        if b % 2 == 0:
            for p in chord[2:4]:
                note("bellows", p, start + .28, 1.18, .021 * soft, .18)
        if 10 <= b <= 18:
            # The jars / swarm use small, separated arpeggiated tines.
            melody = [(.30, chord[2] + 12, .38), (1.65, chord[3] + 12, .31)]
            if b in [13, 14, 15]:
                melody.append((2.50, chord[4] + 12, .27))
        elif b >= 24:
            # Warmly reharmonize the Franklin section; then let the A7 sour it.
            melody = [(0.15, chord[3] + 12, .42), (1.75, chord[2] + 12, .44)]
        else:
            melody = cells[b % len(cells)]
        for beat, pitch, vel in melody:
            when = start + beat * quarter
            # Rests at the pointed lines leave room for the narrator.
            if 30.85 < when < 32.78 or 38.5 < when < 40.2 or when > 52.30:
                continue
            human_offset = float(RNG.uniform(-.009, .009))
            note("box", pitch, when + human_offset, 1.55, .135 * vel * soft, .33)
        # Occasional low clarinet answers are phrases, rather than a lead solo.
        if b in [1, 5, 8, 17, 22, 24, 25]:
            p = chord[3]
            note("reed", p, start + .74, .68, .028 * soft, -.30)
            note("reed", chord[2], start + 1.37, .43, .025 * soft, -.26)
        if 21 <= b <= 23:
            # More motion under the cliff road, still sparse in the mix.
            for j in [1, 3, 5]:
                note("pizz", chord[2] + (0 if j != 3 else 7), start + j * quarter / 2,
                     .35, .025, (-.2 if j % 2 else .2))
    # A brief downward gesture yields into the lever; the exact silence is
    # applied to BOTH stems after room reflections and level processing.
    for at, p in [(51.86, 73), (52.20, 70), (52.58, 69)]:
        note("box", p, at, .70, .035, .18)

    # A fresh, understated major phrase at the invitation to act.
    final_bars = [(54.18, "d"), (56.05, "g"), (57.92, "d"), (59.79, "asus"), (61.66, "d")]
    for b, (start, ch) in enumerate(final_bars):
        chord = CHORDS[ch]
        note("pizz", chord[0], start + .06, .9, .061, -.20)
        note("pizz", chord[1], start + 1.30, .55, .031, -.10)
        for p in chord[2:4]:
            note("bellows", p, start + .11, 1.43, .018, .20)
    for at, p, strength in [(54.39, 74, .046), (55.04, 78, .035), (55.76, 81, .030),
                            (56.36, 79, .032), (57.25, 76, .028), (58.17, 78, .032),
                            (59.22, 76, .026), (60.27, 74, .027), (61.83, 69, .030),
                            (62.39, 73, .021)]:
        note("box", p, at, 1.5, strength, .22)
    # Leave 'This time, it's us' free of attacks. Answer after the last words.
    note("reed", 66, 60.52, .72, .022, -.2)
    note("reed", 62, 61.25, .77, .021, -.2)
    for p, strength, pan in [(50, .073, -.22), (57, .032, -.13), (66, .023, .16)]:
        note("pizz" if p < 60 else "bellows", p, 64.24, 2.85, strength, pan)
    note("box", 74, 64.34, 2.75, .052, .18)
    note("box", 81, 64.98, 2.2, .018, .30)


def band_noise(length: float, low: float, high: float) -> np.ndarray:
    x = RNG.normal(size=round(length * SR))
    return sosfilt(butter(3, [low, high], fs=SR, btype="band", output="sos"), x)


def paper(length=.35, tearing=False) -> np.ndarray:
    t = np.arange(round(length * SR)) / SR
    x = band_noise(length, 550 if tearing else 750, 6500 if tearing else 4700)
    # Irregular friction bursts, not a synthetic white-noise whoosh.
    envelope = np.zeros_like(t)
    for _ in range(22 if tearing else 12):
        center = float(RNG.uniform(.025, length - .02))
        width = float(RNG.uniform(.003, .022))
        envelope += float(RNG.uniform(.1, 1)) * np.exp(-.5 * ((t - center) / width) ** 2)
    envelope *= np.sin(np.pi * np.clip(t / length, 0, 1)) ** .6
    return normalize(x * envelope)


def wood_tap(length=.12, pitch=310.0) -> np.ndarray:
    t = np.arange(round(length * SR)) / SR
    x = np.sin(2 * np.pi * pitch * t) * np.exp(-t / .016)
    x += .23 * np.sin(2 * np.pi * pitch * 2.41 * t) * np.exp(-t / .009)
    x += .035 * band_noise(length, 700, 4800) * np.exp(-t / .005)
    return normalize(x * attack_release(t, length, .0015, .013))


def tick(length=.08, pitch=2100) -> np.ndarray:
    t = np.arange(round(length * SR)) / SR
    x = np.sin(2 * np.pi * pitch * t) * np.exp(-t / .005)
    x += .46 * np.sin(2 * np.pi * 820 * t) * np.exp(-t / .018)
    return normalize(x * attack_release(t, length, .001, .015))


def thunder(length=1.25) -> np.ndarray:
    t = np.arange(round(length * SR)) / SR
    x = band_noise(length, 35, 290)
    env = (1 - np.exp(-t / .11)) * np.exp(-t / .46)
    env *= 1 + .18 * np.sin(2 * np.pi * 6.8 * t) + .09 * np.sin(2 * np.pi * 11.5 * t)
    return normalize(x * env * attack_release(t, length, .035, .2))


def effect(kind: str, at: float, gain: float, pan=0.0, length=None) -> None:
    if kind == "paper": sig = paper(length or .34)
    elif kind == "tear": sig = paper(length or .41, True)
    elif kind == "tap": sig = wood_tap(length or .14, float(RNG.uniform(245, 430)))
    elif kind == "tick": sig = tick(length or .07, float(RNG.uniform(1350, 2450)))
    elif kind == "thunder": sig = thunder(length or 1.25)
    elif kind == "scratch": sig = paper(length or .16, True)
    else: raise ValueError(kind)
    deposit(FOLEY, sig, at, gain, pan)
    EVENTS.append({"stem": "foley", "effect": kind, "time": round(at, 4), "length": len(sig)/SR})


def create_foley() -> None:
    # Wood-letter stamping: YOUR / MY / EVERY; not gunshot-like impacts.
    for at, pan in [(3.74, -.16), (4.82, 0), (5.84, .17)]:
        effect("tap", at, .022, pan)
    effect("paper", 6.81, .014, -.10, .48)
    effect("tear", 9.14, .014, .10, .50)
    effect("paper", 12.24, .017, -.2, .38)
    effect("paper", 15.43, .014, .12, .32)
    # The string snapping is a tiny dry card-pivot snap, never a whip-crack.
    for at in [17.49, 17.67, 17.84]: effect("tick", at, .010, -.08)
    effect("tear", 19.45, .016, -.16, .29)
    # The cabinet gets a measured clockwork pulse; varying pitch prevents a beep.
    for j, at in enumerate(np.arange(20.08, 24.97, .625)):
        effect("tick", float(at), .0039 if j%2 else .0050, (-.13 if j%2 else .13))
    for j, at in enumerate([25.38,25.63,25.88,26.10,26.31]):
        effect("paper", at, .0065, -.33 + j*.16, .10)
    # Swarm: restrained wooden, irregular toy-footsteps. No electronic effects.
    for j, at in enumerate(np.arange(26.72, 30.32, .29)):
        effect("tap", float(at+RNG.uniform(-.025,.025)), .0055, float(np.sin(j)*.30), .065)
    effect("paper", 30.84, .015, -.12, .58)
    effect("paper", 32.95, .012, .10, .28)
    for at, length in [(33.71,.12),(34.11,.19),(34.57,.15),(35.12,.18)]:
        effect("scratch", at, .008, .09, length)
    effect("tap", 36.57, .020, .0)
    effect("paper", 39.99, .018, -.1, .45)
    for at, pan in [(41.47,-.2),(42.26,.13),(43.78,.24),(44.48,-.12)]:
        effect("tap", at, .010, pan)
    effect("paper", 44.98, .010, -.19, .38)
    # A softly swelling distant rumble, no loud thunder impulse under speech.
    effect("thunder", 46.51, .017, -.22, 1.13)
    effect("tick", 47.62, .010, .17)
    effect("thunder", 49.82, .014, .19, 1.37)
    # Curling paper flames, lower and gentler than a stock fire sound.
    for at in [50.32,50.79,51.23,51.62,52.04]:
        effect("paper", at, .007, float(RNG.uniform(-.25,.25)), .20)
    # Lever's click ends before the word PAUSE, followed by real stem silence.
    effect("paper", 52.77, .010, .0, .18)
    effect("tap", 53.06, .028, .0, .06)
    # Three understated printing-press strikes match the call to action.
    for at in [54.18,55.50,56.74]: effect("tap", at, .016, .0)
    effect("scratch", 58.63, .007, .15, .42)
    effect("paper", 60.73, .010, -.12, .27)
    # The last grounded strike follows the spoken rod; leave the final words clear.
    effect("thunder", 62.39, .009, -.22, .36)
    effect("tap", 64.29, .008, .0)


def short_room(x: np.ndarray, amount: float) -> np.ndarray:
    # A few quiet early reflections only. This remains a dry little paper stage.
    out = x.copy()
    for seconds, weight, swap in [(.022,.29,True),(.039,.24,False),(.063,.16,True),(.108,.08,True)]:
        n = round(seconds * SR)
        reflected = x[:-n, ::-1] if swap else x[:-n]
        out[n:] += reflected * (amount * weight)
    return out


def gently_duck(x: np.ndarray) -> np.ndarray:
    voice = ROOT / "audio" / "source_narration.mp3"
    proc = subprocess.run(["ffmpeg","-v","error","-i",str(voice),"-f","f32le","-ac","1","-ar",str(SR),"pipe:1"],
                          check=True, capture_output=True)
    v = np.frombuffer(proc.stdout, dtype="<f4").astype(float)
    # RMS integration and a 400 ms smoothing window prevent per-syllable pumping.
    power = uniform_filter1d(v*v, round(.16*SR), mode="nearest")
    activity = np.sqrt(np.maximum(power,0))
    ceiling = float(np.percentile(activity, 90))
    activity = np.clip(activity/max(ceiling,1e-8),0,1)
    env = np.ones(N)
    env[:len(activity)] = .93 - .23 * activity
    env = uniform_filter1d(env, round(.40*SR), mode="nearest")
    return x * env[:,None]


def contour_and_silence(x: np.ndarray) -> np.ndarray:
    t = np.arange(N)/SR
    env = np.ones(N)
    env *= np.clip(t/.16,0,1)
    # End on clean silence, with no click or truncated resonant tail.
    env *= np.sin(np.clip((DURATION-.06-t)/1.20,0,1)*np.pi/2)**2
    # Silence at PAUSE is applied AFTER all reverb/ducking. Precise endpoints.
    before = (t >= 53.065) & (t < 53.12)
    env[before] *= np.cos((t[before]-53.065)/.055*np.pi/2)**2
    env[(t>=53.12)&(t<54.18)] = 0
    after = (t>=54.18)&(t<54.215)
    env[after] *= np.sin((t[after]-54.18)/.035*np.pi/2)**2
    return x*env[:,None]


def write_wav(path: Path, data: np.ndarray) -> None:
    subprocess.run(["ffmpeg","-v","error","-y","-f","f32le","-ac","2","-ar",str(SR),"-i","pipe:0",
                    "-c:a","pcm_s24le",str(path)], input=data.astype("<f4").tobytes(), check=True)


def measure(path: Path) -> dict:
    proc = subprocess.run(["ffmpeg","-hide_banner","-i",str(path),"-af","loudnorm=I=-24:TP=-2:LRA=7:print_format=json",
                           "-f","null","-"],capture_output=True,text=True,check=True)
    s = proc.stderr
    block = s[s.rfind("{"):s.rfind("}")+1]
    return json.loads(block)


def render_stem(x: np.ndarray, path: Path, target_lufs: float) -> dict:
    # First measure, then make a single constant gain change. No compression or
    # limiting: the original dynamics stay available to the final mixer.
    write_wav(path, x)
    first = measure(path)
    gain_db = target_lufs-float(first["input_i"])
    x *= 10**(gain_db/20)
    peak_db = 20*math.log10(max(np.max(np.abs(x)),1e-12))
    if peak_db > -17:
        x *= 10**((-17-peak_db)/20)
    write_wav(path,x)
    final = measure(path)
    silent = x[round(53.12*SR):round(54.18*SR)]
    return {"path": str(path), "sample_rate": SR, "channels": 2, "pcm_bits":24,
            "duration":DURATION, "integrated_lufs":float(final["input_i"]),
            "true_peak_dbtp":float(final["input_tp"]), "loudness_range_lu":float(final["input_lra"]),
            "sample_peak_dbfs":round(20*math.log10(max(np.max(np.abs(x)),1e-12)),3),
            "pause_53.12_to_54.18_max_absolute":float(np.max(np.abs(silent)))}


def main() -> None:
    compose()
    create_foley()
    music = short_room(MUSIC,.20)
    foley = short_room(FOLEY,.06)
    music = sosfilt(butter(2,35,fs=SR,btype="high",output="sos"),music,axis=0)
    foley = sosfilt(butter(2,32,fs=SR,btype="high",output="sos"),foley,axis=0)
    music = contour_and_silence(gently_duck(music))
    foley = contour_and_silence(foley)
    metadata = {
        "title":"The Paper Lightning Rod",
        "provenance":"Original composition and procedural audio synthesis created for this explainer. No third-party composition, recording, sample, soundfont, or voice used. All stochastic material is seeded noise; all instruments are mathematical oscillator/decay models. The provided narration is read only for a gentle level-following envelope and is absent from both stems.",
        "musical_design":"Sparse original D-minor 3/4 chamber waltz at 96 quarter notes/minute, music-box melody, plucked-string bass, occasional low clarinet answers and brief accordion breaths; transitions to restrained D-major invitation and coda. Not a reconstruction or sound-alike of the reference score.",
        "pause":"Both stems contain exactly zero-valued samples from 53.12 up to 54.18 s. The lever click ends at 53.12 s.",
        "mix_note":"Quiet source stems are intended to sit under the supplied voice. Final narration compression, relative balance and final program loudness are deliberately left to the final mix. Music is gently and slowly ducked from the supplied narration level; foley avoids loud lightning impulses.",
        "timing_source":"audio_alignment.json; actual supplied narration 64.44408163265307 s. Score and end-card coverage 67.5 s.",
        "music":render_stem(music,OUT/"music.wav",-35.5),
        "foley":render_stem(foley,OUT/"foley.wav",-42.0),
        "events":EVENTS,
    }
    (OUT/"score_metadata.json").write_text(json.dumps(metadata,indent=2)+"\n")
    print(json.dumps({k:v for k,v in metadata.items() if k!="events"},indent=2))


if __name__ == "__main__":
    main()
