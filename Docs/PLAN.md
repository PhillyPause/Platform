# PhillyPause website — plan

A Blazor WebAssembly site for PhillyPause at `phillypause.org`, reimagining
[pauseai.info](https://pauseai.info) for one city, sharing a domain and a
design language with [`PhillyPause/ManyHands`](https://github.com/PhillyPause/ManyHands).

Present tense: this file describes what the plan **is**. Conventions follow
ManyHands' `CLAUDE.md` — UPPERCASE doc names, conventional commits, protected
`main`, no ADRs.

---

## 0. Who PhillyPause is, and standing constraints

### 0.0 Positioning — the fact everything else rests on

PhillyPause is an **independent Philadelphia organisation**, loosely affiliated
with **PauseAI Global** (Stichting PauseAI, a Dutch foundation).

**It is not a local group, chapter or branch of PauseAI US.** PauseAI US dropped
all of its local groups, PhillyPause among them. The site must never describe
itself as a chapter of, local group of, under, or part of any national
organisation — in body copy, press boilerplate, meta descriptions or footer.

The affiliation with PauseAI Global is **loose**: shared analysis, a shared
ask, and content reused under CC BY 4.0. It confers no authority in either
direction and PauseAI Global is not responsible for anything written here.

Independence is a content advantage, not an embarrassment to be worked around.
It means the site sets its own priorities and answers to the people who turn
up, and it removes the awkward paragraph every chapter site has to write about
what it is and is not allowed to say.

### 0.1 Constraints

Owner decisions. Settled — do not relitigate.

1. **English only.** No localisation layer, no translation tooling, no
   `lang`-switching routes. PauseAI Global runs Inlang/Paraglide; we do not
   copy it. Content is authored in English and stays that way. The only
   residue is `<html lang="en">` and honest `hreflang` absence.
2. **No communications with PauseAI US.** There is no relationship to
   maintain. Nothing in this project requires their input, sign-off, assets or
   review. The mark is replaced rather than cleared (§3.1), and content comes
   from PauseAI Global's CC-BY licence (§3), which grants permission in advance
   and asks nothing of anyone.
3. **The website ships before any substantial further work on ManyHands.**
   ManyHands is feature-complete for v1 and frozen for the duration. This
   reshapes §5 and §7 — see the ManyHands-dependency note in §5.

## 1. What we are cloning, and what we are not

PauseAI Global is a SvelteKit site on Netlify: Markdown + frontmatter in
`src/posts/`, Decap CMS for non-developer edits, Inlang/Paraglide for
localisation, `vite-imagetools` for build-time AVIF/WebP, Substack RSS for news.
Roughly **120 content pages**. Brand: `#FF9416`, Saira Condensed 700 /
Montserrat Black / Roboto Slab. Content and code are **CC-BY 4.0**, attributed
to Stichting PauseAI (kvk 92951031).

We are not porting 120 pages. The IA below is 14 routes. Three rules decide
what each PauseAI page becomes:

| Tier | Treatment | Examples |
|---|---|---|
| **Adapt** | Rewrite and condense under CC-BY, with visible credit | risks, xrisk, sota, urgency, proposal, 4-levels, faq, quotes, counterarguments |
| **Originate** | Ours, no upstream equivalent | PA delegation, Philly events, campus outreach, local press, tabling spots |
| **Link out** | Never clone what we cannot maintain | donate, vacancies, code of conduct, safeguarding protocol, privacy boilerplate |

Policy documents go in the **link out** tier deliberately. A small group that
publishes its own stale safeguarding protocol is worse off than one that adopts
and points at a maintained original.

## 2. Architecture

### 2.1 The one deviation from a literal reading of "Blazor WASM"

A standalone Blazor WASM site serves crawlers an empty `<div>` and spends
~1.5–2 MB before first paint. For an advocacy site whose entire job is reach —
organic search, press pickup, link unfurls in Discord and Bluesky — that is a
real cost, not a theoretical one.

**Build it as a .NET 10 Blazor Web App with `InteractiveWebAssembly` render
mode and prerendering on.** Components still run as WebAssembly in the
browser, so it is a Blazor WASM site; the host prerenders first paint to real
HTML. Content pages that need no interactivity get static SSR and ship **zero**
WASM. Only the pieces that genuinely need it — the events feed, the email
builder, the delegation filter — opt into `InteractiveWebAssembly`.

If pure standalone WASM is wanted anyway, the fallback is build-time
prerendering (crawl the routes, emit static HTML) so the bundle can sit on a
$0 static host. That is an extra moving part for the same outcome; prefer the
Web App.

### 2.2 Separate repo, shared domain

ManyHands' `Api/Program.cs` ends with:

```csharp
// The bare root is not ours: the app lives under a path on the main site.
app.MapGet("/", () => Results.Redirect("/manyhands/"));
```

The split was designed in from day one. Honour it:

- **`PhillyPause/Platform`** — this repo, its own DigitalOcean App Platform
  component, serving `/`.
- **`PhillyPause/ManyHands`** — unchanged, keeps `/manyhands/`. Its root
  redirect simply stops being reached.
- One domain via App Platform **ingress rules**: `/manyhands` → the ManyHands
  service, `/` → the site.

The site deploys on a content typo without redeploying an app that carries
Postgres RLS, a Discord gateway and an outbox. That separation is the whole
argument; do not collapse the two into one host for the sake of one fewer
pipeline.

`phillypause.org` is **already registered and on Cloudflare nameservers**
(`aldo`/`liv.ns.cloudflare.com`) with no A record. So the "domain name (TBD
since day one)" item in ManyHands' `HANDOFF.md` §5 is further along than that
file records — what is open is where it points.

### 2.3 Content pipeline

Markdown with frontmatter, compiled at **build time**, never fetched at runtime:

```
Content/
  risk/overview.md          ← frontmatter: title, description, source, license, updated
  ask/proposal.md
  pennsylvania/delegation.md
```

An MSBuild task runs Markdig over `Content/**/*.md` and emits a strongly-typed
`PageIndex` with HTML already rendered. No database for content, no CMS
dependency, no runtime parse. Routing is one `[Route("/{*slug}")]` component
resolving against the index.

Mirroring PauseAI's authoring format is deliberate: content moves either
direction with a copy-paste, and a volunteer who can write Markdown can open a
PR without touching C#.

### 2.4 Project layout

```
Website.Portal/       ASP.NET Core host — prerender, routing, OG tags, sitemap, RSS
Website.Client/       Blazor WASM — interactive islands only
Website.Content/      Markdown + the MSBuild task that compiles it
Website.Tests/        xunit; snapshot the rendered HTML of every route
Docs/                 DESIGN.md, CONTENT.md, MOTION.md, HOSTING.md
Deploy/digitalocean/  app spec + ingress rules
```

### 2.5 Project naming

The repo is `github.com/PhillyPause/Platform`. It is named for the platform
rather than the site because both products end up in it.

Every project is `<Product>.<Role>`: the first segment is the product area,
the second is what the project does. Folder name, project file, assembly name
and root namespace are all that same string — one name per project, nothing to
map between.

```
Website.Portal      the host that serves phillypause.org
Website.Client      its interactive islands
Website.Content     its content pipeline
Website.Tests       its tests
```

The solution is `Platform.slnx` at the repo root, named for the
organisation rather than any one product, because **ManyHands moves into this
same solution** when the site has shipped (section 5). Its projects take the
same shape and sit beside the website's as siblings:

```
ManyHands.Api       the service
ManyHands.Core      its domain
ManyHands.Client    its front end, if it keeps one
ManyHands.Tests     its tests
Shared.*            only if something is genuinely shared by both products
```

Two rules that follow from this, and are the reason for the convention:

- **No project is called just `Portal` or `Core`.** The product segment is
  what stops ManyHands' domain and the website's colliding, in folder names,
  in `using` lines and in stack traces.
- **`Shared.*` is earned, not assumed.** Two products in one solution tempt
  you into a shared library on the first duplicated line. Copy it twice
  first; a premature `Shared.Core` couples the site's release to ManyHands',
  which is exactly what section 0.1 forbids.

There is no `PhillyPause.` prefix on assemblies. The repo and the organisation
already say it, and nothing here is published to NuGet. If anything ever is,
that package gets the prefix at pack time rather than the whole tree getting
it now.

Match ManyHands' infrastructure exactly: `Directory.Packages.props` for
central version pinning, `ManagePackageVersionsCentrally`, FluentAssertions
pinned to 7.2.2 (the last Apache-2.0 release), Playwright for the smoke suite,
and the same four required CI checks so the two repos feel like one project.

## 3. Licence and attribution — the part to get right first

PauseAI's content **and** code are CC-BY 4.0. We can take freely. CC-BY
requires, for each adapted work: the creator's name, a copyright notice, a
licence notice, a link to the licence, and **an indication that changes were
made**. Five things, easy to forget on page 9 of 14.

So make it structural, not editorial:

1. Frontmatter carries `source:` and `license:` on every adapted page.
2. A single `<SourceCredit>` Razor component renders the five required elements
   from that frontmatter. A page with `source:` and no credit **fails a test**.
3. `/license` mirrors PauseAI's: PhillyPause's own content is also CC-BY 4.0
   (compatible, and matching their ethos), crediting Stichting PauseAI
   (kvk 92951031), with the list of adapted pages.
4. Code is **MIT**, like ManyHands, so forks stay cheap.

### 3.1 The logo cannot be inherited

CC-BY 4.0 §2(b)(2) licenses no trademark rights. PauseAI's orange
pause-in-a-circle and PauseAI US's flag-in-a-circle are brand marks, not
licensed content — the existing PhillyPause logo (PauseAI US's mark with a
Liberty Bell flag swapped in) is a derivative of someone else's identity.

Under constraint §0.2 we do not ask for sign-off, so it is **retired, not
cleared**. It appears nowhere in the repo, not even as a placeholder, so there
is no path by which it reaches production by accident.

Replacement is the better path regardless of permission, and §0.0 makes it
necessary rather than merely advisable: an independent organisation cannot wear
a derivative of a national organisation's mark it has no affiliation with. The
mark has to read as *Philly* at 16 px in a Discord member list, on its own
terms — not as a national logo with a local decal.

**The mark is produced separately, outside this plan.** What the site needs from
it, as a spec:

- Legible at **16 px** (favicon, Discord member list) and at **28 px** (site
  header) — not just at poster size.
- A **knockout version** that works on `#15213B` ink and on the dark-mode
  ground `#181411`, since both the site and ManyHands follow the reader's
  system theme.
- Square-ish bounding box for avatars, plus a **horizontal lockup** with the
  wordmark for the site header.
- One **accent colour** it can be re-tinted to, so it stays in step with
  `tokens.css` (§5) rather than hard-coding a hex.
- SVG, flat, no gradients or raster effects — it has to animate (§6) and ship
  inside a page budget.

Nothing else in this plan depends on which mark wins.

## 4. Information architecture

14 routes, not 120:

| Route | Source | Holds |
|---|---|---|
| `/` | originate | The ask, what is happening in Philly, latest |
| `/risk` | adapt | Loss of control, condensed from risks + xrisk + outcomes |
| `/risk/now` | adapt | State of the art + urgency — the page that dates fastest |
| `/ask` | adapt | The proposal, and the four levels of regulation |
| `/ask/objections` | adapt | Counterarguments and FAQ in one place |
| `/act` | originate | **The heart of the site.** Events, lobbying, tabling, flyering |
| `/act/congress` | originate | PA delegation: who, what they have said, what to ask |
| `/pennsylvania` | originate | Local orgs, campuses, Harrisburg, local press |
| `/learn` | adapt | Curated reading and quotes |
| `/about` | originate | Who we are — independent, loosely affiliated with PauseAI Global (§0.0) |
| `/join` | originate | → `/manyhands/philly` |
| `/press` | originate | Assets, boilerplate, contact |
| `/license` | originate | §3 |
| `/privacy` | originate | Short, honest, matches what ManyHands actually stores |

`/act` carrying the weight is the point of difference. PauseAI Global is a
movement explainer; this is a **door into a room on a Tuesday night**.

## 5. ManyHands integration

Under §0.3 ManyHands is **frozen**. So this is a one-way, read-only
relationship: the site consumes endpoints that already exist, and **ships no
commits to the ManyHands repo**. Every touchpoint below needs zero changes
there.

A note on what ManyHands now is. Its README describes it as "volunteer
coordination for PauseAI US chapters" and its data model is multi-tenant by
chapter — written when PhillyPause expected to be one. Under §0.0 that framing
is obsolete: ManyHands is **PhillyPause's own volunteer system**, which happens
to be built to host more than one group and is MIT licensed so any other group
can run it. Nothing technical has to change; the multi-tenancy is a harmless
capability rather than a statement of affiliation. Its own copy is stale and
§0.3 says leave it alone — fix it when it next thaws, not before.

- **Events** — `/manyhands/api/chapters/philly/...` is already
  anonymous-readable. Fetch server-side during prerender so events land in the
  HTML for crawlers and unfurls, then hydrate. Same origin, so no CORS, no
  keys, no new endpoint.
- **Join** — `/join` is a page of copy and a link to the built, rate-limited,
  flood-capped join flow. Do not build a second signup form.
- **Calendar** — the existing per-chapter ICS feed is a link, not a feature.
- **Shared tokens** — `Web/ManyHands.Client/wwwroot/css/app.css` already
  defines the palette (`--accent: #c05a0e`, Liberty-Bell orange, with a
  dark-mode `#ee8a3a`). The site's `tokens.css` **copies those values and
  becomes the source of truth**; ManyHands can adopt it whenever it next
  thaws. Inverting the direction keeps §0.3 intact and costs one duplicated
  `:root` block in the meantime.

### 5.1 The site must work when ManyHands is not there

ManyHands is built but **not deployed** — its own `HANDOFF.md` §5 gates on
owner decisions, and §0.3 freezes it. So the site ships first, against an
origin that may 404.

Every ManyHands touchpoint is therefore behind one config flag
(`ManyHands:Enabled`, false by default, matching ManyHands' own
logging-stand-in convention) with a real fallback, not an error:

| Touchpoint | Flag off, or fetch fails |
|---|---|
| Events on `/` and `/act` | A static "next gathering" block from Markdown, hand-edited |
| `/join` | Email address, plus PauseAI Global's directory of groups |
| Calendar feed | Link hidden |

A prerender must never fail because a downstream service is absent. The fetch
is wrapped, timeboxed, and falls through to the static block — tested with the
flag both ways.

This is the honest cost of the ordering in §0.3: `/act` is the page that
justifies a local site at all, and until ManyHands is deployed it is a
hand-edited block rather than a live feed. That is acceptable, and it is cheap to upgrade —
turning the flag on is the whole migration.

## 6. Motion graphics

The mark *is* the motion: two bars that come down and stop a running thing.
Concept E1 (Halted Ring) is built for it — a bell's sound as bars, two tall
ones holding it still.

Techniques, in order of preference:

1. **CSS scroll-driven animations** (`animation-timeline: view()` / `scroll()`).
   No JS, runs on the compositor, cannot jank the main thread.
2. **View Transitions** (`@view-transition`) on route changes, which Blazor's
   enhanced navigation supports directly.
3. **Hand-authored SVG + CSS/WAAPI** for the mark, the bell crack, bar drops.
4. **Lottie** (`dotlottie-wc` behind a lazy-loading Razor component) only where
   1–3 genuinely cannot reach. It is a dependency and a payload; earn it.

Two hard rules:

- **No per-frame C#.** Animation never crosses the WASM boundary. C# sets a
  class or an attribute; CSS and SVG do the work. A marshalled rAF loop will
  drop frames on a mid-range Android, which is what a flyer QR code opens on.
- **`prefers-reduced-motion: reduce` disables every non-essential animation**,
  enforced by a test, not a code review. This site is read by older people and
  by people with vestibular conditions, and it is published by an organisation
  whose argument is about taking risk seriously.

Budget: 100 kB of motion assets total, and first contentful paint under 1.5 s
on a throttled 4G profile. Measured in CI, failing the build.

## 7. Phases

**P0 — Decide** (blocked on owner)
Palette: keep the ManyHands orange, or move primary to Philadelphia civic
navy/gold with orange as an affiliation accent. Where `phillypause.org` points.
The mark arrives on its own schedule (§3.1) and blocks only P3.

**P1 — Skeleton**
Repo, solution, `Directory.Packages.props`, CI with ManyHands' four checks,
DO component, domain, ingress rules, `/` serving one real page.

**P2 — Content engine and the licence spine**
Markdown pipeline, `PageIndex`, `<SourceCredit>` and its test, `/license`,
then the six adapted routes. **Do this before the design system** — the
attribution obligation is the one part of the project with a legal shape, and
it is cheapest to build in before there are 14 pages to retrofit.

**P3 — Identity and motion**
Shared `tokens.css` (and the ManyHands PR), the chosen mark as animated SVG,
scroll-driven hero, reduced-motion tests, performance budget in CI.

**P4 — ManyHands bridge** (read-only, flag-gated — §5.1)
The static fallbacks ship in P2 as ordinary content, so P4 is only the live
path: prerendered events on `/` and `/act`, calendar links, and the flag
flipped. No commits to the ManyHands repo. If ManyHands is still undeployed
when the rest is done, P4 waits and nothing else is blocked.

**P5 — The originals**
`/act/congress` delegation data, `/pennsylvania`, press kit, RSS, generated OG
images per page.

P2 is the long pole. P1 is a day. P0 is a conversation.

## 8. Open decisions

Mirroring ManyHands' `HANDOFF.md` §5, which this plan does not supersede:

- **Palette.** Does orange stay primary, or become an affiliation accent under
  Philadelphia civic navy/gold? This is the one P0 item that actually blocks
  P2, because it decides `tokens.css`. The mark itself does not (§3.1).
- **Where the domain points**, and whether the site fronts Cloudflare (already
  the DNS provider) or sits directly on App Platform.
- **Who writes the adapted content.** The engine is a week; condensing 20
  PauseAI pages into 6 good ones is the actual work, and it is editorial, not
  technical.
- **Whether ManyHands gets deployed during the site build** (§5). Deploying
  what is already built is not "further work on ManyHands" under §0.3, but it
  is still a call, and `/act` is thinner until it happens.

Resolved by §0 and no longer open: localisation, asking PauseAI US about the
mark, and whether the site or ManyHands goes first.
