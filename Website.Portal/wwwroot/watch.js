/* The Watch dialog.

   The dialog itself is CSS, like every other dialog on the page: a radio in
   the "dlg" group. This module does the three things CSS cannot.

   1. It opens the dialog once, on a reader's first visit. Remembered per video
      id, so when the video changes it is offered once more, and never again.
   2. It swaps the poster for the player when pressed. The poster is a real
      link to the video on YouTube, so without this script it still works, in
      a new window. With it, nothing loads from YouTube until that press.
   3. It removes the player when the dialog closes. A hidden iframe keeps
      playing, and sound from a closed dialog is the worst thing it could do.

   Every listener is delegated to the document, because Blazor's enhanced
   navigation replaces the page's elements without re-running this module. */

const SEEN_PREFIX = 'pp-watch-seen:';

function embed(poster) {
  const player = document.createElement('iframe');
  player.className = 'watchvideo';
  // cc_load_policy=0 starts with captions off; the reader can still turn them on.
  player.src = `https://www.youtube-nocookie.com/embed/${encodeURIComponent(poster.dataset.video)}?autoplay=1&rel=0&cc_load_policy=0`;
  player.title = poster.dataset.title ?? 'Video';
  player.allow = 'autoplay; encrypted-media; picture-in-picture; fullscreen';
  player.allowFullscreen = true;
  player.referrerPolicy = 'strict-origin-when-cross-origin';

  poster.hidden = true;
  poster.after(player);
  player.focus();
}

function unembed() {
  document.querySelectorAll('.watchvideo').forEach((player) => player.remove());
  document.querySelectorAll('[data-watch-play]').forEach((poster) => { poster.hidden = false; });
}

function introduceOnce() {
  const toggle = document.getElementById('watch-open');
  const none = document.getElementById('dlg-none');
  const poster = document.querySelector('[data-watch-play]');

  // Not the home page, or the reader already has a dialog open (a restored
  // back-navigation can bring one back checked).
  if (!toggle || !poster || !none?.checked) return;

  const key = SEEN_PREFIX + poster.dataset.video;
  try {
    if (localStorage.getItem(key)) return;
    localStorage.setItem(key, '1');
  } catch {
    // Storage blocked. Never opening is better than opening on every visit.
    return;
  }

  toggle.checked = true;
}

document.addEventListener('click', (event) => {
  const poster = event.target.closest?.('[data-watch-play]');
  if (!poster) return;
  event.preventDefault();
  embed(poster);
});

// A radio fires change only when it becomes checked, so any change in the
// group that is not the Watch radio means the Watch dialog just closed.
document.addEventListener('change', (event) => {
  if (event.target.name === 'dlg' && event.target.id !== 'watch-open') unembed();
});

window.Blazor?.addEventListener?.('enhancedload', introduceOnce);
introduceOnce();
