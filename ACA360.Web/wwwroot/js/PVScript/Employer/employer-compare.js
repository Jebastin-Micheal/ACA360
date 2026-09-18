/* =====================================================================
   Employer detail toolbar: record Prev/Next navigation + keyboard
   shortcuts + filing-year comparison (Phase 1: Employer detail).
   ---------------------------------------------------------------------
   - Toolbar lives ABOVE the detail pane (#employerDetailToolbar) and
     persists across detail reloads.
   - Prev/Next (and Up/Down keys) walk the currently rendered employer
     list rows (.employer-link-am) by re-using their click handler.
   - Picking a filing year different from the active one opens a
     read-only compare card to the RIGHT of the working record and
     highlights every field whose value differs. The employer list
     stays visible.
   - Reuses /Employer/GetCommonDetails (already year-aware). The compare
     pane is strictly read-only; only the active year is editable.
   ===================================================================== */
(function () {
    "use strict";

    var CONTAINER = "#commonDetailContainer";
    var TOOLBAR = "#employerDetailToolbar";
    var LIST = "#employerListContainer";
    var DETAILS_URL = "/Employer/GetCommonDetails";
    var COMPARE_URL = "/Employer/GetCompareDetails"; // resolves compare year by EIN
    var YEARS_URL = "/Employer/GetFilingYears";
    var IDS_URL = "/Employer/GetFilteredEmployerIds";

    var state = {
        employerId: null,
        activeYear: null,     // string
        compareYear: null,    // string or null when off
        yearsCache: null,     // [numbers], newest first
        yearsFilledFor: null, // activeYear the <select> was built for
        navIds: [],           // whole filtered list, ordered (all pages)
        navFilingYear: null,  // filing year for loading off-page records
        pendingSelectId: null // row to highlight after a grid page change
    };

    function $c() { return $(CONTAINER); }
    function $t() { return $(TOOLBAR); }

    // ---- comparable fields / diff helpers ---------------------------

    function comparableFields(root) {
        return $(root)
            .find("input, select, textarea")
            .filter(function () {
                var t = (this.type || "").toLowerCase();
                if (t === "hidden" || t === "button" || t === "submit" || t === "reset") return false;
                // Skip Filing Year — it's the comparison dimension itself, always
                // different, so flagging it as "changed" is just noise.
                if ((this.id || "").replace(/^cmp__/, "") === "FilingYear") return false;
                return true;
            })
            .toArray();
    }

    function fieldValue(el) {
        var t = (el.type || "").toLowerCase();
        var v;
        if (t === "checkbox") v = el.checked ? "1" : "0";
        else if (t === "radio") v = el.checked ? (el.value || "on") : "";
        else if (el.tagName === "SELECT") {
            // Compare dropdowns by their VISIBLE label, not the option value.
            // Option values are per-year database ids (e.g. the same firm /
            // affiliate has a different id each filing year), so comparing by
            // value flags identical selections as "changed". The label is what
            // the user sees and what "same value" means to them.
            var opt = el.options[el.selectedIndex];
            v = opt ? (opt.text || opt.value) : el.value;
        }
        else v = el.value;
        return (v == null ? "" : String(v)).trim();
    }

    function fieldCell(el) {
        // Table inputs (e.g. the 1094 ALE matrix): highlight the individual <td>,
        // not the big column that wraps the whole table.
        var td = el.closest('td');
        if (td) return td;
        // Otherwise the full field cell (column / group) so hiding "same" fields
        // removes the label with the value, not just the orphaned input.
        var cell = el.closest('[class*="col-"], .mb-3, .form-group, .form-check');
        return cell || el.closest('.input-group') || el.parentElement || el;
    }

    // Prefix every id (and its references) so a 2nd copy of the same partial
    // does not collide with the original (tabs, labels, etc.). We PREFIX
    // rather than suffix so id-suffix selectors still match — the tab loader
    // finds content with div[id$="-content"], which a suffix would break.
    function namespaceIds(rootEl, prefix) {
        var idMap = {};
        rootEl.querySelectorAll("[id]").forEach(function (el) {
            if (!el.id) return;
            idMap[el.id] = prefix + el.id;
            el.id = prefix + el.id;
        });
        function remap(attr, hashPrefixed) {
            rootEl.querySelectorAll("[" + attr + "]").forEach(function (el) {
                var val = el.getAttribute(attr);
                if (!val) return;
                var raw = hashPrefixed ? val.replace(/^#/, "") : val;
                if (idMap[raw]) el.setAttribute(attr, (hashPrefixed ? "#" : "") + idMap[raw]);
            });
        }
        remap("for", false);
        remap("href", true);
        remap("data-bs-target", true);
        remap("data-bs-parent", true);
        remap("aria-controls", false);
        remap("aria-labelledby", false);
    }

    // ---- year picker ------------------------------------------------

    function loadYears() {
        if (state.yearsCache) return $.Deferred().resolve(state.yearsCache).promise();
        return $.get(YEARS_URL).then(function (resp) {
            state.yearsCache = (resp && resp.years) ? resp.years : [];
            if (resp && resp.current) state.yearsCurrent = String(resp.current);
            return state.yearsCache;
        });
    }

    // In table view there's no open record, so populate the compare picker with the
    // current filing year's available years — picking one switches to split + compares.
    function fillTablePicker() {
        loadYears().done(function (years) {
            var $sel = $t().find(".cmp-year-select");
            var cur = state.activeYear || state.yearsCurrent || "";
            $sel.empty().append('<option value="">Compare filing year…</option>');
            years.forEach(function (y) { if (String(y) !== String(cur)) $sel.append('<option value="' + y + '">' + y + '</option>'); });
            state.yearsFilledFor = null;   // force a rebuild when a split record later loads
        });
    }

    function fillYearSelect() {
        if (state.yearsFilledFor === state.activeYear) return;
        loadYears().done(function (years) {
            var $sel = $t().find(".cmp-year-select");
            var active = String(state.activeYear);
            $sel.empty();
            $sel.append('<option value="">' + active + ' (current)</option>');
            years.forEach(function (y) {
                if (String(y) === active) return;
                $sel.append('<option value="' + y + '">' + y + '</option>');
            });
            $sel.val(state.compareYear ? String(state.compareYear) : "");
            state.yearsFilledFor = state.activeYear;
        });
    }

    // ---- record navigation (whole filtered list, across pages) ------

    // Rebuild the ordered id list for the current filter/sort using the
    // same params the grid sends, so nav matches exactly what's listed.
    function currentListParams() {
        var st = (window.GridManager && GridManager.getState) ? GridManager.getState() : {};
        var fields = [];
        $(".emp-search-opt:checked").not("#optAll").each(function () { fields.push($(this).val()); });
        return {
            Search: ($("#mainGridSearch").val() || st.search || ""),
            SortColumn: (st.sortColumn != null ? st.sortColumn : 0),
            SortOrder: (st.sortOrder || "asc"),
            TypeFilter: ($("#flt_type").val() || ""),
            SearchFields: (fields.length ? fields.join(",") : "name")
        };
    }

    function refreshNavList() {
        return $.get(IDS_URL, currentListParams()).done(function (resp) {
            state.navIds = (resp && resp.ids) ? resp.ids.map(String) : [];
            state.navFilingYear = (resp && resp.filingYear) ? String(resp.filingYear) : state.navFilingYear;
            updateNavCounter();
        });
    }

    function currentNavIndex() {
        if (!state.employerId) return -1;
        return state.navIds.indexOf(String(state.employerId));
    }

    function updateNavCounter() {
        var n = state.navIds.length;
        var idx = currentNavIndex();
        var $counter = $("#empNavCounter");
        $counter.text((idx >= 0 && n) ? ((idx + 1) + " of " + n) : "");
        $("#empNavPrev").prop("disabled", idx <= 0);
        $("#empNavNext").prop("disabled", idx < 0 || idx >= n - 1);
    }

    // Load the detail pane for a record id (works for any page).
    function loadDetailDirect(id) {
        var year = state.activeYear || state.navFilingYear;
        $c().html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');
        $.get(DETAILS_URL, { EmployerId: id, filingYear: year })
            .done(function (html) {
                $c().html(html);
                onDetailLoaded(id, year);
            })
            .fail(function () {
                $c().html('<div class="alert alert-danger m-3">Failed to load record.</div>');
            });
    }

    function highlightRow(id) {
        $(LIST + " .employer-active").removeClass("table-active active-row");
        var $link = $(LIST + " .employer-link-am").filter(function () {
            return String($(this).data("employer-id")) === String(id);
        }).first();
        var $row = $link.closest(".employer-active");
        if ($row.length) {
            $row.addClass("table-active active-row");
            $row[0].scrollIntoView({ block: "nearest" });
        }
    }

    // Make sure the record's row is on the visible page, then highlight it.
    // Pages the grid when the record lives on another page.
    function revealRowInList(id) {
        var idx = state.navIds.indexOf(String(id));
        if (idx < 0) { highlightRow(id); return; }

        var st = (window.GridManager && GridManager.getState) ? GridManager.getState() : {};
        var pageSize = parseInt(st.pageSize, 10) || 10;
        var currentPage = parseInt(st.pageIndex, 10) || 1;
        var targetPage = Math.floor(idx / pageSize) + 1;

        if (targetPage === currentPage || !(window.GridManager && GridManager.setPageIndex)) {
            highlightRow(id);
        } else {
            // Highlight happens after the new page renders (ajaxComplete).
            state.pendingSelectId = String(id);
            GridManager.setPageIndex(targetPage);
        }
    }

    // Load a record by id: update the detail pane and reveal/highlight its
    // row in the list, auto-paging across pages when needed.
    function loadRecord(id) {
        loadDetailDirect(id);
        revealRowInList(id);
    }

    function goToNav(index) {
        if (index < 0 || index >= state.navIds.length) return;
        loadRecord(state.navIds[index]);
    }

    function goPrev() { goToNav(currentNavIndex() - 1); }
    function goNext() { goToNav(currentNavIndex() + 1); }

    // ---- diff -------------------------------------------------------

    function clearHighlights() {
        $c().find(".cmp-changed-cell").removeClass("cmp-changed-cell");
        $c().find(".cmp-changed-field").removeClass("cmp-changed-field");
    }

    function runDiff($left, $right) {
        var left = comparableFields($left);
        var right = comparableFields($right);
        var n = Math.min(left.length, right.length);
        var changed = 0;
        for (var i = 0; i < n; i++) {
            // The 1094 ALE matrix (vis_*/mx_*) is diffed separately by highlightAle()
            // so the K-System "All 12 Months" rollup is respected — a year stored as an
            // "All" row must compare equal to the same data stored per-month. Skip it here.
            if (matrixLabel(left[i])) continue;
            if (fieldValue(left[i]) === fieldValue(right[i])) continue;
            changed++;
            // Highlight BOTH the current-year (editable) and the compare-year
            // (read-only) side so the difference is obvious on either pane.
            left[i].classList.add("cmp-changed-field");
            fieldCell(left[i]).classList.add("cmp-changed-cell");
            right[i].classList.add("cmp-changed-field");
            fieldCell(right[i]).classList.add("cmp-changed-cell");
        }
        return { changed: changed };
    }

    function showSummary(result, missing) {
        var count = result ? result.changed : 0;
        var $s = $(".cmp-summary");   // lives in the command bar's compare controls
        $s.removeClass("cmp-has-changes cmp-no-changes");
        if (missing) {
            $s.addClass("cmp-no-changes").text("No " + state.compareYear + " record");
        } else if (count === 0) {
            $s.addClass("cmp-no-changes").text("In sync");
        } else {
            $s.addClass("cmp-has-changes").text(count + (count === 1 ? " difference" : " differences"));
        }
        setDiffControls(count, missing);
    }

    // Keep the "only differences" toggle available for the whole comparison and
    // rebuild the list to match the CURRENT record. This runs repeatedly as tabs
    // load, so it must never force-uncheck on a transient (mid-load) count — doing
    // that made the list go stale / look the same across records.
    function setDiffControls(count, missing) {
        if (missing) {
            $(".cmp-onlydiff").addClass("d-none");
            $("#cmpOnlyDiff").prop("checked", false);
            $c().find(".cmp-difflist").remove();
            return;
        }
        $(".cmp-onlydiff").toggleClass("d-none", !state.compareYear);
        reapplyOnlyDiff();   // if checked, rebuild the list from this record's diffs
    }

    // De-duplicated difference count — the same logic the differences list uses,
    // so the toolbar pill and the list agree: every non-matrix changed field counts
    // once, and the ALE matrix contributes its unique changed cells (not the hidden
    // matrix-modal duplicates).
    function computeDiffCount($shell) {
        var left = comparableFields($shell.find(".cmp-left"));
        var n = 0;
        for (var i = 0; i < left.length; i++) {
            if (!left[i].classList.contains("cmp-changed-field")) continue;
            if (matrixLabel(left[i])) continue;   // ALE cells counted below
            n++;
        }
        var ale = buildAleMatrix($shell);
        if (ale) n += aleDiffCount(ale);
        n += serviceDiffRows($shell).length;   // added / removed / status-changed services
        n += roDiffPairs($shell.find(".cmp-left"), $shell.find(".cmp-right")).length;  // read-only display fields
        return n;
    }
    function updateSummaryCount() {
        var $shell = $c().find(".cmp-shell");
        showSummary({ changed: $shell.length ? computeDiffCount($shell) : 0 }, false);
    }

    // Diff-marker scroll rail removed per feedback; these no-op stubs keep the
    // (now inert) call sites harmless.
    function scheduleRail() {}
    function hideRail() {}

    // ---- jump between changed fields (Up/Down) ----------------------

    var jumpIdx = -1;

    function flashEl(el) {
        if (!el) return;
        el.classList.remove("cmp-jump-flash");
        void el.offsetWidth;               // restart the animation
        el.classList.add("cmp-jump-flash");
    }

    // Currently-visible changed fields on the current-year (left) side.
    function visibleChangedFields() {
        return $c().find(".cmp-left .cmp-changed-field").filter(":visible").toArray();
    }

    // The compare-year (right) field paired with a left field, by position.
    function rightTwinOf(leftEl) {
        var lf = comparableFields($c().find(".cmp-left"));
        var idx = lf.indexOf(leftEl);
        if (idx < 0) return null;
        return comparableFields($c().find(".cmp-right"))[idx] || null;
    }

    // Move to the next/previous changed field; scroll both panes to it.
    function jumpChange(dir) {
        if (!state.compareYear) return;                 // only meaningful in compare mode
        var fields = visibleChangedFields();
        if (!fields.length) return;
        jumpIdx = (jumpIdx + dir + fields.length) % fields.length;
        var lEl = fields[jumpIdx];
        var lCell = fieldCell(lEl);
        lCell.scrollIntoView({ block: "center", behavior: "smooth" });
        flashEl(lCell);
        var rEl = rightTwinOf(lEl);
        if (rEl) flashEl(fieldCell(rEl));
    }

    // ---- enter / exit compare mode ----------------------------------

    // reopening=true tears down the current compare UI but stays in compare mode
    // (used when switching year); reopening=false fully returns to the plain detail.
    function exitCompare(reopening) {
        state.compareYear = null;
        jumpIdx = -1;
        clearHighlights();
        $t().find(".cmp-summary").removeClass("cmp-has-changes cmp-no-changes").text("");
        $c().find(".cmp-difflist").remove();
        $("#cmpOnlyDiff").prop("checked", false);

        var $shell = $c().find(".cmp-shell");
        if ($shell.length) {
            // restore the original detail nodes back into the container
            var $left = $shell.find(".cmp-left .cmp-pane-body").children();
            $shell.before($left);
            $shell.remove();
            // bring back the Audit Logs + Notes cards we hid for the comparison
            $c().find(".cmp-hidden-section").removeClass("cmp-hidden-section");
        }

        hideRail();
        if (!reopening) {
            $("#mainListColumn").closest(".row").removeClass("cmp-mode cmp-list-collapsed");
            resetListToggleIcon();
            setCompareChrome(false);   // restore view-toggle + Add button in the command bar
        }
    }

    // Draggable divider between the two compare panes.
    function attachResizer($shell) {
        var resizer = $shell.find(".cmp-resizer")[0];
        var left = $shell.find(".cmp-left")[0];
        if (!resizer || !left) return;
        var dragging = false;
        resizer.addEventListener("pointerdown", function (e) {
            dragging = true;
            resizer.classList.add("dragging");
            document.body.classList.add("cmp-resizing");
            try { resizer.setPointerCapture(e.pointerId); } catch (x) {}
        });
        resizer.addEventListener("pointermove", function (e) {
            if (!dragging) return;
            var rect = $shell[0].getBoundingClientRect();
            var pct = ((e.clientX - rect.left) / rect.width) * 100;
            pct = Math.max(25, Math.min(75, pct));
            left.style.flex = "0 0 " + pct + "%";
        });
        function end() {
            dragging = false;
            resizer.classList.remove("dragging");
            document.body.classList.remove("cmp-resizing");
        }
        resizer.addEventListener("pointerup", end);
        resizer.addEventListener("pointercancel", end);
        resizer.addEventListener("dblclick", function () { left.style.flex = "1 1 0"; });
    }

    function resetListToggleIcon() {
        var $btn = $("#listToggleBtn");
        $btn.find("i").attr("class", "bx bx-chevrons-left");
        $btn.attr("title", "Hide the employer list");
    }

    // Audit Logs + Notes are not field-comparable. In the live current-year pane we
    // HIDE their cards (so they return intact on exit); in the disposable clone we
    // remove them outright.
    function hideNonComparable($root) {
        $root.find(".notes-widget").closest(".card").addClass("cmp-hidden-section");
        $root.find('[id$="Employerauditlog"]').closest(".card").addClass("cmp-hidden-section");
        $root.find('[id$="auditLogContainer_Employer"]').closest(".card").addClass("cmp-hidden-section");
    }
    function removeNonComparable(rootEl) {
        var $r = $(rootEl);
        $r.find(".notes-widget").closest(".card").remove();
        $r.find(".notes-widget").remove();
        $r.find('[id$="Employerauditlog"]').closest(".card").remove();
        $r.find('[id$="auditLogContainer_Employer"]').closest(".card").remove();
    }

    function enterCompare(year) {
        state.compareYear = String(year);
        jumpIdx = -1;
        setCompareChrome(true);   // swap the command-bar right side to compare controls

        $c().find(".cmp-difflist").remove();          // never wrap a stale diff list
        var $detailNodes = $c().children().not(".cmp-shell, .cmp-difflist");

        var $shell = $(
            '<div class="cmp-shell cmp-active">' +
              '<div class="cmp-pane cmp-left">' +
                '<div class="cmp-pane-header"><span>' + state.activeYear + ' (current)</span></div>' +
                '<div class="cmp-pane-body"></div>' +
              '</div>' +
              '<div class="cmp-resizer" role="separator" aria-orientation="vertical" ' +
                   'aria-label="Resize panes" title="Drag to resize · double-click to reset"></div>' +
              '<div class="cmp-pane cmp-right">' +
                '<div class="cmp-pane-header"><span>' + state.compareYear + '</span>' +
                  '<span class="cmp-head-right">' +
                    '<span class="cmp-readonly-tag"><i class="bx bx-lock-alt"></i> read-only</span>' +
                    '<button type="button" class="cmp-close" title="Back to current year — close comparison">' +
                      '<i class="bx bx-x"></i></button>' +
                  '</span></div>' +
                '<div class="cmp-pane-body">' +
                  '<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>' +
                '</div>' +
              '</div>' +
            '</div>'
        );
        $c().append($shell);
        $shell.find(".cmp-left .cmp-pane-body").append($detailNodes);
        // Audit Logs and Notes aren't field-comparable — hide (not remove) them in
        // the current-year pane so they come back intact when compare is closed.
        hideNonComparable($shell.find(".cmp-left"));
        $("#mainListColumn").closest(".row").addClass("cmp-mode");
        attachResizer($shell);

        var $rightBody = $shell.find(".cmp-right .cmp-pane-body");

        $.get(COMPARE_URL, { EmployerId: state.employerId, filingYear: state.compareYear })
            .done(function (html) {
                var tmp = document.createElement("div");
                tmp.innerHTML = html;
                tmp.querySelectorAll("script").forEach(function (s) { s.remove(); });
                // Drop Audit Logs + Notes from the (disposable) compare clone.
                removeNonComparable(tmp);

                var idEl = tmp.querySelector("#hdnEmployerId");
                var hasRecord = idEl && (idEl.value || "") !== "";
                if (!hasRecord) {
                    $rightBody.html(
                        '<div class="cmp-empty"><i class="bx bx-folder-open bx-lg d-block mb-2 mx-auto"></i>' +
                        'No employer record for ' + state.compareYear + '.</div>'
                    );
                    showSummary(null, true);
                    return;
                }

                namespaceIds(tmp, "cmp__");
                $rightBody.empty();
                while (tmp.firstChild) $rightBody[0].appendChild(tmp.firstChild);

                var $right = $shell.find(".cmp-right");
                $right.find("input, select, textarea").prop("disabled", true);
                // Hide every action button (Save, Save & Continue, Edit, …) — keep
                // only the tab links and the compare-close control.
                $right.find("button, .btn").not(".nav-link, [data-bs-toggle='tab'], .cmp-close").hide();
                $right.find("fieldset").prop("disabled", true);
                $right.find(".nav-link, .nav-item").prop("disabled", false).removeClass("disabled");

                // Diff only the always-symmetric Employer Profile section here;
                // tabs are diffed per-tab as they load (both panes in sync).
                var $lProfile = $shell.find('.cmp-left [id$="employerFieldset"]');
                var $rProfile = $right.find('[id$="employerFieldset"]');
                runDiff($lProfile.length ? $lProfile : $shell.find(".cmp-left"),
                        $rProfile.length ? $rProfile : $right);
                updateSummaryCount();

                // Eager-load every sub-tab in both panes and diff them, so the
                // summary shows a true grand total and highlights are ready
                // before you open a tab.
                loadAllTabsAndDiff($shell);

                // Always open the comparison on the FIRST sub-tab, regardless of which
                // tab was active before compare (the sync handler aligns the other pane).
                var $firstTab = $shell.find(".cmp-left .nav-link[data-bs-toggle='tab']").first();
                if ($firstTab.length) $firstTab.trigger("click");
            })
            .fail(function () {
                $rightBody.html(
                    '<div class="cmp-empty text-danger">Failed to load ' + state.compareYear + ' record.</div>'
                );
            });
    }

    // ---- public entry point (called after a detail loads) -----------

    function onDetailLoaded(employerId, activeYear) {
        // Preserve an active comparison so Prev/Next keep comparing the same
        // filing year as you move from one employer to the next.
        var keepCompareYear = state.compareYear;

        state.employerId = employerId != null ? String(employerId) : null;
        state.activeYear = activeYear != null ? String(activeYear) : null;
        state.compareYear = null;

        if (!state.employerId) { $t().addClass("d-none"); return; }

        $t().removeClass("d-none cmp-table-mode");   // split view: full toolbar
        fillYearSelect();
        highlightRow(state.employerId); // one consistent selection style everywhere
        updateNavCounter();

        // Comparison requested from table view: now that split has loaded its first
        // record, open the comparison for the year that was picked in the table.
        if (pendingCompareYear) {
            var pend = pendingCompareYear; pendingCompareYear = null;
            if (String(pend) !== String(state.activeYear)) {
                $t().find(".cmp-year-select").val(String(pend));
                enterCompare(pend);
                return;
            }
        }

        if (keepCompareYear && String(keepCompareYear) !== String(state.activeYear)) {
            // Re-open the comparison for the newly loaded record.
            $t().find(".cmp-year-select").val(String(keepCompareYear));
            enterCompare(keepCompareYear);
        } else {
            $t().find(".cmp-year-select").val("");
            $t().find(".cmp-summary").text("");
            setCompareChrome(false);
        }
    }

    // ---- event wiring (registered once) -----------------------------

    // Is the split-view list showing (vs the flat table view)?
    function viewIsSplit() { return $(LIST + " .employer-link-am").length > 0; }
    var pendingCompareYear = null;

    $(document).on("change", TOOLBAR + " .cmp-year-select", function () {
        var val = $(this).val();
        // From TABLE view a comparison can't render side-by-side — switch to Split
        // view first, then compare the chosen year once its first record loads.
        if (!viewIsSplit()) {
            if (val) {
                pendingCompareYear = val;
                $('.grid-view-btn[data-mode="Split"]').trigger("click");
            }
            return;
        }
        var goCompare = val && String(val) !== String(state.activeYear);
        exitCompare(goCompare); // reopening=true keeps compare mode when switching year
        if (goCompare) enterCompare(val);
    });

    // ---- "Show only differences": one flat list of every changed field --------
    function cmpEsc(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }
    function fieldDisplayValue(el) {
        var t = (el.type || "").toLowerCase();
        if (t === "checkbox") return el.checked ? "Yes" : "No";
        if (el.tagName === "SELECT") { var o = el.options[el.selectedIndex]; return o ? (o.text || o.value).trim() : ""; }
        var v = (el.value || "").trim();
        return v;
    }
    // The 1094-C ALE table cells are inputs with no labels — build "Metric — Month".
    var CMP_MONTHS = ["All 12 Months", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                      "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    var CMP_METRICS = { minCov: "Minimum Coverage", ft: "Full-Time", tot: "Total", agg: "Aggregate Group", sec: "Sec 4980H" };
    // Matches BOTH the visible ALE table inputs (vis_*) and the hidden matrix-modal
    // duplicates (mx_*) — both are folded into the ALE table, never listed one-by-one.
    function matrixLabel(el) {
        var m = (el.id || "").match(/^(?:cmp__)?(?:vis|mx)_(minCov|ft|tot|agg|sec)_(\d+)$/);
        if (!m) return null;
        return CMP_METRICS[m[1]] + " — " + (CMP_MONTHS[parseInt(m[2], 10)] || ("M" + m[2]));
    }
    function fieldLabelText(el) {
        // 0. 1094-C ALE matrix cell -> "Full-Time — Feb", etc.
        var ml = matrixLabel(el);
        if (ml) return ml;
        // 1. explicit label[for=id]
        if (el.id) {
            var $lf = $('label[for="' + el.id.replace(/"/g, "") + '"]').first();
            var tf = ($lf.text() || "").trim();
            if (tf) return tf;
        }
        // 2. nearest label OR section heading that PRECEDES this field in document
        //    order (some fields use an <h6> heading instead of a <label>, e.g. Broker;
        //    fields share coarse columns, so the closest preceding one is the right one).
        var scope = el.closest(".tab-pane, form, .cmp-left, .cmp-pane") || document;
        var labels = scope.querySelectorAll("label, .form-label, h4, h5, h6");
        var best = null;
        for (var i = 0; i < labels.length; i++) {
            if (labels[i].compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) best = labels[i];
            else break; // labels beyond the field
        }
        if (best) { var tb = (best.textContent || "").trim(); if (tb) return tb; }
        return el.getAttribute("placeholder") || el.name || "Field";
    }
    // Which section/tab the field belongs to — match the field's tab-pane to the
    // nav button that controls it (by id), never by DOM position (panes and buttons
    // aren't in the same order, and nested panes throw indexes off).
    function fieldSectionName(el, $pane) {
        var $tp = $(el).closest(".tab-pane");
        if (!$tp.length) return "Employer Profile";
        var pid = $tp.attr("id");
        if (pid) {
            var $btn = $pane.find('.nav-link[data-bs-toggle="tab"]').filter(function () {
                var tgt = ($(this).attr("data-bs-target") || $(this).attr("href") || "").replace(/^#/, "");
                return tgt === pid || $(this).attr("aria-controls") === pid;
            }).first();
            var tt = ($btn.text() || "").trim();
            if (tt) return tt;
        }
        return "Details";
    }
    function buildDiffData($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var left = comparableFields($left), right = comparableFields($right);
        var rows = [];
        for (var i = 0; i < left.length; i++) {
            if (!left[i].classList.contains("cmp-changed-field")) continue;
            if (matrixLabel(left[i])) continue;   // ALE matrix shown as its own table below
            rows.push({
                section: fieldSectionName(left[i], $left),
                label: fieldLabelText(left[i]),
                cur: fieldDisplayValue(left[i]),
                cmp: right[i] ? fieldDisplayValue(right[i]) : ""
            });
        }
        // List-level service differences (added / removed / status) — these aren't
        // form fields, so add them under the Services section explicitly.
        var svc = servicesTabInfo($shell);
        serviceDiffRows($shell).forEach(function (r) {
            rows.push({ section: svc.name, label: r.label, cur: r.cur, cmp: r.cmp });
        });
        // Read-only display-field differences (matched by their data-cmp-field label).
        roDiffPairs($left, $right).forEach(function (p) {
            rows.push({ section: fieldSectionName(p.lEl, $left), label: p.label, cur: p.cur, cmp: p.cmp });
        });
        return { rows: rows, ale: buildAleMatrix($shell) };
    }

    // The 1094-C ALE table for both years, showing only the months that changed.
    function aleCell(el, metric) {
        if (!el) return "";
        if (metric === "minCov" || metric === "agg") return el.checked ? "✓" : "—";
        var v = (el.value || "").trim();
        return v === "" ? "0" : v;
    }
    var ALE_METRICS = [["minCov", "Min Cov"], ["ft", "Full-Time"], ["tot", "Total"], ["agg", "Aggregate"], ["sec", "Sec 4980H"]];
    function aleCellEl($pane, metric, idx) { return $pane.find('[id$="vis_' + metric + '_' + idx + '"]')[0]; }
    // K-System "All 12 Months" rollup is PER-METRIC: each 1094-C column stores its data
    // either on the "All" row (index 0, months blank) or per-month, independently of the
    // others. Detect it per metric so the same data stored either way compares equal, and
    // so a mixed record (e.g. Min Coverage on "All", Full-Time per-month) reads correctly.
    function aleMetricAllMode($pane, metric) {
        var isChk = (metric === "minCov" || metric === "agg");
        function used(idx) {
            var el = aleCellEl($pane, metric, idx);
            if (!el) return false;
            if (isChk) return !!el.checked;
            var v = (el.value || "").trim();
            return v !== "" && (parseInt(v, 10) || 0) !== 0;
        }
        for (var j = 1; j <= 12; j++) if (used(j)) return false;
        return used(0);
    }
    // Per-side, per-metric "All" mode map, computed once.
    function aleModeMap($pane) {
        var m = {};
        ALE_METRICS.forEach(function (x) { m[x[0]] = aleMetricAllMode($pane, x[0]); });
        return m;
    }
    // Build the ALE diff table on NORMALIZED per-metric values so the same data stored as an
    // "All 12 Months" rollup or per-month compares equal.
    function buildAleMatrix($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        if (!aleCellEl($left, "ft", 0) && !aleCellEl($right, "ft", 0)) return null; // no 1094 tab loaded
        var lMode = aleModeMap($left), rMode = aleModeMap($right);
        var rows = [], any = false;
        for (var i = 1; i <= 12; i++) {
            var cells = [], rowDiff = false;
            for (var k = 0; k < ALE_METRICS.length; k++) {
                var mk = ALE_METRICS[k][0];
                var cv = aleCell(aleCellEl($left, mk, lMode[mk] ? 0 : i), mk);
                var pv = aleCell(aleCellEl($right, mk, rMode[mk] ? 0 : i), mk);
                var d = cv !== pv;
                if (d) { rowDiff = true; any = true; }
                cells.push({ cur: cv, cmp: pv, diff: d });
            }
            if (rowDiff) rows.push({ month: CMP_MONTHS[i], cells: cells });
        }
        return any ? { headers: ALE_METRICS.map(function (m) { return m[1]; }), rows: rows } : null;
    }
    // Highlight the differing ALE cells on BOTH panes (rollup-aware), and return the
    // count. When a side has a metric in "All" mode the diff shows on its All-row cell.
    function highlightAle($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        if (!aleCellEl($left, "ft", 0) && !aleCellEl($right, "ft", 0)) return 0;
        var lMode = aleModeMap($left), rMode = aleModeMap($right);
        var changed = 0;
        for (var i = 1; i <= 12; i++) {
            for (var k = 0; k < ALE_METRICS.length; k++) {
                var mk = ALE_METRICS[k][0];
                var lEl = aleCellEl($left, mk, lMode[mk] ? 0 : i), rEl = aleCellEl($right, mk, rMode[mk] ? 0 : i);
                if (!lEl || !rEl) continue;
                if (aleCell(lEl, mk) === aleCell(rEl, mk)) continue;
                changed++;
                lEl.classList.add("cmp-changed-field"); fieldCell(lEl).classList.add("cmp-changed-cell");
                rEl.classList.add("cmp-changed-field"); fieldCell(rEl).classList.add("cmp-changed-cell");
            }
        }
        return changed;
    }
    function sectionIcon(name) {
        var m = {
            "Employer Profile": "bx-building-house", "ER Info": "bx-id-card",
            "1094 Details": "bx-file", "Service Info": "bx-cog", "Other Info": "bx-info-circle"
        };
        return m[name] || "bx-detail";
    }
    function aleDiffCount(ale) {
        var n = 0;
        ale.rows.forEach(function (r) { r.cells.forEach(function (c) { if (c.diff) n++; }); });
        return n;
    }
    function renderAleMatrix(ale) {
        function tbl(side) {
            var h = '<table class="cmp-ale-tbl"><thead><tr><th>Month</th>';
            ale.headers.forEach(function (m) { h += '<th>' + cmpEsc(m) + '</th>'; });
            h += '</tr></thead><tbody>';
            ale.rows.forEach(function (r) {
                h += '<tr><td class="cmp-ale-mo">' + cmpEsc(r.month) + '</td>';
                r.cells.forEach(function (c) {
                    h += '<td class="' + (c.diff ? 'cmp-ale-diff' : '') + '">' + cmpEsc(side === "cur" ? c.cur : c.cmp) + '</td>';
                });
                h += '</tr>';
            });
            return h + '</tbody></table>';
        }
        return '<div class="cmp-ale-block">' +
            '<div class="cmp-ale-title"><i class="bx bx-table"></i> ALE by month</div>' +
            '<div class="cmp-ale-wrap">' +
              '<div class="cmp-ale-pane"><div class="cmp-ale-head cmp-ale-cur">' + cmpEsc(state.activeYear) + ' · current</div>' + tbl("cur") + '</div>' +
              '<div class="cmp-ale-pane"><div class="cmp-ale-head">' + cmpEsc(state.compareYear) + '</div>' + tbl("cmp") + '</div>' +
            '</div></div>';
    }
    function valChip(v, cls) {
        return v ? '<span class="cmp-chip ' + cls + '">' + cmpEsc(v) + '</span>'
                 : '<span class="cmp-chip cmp-chip-empty">empty</span>';
    }
    function renderDiffList(data) {
        var cur = cmpEsc(state.activeYear), cmp = cmpEsc(state.compareYear);
        if (!data.rows.length && !data.ale) {
            return $('<div class="cmp-difflist"><div class="cmp-diff-empty">' +
                '<i class="bx bx-check-circle"></i><div><strong>In sync</strong><span>No differences between ' + cur + ' and ' + cmp + '.</span></div></div></div>');
        }

        // group rows by section, preserving first-seen order
        var order = [], groups = {};
        data.rows.forEach(function (r) {
            if (!groups[r.section]) { groups[r.section] = []; order.push(r.section); }
            groups[r.section].push(r);
        });
        var ONEK = "1094 Details";
        if (data.ale && order.indexOf(ONEK) < 0) { order.push(ONEK); groups[ONEK] = []; }

        var total = data.rows.length + (data.ale ? aleDiffCount(data.ale) : 0);
        var h = '<div class="cmp-difflist">' +
            '<div class="cmp-diff-summary">' +
              '<span class="cmp-diff-count"><span class="cmp-diff-dot"></span>' + total + ' difference' + (total === 1 ? '' : 's') + '</span>' +
              '<span class="cmp-diff-legend"><span class="cmp-lg cmp-lg-cur">' + cur + ' · current</span><span class="cmp-lg cmp-lg-cmp">' + cmp + '</span></span>' +
            '</div>';

        order.forEach(function (sec) {
            var rows = groups[sec];
            var aleHere = (sec === ONEK && data.ale);
            var count = rows.length + (aleHere ? aleDiffCount(data.ale) : 0);
            h += '<div class="cmp-diffcard">' +
                 '<div class="cmp-diffcard-head"><i class="bx ' + sectionIcon(sec) + '"></i><span>' + cmpEsc(sec) + '</span>' +
                 '<span class="cmp-diffcard-badge">' + count + '</span></div>' +
                 '<div class="cmp-diffcard-body">';
            rows.forEach(function (r) {
                h += '<div class="cmp-diffitem">' +
                     '<div class="cmp-diffitem-label">' + cmpEsc(r.label) + '</div>' +
                     '<div class="cmp-diffitem-vals">' + valChip(r.cur, "cmp-chip-cur") + valChip(r.cmp, "cmp-chip-cmp") + '</div>' +
                     '</div>';
            });
            if (aleHere) h += renderAleMatrix(data.ale);
            h += '</div></div>';
        });
        return $(h + '</div>');
    }
    // ON  -> replace the side-by-side panes with a single list of all differences.
    // OFF -> restore the normal side-by-side comparison.
    function applyOnlyDiff(on) {
        var $shell = $c().find(".cmp-shell");
        if (!$shell.length) return;
        $c().find(".cmp-difflist").remove();
        if (on) {
            $shell.after(renderDiffList(buildDiffData($shell)));
            $shell.addClass("d-none");
            hideRail();
        } else {
            $shell.removeClass("d-none");
            scheduleRail();
        }
    }
    // Rebuild the list as tabs finish loading / re-diffing while the toggle is on.
    function reapplyOnlyDiff() {
        if ($("#cmpOnlyDiff").is(":checked")) applyOnlyDiff(true);
    }

    $(document).on("change", "#cmpOnlyDiff", function () { applyOnlyDiff(this.checked); });

    // "Return to normal view" — leaves compare and restores the single record.
    $(document).on("click", "#cmpReturnBtn", function () {
        $t().find(".cmp-year-select").val("");
        exitCompare(false);
    });

    // Toggle the command-bar chrome for compare mode: hide the view-mode toggle and
    // Add button, and reveal the compare controls (diff pill / only-differences /
    // return) that sit in their place.
    function setCompareChrome(on) {
        $("#gridToolbarDefaultRight").toggleClass("d-none", on);
        $("#gridToolbarCompareRight").toggleClass("d-none", !on);
    }

    $(document).on("click", "#empNavPrev", function () { goPrev(); });
    $(document).on("click", "#empNavNext", function () { goNext(); });

    // Collapse / expand the employer list.
    $(document).on("click", "#listToggleBtn", function () {
        var $row = $("#mainListColumn").closest(".row");
        var collapsed = $row.toggleClass("cmp-list-collapsed").hasClass("cmp-list-collapsed");
        $(this).find("i").attr("class", collapsed ? "bx bx-chevrons-right" : "bx bx-chevrons-left");
        $(this).attr("title", collapsed ? "Show the employer list" : "Hide the employer list");
        scheduleRail();
    });
    // Clicking the collapsed vertical rail re-opens the list.
    $(document).on("click", ".emp-list-rail", function () { $("#listToggleBtn").trigger("click"); });

    // Return to the current-year record and close the comparison.
    $(document).on("click", ".cmp-close", function () {
        $t().find(".cmp-year-select").val("");
        exitCompare(false);
    });

    // Rebuild the whole-list nav ids whenever the employer list (re)loads
    // via the grid (search / filter / sort / page change).
    $(document).ajaxComplete(function (evt, xhr, settings) {
        if (!(settings && settings.url && settings.url.indexOf("EmployerListPartial") !== -1)) return;
        refreshNavList();

        // Prev/Next auto-paged to reveal a specific record: highlight it and stop.
        var pending = state.pendingSelectId;
        state.pendingSelectId = null;
        if (pending) { highlightRow(pending); return; }

        // Search / filter / sort / page-size reloads reset the grid to page 1
        // -> jump to the first result (or empty view). Plain pagination to a
        // later page keeps the current selection instead.
        var m = /[?&]pageindex=(\d+)/i.exec(settings.url || "");
        var pageIndex = m ? parseInt(m[1], 10) : 1;
        if (pageIndex === 1) {
            autoSelectFirst(true);
        } else if (state.employerId) {
            highlightRow(state.employerId);
        }
    });

    // Open the first list row so the detail pane shows a real record instead
    // of the default placeholder. Used on initial load and after a
    // search/filter reload. When forced (filter reload) it re-picks even if a
    // record is already open, and shows an empty view when there are no
    // results. Only applies to the split view (.employer-link-am).
    // After a view switch to Split the list re-renders asynchronously; wait for
    // its rows, then open the first record if nothing is selected yet. Fixes the
    // intermittent "detail stays on the default view after switching to Split".
    function selectFirstWhenReady(tries) {
        tries = tries || 0;
        var $links = $(LIST + " .employer-link-am");
        if ($links.length) {
            if (!$(LIST + " .employer-active.active-row").length) autoSelectFirst(true);
            return;
        }
        if (tries < 25) setTimeout(function () { selectFirstWhenReady(tries + 1); }, 100);
    }

    function autoSelectFirst(force) {
        if (!force && state.employerId) return; // keep the open record
        var $first = $(LIST + " .employer-link-am").first();
        if ($first.length) {
            $first.trigger("click");
        } else if (force) {
            // Filtered to no results: reset to the default/empty view.
            state.employerId = null;
            state.compareYear = null;
            $t().addClass("d-none");
            $c().html(
                '<div class="cmp-empty"><i class="bx bx-folder-open bx-lg d-block mb-2 mx-auto"></i>' +
                'No employer records match the current filter.</div>'
            );
        }
    }

    // Dock the compare toolbar into the top command bar's center slot, so the
    // record nav + "Compare filing" picker + diff summary sit inline between the
    // page-size selector and the view-mode icons (instead of a strip of their own).
    function dockToolbar() {
        var slot = document.getElementById("gridToolbarCenterSlot");
        var bar = document.getElementById("employerDetailToolbar");
        if (slot && bar && bar.parentElement !== slot) {
            bar.classList.add("cmp-toolbar-inline");
            slot.appendChild(bar);
        }
        // Dock the compare-mode command-bar controls (diff pill / only-differences /
        // return) into the right slot, next to where the view toggle + Add sit.
        var rslot = document.getElementById("gridToolbarCompareRight");
        var rctl = document.getElementById("compareRightControls");
        if (rslot && rctl && rctl.parentElement !== rslot) {
            rslot.appendChild(rctl);
            rctl.classList.remove("d-none");   // slot visibility now governs it
        }
    }

    // Initial build (the first list render is server-side, no ajax).
    // Deferred so GridManager.init() has run and getState() is available.
    $(function () {
        setTimeout(function () {
            dockToolbar();
            refreshNavList();
            if (viewIsSplit()) {
                autoSelectFirst();
            } else {
                // Loaded in table view: offer the compare picker (switches to split on use).
                $t().removeClass("d-none").addClass("cmp-table-mode");
                fillTablePicker();
            }
        }, 0);
    });

    // ---- keyboard navigation --------------------------------------------
    // Up / Down move to the previous / next record (loads it + highlights the row),
    // from ANYWHERE on the page — the search box, the year picker, etc. — so you can
    // navigate the list without clicking into it first. Esc leaves the comparison.
    $(document).on("keydown", function (e) {
        if (e.altKey || e.ctrlKey || e.metaKey) return;

        if (e.key === "Escape") {
            if (state.compareYear) {
                e.preventDefault();
                $t().find(".cmp-year-select").val("");
                exitCompare(false);
            }
            return;
        }

        if (e.key !== "ArrowUp" && e.key !== "ArrowDown") return;

        // Leave multi-line / rich-text editing alone (arrows move the caret there).
        var el = e.target, tag = (el.tagName || "").toLowerCase();
        if (tag === "textarea" || el.isContentEditable) return;

        // Only when the split-view list is active with a record open.
        if (!state.employerId || !$(LIST + " .employer-link-am").length) return;

        e.preventDefault();
        if (e.key === "ArrowDown") goNext(); else goPrev();
    });

    // ---- tab sync + per-tab diff (compare mode) ---------------------
    // Clicking a sub-tab in either pane switches BOTH panes to it and diffs
    // that tab's fields (highlighting the current-year side only). Tabs are
    // matched across panes by position (both render the same partial; ids
    // differ per render + the cmp__ prefix, so position is the stable key).

    function paneTabBtns($pane) {
        return $pane.find('.nav-link[data-bs-toggle="tab"]');
    }
    function tabPaneFor($pane, index) {
        var btn = paneTabBtns($pane).get(index);
        if (!btn) return $();
        return $pane.find($(btn).attr("data-bs-target"));
    }
    function tabReady($pane, index) {
        var btn = paneTabBtns($pane).get(index);
        if (!btn) return true;
        if (!$(btn).data("url")) return true;            // inline tab (no lazy load)
        var $holder = tabPaneFor($pane, index).find('[id*="-content"]').first();
        return $holder.length ? $holder.children().length > 0 : true;
    }
    function markTabChanged($pane, index, changed) {
        var btn = paneTabBtns($pane).get(index);
        if (btn) $(btn).toggleClass("cmp-tab-changed", changed);
    }
    function diffActiveTab($left, $right, index) {
        var $lp = tabPaneFor($left, index), $rp = tabPaneFor($right, index);
        if (!$lp.length || !$rp.length) return;
        $lp.add($rp).find(".cmp-changed-cell").removeClass("cmp-changed-cell");
        $lp.add($rp).find(".cmp-changed-field").removeClass("cmp-changed-field");
        var res = runDiff($lp, $rp); // highlights both sides (ALE matrix excluded)
        var changed = res.changed > 0;
        var $shell = $left.closest(".cmp-shell");
        // 1094 ALE matrix: diff the two years rollup-aware ("All 12 Months" vs per-month
        // are the same data) and highlight the differing cells on both panes.
        if ($lp.find('[id$="vis_ft_0"], [id$="vis_minCov_0"]').length) {
            changed = highlightAle($shell) > 0 || changed;
        }
        // Services tab: list-level diffs (a service in only one year, or a status
        // change) also count — not just the open service's detail fields.
        if ($lp.find('.service-item, [id$="serviceDetailContainer"]').length) {
            changed = changed || serviceDiffRows($shell).length > 0;
        }
        // Read-only display fields (e.g. Other Info: hire/term dates, Safe Harbor counts).
        changed = highlightRoTab($lp, $rp) > 0 || changed;
        markTabChanged($left, index, changed);
        markTabChanged($right, index, changed);
        updateSummaryCount();
        jumpIdx = -1;
    }

    // Load one tab's lazy content into a pane WITHOUT switching the visible
    // tab (used to eager-load everything for an accurate grand total).
    function loadTabInto($pane, index, cb) {
        function done() { if (cb) cb(); }
        var btn = paneTabBtns($pane).get(index);
        if (!btn) { done(); return; }
        var $btn = $(btn);
        var url = $btn.data("url");
        if (!url) { done(); return; }                 // inline tab (ER Info)
        var $holder = tabPaneFor($pane, index).find('[id*="-content"]').first();
        if (!$holder.length || $holder.children().length > 0) { done(); return; } // already loaded
        $.get(url).done(function (html) {
            var t = document.createElement("div");
            t.innerHTML = html;
            t.querySelectorAll("script").forEach(function (s) { s.remove(); });
            t.querySelectorAll(".notes-widget").forEach(function (el) { el.remove(); });
            if ($pane.hasClass("cmp-right")) namespaceIds(t, "cmp__");
            $holder.empty();
            while (t.firstChild) $holder[0].appendChild(t.firstChild);
            $holder.data("loaded", true);             // stop the partial loader re-fetching on click
            if ($pane.hasClass("cmp-right")) $holder.find("input, select, textarea").prop("disabled", true);
        }).always(done);
    }

    // Eager-load every sub-tab in both panes, then diff each one so the summary
    // reflects a true grand total before any tab is opened.
    function loadAllTabsAndDiff($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var count = paneTabBtns($left).length;
        var pending = 0;
        function afterEach() {
            if (--pending > 0) return;
            for (var k = 0; k < count; k++) diffActiveTab($left, $right, k);
            updateSummaryCount();
            syncCompareServices($shell);
        }
        for (var i = 0; i < count; i++) {
            pending += 2;
            loadTabInto($left, i, afterEach);
            loadTabInto($right, i, afterEach);
        }
        if (pending === 0) {
            for (var k = 0; k < count; k++) diffActiveTab($left, $right, k);
            updateSummaryCount();
            syncCompareServices($shell);
        }
    }

    // ---- service comparison, matched by NAME across the two years ------------
    function svcName(el) {
        var $h = $(el).find("h6").first();
        return ($h.attr("title") || $h.text() || "").trim().toLowerCase();
    }
    function svcItems($pane) {
        return $pane.find(".service-item").toArray().map(function (el) { return { el: el, name: svcName(el) }; });
    }
    // Original-case service name (for display) and its status (from the list badge).
    function svcDisplayName(el) {
        var $h = $(el).find("h6").first();
        return ($h.attr("title") || $h.text() || "").trim();
    }
    function svcStatus(el) { return ($(el).find("small").first().text() || "").trim().toLowerCase(); }
    function svcCap(s) { s = String(s == null ? "" : s).trim(); return s ? s.charAt(0).toUpperCase() + s.slice(1).toLowerCase() : s; }
    function svcInfo($pane) {
        return $pane.find(".service-item").toArray().map(function (el) {
            return { el: el, name: svcName(el), disp: svcDisplayName(el), status: svcStatus(el) };
        });
    }
    // Which sub-tab holds the services, and its label (for the diff-list section).
    function servicesTabInfo($shell) {
        var $left = $shell.find(".cmp-left");
        var out = { index: -1, name: "Service Info" };
        paneTabBtns($left).each(function (i) {
            if (tabPaneFor($left, i).find('.service-item, [id$="serviceDetailContainer"]').length) {
                out.index = i;
                var t = ($(this).text() || "").trim();
                if (t) out.name = t;
                return false;
            }
        });
        return out;
    }
    // Service-level differences read cheaply from the two service LISTS (no need to
    // open every service): a service only in one year, or a common service whose
    // status badge differs. The status of the currently-open service is skipped —
    // its detail fields are already diffed field-by-field.
    function serviceDiffRows($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var L = svcInfo($left), R = svcInfo($right);
        if (!L.length && !R.length) return [];
        var rMap = {}; R.forEach(function (x) { rMap[x.name] = x; });
        var lMap = {}; L.forEach(function (x) { lMap[x.name] = x; });
        var rows = [];
        L.forEach(function (x) {
            var r = rMap[x.name];
            if (!r) {
                rows.push({ label: x.disp, cur: x.status ? svcCap(x.status) : "Present", cmp: "" });
            } else if (x.status !== r.status && !x.el.classList.contains("active")) {
                rows.push({ label: x.disp + " — status", cur: svcCap(x.status), cmp: svcCap(r.status) });
            }
        });
        R.forEach(function (x) {
            if (!lMap[x.name]) rows.push({ label: x.disp, cur: "", cmp: x.status ? svcCap(x.status) : "Present" });
        });
        return rows;
    }
    // Set the Service tab's orange dot from BOTH field-level detail diffs and the
    // list-level service diffs (added / removed / status).
    function refreshServiceTabDot($shell) {
        var svc = servicesTabInfo($shell);
        if (svc.index < 0) return;
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var fieldChanged = tabPaneFor($left, svc.index).find(".cmp-changed-field").length > 0;
        var changed = fieldChanged || serviceDiffRows($shell).length > 0;
        markTabChanged($left, svc.index, changed);
        markTabChanged($right, svc.index, changed);
    }
    // ---- read-only display-field diff (e.g. Other Info: hire/term dates, Safe Harbor) ----
    // Some tabs show values as plain text (spans/divs), not form inputs, so runDiff can't
    // see them. Any element tagged data-cmp-field="<label>" is compared by its text, matched
    // across panes by that label.
    function roItems($scope) {
        return $scope.find("[data-cmp-field]").toArray().map(function (el) {
            return { el: el, label: ($(el).attr("data-cmp-field") || "").trim(), value: ($(el).text() || "").trim() };
        });
    }
    function roDiffPairs($lScope, $rScope) {
        var L = roItems($lScope), R = roItems($rScope);
        var rmap = {}; R.forEach(function (x) { rmap[x.label] = x; });
        var out = [];
        L.forEach(function (x) {
            var r = rmap[x.label];
            if (r && x.value !== r.value) out.push({ label: x.label, cur: x.value, cmp: r.value, lEl: x.el, rEl: r.el });
        });
        return out;
    }
    // The tight box to highlight for a read-only value — the small stat card (Safe Harbor)
    // or the label/value row (dates), NOT the whole column that fieldCell() would climb to.
    function roCell(el) {
        return el.closest(".card.flex-fill, .d-flex") || el.parentElement || el;
    }
    // Highlight differing read-only fields within a tab (both panes); return the count.
    function highlightRoTab($lp, $rp) {
        var pairs = roDiffPairs($lp, $rp);
        pairs.forEach(function (p) {
            roCell(p.lEl).classList.add("cmp-changed-cell");
            roCell(p.rEl).classList.add("cmp-changed-cell");
        });
        return pairs.length;
    }
    // Load a service's detail into its OWN pane's container. Self-contained (the
    // service partial's click handler is stripped from the compare clone, so we
    // can't rely on it). A re-diff runs when the detail load completes.
    function loadServiceDetailInPane($item) {
        var $ws = $item.closest('[id$="main-workspace"]');
        if (!$ws.length) $ws = $item.closest(".cmp-pane");
        var readOnly = $item.closest(".cmp-right").length > 0;
        $ws.find(".service-item").removeClass("active");
        $item.addClass("active");
        var $container = $ws.find('[id$="serviceDetailContainer"]').first();
        if (!$container.length) return;
        $container.html('<div class="d-flex justify-content-center align-items-center h-100 p-4"><div class="spinner-grow text-primary"></div></div>');
        $.get("/Employer/LoadServiceDetail", {
            employerId: $item.data("employer-id"),
            planYear: $item.data("plan-year"),
            serviceId: $item.data("service-id")
        }).done(function (res) {
            $container.html(res);
            if (readOnly) {
                $container.find("input, select, textarea").prop("disabled", true);
                $container.find(".btn").not(".nav-link, [data-bs-toggle='tab']").hide();
            }
        });
    }
    // Open the service with `name` in BOTH panes so their details compare.
    function openCompareService($shell, name) {
        [$shell.find(".cmp-left"), $shell.find(".cmp-right")].forEach(function ($p) {
            var it = svcItems($p).filter(function (x) { return x.name === name; })[0];
            if (it) loadServiceDetailInPane($(it.el));
        });
    }
    // Flag services present in only one year, and auto-open the first service common
    // to both so its detail (month grid + status) is compared.
    function syncCompareServices($shell) {
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var L = svcItems($left), R = svcItems($right);
        if (!L.length && !R.length) return;
        var rNames = R.map(function (x) { return x.name; }), lNames = L.map(function (x) { return x.name; });
        L.forEach(function (x) {
            var only = rNames.indexOf(x.name) < 0;
            x.el.classList.toggle("svc-only", only);
            x.el.title = only ? "Not present in " + state.compareYear : "";
        });
        R.forEach(function (x) {
            var only = lNames.indexOf(x.name) < 0;
            x.el.classList.toggle("svc-only", only);
            x.el.title = only ? "Not present in " + state.activeYear : "";
        });
        // Now that both service lists are known, light the Service tab's dot and
        // fold the list-level differences into the summary count + "only differences"
        // list (this runs before any service detail has loaded).
        refreshServiceTabDot($shell);
        updateSummaryCount();

        // only auto-open once
        if ($shell.data("svcOpened")) return;
        $shell.data("svcOpened", true);
        var firstCommon = L.filter(function (x) { return rNames.indexOf(x.name) >= 0; })[0];
        if (firstCommon) {
            openCompareService($shell, firstCommon.name);
        } else {
            if (L[0]) loadServiceDetailInPane($(L[0].el));
            if (R[0]) loadServiceDetailInPane($(R[0].el));
        }
    }
    // Clicking a service in a compare pane loads it AND the same-named service in the
    // other pane, so the two years' details sit side by side and diff.
    $(document).on("click", ".cmp-shell .service-item", function () {
        var $shell = $(this).closest(".cmp-shell");
        var name = svcName(this);
        loadServiceDetailInPane($(this));
        var $other = $(this).closest(".cmp-right").length ? $shell.find(".cmp-left") : $shell.find(".cmp-right");
        var it = svcItems($other).filter(function (x) { return x.name === name; })[0];
        if (it) loadServiceDetailInPane($(it.el));
    });
    // Other Info "Save & Finish" inside a comparison: the tab's own <script> is stripped
    // from the (editable) left pane when it lazy-loads in compare, so its click handler
    // never binds and the memo won't save. Provide a self-contained handler, scoped to
    // .cmp-shell so it only runs in compare (no double-save in the normal tab).
    $(document).on("click", ".cmp-shell #btnSaveFinishOtherInfo", function (e) {
        e.preventDefault();
        var $btn = $(this);
        var $form = $btn.closest("form");            // #frmOtherInfo in the left pane
        if (!$form.length) return;
        var original = $btn.html();
        $btn.prop("disabled", true).html('<i class="bx bx-loader-alt bx-spin"></i> Saving...');
        var token = $('input[name="__RequestVerificationToken"]').val();
        $.ajax({
            url: "/Employer/SaveEmployerImportantInfo",
            type: "POST",
            data: $form.serialize(),
            headers: token ? { "RequestVerificationToken": token } : {},
            success: function (res) {
                if (res && res.success) {
                    if (window.Swal) Swal.fire({ icon: "success", title: "Saved!", text: res.message, timer: 1200, showConfirmButton: false });
                } else if (window.Swal) {
                    Swal.fire({ icon: "error", title: "Error", text: (res && res.message) || "Could not save." });
                }
            },
            error: function () { if (window.Swal) Swal.fire({ icon: "error", title: "System Error", text: "Could not save data." }); },
            complete: function () { $btn.prop("disabled", false).html(original); }
        });
    });
    // A service's detail (month grid) loads async after its tab, so re-diff the
    // Services tab whenever a service detail finishes loading inside the compare shell.
    $(document).ajaxComplete(function (evt, xhr, settings) {
        if (!settings || !settings.url || settings.url.indexOf("LoadServiceDetail") === -1) return;
        var $shell = $c().find(".cmp-shell");
        if (!$shell.length) return;
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var idx = -1;
        paneTabBtns($left).each(function (i) {
            if (tabPaneFor($left, i).find('[id$="serviceDetailContainer"]').length) { idx = i; return false; }
        });
        if (idx >= 0) {
            // compare pane detail must stay read-only after its async load
            $right.find('[id$="serviceDetailContainer"]').find("input, select, textarea").prop("disabled", true);
            diffActiveTab($left, $right, idx);
        }
    });

    var tabSyncIndex = null;
    $(document).on("shown.bs.tab", '.cmp-shell .nav-link[data-bs-toggle="tab"]', function () {
        var $btn = $(this);
        var $shell = $btn.closest(".cmp-shell");
        var $pane = $btn.closest(".cmp-pane");
        var index = paneTabBtns($pane).index(this);
        if (tabSyncIndex === index) return;              // echo from our own sync

        tabSyncIndex = index;
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var $other = $pane.hasClass("cmp-left") ? $right : $left;
        var otherBtn = paneTabBtns($other).get(index);
        if (otherBtn) $(otherBtn).trigger("click");      // switch + lazy-load the other pane

        // Both panes load their tab content async; diff once both are ready.
        var tries = 0;
        (function waitDiff() {
            if ((tabReady($left, index) && tabReady($right, index)) || tries++ > 25) {
                diffActiveTab($left, $right, index);
                tabSyncIndex = null;
            } else {
                setTimeout(waitDiff, 100);
            }
        })();
    });

    window.EmployerCompare = {
        onDetailLoaded: onDetailLoaded,
        highlightRow: highlightRow,
        selectFirst: function () { selectFirstWhenReady(0); },
        onViewChange: function (mode) {
            if (mode === "Table") {
                // No side-by-side in table view: close any comparison and show just the
                // compare picker (record nav hidden via .cmp-table-mode).
                if (state.compareYear) exitCompare(false);
                state.employerId = null;
                $t().removeClass("d-none").addClass("cmp-table-mode");
                fillTablePicker();
            } else {
                $t().removeClass("cmp-table-mode");
            }
        }
    };
})();
