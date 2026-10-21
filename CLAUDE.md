# PhillyPause Platform

`github.com/PhillyPause/Platform` — the organisation's solution. It holds the
public website and ManyHands, PhillyPause's volunteer system. It is named for
the platform rather than the site because it holds more than one product
(PLAN.md §2.5).

Public site for PhillyPause, at `/` on `phillypause.org`. Open source (MIT).
ManyHands is served by the same host and is off unless `ManyHands:Enabled` is
set; its design, rules and status are [`Docs/MANYHANDS.md`](Docs/MANYHANDS.md).
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
| `ManyHands.Core/` | ManyHands' domain: records, enums, results; no framework |
| `ManyHands.Data/` | Dapper repositories, DbUp migrations, mail, Discord, background workers |
| `ManyHands.Tests/` | xunit against real Postgres (Testcontainers) |
| `Platform.AppHost/` | .NET Aspire: local orchestration only, never deployed |
| `Platform.ServiceDefaults/` | `AddServiceDefaults()`: OpenTelemetry and health checks, for every service |
| `Docs/` | PLAN, MANYHANDS, HOSTING, KIOSK (the table's screen, `/kiosk`), and DESIGN / CONTENT / MOTION as they land |

Projects are `<Product>.<Role>` — product area first, job second. Folder,
project file, assembly and root namespace are all the same string, with no
`PhillyPause.` prefix: the repo already says it. The solution is
`Platform.slnx` because other products join it later, each as
`<Product>.<Role>` (PLAN.md §2.5). So never name a project just `Portal` or
`Core` — the product segment is what keeps two products apart. `Shared.*` is
earned by a third duplication, not assumed at the first.

Docs filenames: UPPERCASE, underscores not dashes, short.

## Commands

- Build: `dotnet build Platform.slnx`
- Run everything: `dotnet run --project Platform.AppHost` — Aspire starts
  Postgres and the site with ManyHands on and seeded; dashboard at
  https://localhost:17203 (logs, traces, metrics), site at
  http://localhost:5203. Needs Docker (MANYHANDS.md §6)
- Run the site alone: `dotnet run --project Website.Portal` (no Docker, no
  database, ManyHands off)
- Test: `dotnet test Platform.slnx` — **Docker must be running**: ManyHands'
  tests start their own Postgres
- New machine: `git pull` then `.\go.ps1` — sets the repo-local commit
  identity, points pushes at the `gh` CLI, checks the account, scope and SDK,
  then runs the site. Idempotent. README has the detail.

## Switching machines: `pickup`

Work moves between PCs through the remote. Before a hand-off, the last commit
on the working branch carries the hand-off in its message: what was done,
the command that reproduces it, and what is outstanding.

When the user's whole message is **pickup**, they have just pulled on another
machine, on whatever branch it happened to be. Run `git fetch --prune`, then
switch to the remote branch other than `main` with the newest commit
(`git branch -r --sort=-committerdate`), unless already on it, and say which.
If uncommitted changes would block the switch, stop and ask; never discard
them. Read `git log -1` and the docs it names. Set up what the machine lacks,
without asking: for `Video/Explainer`, Git LFS content, ffmpeg on `PATH`, and
`.venv` from `production/requirements.txt`. Make sure the user-level
`~/.claude/CLAUDE.md` carries this rule too, so `pickup` works on that machine
from any branch, even one older than the rule. Run the tests for the area,
then report where things stand in a few lines and carry on with what is
outstanding, asking only about decisions that are the user's.

## Deploying

**The site is live at `phillypause.org`**, on DigitalOcean App Platform behind
Cloudflare. Shipping is merging to `main` — the spec sets `deploy_on_push`, and
`main` requires `build-and-test`, so nothing ships that has not passed CI.

`Deploy/digitalocean/app.yaml` **is** the deployed configuration. Change it
there and `doctl apps update`; never touch the control panel, or the two drift
and the panel wins silently. This has already cost one failed deploy.

Full detail, including the two constraints the .NET buildpack imposes and why
`Website.Client` is `IsPublishable=false`: [`Docs/HOSTING.md`](Docs/HOSTING.md).

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
- **ManyHands serves PhillyPause and nothing else.** One organisation: no
  chapters, tenants, "national" tier or row-level security, ever. Simplicity
  is the point; MANYHANDS.md §2 has the rest of its rules. Until it is enabled
  in production, no page links to it or mentions it to a reader.
- **The old logo is retired, not cleared.** PauseAI US's mark with a Liberty
  Bell flag swapped in is a derivative of another organisation's identity, and
  one we have no affiliation with. It appears nowhere in this repo, not even as
  a placeholder. The replacement mark is produced separately; its spec is
  PLAN.md §3.1.

## Addresses

All aliases on `phillypause.org`. Every `mailto:` carries a `?subject=`, so
mail arrives pre-sorted — a bare `mailto:` is a bug.

| Alias | Carries |
|---|---|
| `info@` | general enquiries, the footer greeting |
| `join@` | the list to import from when a provider lands. Never a signup: joining is the Join us dialog's form |
| `support@` | corrections, privacy and deletion requests |

`press@` is not set up; the archived press page still points at `info@`.

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
- **Every external link opens a new window** — `target="_blank"` plus
  `rel="noopener"`, no exceptions. A reader halfway down this page should not
  lose it to a side trip. Internal routes (`/privacy`, `/license`) stay in the
  tab.

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
  repeatedly.
- **Keep `.ps1` files pure ASCII.** Windows PowerShell 5.1 reads a BOM-less
  UTF-8 file as Windows-1252, so an em dash decodes to three characters, one
  of which is `"` — the script then dies with "string is missing the
  terminator" pointing at a line nowhere near the real one. Write `--`.
- **Quote git config keys in PowerShell.** `credential."https://github.com".helper`
  as a bare argument starts a string and swallows the line; write
  `'credential.https://github.com.helper'`.
- `TreatWarningsAsErrors` is on. A warning is a build break, by choice.
- Central package management is on: versions go in `Directory.Packages.props`,
  never in a `.csproj`. A version attribute on a `PackageReference` is NU1008.
- FluentAssertions is pinned to 7.2.2 — the last Apache-2.0 release before the
  paid Xceed licence. Do not "update" it.
- **`Website.Client` and `Website.Tests` are `IsPublishable=false`.** Leave
  them that way. The deploy buildpack publishes the whole solution, and a WASM
  project published both standalone and as a dependency collides with itself
  over `obj/` — compression, then the IL-trimmer, then webcil
  (dotnet/aspnetcore#38429). The client is still published as the portal's
  dependency, which is the only way its assets are served. `ManyHands.Core`,
  `ManyHands.Data`, `ManyHands.Tests`, `Platform.AppHost` and
  `Platform.ServiceDefaults` are `IsPublishable=false` too; the libraries
  reach the image as the portal's dependencies. Any new project gets it.
- **Aspire's service defaults leave out the HTTP resilience handler** on
  purpose: its 30-second total timeout would cut off the two-minute Congress
  download. Add resilience per client, never globally.
- **`NuGetAuditMode` is `all`**, so a transitive advisory breaks the build.
  Fix it by pinning the patched version in `Directory.Packages.props`
  (transitive pinning is on), as `MessagePack` is for Aspire.
- **ManyHands is behind `ManyHands:Enabled`, off by default.** Off, nothing of
  it is registered and the site needs no database. A prerender must never fail
  for want of it: any page that shows ManyHands data falls back to static
  content when it is off or unreachable.
- **Dapper and Npgsql fail at runtime, not compile time** — value tuples never
  map, and `timestamptz`, `bigint` and arrays need the handlers in
  `ManyHands.Data/DapperTypeHandlers.cs`. MANYHANDS.md §4 has the table.
  Test SQL against real Postgres, never a stub reader.
