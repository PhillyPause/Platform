# The kiosk

[phillypause.org/kiosk](https://phillypause.org/kiosk) is what the table's
screen shows: a loop of bold, full-screen beats meant to be read from ten
feet away, outdoors, by people who were not planning to stop. It is part of
the site, not a second one, and installs as an app of its own. Unlisted and
`noindex`; nothing on the site links to it.

The first half of this page is for whoever runs the table. The rest is how
it works.

## Running a table

### The day before, at home

1. Charge the screen fully.
2. Open **phillypause.org/kiosk?at=Penn** in the screen's web browser
   (`at=` is the greeting, "Hey, Penn!"; use wherever the table will be).
   The screen remembers it, so later you can open plain
   **phillypause.org/kiosk**.
3. Tap the screen once. It goes full screen and the loop starts.
4. Check the bottom ticker says **TAP THE SCREEN** within half a minute.
   That means the screen can reach the site.
5. Tap. The website opens under a red **← Back** bar. Press **← Back**.
6. Hold the screen for **three seconds**. The kiosk menu opens. Press
   **Keep going**.
7. Leave it untouched for longer than the screen saver used to take. It
   should stay on.

Loading it with a connection is also what saves the copy it runs from when
there is no connection.

### At the table

1. Connect the screen to Wi-Fi or a phone hotspot if there is one. On a
   campus network that asks you to sign in, sign in from the browser first.
2. Open **phillypause.org/kiosk** and tap the screen once to go full
   screen.
3. Leave it. It loops by itself, about 54 seconds a time, and ends on the
   QR code.

### What visitors can do

- **Watch.** Nothing needs touching.
- **Scan the QR code** on the last screen with their phone. It opens
  phillypause.org on their own phone, connection or not at the table.
- **Tap the screen** (only while "Tap the screen" is in the ticker) to
  browse the website. **← Back** returns to the loop, and so does leaving it
  for 90 seconds. Whatever they typed is thrown away, so the next person
  starts clean.
- **Tap a link to another site or an email address.** It doesn't leave the
  screen: a QR code comes up instead, to carry on on their own phone.

### Getting out

Hold the screen for **three seconds**, or press **Back** on the remote.
The kiosk menu opens:

- **Leave full screen** brings back the browser's own toolbar, and from
  there the screen's home and settings. The kiosk stops going full screen
  until it is reloaded. If the browser is keeping full screen itself (its
  own full-screen button, or a TV browser that only runs full screen), a
  page can't undo that, and the menu says so instead: press **Home** or
  **Back** on the remote, or use the browser's own full-screen button.
- **Keep going** closes the menu. It closes by itself after 20 seconds, in
  case a visitor finds it.

### If something goes wrong

| What you see | What to do |
|---|---|
| No **TAP THE SCREEN** in the ticker | The screen can't reach phillypause.org. The loop and the QR code work anyway. To fix it: hold three seconds, **Leave full screen**, sign in to the Wi-Fi or switch to a hotspot, then reload the page |
| Stuck in the website | **← Back**, top left, or wait 90 seconds |
| Stuck in full screen | Hold the screen three seconds |
| Screen saver comes on | Turn it off in the screen's settings (see below). If it still comes on, the silent video isn't enough on this screen: tell whoever looks after the site |
| Wrong greeting | Open **phillypause.org/kiosk?at=** with the right place, once |
| An old version after a change to the site | Reload it with a connection |

## Setting up a screen

**LG StanbyME 2** (webOS): open the address in its web browser and tap the
screen once to go full screen. In the screen's settings, turn off
anything called *Screen Saver*, *Energy Saving*, *Auto Power Off* or
*Sleep Timer*. Names and places vary between webOS versions, and newer
ones may not offer a screen saver switch at all, which is what the silent
video is for. Whether its browser offers *Install*, and whether it keeps
the offline copy, has not been tried yet. If the offline copy doesn't
stick, the loop still runs as long as the screen stays connected.

**Chrome or Edge** (a laptop, a Windows or Android tablet): open
`phillypause.org/kiosk?at=<place>`, then *Install app* from the address bar
or the menu. Launch "PhillyPause Kiosk" from then on: it opens full screen,
and is closed like any app. Turn off the device's screen timeout and sleep.

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
  connection drops. The framed page is thrown away on the way back. Form
  fields in it are set not to autofill.
- **Keeps people on the screen.** A link that would leave it (another site,
  a new window, an email address) shows the QR code instead. The Watch
  dialog's video still plays in place.
- **Offline**, a tap skips to the last beat.
- **Keeps the screen awake** two ways: the Wake Lock, where the browser has
  one, and `kiosk-awake.mp4`, four seconds of silent black looping behind
  the loop. LG's webOS gives a web page no way to stop its screen saver,
  and its screen saver ignores animation, but LG says it stays off while
  video plays.
- **Goes full screen** on the first tap when it is not running as an
  installed app.
- **Has a menu for whoever runs the table**, on a three-second hold or Back
  on a remote (Escape on a keyboard). Back on a remote while the site is
  open goes straight back to the loop.

The greeting is "Hey, Philly!" until `?at=` sets it, and the device keeps
the last one it was given, in the browser and in the installed app.

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

## Files

| File | Does |
|---|---|
| `Website.Portal/Components/Pages/Kiosk.razor` | The beats, the tickers, the menu, the framed site |
| `Website.Portal/Components/Layout/KioskLayout.razor` | No site chrome |
| `Website.Portal/wwwroot/kiosk.css` | All of the motion |
| `Website.Portal/wwwroot/kiosk.js` | The loop, the connection check, the framed site, the menu, keeping awake |
| `Website.Portal/wwwroot/kiosk-sw.js` | The offline copy |
| `Website.Portal/wwwroot/kiosk-awake.mp4` | Silent video that keeps a TV's screen saver off |
| `Website.Portal/wwwroot/kiosk.webmanifest` | Installs `/kiosk` as "PhillyPause Kiosk", full screen |
| `Website.Portal/wwwroot/img/kiosk-*` | App icons, and the QR code for `https://phillypause.org` |
