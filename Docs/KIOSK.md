# The kiosk

`phillypause.org/kiosk` is what the table's screen shows: a loop of bold,
full-screen beats meant to be read from ten feet away, outdoors, by people
who were not planning to stop. It is part of the site, not a second one, and
installs as an app of its own. Unlisted and `noindex`; nothing on the site
links to it.

| File | Does |
|---|---|
| `Website.Portal/Components/Pages/Kiosk.razor` | The beats, the tickers, the framed site |
| `Website.Portal/Components/Layout/KioskLayout.razor` | No site chrome |
| `Website.Portal/wwwroot/kiosk.css` | All of the motion |
| `Website.Portal/wwwroot/kiosk.js` | The loop, the connection check, the framed site, wake lock |
| `Website.Portal/wwwroot/kiosk-sw.js` | The offline copy |
| `Website.Portal/wwwroot/kiosk.webmanifest` | Installs `/kiosk` as "PhillyPause Kiosk", full screen |
| `Website.Portal/wwwroot/img/kiosk-*` | App icons, and the QR code for `https://phillypause.org` |

## What it does

- **Reads from ten feet.** Nothing on the loop is smaller than 13% of the
  screen's short side: 1.7-inch type on the StanbyME 2's 27-inch panel,
  either way up, with capitals over the 1⅛ inches ADA signage asks for at
  ten feet. All capitals, in condensed Archivo and Playfair Display, with
  each line grown to fill the screen's width. A test holds the CSS to it.
- **Never stands still.** Seventeen beats of two to four words each, about
  54 seconds in all, each arriving by its own transition (wipe, iris, drop,
  zoom, flip, slide). Letters tumble in, words slam, race across, and
  wobble out of control, a count runs to 700, backgrounds spin, drift and
  change colour, and tickers scroll along the bottom (and the top, upright).
  The last beat is the QR code: "Scan me to join us". Flashes are few:
  never more than two in a second, inside the three-a-second limit for
  photosensitive seizures (WCAG 2.3.1).
- **Opens the site on a tap**, framed under a bar with a large Back button,
  but only while `phillypause.org` answers. The check runs every 20 seconds
  and whenever the device's connection changes, and it asks for the kiosk's
  own manifest, so a campus Wi-Fi sign-in page does not count as online.
  "Tap the screen" runs in the tickers only while it answers.
- **Goes back by itself** after 90 seconds untouched, or the moment the
  connection drops. The framed page is thrown away on the way back, so the
  next person never sees the last one's. Form fields in it are set not to
  autofill.
- **Keeps people on the screen.** A link that would leave it (another site,
  a new window, an email address) shows the QR code instead, with "scan to
  carry on on your own phone". The Watch dialog's video still plays in place.
- **Offline**, a tap skips to the last beat. The QR code needs no connection
  at the table: it takes people to the site on their own phones.
- **Keeps the screen awake** where the browser allows it, and goes full
  screen on the first tap when it is not running as an installed app.

The greeting is "Hey, Philly!" by default. Add `?at=` once to change it on
that device: `phillypause.org/kiosk?at=Penn` says "Hey, Penn!" from then on,
in the browser and in the installed app.

Every claim on it is from [INCIDENT_FACTS.md](INCIDENT_FACTS.md) or the
explainer's script. Beats are edited in `Kiosk.razor`; each one's time on
screen is its `data-seconds`.

## Offline

`kiosk-sw.js` is registered with scope `/kiosk`, so it answers for the kiosk
and the files the kiosk loads, fonts included, and for nothing else on the
site. The framed site always comes live. On every load with a connection
the page sends the worker the list of files it used; the worker keeps those
and drops anything older. The page itself comes from the network when it
answers within four seconds, so a deploy reaches the screen the next time it
loads online; otherwise the last copy kept is shown.

Load it once with a connection before relying on it offline.

## Setting up a screen

**Chrome or Edge** (a laptop, a Windows or Android tablet): open
`phillypause.org/kiosk?at=<place>`, then *Install app* from the address bar
or the menu. Launch "PhillyPause Kiosk" from then on: it opens full screen.
Turn off the device's screen timeout and sleep.

**LG StanbyME 2** (webOS): open the same address in its web browser and
tap the screen once to go full screen. Whether its browser offers *Install*,
and whether it keeps the offline copy, has not been tried yet: check both at
home before relying on either at a table. If the offline copy does not
stick, the loop still runs as long as the screen stays connected.

Before each table: open the kiosk with a connection, check that "Tap the
screen" runs in the ticker, tap, and press Back.
