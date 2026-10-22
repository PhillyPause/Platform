/* Joining on a bad connection (JoinForm.razor, in the Join us dialog).

   Pressing Join with no connection would trade the form for the browser's
   own error page, and what the person typed with it. So the press first
   asks the site whether it is there, with the kiosk's probe (a captive
   portal's sign-in page answers too, so the reply must be our manifest).
   Only then does the form go; otherwise it stays, filled in, and says so.

   A page the kiosk's worker served from its kept copy (<html data-kept>)
   carries a form token that may have expired, so once the site answers, a
   fresh token is fetched before the form goes. */

const PROBE_TIMEOUT_MS = 6_000;
const NO_CONNECTION = 'No connection just now. What you typed is still here: press Join again in a moment.';

for (const form of document.querySelectorAll('form.joinfields')) watch(form);

function watch(form) {
  let checked = false;
  form.addEventListener('submit', async (event) => {
    if (checked) return;
    event.preventDefault();
    const button = form.querySelector('button[type=submit]');
    const label = button?.textContent;
    if (button) {
      button.disabled = true;
      button.textContent = 'Checking…';
    }
    const ready = await reachable() && await freshToken(form);
    if (button) {
      button.disabled = false;
      button.textContent = label;
    }
    if (!ready) return say(form, NO_CONNECTION);
    say(form, null);
    checked = true;
    form.submit();
  });
}

async function reachable() {
  if (!navigator.onLine) return false;
  const reply = await ask(`/kiosk.webmanifest?probe=${Date.now()}`);
  return Boolean(reply?.ok && !reply.redirected && (reply.headers.get('content-type') ?? '').includes('manifest'));
}

async function freshToken(form) {
  if (!document.documentElement.hasAttribute('data-kept')) return true;
  const reply = await ask(`${location.pathname}?probe=${Date.now()}`);
  if (!reply?.ok) return false;
  const page = new DOMParser().parseFromString(await reply.text(), 'text/html');
  const token = page.querySelector('form.joinfields input[name="__RequestVerificationToken"]')?.value;
  const field = form.querySelector('input[name="__RequestVerificationToken"]');
  if (token && field) field.value = token;
  return true;
}

async function ask(url) {
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), PROBE_TIMEOUT_MS);
  try {
    return await fetch(url, { cache: 'no-store', signal: abort.signal });
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

function say(form, text) {
  let line = form.querySelector('[data-join-offline]');
  if (!text) return line?.remove();
  if (!line) {
    line = document.createElement('p');
    line.className = 'joinerror';
    line.setAttribute('role', 'alert');
    line.setAttribute('data-join-offline', '');
    form.querySelector('.signuprow')?.before(line);
  }
  line.textContent = text;
}
