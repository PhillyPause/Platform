# Brand

[`Brand/`](../../Brand/) holds everything made from the PhillyPause mark,
outside the code that serves it.

| Folder | Holds |
|---|---|
| [`Brand/Logo/`](../../Brand/Logo/) | `PhillyPause.svg`, the master: the navy Liberty Bell with its pause bars over the PhillyPause wordmark, cleared to use (PLAN.md §3.1) |
| [`Brand/YouTube/`](../../Brand/YouTube/) | The channel's picture, banner and watermark; [YOUTUBE.md](YOUTUBE.md) says where each goes |
| [`Brand/Print/`](../../Brand/Print/) | The 24 × 36 single-sided poster |

Two copies of the logo are in use: the site's,
`Website/Website.Portal/wwwroot/phillypause-logo.svg`, and the explainer
video's, `Video/Explainer/production/assets/brand/logo.svg`. Both have to
stay byte for byte the same as `Brand/Logo/PhillyPause.svg`, and a test fails
if either drifts: change the master, then copy it over both.

The retired logo, PauseAI US's mark with a Liberty Bell flag, never goes
in `Brand/` or anywhere else in the repo (CLAUDE.md).
