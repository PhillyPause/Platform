# PhillyPause Platform

`github.com/PhillyPause/Platform` — the organisation's solution. It holds the
public website. It is named for the platform rather than the site because
other projects join the solution later (PLAN.md §2.5).

Public site for PhillyPause, at `/` on `phillypause.org`. Open source (MIT).
Present-tense docs only: this file and `Docs/` describe what IS; `git log` is
the only history.

The full plan — architecture, content strategy, licensing, phases — is
[`Docs/PLAN.md`](Docs/PLAN.md). Read its §0 before proposing anything.

## Layout

| Folder | Holds |
|---|---|
| `Website.Portal/` | ASP.NET Core host; prerender, routing, OG tags, sitemap, RSS |
| `Website.Client/` | Blazor WASM; interactive islands only |
| `Website.Content/` | Markdown content + the build-time compile pipeline |
| `Website.Tests/` | xunit |
| `Docs/` | PLAN, and DESIGN / CONTENT / MOTION / HOSTING as they land |

Projects are `<Product>.<Role>` — product area first, job second. Folder,
project file, assembly and root namespace are all the same string, with no
`PhillyPause.` prefix: the repo already says it. The solution is
`Platform.slnx` because other products join it later, each as
`<Product>.<Role>` (PLAN.md §2.5). So never name a project just `Portal` or
`Core` — the product segment is what keeps two products apart. `Shared.*` is
earned by a third duplication, not assumed at the first.

Docs filenames: UPPERCASE, underscores not dashes, short. Same as ManyHands.

## Commands

- Build: `dotnet build Platform.slnx`
- Run: `dotnet run --project Website.Portal` (no Docker, no database)
- Test: `dotnet test Platform.slnx`

## Who PhillyPause is (get this right — it has been got wrong)

**An independent Philadelphia organisation, loosely affiliated with PauseAI
Global** (Stichting PauseAI, a Dutch foundation).

**PhillyPause is NOT a local group, chapter or branch of PauseAI US.** PauseAI
US dropped all of its local groups, PhillyPause among them. Never write
"chapter of", "local group of", "under" or "part of" about any national
organisation. The affiliation with PauseAI Global is loose, and amounts to
shared analysis plus content reused under CC BY 4.0 — not authority in either
direction.

## Standing constraints (Docs/PLAN.md §0 — do not relitigate)

- **English only.** No localisation layer, no translation tooling, no
  language-switching routes. PauseAI Global runs Inlang/Paraglide; we do not.
- **No communications with PauseAI US.** There is no relationship to maintain.
  Nothing here needs their input, assets, sign-off or review, and no proposal
  should assume otherwise.
- **The site ships before any work on ManyHands.** ManyHands is a future
  project: nothing in this repo depends on it, links to it, or mentions it to a
  reader. It is not deployed and its repo is not public, so a link to it is a
  404 (PLAN.md §5).
- **The old logo is retired, not cleared.** PauseAI US's mark with a Liberty
  Bell flag swapped in is a derivative of another organisation's identity, and
  one we have no affiliation with. It appears nowhere in this repo, not even as
  a placeholder. The replacement mark is produced separately; its spec is
  PLAN.md §3.1.

## Domain rules (do not relitigate)

- Content is **Markdown compiled at build time**, never parsed at runtime and
  never in a database. A volunteer who can write Markdown can open a PR.
- Attribution is **structural, not editorial**: `<SourceCredit>` renders the
  five elements CC BY 4.0 requires from frontmatter. A page with `source:` and
  no credit fails a test.
- **Static server rendering is the default.** A page becomes
  `InteractiveWebAssembly` only when it genuinely needs interactivity. Every
  such opt-in costs the reader a WASM download; justify it.
- **No per-frame C#.** Animation never crosses the WASM boundary — C# sets a
  class or an attribute, CSS and SVG do the work (PLAN.md §6).
- `prefers-reduced-motion: reduce` disables every non-essential animation,
  enforced by a test rather than a code review.
- Policy documents (code of conduct, safeguarding) are **linked, never
  cloned**. A chapter publishing its own stale policy is worse off.

## Git — non-negotiable

- **Never leave work uncommitted.** Commit before any restructure, rename or delete.
- `main` is protected; all work on `feature/*` / `fix/*` / `docs/*` branches,
  merged via PR. Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`).
- Windows is case-insensitive: `docs` and `Docs` are the SAME directory.
  Case-only renames must bounce through a temp name; never `rm -rf` without
  listing contents first.

## Sharp edges

- **Bash heredocs on this machine mangle backslashes** — a `<<'EOF'` block
  embedding `\n` or `\\` can arrive corrupted. Write multi-line files with a
  file tool, or write to the scratchpad and run the file. This has bitten
  repeatedly in ManyHands.
- `TreatWarningsAsErrors` is on. A warning is a build break, by choice.
- Central package management is on: versions go in `Directory.Packages.props`,
  never in a `.csproj`. A version attribute on a `PackageReference` is NU1008.
- FluentAssertions is pinned to 7.2.2 — the last Apache-2.0 release before the
  paid Xceed licence. Do not "update" it.
- **Nothing here calls ManyHands, and no such integration exists yet.** When
  one lands it goes behind a config flag with a static fallback, because a
  prerender must never fail for want of a downstream service (PLAN.md §5.1).
  Until then, do not describe the flag as if it were there.
