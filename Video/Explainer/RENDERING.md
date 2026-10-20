# Rendering the explainer

Rendering (`film.py`, `render_review.py`) reads `production/` and writes
nothing there except under `production/build/`, its default destination. An
explicit `--output` outside `production/` is honoured. On one machine, rendering twice gives byte-identical
output, and deleting `build/` changes nothing but the time the next run takes.
`production/paths.py` holds the rule and enforces it: any write into
`production/` outside `build/` raises `PermissionError`.

The audio and finishing scripts are not covered. `audio_score.py`,
`revision4_timing.py` and `finish.py` still write into `production/audio/`,
`captions/` and the caption files, as they always have.

## Source and output

| Path | What it is | Written by rendering |
|---|---|---|
| `production/assets/historical/`, `cutouts/`, `brand/`, `fonts/` | Scans, cutouts, logo, QR, fonts | Never |
| `production/assets/render/` | The frozen R5 artwork: every puppet part, prop and label as approved | Never |
| `production/assets/transitions/crumple_r5/` | The frozen R5 fold poses for the two crumple handoffs (landscape) | Never |
| `production/audio/`, `captions/`, `*.srt`, `*.vtt` | Mix, stems, captions | Never |
| `production/build/` (gitignored) | Proofs, renders, traces, and any artwork or fold poses the baseline lacks | Always |

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

## The crumple poses

R5 keys its fold poses on an exact hash of the scene's pixels, which only the
machine that made them reproduces. Rendering uses the frozen poses when
`paper_crumple.py` is byte-for-byte R5's (line endings aside) and the live scene
matches R5's `source_snapshot.png` within 8/255 per channel and 0.25 on average.
Machines differ by about 3/255; any real artwork edit differs by far more.
Otherwise the sheet is folded afresh into `build/transitions/`. Editing
`paper_crumple.py` therefore refolds both handoffs.

Each proof writes `build/proof/<format>_provenance.json`. It records which
artwork came from the baseline, the source folders or `build/`, and where each
crumple's poses came from.

## Commands

From `Video/Explainer`, with the virtualenv:

```
.venv/Scripts/python production/film.py --format landscape --proof
.venv/Scripts/python -m unittest discover -s production/tests -v
```

The test runs the proof four times in fresh processes against temporary build
folders: R5 baseline and full regeneration (`PHILLYPAUSE_IGNORE_BASELINE=1`),
each cold then warm. It asserts that every file under `Video/Explainer` is
byte-identical afterwards, that cold and warm runs agree byte for byte, and that
the baseline proof draws nothing itself.
