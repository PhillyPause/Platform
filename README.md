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

```bash
git clone https://github.com/PhillyPause/Platform.git && cd Platform
dotnet run --project Website.Portal
```

The URL prints to the console. Nothing external is contacted — no database, no
API, no service to stand up first.

## Tests

```bash
dotnet test Platform.slnx
```

## Solution layout

| Project | Purpose |
|---|---|
| `Website.Portal` | ASP.NET Core host — prerender, routing, OG tags, sitemap, RSS |
| `Website.Client` | Blazor WASM — interactive islands only |
| `Website.Content` | Markdown + the build-time pipeline that compiles it |
| `Website.Tests` | xunit |

Projects are named `<Product>.<Role>`, and folder, assembly and namespace are
all that same string. The solution is `Platform.slnx` rather than
`Website.slnx` because other products join the solution later, each taking the
same `<Product>.<Role>` shape — see `Docs/PLAN.md` §2.5.

## Content and licensing

Site content is **CC BY 4.0**. Pages adapted from PauseAI carry attribution
generated from their frontmatter, because CC BY 4.0 requires five specific
elements and a hand-written credit forgets one. A page declaring `source:`
without rendering a credit fails a test. See [`Docs/PLAN.md`](Docs/PLAN.md) §3.

Code is [MIT](LICENSE). Neither licence grants rights in any trademark or logo.

## Contributing

Content lives in `Website.Content/Content/**/*.md` — Markdown with
frontmatter. Writing a page needs no C#.
