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

The joining emails show `Website/Website.Portal/wwwroot/img/email-logo.png`
instead, because mail clients do not draw SVG. It is a rendering, not a copy:
the master drawn at full size on white by headless Chrome, trimmed to the
artwork and scaled to 280 pixels wide, twice the width the email shows it
at. Change the master, and make it again the same way.

The retired logo, PauseAI US's mark with a Liberty Bell flag, never goes
in `Brand/` or anywhere else in the repo (CLAUDE.md).
