/* The kiosk's service worker. Registered by kiosk.js with scope /kiosk, so it
   answers for the kiosk page and what that page loads, and for nothing else
   on the site: the framed site is outside its scope and always comes live.

   The page: from the network when it answers within PAGE_TIMEOUT_MS, so a
   deploy reaches the screen; otherwise the last copy kept. Everything the
   page loads: the kept copy at once, refreshed behind it. kiosk.js sends the
   list of files the page used ("keep"); anything kept that is not on it
   belongs to an older deploy and goes. The probe (?probe=) is never
   answered from here: it exists to ask the network. */

const CACHE = 'pp-kiosk-v1';
const PAGE = '/kiosk';
const PAGE_TIMEOUT_MS = 4_000;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => event.waitUntil((async () => {
  for (const key of await caches.keys()) {
    if (key.startsWith('pp-kiosk-') && key !== CACHE) await caches.delete(key);
  }
  await self.clients.claim();
})()));

self.addEventListener('message', (event) => {
  if (event.data?.type === 'keep') event.waitUntil(keep(event.data.urls));
});

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET') return;
  const url = new URL(request.url);
  if (url.searchParams.has('probe')) return;
  if (request.mode === 'navigate') {
    if (url.origin === self.location.origin && url.pathname === PAGE) event.respondWith(page(request));
    return;
  }
  event.respondWith(file(request, event));
});

async function page(request) {
  const cache = await caches.open(CACHE);
  const key = new URL(PAGE, self.location.origin).href;
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), PAGE_TIMEOUT_MS);
  try {
    const reply = await fetch(request, { signal: abort.signal });
    if (reply.ok) {
      await cache.put(key, reply.clone());
      return reply;
    }
    return (await cache.match(key)) ?? reply;
  } catch {
    return (await cache.match(key)) ?? Response.error();
  } finally {
    clearTimeout(timer);
  }
}

async function file(request, event) {
  const cache = await caches.open(CACHE);
  const kept = await cache.match(request, { ignoreVary: true });
  const fresh = fetch(request).then(async (reply) => {
    if (reply.ok || reply.type === 'opaque') await cache.put(request, reply.clone());
    return reply;
  });
  if (!kept) return fresh;
  event.waitUntil(fresh.catch(() => {}));
  return kept;
}

async function keep(urls) {
  const cache = await caches.open(CACHE);
  const wanted = new Set([PAGE, ...urls].map((url) => new URL(url, self.location.origin).href));
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
