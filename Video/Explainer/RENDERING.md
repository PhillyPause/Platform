# Rendering and finishing the explainer

Every production script reads `production/` and writes nothing there except
under `production/build/`, its default destination: rendering (`film.py`,
`render_review.py`), sound and captions (`revision4_timing.py`,
`audio_score.py`) and finishing (`finish.py`, `package_project.py`). An
explicit `--output` outside `production/` is honoured. On one machine, running
a step twice gives byte-identical output, and deleting `build/` changes nothing
but the time the next run takes. `production/paths.py` holds the rule and
enforces it: any write into `production/` outside `build/` raises
`PermissionError`.

The asset scripts under `production/assets/` (`generate_qr.py`,
`generate_qr_parchment.py`, `build_cutouts.py`, `build_asset_log.py`) are the
exception: they make source assets and write them in place, and
`generate_qr.py --verify` also writes `production/QR_Validation.json`.

## Source and output

| Path | What it is | Written by the pipeline |
|---|---|---|
| `production/assets/historical/`, `cutouts/`, `brand/`, `fonts/` | Scans, cutouts, logo, QR, fonts | Never |
| `production/assets/render/` | The frozen R5 artwork: every puppet part, prop and label as approved | Never |
| `production/assets/transitions/crumple_r5/` | The frozen R5 fold poses for the two crumple handoffs (landscape) | Never |
| `production/audio/` | The narration, the approved mix and its stems, the approved 76-second mix | Never |
| `production/audio_alignment.json` | Word and caption timings measured from the narration | Never |
| `production/PhillyPause_Captions.srt`, `.vtt` | The approved caption tracks | Never |
| `production/captions/*.ass` | Revision 3's burned-in layouts, on the story clock; the reference the current layouts are tested against | Never |
| `production/build/` (gitignored) | Proofs, renders, traces, soundtrack, captions, exports, reports, and any artwork or fold poses the baseline lacks | Always |

`PHILLYPAUSE_BUILD` moves the build folder. It may not point at or inside
`production/`'s source folders.

## Why the R5 artwork is frozen rather than regenerated

The PNGs in `assets/render/` were drawn by R5's code, but they are source now.
Drawn again on this toolchain, everything with lettering comes out slightly
different (glyph advances shift by a few pixels across a line). Plain artwork
matches within 1/255. The approved look is the committed files, so rendering
loads them and draws only what they lack.

To change a piece of artwork, give it a new name in code. The existing
convention is `jar_r5`, `blank_scroll_r5`, `curtain_leaf_r4`. The new name is
missing from the baseline, so it is drawn into `build/artwork/<recipe>/`. The
recipe is a hash of `art.py`, `film.py`, the fonts and the texture scans, so an
edit can never be hidden behind a stale cached picture. Editing a drawing
without renaming it changes nothing on screen, because the frozen PNG wins.

Approving new artwork means copying it from `build/artwork/<recipe>/` into
`assets/render/`. R7's closing banner and on-card copyright
(`endbanner_r7`, `endcopyright_r7`) are frozen that way.

## The crumple poses

R5 keys its fold poses on an exact hash of the scene's pixels, which only the
machine that made them reproduces. Rendering uses the frozen poses when
`paper_crumple.py` is byte-for-byte R5's (line endings aside) and the live scene
matches R5's `source_snapshot.png` within 8/255 per channel and 0.25 on average.
Machines differ by about 3/255; any real artwork edit differs by far more.
Otherwise the sheet is folded afresh into `build/transitions/`. Editing
`paper_crumple.py` therefore refolds both handoffs.

Changing the outgoing scene refolds its handoff too. R7 took the loose gear
out of scene 3, so `crumple_r5/landscape_03` holds poses refolded from the
gearless scene. To freeze a refold, copy its folder from `build/transitions/`
and set `renderer_sha256` in its `folds.json` to the line-ending-folded hash
(`paths.text_sha256`) the baseline check compares. A fresh fold records the
raw bytes, which differ on a CRLF checkout.

Each proof writes `build/proof/<format>_provenance.json`. It records which
artwork came from the baseline, the source folders or `build/`, and where each
crumple's poses came from.

## Sound, captions and the lead-in

The film runs 76 seconds at 24 fps: one second of moving lead-in, the story,
then the closing card held still from 66 seconds to the end (`INTRO_SECONDS`,
`END_HOLD_START` and `DURATION` in `film.py`). The narration, the approved mix
`audio/mix.wav` (exactly voice + music + foley) and the caption timings in
`audio_alignment.json` all run on the story clock, which starts with the
narration. `revision4_timing.py` delays sound and captions by the lead-in. No
other script shifts a sound or caption file. The After Effects timeline from
`film.py --trace` uses the approved 76-second mix as it stands, and starts the
muted story-clock stems `INTRO_SECONDS` in (each audio entry's `startTime`).
The builder's storyboard markers are the story beats moved by the trace's
`leadInSeconds`, with the end card at `endHoldStart`. The timing outputs:

- `build/audio/mix_revision4.wav`: 48,000 samples of digital silence, then
  `mix.wav` bit for bit, then silence to 76 seconds.
- `build/captions/PhillyPause_Captions.srt` and `.vtt`: every cue in
  `audio_alignment.json`, 1,000 ms later.

Each output is compared with the approved file it stands for, and the build
stops if they differ. The mix must match `audio/mix_revision4.wav` sample for
sample. The whole files differ only because the WAV header records the ffmpeg
version. The captions must match the tracked SRT and VTT byte for byte, with
line endings folded. The build also finds the supplied narration inside the
new mix by cross-correlation, and it must start exactly one second in. The
input is always narration time, never a previous output, so a second run
cannot shift anything twice. To change a caption, edit `audio_alignment.json`,
build, review `build/captions/`, and approve it by copying the result over the
tracked file.

`finish.py` writes the burned-in layouts (`build/captions/web.ass`,
`vertical.ass`, `square.ass`) from the same shifted cues. Their two placement
rules, for the vertical cabinet captions and the TODAY'S placard, are set on
the story clock.

`audio_score.py` regenerates the music and foley stems into
`build/audio/score/`. On this toolchain they match `audio/music.wav` and
`audio/foley.wav` sample for sample. The approved stems are never replaced.

## Finishing

`finish.py` builds the soundtrack and captions, renders each picture afresh
with `film.py` into `build/renders/`, muxes, and checks every result:

| File | Picture | Captions |
|---|---|---|
| `PhillyPause_Master_1080p.mp4` | landscape 1920 × 1080, the rendered H.264 | — |
| `PhillyPause_Master_ProRes422.mov` | landscape 1920 × 1080, ProRes 422, PCM audio | — |
| `PhillyPause_Web_720p.mp4` | landscape 1280 × 720 | — |
| `PhillyPause_Web_720p_Captioned.mp4` | landscape 1280 × 720 | burned in |
| `PhillyPause_Vertical_1080x1920.mp4` | vertical 1080 × 1920, for phones | — |
| `PhillyPause_Vertical_1080x1920_Captioned.mp4` | vertical 1080 × 1920, for phones | burned in |
| `PhillyPause_Vertical_1440x2560.mp4` | vertical 1440 × 2560, the LG StanbyME 2 upright | — |
| `PhillyPause_Vertical_1440x2560_Captioned.mp4` | vertical 1440 × 2560, the LG StanbyME 2 upright | burned in |
| `PhillyPause_Square_1080x1080.mp4` | square 1080 × 1080 | burned in |

Alongside them go the SRT and VTT, `PhillyPause_Poster.jpg` (the closing card,
made whenever the web file is), and `finish_report.json`. Every file must have
1,824 frames over 76 seconds, its audio must match the soundtrack within two
samples at 16 kHz, and its first 0.9 seconds must be silent. Its picture must
move through the lead-in and stay still from 66 seconds to the end: no pixel
may move more than 4 levels, which is codec noise. Any failure exits nonzero.
Every file's metadata title is `PhillyPause`.

Every H.264 encode, the picture and the finished file alike, starts a keyframe
at 1 and 66 seconds. Without the second, the encoder spends the hold's first
frames refining the card that has just moved in, and some of its pixels shift
by 35 levels or more. At CRF 20 it does so even after the keyframe, so the
re-encodes in `finish.py` and `render_review.py` also code every later frame
of the hold at QP 51 (an x264 zone), which leaves each one an exact copy of
the keyframe. That is safe only because the hold's source frames are identical
and no caption runs into it, which `revision4_timing.py` enforces.

`--draft` makes the same files at a quarter of their size into `build/draft/`
(the web pair at 320 × 180), to check sync and captions without a
full-resolution render. Each draft picture takes about three minutes.

`render_review.py` makes the 1280 × 720 approval preview in one pass, muxed
with the same built soundtrack. `package_project.py` archives the source with
the web export. It needs revision 2 handoff files that are not in this
repository (`README.txt`, `Production_Notes.txt`, `master_timeline.json`, the
validation JSON) and stops with the list.

## Commands

From `Video/Explainer`, with the virtualenv. The finished website video, with
synchronised sound, captions and poster, written to `production/build/exports/`:

```
.venv/Scripts/python production/finish.py --only PhillyPause_Web_720p.mp4 PhillyPause_Web_720p_Captioned.mp4
```

The rest:

```
.venv/Scripts/python production/finish.py                  # every delivery file
.venv/Scripts/python production/finish.py --draft          # the same at quarter size, into build/draft/
.venv/Scripts/python production/revision4_timing.py        # soundtrack and SRT/VTT only
.venv/Scripts/python production/revision4_timing.py --verify-preview path/to/film.mp4
.venv/Scripts/python production/audio_score.py             # regenerate the score stems
.venv/Scripts/python production/film.py --format landscape --proof
.venv/Scripts/python -m unittest discover -s production/tests -v
```

## Tests

`test_render_isolation.py` runs the proof four times in fresh processes
against temporary build folders: R5 baseline and full regeneration
(`PHILLYPAUSE_IGNORE_BASELINE=1`), each cold then warm. It asserts that every
file under `Video/Explainer` is byte-identical afterwards, that cold and warm
runs agree byte for byte, and that the baseline proof draws nothing itself.

`test_finishing.py` pins the approved narration, mixes, stems, alignment and
caption files by hash. It checks the timeline constants, that the picture
moves through the lead-in and holds still from 66 seconds, and that every
caption starts on its first spoken word and is shifted exactly 1,000 ms. Then
it runs `revision4_timing.py`, `audio_score.py` and `finish.py --draft` for the
web pair in fresh processes against a temporary build folder, and asserts:

- every file under `Video/Explainer` is byte-identical afterwards, and repeat
  builds are byte-identical;
- the soundtrack and the regenerated stems match the approved ones sample for
  sample;
- the burned-in layouts are revision 3's, shifted by exactly one second;
- each finished film has 1,824 frames over 76 seconds and the right title,
  its audio matches the approved mix, and the narration starts one second in;
- its lead-in moves, and its closing card is still to within codec noise from
  the first frame of the hold;
- the burned captions are on screen at every cue's midpoint and absent before
  the first cue, in every gap of a quarter-second or more, and through the
  closing card.
