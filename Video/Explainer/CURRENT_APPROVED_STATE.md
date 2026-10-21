# PhillyPause — Recovery Asset Set and Revision Handoff

**Archive purpose:** preserve every recoverable non-video source asset and source
code needed to rebuild the PhillyPause explainer from the supplied brief and
narration. This is an ASSET/REBUILD package, not a rendered movie or a verified
final R7 project.

## What is actually recoverable

- **Full production base:** October 19 revision 2, including public-domain
  historical source scans (some full-resolution TIFF), separated transparent
  illustration elements, jointed puppet body parts, textures, endpaper,
  scene artwork, transition artwork, QR graphics, original narration, finished
  mix and separate voice/music/foley stems, rendering scripts, and an After
  Effects reconstruction JSX plus its old trace.
- **Latest persisted source overlay:** October 20 revision 5. These newer
  code/asset versions are merged OVER the R2 production files in
  `production/`; matching filenames are replaced by R5, not kept
  as parallel conflicting active files. Includes connected whole-sheet
  origami/crumpling source, revised jar geometry, continuous cylinder, rod
  connectors, puppet line connections, one-second intro, ten-second final
  hold, and updated 76-second audio and captions.
- **Later approved R6/R7 work:** the final user-requested ending and reframing
  requirements are recorded here, but the finalized R7 source was not saved
  successfully and is NOT recoverable from these archived files. Therefore
  treat R5 as the last complete recovered executable source, NOT as final R7.

## Where the source stands

The source in this repository implements every requirement below. R5 already
met 1, 2, 6, 7, 9, 10, 12 and 13. R7 adds the rest:

- **3:** the vertical ledger fits the frame, so both page titles show, and the
  vertical burning town keeps its houses visible under the flames.
- **4:** the closing card has one centred banner, SCAN TO JOIN ↔ SHARE THIS
  VIDEO, with the arrow drawn as a shape, plus the logo, phillypause.org, the
  QR, and © 2026 PhillyPause printed legibly on the card.
- **5:** the QR sits directly on the parchment card with its quiet zone and no
  white field. It is checked by decoding frames of the encoded film.
- **8:** the three gears that floated free of any part are gone. The chest gear
  stays with the robot's torso.
- **11:** the SUPERINTELLIGENCE scene has no stray lines.
- **14:** every export is rebuilt from this one source by `finish.py`
  (RENDERING.md).

## Final creative requirements

1. Preserve the verbatim words and scene order of `production/Animation_Brief.md`, timed to the attached voice track. Do not replace/revoice narration.
2. Begin with one full second of **moving** lead-in before first word; captions and audio begin together afterward. Total video 76 seconds at 24 fps, final card completely motionless from 66 to 76 seconds.
3. Every aspect ratio (16:9 720/1080/2160, 1:1 and 9:16) must show **substantially identical story, wording, imagery, and effects** with individually adjusted compositions rather than blind crops. Do not regress any aspect ratio to older assets.
4. Closing card uses the matching PhillyPause logo, the exact centered one-banner line:

   **SCAN TO JOIN ↔ SHARE THIS VIDEO**

   with one double-ended arrow between the two phrases; it must NOT have separate boxed labels. No text "A lightning rod for the AI age." Include **© 2026 PhillyPause** and legible **phillypause.org**. Include a scannable QR linking to https://phillypause.org.
5. The QR must NOT appear as a jarring white rectangle, in **any** format. The recoverable `phillypause_qr_parchment.png` is a transparent-quiet-zone PNG; place on a similar opaque parchment field and retain a full clear margin/quiet zone around the code. Verify actual **encoded output** scanning on phone-sized playback, not merely the source PNG.
6. Varied transitions: rotations/tumbles in both directions, diagonal and vertical slides, right-to-left and left-to-right slides, plus organic one-piece PAPER CRUMPLING into an angular connected origami wad. Do NOT substitute many independent scattering fragments.
7. No dangling black control strings or unexplained banner support lines. All maker-to-robot, hand-to-board, kite-to-hand, rod-to-lightning, and other connections must visibly land on something in every sampled animation pose.
8. No robot head or limb escapes its bell jar during the sway; feet sit inside on the platter; all jar caps meet their neck/dome. The small chest gear belongs to the robot torso and moves WITH it; eliminate the large unattached floating gear.
9. Instruction-cylinder top/bottom arcs must join the side silhouettes cleanly without open corners or double seams.
10. Curtains: exactly two full-height curtains on one shared rail, closing over all buildings; no three undersized partial curtains. Preserve the intended stage-reveal movement afterward.
11. No stray vertical black lines on the SUPERINTELLIGENCE scene. Preserve intentional scene labels and the TODAY'S AI beat. Preserve correct 1,000+ and 700 counters.
12. Keep period Philadelphia paper theater style: original articulated puppet layers, Caslon-like wood type, paper and fabric textures, original instrumental and foley cues, no stock futuristic screens or licensed Lemony Snicket material.
13. Caption files `.srt` (YouTube) and `.vtt` (website) must match revised narration time; burnt-in social caption alternatives, if desired, should correspond exactly.
14. Every accompanying still, poster, timeline, storyboard, director guide, source package and render manifest MUST derive from the exact same frozen source revision. Rebuild all aspect ratios after any visual or narration edit and compare final compressed files, not just source frames.

## Source and rerender starting point

`production/` holds the source: the merged R2 foundation and R5 update, with
R7 on top. Some generated artwork caches in `assets/render` and
`assets/transitions` came from earlier iterations; **do not treat all cached
artwork as final**. Regenerate caches when render code or labels change. Use
the live source `film.py`, `art.py`, `paper_crumple.py`, `transitions.py`,
`render_review.py`, and `revision4_timing.py`. RENDERING.md has the commands
for every export.

The R2 After Effects JSX and `master_timeline.json` describe the old
67.46-second timing and a separate older closing card, so they **cannot be
used as a current R7 match** without regenerating the timeline and auditing
its layers. The JSX has not been verified in the actual After Effects
application.

Environment: Python >=3.12, FFmpeg/ffprobe on PATH, dependencies in
`production/requirements.txt`. The source bundle does **not**
include redistributable font binary files. Install fonts yourself from their
rights-cleared sources (Libre Caslon Text, Rye and DejaVu Sans); the retained
OFL/license texts describe names and terms. Source scan licenses and links
are in `assets/historical/` and `Asset_Sources.txt`.

The R2 original mix and independent music/foley/voice stems are in
`production/audio/`. The newer 76-second R4/R5 mix, which contains the
one-second pre-roll, is `audio/mix_revision4.wav`. Use either the MIX or its
COMPONENT stems appropriately; do not stack the mixed track atop the same
components. The original narration MP3 is `audio/PhillyPauseSiteHeroVideo.mp3`,
copied as `audio/source_narration.mp3`.

## Important acceptance and limitations

**Not certified to reproduce final R7:** the complete final R7 rebuild was
reported as rendering in the interrupted thread but no complete R7 source
archive was actually persisted. R5 code plus this spec is the best recoverable
material. Before publication your director must implement the outstanding
R6/R7 changes, test synchronized aspect ratios and regenerate the film.

The repo holds no rendered `.mp4`/`.mov` video and no font binaries: the
films are built from source (RENDERING.md), and git, with Git LFS for the
artwork and audio, is the record of what every asset is.
