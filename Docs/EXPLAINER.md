# The explainer

The home page centrepiece: a narrated motion graphic, eight scenes, 92 seconds.
Introduce the risk, explain why it is hard, ask for one thing.

## How it is built, and why not video

It is **HTML, CSS and SVG driven by a timeline**, not an MP4. That buys:

- ~15 kB instead of ~20 MB, and no encoding step.
- Crisp at any size, including a 4K monitor and a phone.
- The script is real text in the page — crawlable, selectable, translatable,
  and readable by a screen reader.
- Edits are a text change, not a re-render and re-upload.

Narration is an **enhancement, not a dependency**. With no audio file present
the piece advances on a timer with on-screen captions, which is how most people
watch explainers anyway — muted. Drop in an audio file and it syncs to that
instead.

Files:

| File | Holds |
|---|---|
| `Components/Shared/Explainer.razor` | the eight scenes as markup, and the transcript |
| `wwwroot/explainer.css` | scene motion |
| `wwwroot/explainer.js` | the timeline, controls, captions, audio sync |

Nothing here is interactive Blazor. It is static server-rendered markup plus a
JavaScript module, so the page still ships **zero WebAssembly** — the
render-mode budget in `Website.Tests` stays at zero.

## Recording the narration

Read at roughly 150 words per minute, conversational, no music bed needed.
Scene boundaries are where the visuals cut, so land close to them — within half
a second is fine, the player tolerates drift.

Export as `wwwroot/media/explainer.mp3` (mono, 96 kbps is plenty). The player
picks it up automatically; no code change. Then record the exact timings and
update `SCENES` in `explainer.js` if your read differs from the plan by more
than a second or two.

## The script

Timings are start marks in seconds.

| # | At | Narration | On screen |
|---|---|---|---|
| 1 | 0 | In 2023, hundreds of the people building artificial intelligence signed a single sentence. | **2023**; signature strokes drawing in |
| 2 | 9 | It said that the risk of human extinction from AI should be a global priority — alongside pandemics and nuclear war. | The sentence in serif; *extinction* in ember |
| 3 | 21 | Then they went back to work. Investment climbed. Capability climbed. The part nobody had solved stayed unsolved. | Three rising bars, one flat line |
| 4 | 31 | Because here is what most people do not know. Nobody can explain how these systems work. They are not built. They are grown — adjusted, billions of times, until the output looks right. | A box filling with tangle; "no one can read this" |
| 5 | 44 | So why not slow down? Because nobody can slow down alone. Safety costs time. Time costs the lead. Whoever hesitates, loses. | Two racers; a brake greying out |
| 6 | 56 | That is not a problem engineers can fix from the inside. It is a problem rules fix. And there is a lever: these systems need enormous numbers of specialised chips, from a handful of suppliers. | A chip grid narrowing to a few |
| 7 | 68 | Countable. Traceable. Already controlled. Pause the largest training runs until safety can be shown. | The pause bars descend; everything stills |
| 8 | 79 | That decision gets made in Washington. Pennsylvania has nineteen votes there. Most of those offices have never heard from a constituent about this. You could be the first. | Nineteen seats; one lights up |

Ends at 92 seconds.

### The quotation in scene 2

Scene 2 paraphrases rather than quotes. The underlying statement is the
Center for AI Safety's one-sentence Statement on AI Risk, signed in 2023 by
researchers and by the heads of the major labs. The transcript links to the
original so a reader can check the wording themselves, which is better practice
than reproducing it inside an animation where the attribution scrolls past.

## Claims that need checking before this goes live

Written from general knowledge and **not yet verified**:

- **"hundreds"** of signatories in scene 1. The real figure is higher; confirm
  and use the real number, which is stronger.
- **"nineteen votes"** in scene 8 — 2 senators + 17 representatives. Confirm
  against the current Pennsylvania delegation; reapportionment changes this.
- **"most of those offices have never heard from a constituent about this"** is
  an assertion we cannot source. Either soften it to something defensible, or
  drop it. It is the most quotable line in the script and the least supported.

## What comes after

The explainer introduces. The pages after it educate and then ask — the three
things in order. Everything previously written for those is set aside under
`Website.Content/Content/_archive/` and will be rewritten much
shorter, after this lands.
