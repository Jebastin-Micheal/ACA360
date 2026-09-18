// --- 1. Page Loader Logic ---
// Show loader when navigating away
window.addEventListener("beforeunload", function () {
    var loader = document.getElementById("page-loader");
    if (loader) {
        loader.style.display = "flex";
        loader.style.opacity = "1";
    }
});

// Hide loader when DOM is ready
window.addEventListener("DOMContentLoaded", () => {
    document.body.classList.add("loaded");
    var loader = document.getElementById("page-loader");
    if (loader) {
        // Fade out using CSS transition
        loader.style.opacity = '0';
        setTimeout(() => {
            loader.style.display = 'none';
        }, 500); // Match CSS transition duration
    }
});

// --- 2. Filing Year Selection Logic ---

document.addEventListener('DOMContentLoaded', async function () {
    const yearDisplay = document.querySelector('.med_fillingyear');
    const yearItems = document.querySelectorAll('.dropdown-item[data-fillingyear]');
    const employerDisplay = document.querySelector('.selectemployer');
    // Ensure getSession exists before calling
    if (typeof getSession === 'function') {
        let selectedFilingYear = await getSession('SelectedFilingYear') || new Date().getFullYear().toString();
        if (yearDisplay) yearDisplay.textContent = selectedFilingYear;
    }
    // Handle Filing Year change
    yearItems.forEach(item => {
        item.addEventListener('click', async function (e) {
            e.preventDefault();
            const year = this.dataset.fillingyear;
            // 1. Instantly clear out navigation UI text layout elements
            if (yearDisplay) yearDisplay.textContent = year;
            if (employerDisplay) {
                employerDisplay.textContent = "No Employer Selected";
                employerDisplay.setAttribute('title', "No Employer Selected");
            }
            // 2. Display the main layout page loader element
            var loader = document.getElementById("page-loader");
            if (loader) {
                loader.style.display = "flex";
                loader.style.opacity = "1";
            }

            // 3. Only send ONE single network request. 
            // The updated C# controller now handles clearing the employer records instantly.
            if (typeof setSession === 'function') {
                await setSession("SelectedFilingYear", year);
            }
            // 4. Safe Redirect: The server is now fully ready, so the side menus will hide perfectly
            if (employerDisplay) {
                window.location.href = '/SelectEmployer/Index';
            } else {
                window.location.reload();
            }
        });
    });
});

// --- 3. Employer Family Switching (Primary employer switching between itself and its affiliates) ---
document.addEventListener('DOMContentLoaded', function () {
    const employerFamilyDisplay = document.querySelector('.med_employerfamily');
    const employerFamilyItems = document.querySelectorAll('.dropdown-item[data-employerid]');

    employerFamilyItems.forEach(item => {
        item.addEventListener('click', async function (e) {
            e.preventDefault();
            const employerId = this.dataset.employerid;
            const employerName = this.dataset.employername;

            if (employerFamilyDisplay) {
                employerFamilyDisplay.textContent = employerName;
                employerFamilyDisplay.setAttribute('title', employerName);
            }
            var loader = document.getElementById("page-loader");
            if (loader) {
                loader.style.display = "flex";
                loader.style.opacity = "1";
            }
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const year = (typeof getSession === 'function')
                ? (await getSession('SelectedFilingYear') || new Date().getFullYear().toString())
                : new Date().getFullYear().toString();

            const formData = new URLSearchParams();
            formData.append('employerId', employerId);
            formData.append('year', year);

            await fetch('/SelectEmployer/SetEmployer', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                    'RequestVerificationToken': token
                },
                body: formData
            });

            window.location.reload();
        });
    });
});


// --- 4. UI Settings Synchronization (Cache-First — No repeated DB calls) ---
document.addEventListener("DOMContentLoaded", function () {
    const UI_CACHE_KEY = 'aca360_ui_settings';
    const UI_CACHE_TTL = 60 * 60 * 1000; // 1 hour in milliseconds

    // ─────────────────────────────────────────────────────────────────────────
    // applyUISettings: applies DB-cached settings to the page.
    //
    // THEME PRIORITY:
    //   1. templateCustomizer-{layoutName}--Theme  (user's live device toggle)
    //   2. data.theme from DB cache                (fallback only)
    //
    // This prevents the cached DB value (e.g. "dark") from overriding a theme
    // the user already changed on this device via the quick Light/Dark toggle.
    // ─────────────────────────────────────────────────────────────────────────
    function applyUISettings(data) {
        if (!data) return;

        // Determine the active layout template name (e.g. "horizontal-menu-template")
        const layoutName = document.documentElement.getAttribute("data-template") || "horizontal-menu-template";

        // Prefer the live local theme toggle over whatever is stored in the DB cache
        const localTheme = localStorage.getItem("templateCustomizer-" + layoutName + "--Theme");
        const themeToApply = localTheme || data.theme;
        if (themeToApply) {
            document.documentElement.setAttribute("data-bs-theme", themeToApply);
        }

        // Apply primary color from DB cache (no local override for this)
        if (data.primaryColor) {
            document.documentElement.style.setProperty('--bs-primary', data.primaryColor);
        }

        // Sync layout keys to the sneat templateCustomizer namespace
        if (data.contentLayout) localStorage.setItem('templateCustomizer-sneat--contentLayout', data.contentLayout);
        if (data.headerType) localStorage.setItem('templateCustomizer-sneat--HeaderType', data.headerType);
        if (data.sidenavHeader) localStorage.setItem('templateCustomizer-sneat--SidenavHeader', data.sidenavHeader);

        // Only trigger a reload if the NavigationLike layout has actually changed
        if (data.navigationLike) {
            localStorage.setItem('templateCustomizer-sneat--NavigationLike', data.navigationLike);
            const currentCookie = getCookie("NavigationLike");
            if (currentCookie !== data.navigationLike) {
                document.cookie = "NavigationLike=" + data.navigationLike + "; path=/; max-age=31536000";
                window.location.reload();
            }
        }
    }

    // Fetches fresh settings from the DB, caches them, and applies them
    function fetchAndCacheUISettings() {
        fetch('/UISettings/Get')
            .then(response => response.json())
            .then(data => {
                if (data) {
                    data.cachedAt = Date.now();
                    localStorage.setItem(UI_CACHE_KEY, JSON.stringify(data));
                    applyUISettings(data);
                }
            })
            .catch(error => console.error('[UISettings] DB fetch error:', error));
    }

    // ── Main Cache-First Logic ────────────────────────────────────────────────
    const raw = localStorage.getItem(UI_CACHE_KEY);

    if (raw) {
        try {
            const cached = JSON.parse(raw);
            const age = Date.now() - (cached.cachedAt || 0);

            if (age < UI_CACHE_TTL) {
                // ✅ Cache is fresh — apply it, NO DB call made
                applyUISettings(cached);
                return; // ← stop here, do NOT fetch
            }

            // Cache is stale — apply instantly (no flicker), then refresh silently in background
            applyUISettings(cached);
            fetchAndCacheUISettings();

        } catch (e) {
            // Corrupt cache — clear it and re-fetch from DB
            localStorage.removeItem(UI_CACHE_KEY);
            fetchAndCacheUISettings();
        }
    } else {
        // No cache at all — first load on this device, fetch from DB
        fetchAndCacheUISettings();
    }
});

// Helper to read cookies
function getCookie(name) {
    const value = `; ${document.cookie}`;
    const parts = value.split(`; ${name}=`);
    if (parts.length === 2) return parts.pop().split(';').shift();
    return null;
}