/* ACA360 as an installable app (PWA).
   - Registers the service worker (wwwroot/sw.js), next to the manifest so a deployment under
     a sub-folder still works.
   - Chrome and Edge (PC and Android) fire "beforeinstallprompt"; it is kept and replayed when
     the user clicks an element marked [data-pwa-install], which is shown only then.
   - iPhone / iPad Safari has no install prompt, so elements marked [data-pwa-ios-hint]
     explain Share -> Add to Home Screen instead.
   Nothing is shown when ACA360 is already running as an installed app. */
(function () {
    'use strict';

    var manifest = document.querySelector('link[rel="manifest"]');
    if ('serviceWorker' in navigator && manifest) {
        var swUrl = new URL('sw.js', manifest.href).href;
        window.addEventListener('load', function () {
            navigator.serviceWorker.register(swUrl).catch(function (e) {
                console.warn('[PWA] service worker not registered', e);
            });
        });
    }

    /* ---- App cache version. A SuperAdmin's "Clear app cache on all devices" (System settings >
       Maintenance) raises the version the server writes into every page. A browser that has
       seen an older version drops ACA360's cached files and browser-side caches, refreshes the
       service worker and reloads once. Personal choices (e.g. the light / dark toggle) stay. */
    var VERSION_KEY = 'aca360_cache_version';

    function clearAppCaches() {
        var jobs = [];
        if (window.caches) {
            jobs.push(caches.keys().then(function (keys) {
                return Promise.all(keys.filter(function (k) { return k.indexOf('aca360-') === 0; })
                                       .map(function (k) { return caches.delete(k); }));
            }));
        }
        try {
            Object.keys(localStorage).forEach(function (k) {
                if (k === 'aca360_ui_settings' || k === 'uiSettingsReloadedOnce' || k.indexOf('aca360_layout_') === 0)
                    localStorage.removeItem(k);
            });
        } catch (e) { /* storage blocked */ }
        if ('serviceWorker' in navigator) {
            jobs.push(navigator.serviceWorker.getRegistration()
                .then(function (reg) { return reg && reg.update(); })
                .catch(function () { }));
        }
        return Promise.all(jobs).catch(function () { });
    }
    window.ACA360ClearAppCache = function (newVersion) {
        try { if (newVersion) localStorage.setItem(VERSION_KEY, newVersion); } catch (e) { }
        return clearAppCaches();
    };

    var versionMeta = document.querySelector('meta[name="aca360-cache-version"]');
    var serverVersion = versionMeta ? versionMeta.content : null;
    if (serverVersion) {
        var seenVersion = null;
        try { seenVersion = localStorage.getItem(VERSION_KEY); } catch (e) { }
        try { localStorage.setItem(VERSION_KEY, serverVersion); } catch (e) { }   // stored first: never a reload loop
        if (seenVersion !== null && seenVersion !== serverVersion) {
            clearAppCaches().then(function () { window.location.reload(); });
        }
    }

    var installed = window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    var deferredPrompt = null;

    function show(selector, visible) {
        document.querySelectorAll(selector).forEach(function (el) { el.classList.toggle('d-none', !visible); });
    }

    var isIos = /iphone|ipad|ipod/i.test(navigator.userAgent) ||
                (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

    // Resolves with the user's choice ('accepted' / 'dismissed'), or null when there is nothing to prompt.
    function promptInstall() {
        if (!deferredPrompt) return Promise.resolve(null);
        var p = deferredPrompt;
        deferredPrompt = null;                      // a prompt event can be used only once
        p.prompt();
        return p.userChoice.then(function (choice) {
            show('[data-pwa-install]', false);
            return choice && choice.outcome;
        }).catch(function () { return null; });
    }

    window.addEventListener('beforeinstallprompt', function (e) {
        e.preventDefault();                         // our own button, not the mini info bar
        deferredPrompt = e;
        if (!installed) show('[data-pwa-install]', true);
        offerInstall();
    });

    window.addEventListener('appinstalled', function () {
        deferredPrompt = null;
        show('[data-pwa-install]', false);
    });

    document.addEventListener('click', function (e) {
        var trigger = e.target.closest ? e.target.closest('[data-pwa-install]') : null;
        if (!trigger || !deferredPrompt) return;
        e.preventDefault();
        promptInstall();
    });

    if (isIos && !installed) {
        document.addEventListener('DOMContentLoaded', function () { show('[data-pwa-ios-hint]', true); });
    }

    /* ---- "Install the ACA360 app" popup (Views/Shared/Components/Modals/_InstallAppModal), on the
       first app page after signing in. The sign-in pages carry no popup and reset the "offered in
       this session" flag, so it can come back after the next login. "Not now" or closing it waits
       14 days on this device; "Don't ask again" stops it. Not shown during View As. */
    var SNOOZE_KEY = 'aca360_install_snooze';       // 'never' or the time (ms) it may ask again
    var OFFERED_KEY = 'aca360_install_offered';     // sessionStorage: already offered since sign-in
    var SNOOZE_MS = 14 * 24 * 60 * 60 * 1000;
    var offerTimer = null;

    function store(area, key, value) {
        try { if (value === null) window[area].removeItem(key); else window[area].setItem(key, value); } catch (e) { }
    }
    function read(area, key) {
        try { return window[area].getItem(key); } catch (e) { return null; }
    }
    function snoozed() {
        var v = read('localStorage', SNOOZE_KEY);
        return v === 'never' || (v !== null && Date.now() < Number(v));
    }

    function offerInstall() {
        var modalEl = document.getElementById('installAppModal');
        if (!modalEl) return;
        if (installed || snoozed() || read('sessionStorage', OFFERED_KEY)) return;
        if (!deferredPrompt && !isIos) return;      // this browser cannot install ACA360
        var viewAs = document.querySelector('meta[name="aca360-view-as"]');   // _Layout, every page
        if (viewAs && viewAs.content === '1') return;
        if (!window.bootstrap || !bootstrap.Modal) return;

        clearTimeout(offerTimer);
        offerTimer = setTimeout(function () {       // let the page settle first
            // Another dialog or the guided tour (js/aca360-tour.js) is open: offer it on the next page.
            if (document.querySelector('.modal.show') || document.body.classList.contains('driver-active')) return;
            store('sessionStorage', OFFERED_KEY, '1');
            modalEl.querySelector('[data-install-steps="ios"]').classList.toggle('d-none', !isIos);
            modalEl.querySelector('[data-install-choice="install"]').classList.toggle('d-none', isIos);
            bootstrap.Modal.getOrCreateInstance(modalEl).show();
        }, 1500);
    }

    document.addEventListener('DOMContentLoaded', function () {
        var modalEl = document.getElementById('installAppModal');
        if (!modalEl) {                             // a sign-in page
            store('sessionStorage', OFFERED_KEY, null);
            return;
        }

        var choice = null;
        modalEl.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-install-choice]');
            if (!btn) return;
            choice = btn.getAttribute('data-install-choice');
            if (choice === 'never') store('localStorage', SNOOZE_KEY, 'never');
            bootstrap.Modal.getOrCreateInstance(modalEl).hide();
            if (choice === 'install') {
                promptInstall().then(function (outcome) {
                    // Cancelled in the browser's own dialog: same as "Not now".
                    if (outcome !== 'accepted') store('localStorage', SNOOZE_KEY, String(Date.now() + SNOOZE_MS));
                });
            }
        });
        modalEl.addEventListener('hidden.bs.modal', function () {
            if (choice === null || choice === 'later') store('localStorage', SNOOZE_KEY, String(Date.now() + SNOOZE_MS));
            choice = null;
        });

        offerInstall();                             // iPhone / iPad, or the prompt event came early
    });
})();
