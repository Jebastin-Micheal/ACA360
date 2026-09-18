/**
 * Advanced View Switcher with Cookie Persistence
 * @param {Object} config - Configuration object
 */
function ViewManager(config) {
    // 1. Setup Config with Defaults
    const settings = $.extend({
        pageKey: 'default_page',         // Unique name for the cookie (e.g., 'aca_view_plan')
        initialMode: 'Table',            // Default if no cookie exists
        btnTable: '#btnViewTable',
        btnSplit: '#btnViewSplit',
        listCol: '#mainListColumn',
        detailCol: '#mainDetailColumn',
        container: '#listContainer',     // The main div holding the partial
        onReload: function (mode) { }      // Function to call when view changes
    }, config);

    let currentMode = settings.initialMode;

    // 2. Initialize (Set Initial UI State)
    // We don't trigger reload here because the Server should have already rendered the correct HTML
    updateUI(currentMode);

    // 3. Bind Events
    $(settings.btnTable).click(function () {
        if (currentMode !== "Table") switchMode("Table");
    });

    $(settings.btnSplit).click(function () {
        if (currentMode !== "Split") switchMode("Split");
    });

    // 4. Core Switching Logic
    function switchMode(mode) {
        currentMode = mode;

        // Save to Cookie (Valid for 30 days)
        setCookie(settings.pageKey, mode, 30);

        // Update UI Classes
        updateUI(mode);

        // Trigger Data Reload (Callback)
        if (typeof settings.onReload === 'function') {
            settings.onReload(mode);
        }
    }

    function updateUI(mode) {
        if (mode === "Table") {
            $(settings.btnTable).addClass('active btn-primary').removeClass('btn-outline-primary');
            $(settings.btnSplit).removeClass('active btn-primary').addClass('btn-outline-primary');

            $(settings.listCol).removeClass('col-lg-4 col-md-5').addClass('col-12');
            $(settings.detailCol).addClass('d-none').removeClass('col-lg-8 col-md-7');
            $(settings.container).removeClass('split-mode-list');
        } else {
            $(settings.btnSplit).addClass('active btn-primary').removeClass('btn-outline-primary');
            $(settings.btnTable).removeClass('active btn-primary').addClass('btn-outline-primary');

            $(settings.listCol).removeClass('col-12').addClass('col-lg-4 col-md-5');
            $(settings.detailCol).removeClass('d-none').addClass('col-lg-8 col-md-7');
            $(settings.container).addClass('split-mode-list');
        }
    }

    // Cookie Helper
    function setCookie(name, value, days) {
        var expires = "";
        if (days) {
            var date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            expires = "; expires=" + date.toUTCString();
        }
        document.cookie = name + "=" + (value || "") + expires + "; path=/";
    }

    // Public API
    return {
        getMode: function () { return currentMode; }
    };
}