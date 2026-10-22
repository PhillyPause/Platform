/* The kiosk's service worker. Registered by kiosk.js with scope /kiosk, so it
   answers for the kiosk page and what that page loads, for the site as the
   kiosk frames it (/kiosk/site, the front page at an address in scope) and
   what that loads, and for nothing else on the site.

   A page: from the network when it answers within PAGE_TIMEOUT_MS, so a
   deploy reaches the screen; otherwise the last copy kept. Everything a page
   loads: the kept copy at once, refreshed behind it. The loop and the framed
   site keep separate copies. kiosk.js sends the list of files each one used
   ("keep", "keep-site"), and anything kept that is not on its list belongs
   to an older deploy and goes. The loop's list is sent on every load; the
   site's only after it loaded from the network, so a visit with no
   connection never throws the site's copy away. The probe (?probe=) is never
   answered from here: it exists to ask the network. */

const CACHE = 'pp-kiosk-v1';
const SITE_CACHE = 'pp-kiosk-site-v1';
const PAGE = '/kiosk';
const SITE = '/kiosk/site';
const PAGE_TIMEOUT_MS = 4_000;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => event.waitUntil((async () => {
  for (const key of await caches.keys()) {
    if (key.startsWith('pp-kiosk-') && key !== CACHE && key !== SITE_CACHE) await caches.delete(key);
  }
  await self.clients.claim();
})()));

self.addEventListener('message', (event) => {
  if (event.data?.type === 'keep') event.waitUntil(keep(CACHE, PAGE, event.data.urls));
  if (event.data?.type === 'keep-site') event.waitUntil(keep(SITE_CACHE, SITE, event.data.urls));
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.searchParams.has('probe')) return;
  if (request.mode === 'navigate') {
    if (url.origin !== self.location.origin) return;
    if (url.pathname === PAGE) event.respondWith(page(request, CACHE, PAGE));
    else if (url.pathname === SITE) event.respondWith(page(request, SITE_CACHE, SITE));
    return;
  }
  // A page fetched by Blazor's enhanced navigation inside the framed site
  // is a page, not a file: it always comes live.
  if ((request.headers.get('accept') ?? '').includes('text/html')) return;
  event.respondWith(cacheFor(event).then((name) => file(request, event, name)));
});

// Which copy a file belongs to: the framed site's, if the framed site asked.
async function cacheFor(event) {
  const client = event.clientId ? await self.clients.get(event.clientId) : null;
  return client && new URL(client.url).pathname === SITE ? SITE_CACHE : CACHE;
}

async function page(request, name, path) {
  const cache = await caches.open(name);
  const key = new URL(path, self.location.origin).href;
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), PAGE_TIMEOUT_MS);
  try {
    const reply = await fetch(request, { signal: abort.signal });
    if (reply.ok) {
      await cache.put(key, reply.clone());
      return reply;
    }
    return (await kept(cache, key, path)) ?? reply;
  } catch {
    return (await kept(cache, key, path)) ?? Response.error();
  } finally {
    clearTimeout(timer);
  }
}

// The kept copy of a page. The site's is marked data-kept on <html>, so
// join.js knows its form token may be stale and fetches a fresh one first.
async function kept(cache, key, path) {
  const copy = await cache.match(key);
  if (!copy || path !== SITE) return copy;
  const html = (await copy.text()).replace('<html', '<html data-kept');
  return new Response(html, { status: copy.status, headers: copy.headers });
}

async function file(request, event, name) {
  const cache = await caches.open(name);
  const copy = await cache.match(request, { ignoreVary: true });
  const fresh = fetch(request).then(async (reply) => {
    if (reply.ok || reply.type === 'opaque') await cache.put(request, reply.clone());
    return reply;
  });
  if (!copy) return fresh;
  event.waitUntil(fresh.catch(() => {}));
  return copy;
}

async function keep(name, path, urls) {
  const cache = await caches.open(name);
  const wanted = new Set([path, ...urls].map((url) => new URL(url, self.location.origin).href));
  for (const request of await cache.keys()) {
    if (!wanted.has(request.url)) await cache.delete(request);
  }
  await Promise.all([...wanted].map(async (url) => {
    if (await cache.match(url, { ignoreVary: true })) return;
    try {
      const reply = await grab(url);
      if (reply.ok || reply.type === 'opaque') await cache.put(url, reply);
    } catch {
      // Unreachable now; the next visit online sends the list again.
    }
  }));
}

// Fonts must come back CORS-readable or the browser refuses them, so a
// cross-origin file is asked for that way first.
async function grab(url) {
  if (new URL(url).origin === self.location.origin) return fetch(url, { cache: 'no-cache' });
  try {
    return await fetch(url, { mode: 'cors', credentials: 'omit' });
  } catch {
    return fetch(url, { mode: 'no-cors' });
  }
}
