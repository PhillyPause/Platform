/* The kiosk (/kiosk, Components/Pages/Kiosk.razor).

   kiosk.css does the motion; this module does what CSS cannot.

   1. It runs the loop: which beat is on, for its data-seconds. It sizes
      each line to fill the screen's width, never below FLOOR.
   2. It asks phillypause.org whether it is there, every PROBE_MS and whenever
      the browser's own idea of online changes. Only while it answers is the
      root .is-online, which puts "Tap the screen" in the tickers, and a tap
      opens the site.
   3. It frames the site under a Back bar, and goes back by itself after
      IDLE_MS untouched or if the connection drops. The frame is removed on
      the way back, so the next person starts clean. Links that would leave
      the screen (another site, a new window, an email) show a QR code
      instead: they belong on the reader's own phone.
   4. It keeps the screen awake, goes full screen on the first tap when not
      installed as an app, and registers kiosk-sw.js so the loop runs offline.
      Awake is the Wake Lock where the browser has one, and the silent video
      behind the loop kept playing, for TVs whose screen saver waits on video.
   5. It opens the kiosk menu when the screen is held for HOLD_MS or Back is
      pressed on a remote: the way out of full screen, for whoever runs the
      table. The menu closes by itself after MENU_MS.

   ?at=Penn on the address sets the greeting ("Hey, Penn.") and is
   remembered on this device, so the installed app keeps it. */

const IDLE_MS = 90_000;
const PROBE_MS = 20_000;
const PROBE_TIMEOUT_MS = 5_000;
const HOLD_MS = 3_000;
const MENU_MS = 20_000;
const PLACE_KEY = 'pp-kiosk-at';
const FIT_GROWTH = 2.4;
// Of the screen's short side: kiosk.css's smallest type, legible from ten feet.
const FLOOR = 0.13;

const root = document.querySelector('[data-kiosk]');
if (root) start(root);

function start(root) {
  const beats = [...root.querySelectorAll('.kbeat')];
  const last = beats.findIndex((beat) => beat.hasAttribute('data-kiosk-last'));
  const site = root.querySelector('[data-kiosk-site]');
  const leaving = root.querySelector('[data-kiosk-leaving]');
  const menu = root.querySelector('[data-kiosk-menu]');
  const awake = root.querySelector('[data-kiosk-awake]');

  let current = -1;
  let beatTimer = 0;
  let idleTimer = 0;
  let online = false;
  let browsing = false;
  let holdTimer = 0;
  let held = false;
  let menuTimer = 0;
  let windowed = false;

  greet(root);
  fit(root);
  addEventListener('resize', () => fit(root));
  document.fonts?.ready.then(() => fit(root));
  document.fonts?.addEventListener('loadingdone', () => fit(root));

  // 1. The loop.
  function show(index) {
    clearTimeout(beatTimer);
    const next = beats[index];
    beats.forEach((beat) => beat.classList.remove('was-on'));
    if (current >= 0 && beats[current] !== next) {
      beats[current].classList.remove('is-on');
      beats[current].classList.add('was-on');
    }
    // Restart the beat's animations even when it is shown twice running.
    next.classList.remove('is-on');
    void next.offsetWidth;
    next.classList.add('is-on');
    current = index;
    beatTimer = setTimeout(() => show((current + 1) % beats.length), Number(next.dataset.seconds) * 1000);
  }

  // 2. Is the site there? A captive portal answers too, so the reply has to
  // be our manifest, not just any 200.
  async function probe() {
    if (!navigator.onLine) return setOnline(false);
    const abort = new AbortController();
    const timer = setTimeout(() => abort.abort(), PROBE_TIMEOUT_MS);
    try {
      const reply = await fetch(`/kiosk.webmanifest?probe=${Date.now()}`, { cache: 'no-store', signal: abort.signal });
      setOnline(reply.ok && !reply.redirected && (reply.headers.get('content-type') ?? '').includes('manifest'));
    } catch {
      setOnline(false);
    } finally {
      clearTimeout(timer);
    }
  }

  function setOnline(value) {
    online = value;
    root.classList.toggle('is-online', online);
    if (!online && browsing) back();
  }

  // 3. The site, framed.
  function openSite() {
    browsing = true;
    clearTimeout(beatTimer);
    const frame = document.createElement('iframe');
    frame.className = 'ksite-frame';
    frame.title = 'phillypause.org';
    frame.src = '/';
    frame.addEventListener('load', () => guard(frame));
    site.append(frame);
    site.hidden = false;
    touched();
  }

  function back() {
    browsing = false;
    clearTimeout(idleTimer);
    site.querySelector('.ksite-frame')?.remove();
    site.hidden = true;
    leaving.hidden = true;
    show(0);
  }

  function touched() {
    clearTimeout(idleTimer);
    idleTimer = setTimeout(back, IDLE_MS);
  }

  function guard(frame) {
    let page;
    try {
      page = frame.contentDocument;
    } catch {
      page = null;
    }
    // Not our own page any more (it cannot be read): back to the loop.
    if (!page) return back();

    for (const type of ['pointerdown', 'keydown', 'wheel', 'touchstart', 'scroll']) {
      page.addEventListener(type, touched, { capture: true, passive: true });
    }
    page.addEventListener('keydown', (event) => {
      if (!isBack(event)) return;
      event.preventDefault();
      back();
    });
    quiet(page);
    page.addEventListener('click', (event) => {
      touched();
      quiet(page);
      const link = event.target.closest?.('a[href]');
      // The Watch dialog's poster is a YouTube link its own script plays in place.
      if (!link || link.matches('[data-watch-play]')) return;
      const url = new URL(link.getAttribute('href'), page.baseURI);
      const stays = url.origin === location.origin && link.target !== '_blank' && !url.pathname.startsWith('/kiosk');
      if (stays) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      leaving.hidden = false;
    }, true);
  }

  root.querySelector('[data-kiosk-back]').addEventListener('click', back);
  root.querySelector('[data-kiosk-stay]').addEventListener('click', () => { leaving.hidden = true; touched(); });
  site.addEventListener('pointerdown', touched);

  // A tap on the loop: the site when it is there, otherwise the last beat.
  // The tap that ends a hold opened the menu; it is not a tap on the loop.
  root.addEventListener('click', (event) => {
    if (held) {
      held = false;
      return;
    }
    if (browsing || site.contains(event.target) || menu.contains(event.target)) return;
    if (online) openSite();
    else if (current !== last) show(last);
  });

  // 4. Awake, full screen, and offline.
  const keepPlaying = () => awake.play().catch(() => {});
  stayAwake();
  keepPlaying();
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState !== 'visible') return;
    stayAwake();
    keepPlaying();
  });
  root.addEventListener('pointerdown', (event) => {
    keepPlaying();
    if (!windowed && !menu.contains(event.target)) fullScreen();
  });

  // 5. The kiosk menu.
  function openMenu() {
    menu.hidden = false;
    // An installed app is full screen for good: it is closed like any app.
    root.querySelector('[data-kiosk-windowed]').hidden = installed();
    clearTimeout(menuTimer);
    menuTimer = setTimeout(closeMenu, MENU_MS);
  }

  function closeMenu() {
    clearTimeout(menuTimer);
    menu.hidden = true;
  }

  root.addEventListener('pointerdown', (event) => {
    if (browsing || menu.contains(event.target)) return;
    held = false;
    clearTimeout(holdTimer);
    holdTimer = setTimeout(() => {
      held = true;
      openMenu();
    }, HOLD_MS);
  });
  for (const type of ['pointerup', 'pointercancel', 'pointerleave']) {
    root.addEventListener(type, () => clearTimeout(holdTimer));
  }
  addEventListener('keydown', (event) => {
    if (!isBack(event)) return;
    event.preventDefault();
    if (browsing) back();
    else if (menu.hidden) openMenu();
    else closeMenu();
  });
  root.querySelector('[data-kiosk-resume]').addEventListener('click', closeMenu);
  root.querySelector('[data-kiosk-windowed]').addEventListener('click', () => {
    windowed = true;
    closeMenu();
    document.exitFullscreen?.().catch(() => {});
  });

  addEventListener('online', probe);
  addEventListener('offline', probe);
  setInterval(probe, PROBE_MS);
  probe();
  keepOffline();
  show(0);
}

// The greeting's place: from ?at=, else what this device was last given.
function greet(root) {
  let place = new URLSearchParams(location.search).get('at')?.trim().slice(0, 24);
  try {
    if (place) localStorage.setItem(PLACE_KEY, place);
    else place = localStorage.getItem(PLACE_KEY);
  } catch {
    // Storage blocked: the address still works for this visit.
  }
  if (place) root.querySelectorAll('[data-kiosk-place]').forEach((span) => { span.textContent = place; });
}

// Set each .kfit line to the width it stands in: shrunk if it runs over,
// grown up to FIT_GROWTH times its CSS size if it falls short. Then, while a
// beat is taller than its room, its .kfit lines give up the difference. No
// line goes below FLOOR. Layout sizes, not on-screen boxes, so a line still
// flying in measures where it lands. On resize and as fonts arrive, never
// per frame.
function fit(root) {
  const floor = FLOOR * Math.min(innerWidth, innerHeight);
  const size = (line) => parseFloat(getComputedStyle(line).fontSize);
  const resize = (line, px) => { line.style.fontSize = `${Math.max(px, floor)}px`; };

  root.querySelectorAll('.kbody').forEach((body) => {
    const lines = [...body.querySelectorAll('.kfit')];
    if (lines.length === 0) return;

    lines.forEach((line) => {
      line.style.fontSize = '';
      resize(line, size(line) * Math.min(line.parentElement.clientWidth / line.scrollWidth, FIT_GROWTH));
    });

    for (let pass = 0; pass < 3; pass++) {
      const shown = [...body.children];
      const used = Math.max(...shown.map((child) => child.offsetTop + child.offsetHeight)) - Math.min(...shown.map((child) => child.offsetTop));
      if (used <= body.clientHeight) break;
      const fitted = lines.reduce((sum, line) => sum + line.offsetHeight, 0);
      const ratio = Math.max(0.3, 1 - (used - body.clientHeight) / fitted);
      lines.forEach((line) => resize(line, size(line) * ratio));
    }
  });
}

// A shared screen should not offer the last person's email to the next.
function quiet(page) {
  page.querySelectorAll('input, textarea').forEach((field) => field.setAttribute('autocomplete', 'off'));
}

async function stayAwake() {
  try {
    await navigator.wakeLock?.request('screen');
  } catch {
    // Refused (battery saver, or not visible): the screen's own settings decide.
  }
}

function installed() {
  return matchMedia('(display-mode: fullscreen), (display-mode: standalone)').matches;
}

function fullScreen() {
  if (!installed() && !document.fullscreenElement) document.documentElement.requestFullscreen?.().catch(() => {});
}

// Escape on a keyboard, Back on a remote (461 is LG webOS's).
function isBack(event) {
  return event.key === 'Escape' || event.key === 'GoBack' || event.key === 'BrowserBack' || event.keyCode === 461;
}

// Register the worker, then hand it every file this page loaded, so the next
// visit with no connection finds them all.
async function keepOffline() {
  if (!('serviceWorker' in navigator)) return;
  try {
    await navigator.serviceWorker.register('/kiosk-sw.js', { scope: '/kiosk' });
    const registration = await navigator.serviceWorker.ready;
    const files = performance.getEntriesByType('resource').map((entry) => entry.name).filter((url) => !url.includes('probe='));
    registration.active?.postMessage({ type: 'keep', urls: [location.pathname, '/kiosk.webmanifest', ...files] });
  } catch {
    // No worker: the kiosk still runs while online.
  }
}
