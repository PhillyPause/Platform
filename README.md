# PhillyPause Platform

`PhillyPause/Platform` is the organisation's solution. It holds the public
website. It is named for the platform rather than the site because other
projects join it later — see [`Docs/PLAN.md`](Docs/PLAN.md) §2.5 for how
projects are named.

The public website for [PhillyPause](https://phillypause.org) — AI pause
advocacy in Philadelphia. An **independent organisation**, loosely affiliated
with [PauseAI Global](https://pauseai.info) and **not** a local group of
PauseAI US. Reimagines [pauseai.info](https://pauseai.info) for a city, in
Blazor WebAssembly. MIT licensed; fork away.

> Full plan: [`Docs/PLAN.md`](Docs/PLAN.md) — architecture, content strategy,
> licensing, phases, open decisions.

## Stack

C# / .NET 10 · **Blazor Web App** with `InteractiveWebAssembly` render mode and
prerendering · Markdown content compiled at build time (Markdig) · no database
· DigitalOcean App Platform.

Components run as WebAssembly in the browser, but the host prerenders first
paint to real HTML, so crawlers, press and link unfurls get content rather than
an empty `<div>`. Content pages that need no interactivity use static server
rendering and ship **zero** WASM; pages opt in individually. The reasoning is
in [`Docs/PLAN.md`](Docs/PLAN.md) §2.1.

## Getting started

Prereqs: .NET 10 SDK. That is all — no Docker, no database, no API keys.

```powershell
git clone https://github.com/PhillyPause/Platform.git
cd Platform
.\go.ps1
```

**Two commands to remember: pull, then `.\go.ps1`.**

`go.ps1` sets up whatever a clone cannot carry and then runs the site. It is
safe to run every time — each step checks before it changes anything.

```powershell
git pull
.\go.ps1
```

It takes care of:

| | |
|---|---|
| Commit identity | `Louis S. Berman <louis@phillypause.org>`, set **repo-local** so it never leaks into other work on that machine |
| Push credentials | points this repo at the `gh` CLI, because Git Credential Manager may hold a different GitHub account |
| `gh` account | checks you are `louis-phillypause`, and that the token carries the `workflow` scope — without it, any push touching `.github/workflows/` is rejected |
| SDK | checks .NET 10 is on `PATH` |

If `gh` is not logged in it tells you the two commands to run, which need a
browser and so cannot be scripted:

```powershell
gh auth login                                  # choose louis-phillypause
gh auth refresh -h github.com -s workflow
```

`.\go.ps1 -NoRun` does the setup without starting the site.

The URL prints to the console. Nothing external is contacted — no database, no
API, no service to stand up first.

### What does not travel with a clone

Only two things, neither a loss. `.claude/` holds local tool permissions and
regenerates itself. The unused Wikimedia portrait candidates were never
committed — `Docs/PHOTO_SOURCES.md` records every filename and licence, so any
of them is one download away.

## Tests

```bash
dotnet test Platform.slnx
```

## Deploying

The site is live at **[phillypause.org](https://phillypause.org)**, on
DigitalOcean App Platform behind Cloudflare.

Shipping is merging to `main`. The branch requires `build-and-test`, and the
app spec sets `deploy_on_push`, so a merge that passed CI builds and goes out —
about three minutes.

`Deploy/digitalocean/app.yaml` is the deployed configuration. Change it there
and apply it; do not edit the app in the control panel.

```bash
doctl apps update a21db074-3a86-449d-810f-edb0d48e45d6 \
  --spec Deploy/digitalocean/app.yaml
```

[`Docs/HOSTING.md`](Docs/HOSTING.md) has the rest: DNS, TLS, build logs, how to
undo a release, and the two constraints App Platform's .NET buildpack imposes.

## Solution layout

| Folder | Projects |
|---|---|
| `Website/` | `Website.Portal` (the ASP.NET Core host), `Website.Client` (Blazor WASM islands), `Website.Content` (Markdown and the pipeline that compiles it), `Website.Tests` |
| `ManyHands/` | `ManyHands.Core`, `ManyHands.Data`, `ManyHands.Tests`: the volunteer system ([`Docs/MANYHANDS.md`](Docs/MANYHANDS.md)) |
| `Platform/` | `Platform.AppHost` (.NET Aspire, local only), `Platform.ServiceDefaults` |
| `Brand/` | The master logo, the YouTube images, the print poster |

Projects are named `<Product>.<Role>` and grouped in a folder per product;
folder, assembly and namespace are all that same string. The solution is `Platform.slnx` rather than
`Website.slnx` because other products join the solution later, each taking the
same `<Product>.<Role>` shape — see `Docs/PLAN.md` §2.5.

## Content and licensing

Site content is **CC BY 4.0**. Pages adapted from PauseAI carry attribution
generated from their frontmatter, because CC BY 4.0 requires five specific
elements and a hand-written credit forgets one. A page declaring `source:`
without rendering a credit fails a test. See [`Docs/PLAN.md`](Docs/PLAN.md) §3.

Code is [MIT](LICENSE). Neither licence grants rights in any trademark or logo.

## Contributing

Content lives in `Website/Website.Content/Content/**/*.md` — Markdown with
frontmatter. Writing a page needs no C#.
