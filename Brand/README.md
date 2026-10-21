# Brand

Everything made from the PhillyPause mark, outside the code that serves it.

| Folder | Holds |
|---|---|
| `Logo/` | `PhillyPause.svg`, the master: the navy Liberty Bell with its pause bars over the PhillyPause wordmark, cleared to use (PLAN.md §3.1) |
| `YouTube/` | The channel's picture, banner and watermark; [Docs/Outreach/YOUTUBE.md](../Docs/Outreach/YOUTUBE.md) says where each goes |
| `Print/` | The 24 × 36 single-sided poster |

The site serves its own copy of the logo,
`Website/Website.Portal/wwwroot/phillypause-logo.svg`. It has to stay byte for byte
the same as `Logo/PhillyPause.svg`, and a test fails if they drift: change
the master, then copy it over.

The retired logo, PauseAI US's mark with a Liberty Bell flag, never goes
here or anywhere else in the repo (CLAUDE.md).
