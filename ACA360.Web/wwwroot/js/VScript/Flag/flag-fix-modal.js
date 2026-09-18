// Load once on the parent page, after Bootstrap 5 and flag-fix.js.
// Replace the previous opener; do not register both.
(function () {
    'use strict';
    let host = null, opening = false, shellRequest = null, requestId = 0;
    window.openFlaggedGrid = async function (employerIds, filingYear, shellUrl) {
        if (opening || host?.classList.contains('show')) return;
        const year = Number(filingYear);
        if (!Number.isInteger(year) || year < 1000 || year > 9999) {
            window.alert('Select a valid filing year.'); return;
        }
        if (!window.bootstrap?.Modal || typeof window.initializeFlaggedGrid !== 'function') {
            window.alert('Load Bootstrap 5 and flag-fix.js before opening this grid.'); return;
        }
        opening = true;
        const sequence = ++requestId;
        shellRequest = new AbortController();
        if (!host) {
            host = document.createElement('div');
            host.id = 'ffGridModal'; host.className = 'modal fade'; host.tabIndex = -1;
            host.setAttribute('aria-label', 'Fix flagged employees');
            host.innerHTML = '<div class="modal-dialog modal-fullscreen"><div class="modal-content"></div></div>';
            document.body.appendChild(host);
            host.addEventListener('hidden.bs.modal', function () {
                requestId++; opening = false;
                shellRequest?.abort(); shellRequest = null;
                window.disposeFlaggedGrid?.();
                host.querySelector('.modal-content').replaceChildren();
            });
        }
        const content = host.querySelector('.modal-content');
        content.innerHTML = '<div class="modal-header"><h5 class="modal-title">Fix Flagged Employees</h5><button class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div><div class="p-4" role="status">Loading flagged employees…</div>';
        // Display immediately, before fetching either the shell or the grid rows.
        bootstrap.Modal.getOrCreateInstance(host).show();
        try {
            const url = new URL(shellUrl || '/Flags/FlaggedGrid', window.location.origin);
            url.searchParams.set('employerIds', Array.isArray(employerIds) ? employerIds.join(',') : (employerIds || ''));
            url.searchParams.set('filingYear', String(year));
            const response = await fetch(url, { credentials: 'same-origin', signal: shellRequest.signal });
            if (!response.ok) throw new Error(`Unable to open grid (HTTP ${response.status}).`);
            const html = await response.text();
            if (sequence !== requestId) return;
            content.innerHTML = html; // Partial contains markup and CSS only. No injected script evaluation.
            const root = content.querySelector('#ffg-root');
            if (!root) throw new Error('Grid markup is missing. Check _FlagFixGrid.cshtml and session/login status.');
            window.initializeFlaggedGrid(root); // Exactly once; it issues the sole initial data request.
        } catch (error) {
            if (error.name === 'AbortError' || sequence !== requestId) return;
            content.innerHTML = '<div class="modal-header"><h5 class="modal-title">Grid Error</h5><button class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button></div><div class="alert alert-danger m-4" role="alert"></div>';
            content.querySelector('[role="alert"]').textContent = error.message;
        } finally {
            if (sequence === requestId) { opening = false; shellRequest = null; }
        }
    };
})();
