/* ACA360 service worker - makes the app installable and quick to start.

   ACA360 shows personal data (SSNs, pay, coverage), so pages and data are NEVER cached:
   they always come from the network, exactly as without the service worker. Only static
   files (styles, scripts, fonts, images) are cached, and an offline page is shown when a
   page cannot be reached at all. Change VERSION to drop old caches after a release. */

const VERSION = 'aca360-v1';
const STATIC_CACHE = VERSION + '-static';
const SCOPE = self.registration.scope;                      // e.g. https://host/ or https://host/app/
const at = function (path) { return new URL(path, SCOPE); };

const OFFLINE_URL = at('offline.html').href;
const PRECACHE = ['offline.html', 'icons/icon-192.png', 'images/medcom_logo_small.png'].map(function (p) { return at(p).href; });

// Folders whose files are static and safe to keep.
const STATIC_DIRS = ['css/', 'js/', 'lib/', 'assets/', 'fonts/', 'images/', 'icons/'].map(function (p) { return at(p).pathname; });
// Never touched by the service worker at all (the employee portal is a separate site;
// uploads hold user files).
const BYPASS = ['Portal/', 'api/', 'hangfire', 'uploads/'].map(function (p) { return at(p).pathname.toLowerCase(); });

self.addEventListener('install', function (event) {
    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then(function (cache) { return cache.addAll(PRECACHE); })
            .then(function () { return self.skipWaiting(); })
    );
});

self.addEventListener('activate', function (event) {
    event.waitUntil(
        caches.keys()
            .then(function (keys) {
                return Promise.all(keys
                    .filter(function (k) { return k.indexOf('aca360-') === 0 && k !== STATIC_CACHE; })
                    .map(function (k) { return caches.delete(k); }));
            })
            .then(function () { return self.clients.claim(); })
    );
});

self.addEventListener('fetch', function (event) {
    const req = event.request;
    if (req.method !== 'GET') return;

    const url = new URL(req.url);
    if (url.origin !== self.location.origin) return;
    const path = url.pathname.toLowerCase();
    if (BYPASS.some(function (p) { return path.indexOf(p) === 0; })) return;

    // Pages: always from the network; the offline page only when the network is unreachable.
    if (req.mode === 'navigate') {
        event.respondWith(fetch(req).catch(function () { return caches.match(OFFLINE_URL); }));
        return;
    }

    // Static files: answer from the cache at once and refresh it in the background.
    if (STATIC_DIRS.some(function (p) { return url.pathname.indexOf(p) === 0; })) {
        event.respondWith(staleWhileRevalidate(req, event));
    }

    // Everything else (data, partial pages, downloads) goes straight to the network and is never stored.
});

function staleWhileRevalidate(req, event) {
    return caches.open(STATIC_CACHE).then(function (cache) {
        return cache.match(req).then(function (cached) {
            const network = fetch(req)
                .then(function (res) {
                    if (res.ok && res.type === 'basic') cache.put(req, res.clone());
                    return res;
                })
                .catch(function () { return cached; });
            if (cached) {
                event.waitUntil(network);
                return cached;
            }
            return network;
        });
    });
}
