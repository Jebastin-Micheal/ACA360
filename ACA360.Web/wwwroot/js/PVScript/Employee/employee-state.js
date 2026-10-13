// Location: wwwroot/js/PVScript/Employee/employee-state.js
const EmployeeState = (function () {
    let currentEmployeeId = null;
    let pendingDrafts = {};

    // ─── Navigation list (filter-aware) ───────────────────────────────────────
    // filteredIds : ordered array of employee IDs visible after the last grid load
    // currentIndex: 0-based index of the currently open employee in filteredIds
    let filteredIds = [];
    let currentIndex = -1;

    return {
        // ── Existing API ──────────────────────────────────────────────────────
        setEmployeeId: function (id) {
            if (id) {
                currentEmployeeId = String(id);
            }
        },
        getEmployeeId: function () {
            return currentEmployeeId;
        },
        saveDraft: function (section, data) {
            pendingDrafts[section] = data;
        },
        getDraft: function (section) {
            return pendingDrafts[section] || null;
        },
        clearDrafts: function () {
            pendingDrafts = {};
        },

        // ── Navigation list API ───────────────────────────────────────────────

        /**
         * Replace the tracked filtered list and recompute the current index.
         * Called after every GridManager reload by syncing from DOM row data-ids.
         * @param {Array<string|number>} ids  Ordered list of employee IDs
         */
        setFilteredList: function (ids) {
            filteredIds = (ids || []).map(String);
            // Recompute current index in case the list changed around the open employee
            currentIndex = currentEmployeeId
                ? filteredIds.indexOf(String(currentEmployeeId))
                : -1;
            EmployeeState._fireNavUpdate();
        },

        /**
         * Track which employee is now open and update index accordingly.
         * Always call this when loading a new employee into split/modal.
         */
        setEmployeeIdAndSync: function (id) {
            if (!id) return;
            currentEmployeeId = String(id);
            currentIndex = filteredIds.indexOf(currentEmployeeId);
            EmployeeState._fireNavUpdate();
        },

        getFilteredList: function () { return filteredIds.slice(); },

        getCurrentIndex: function () { return currentIndex; },

        getTotalCount: function () { return filteredIds.length; },

        hasPrev: function () { return currentIndex > 0; },

        hasNext: function () { return currentIndex >= 0 && currentIndex < filteredIds.length - 1; },

        getPrevId: function () {
            return this.hasPrev() ? filteredIds[currentIndex - 1] : null;
        },

        getNextId: function () {
            return this.hasNext() ? filteredIds[currentIndex + 1] : null;
        },

        /**
         * Move to adjacent record.
         * @param {string} direction  'prev' | 'next'
         * @returns {string|null}     The new employee ID, or null if at boundary
         */
        navigate: function (direction) {
            var newIndex = direction === 'prev' ? currentIndex - 1 : currentIndex + 1;
            if (newIndex < 0 || newIndex >= filteredIds.length) return null;
            currentIndex = newIndex;
            currentEmployeeId = filteredIds[currentIndex];
            EmployeeState._fireNavUpdate();
            return currentEmployeeId;
        },

        /**
         * Internal: notify any listener that nav state has changed.
         * Attach a handler via EmployeeState.onNavUpdate = function(state){...}
         */
        _fireNavUpdate: function () {
            if (typeof EmployeeState.onNavUpdate === 'function') {
                EmployeeState.onNavUpdate({
                    currentIndex: currentIndex,
                    total: filteredIds.length,
                    hasPrev: EmployeeState.hasPrev(),
                    hasNext: EmployeeState.hasNext(),
                    currentId: currentEmployeeId
                });
            }
        },

        /** Callback slot – set this to react to any nav-state change. */
        onNavUpdate: null
    };
})();
