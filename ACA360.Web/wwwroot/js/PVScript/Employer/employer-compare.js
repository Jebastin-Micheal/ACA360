/* =====================================================================
   Employer filing-year comparison — thin config over compare-core.js.
   ---------------------------------------------------------------------
   This used to be a full standalone reimplementation of the comparison
   engine (nav, year picker, shell building, generic field diff, "show
   only differences" list, tab sync) that happened to share an approach
   with compare-core.js without ever calling it — everything generic is
   now that shared engine; only what's genuinely Employer-specific stays
   here as config + plugins + a little extra event wiring:

     - the 1094-C ALE matrix (a fixed grid of checkbox/number cells with
       no per-cell label, and a per-metric "All 12 Months" rollup that
       must compare equal to the same data stored per-month) — supplied
       as cfg.plugins[0], matching how compare-core.js's own header
       comment says module-unique comparisons should work.
     - the Services tab: a LIST of named items (not a table row like
       Plan's banding), matched by name across years rather than by
       position, where a service can be present in only one year or
       have a different status without any of its detail fields
       differing — also plugins[0], plus its own async load-a-service
       click wiring that isn't part of what the generic engine covers.
     - the record nav auto-pages the Employer grid when Prev/Next lands
       on a row that isn't on the currently visible page — genuinely
       more than compare-core.js's generic nav does, so it's supplied
       via cfg.highlightRow.

   Employer/Index.cshtml was switched from its own hand-copied toolbar
   markup to the shared _CompareToolbar.cshtml partial as part of this —
   its ids (#cmpNavPrev etc.) are what compare-core.js's generic click
   handlers are bound to; the old #empNavPrev/#empNavNext/
   #compareRightControls ids don't exist anymore.

   window.EmployerCompare keeps the same 4-method public shape the
   page's own inline script already calls (onDetailLoaded, highlightRow,
   onViewChange, selectFirst) so nothing outside this file needed to
   change.
   ===================================================================== */
(function () {
    "use strict";
    if (!window.CompareCore) return;

    var LIST = "#employerListContainer";
    var IDS_URL = "/Employer/GetFilteredEmployerIds";

    // ---- current grid filter/sort params, for the nav id list -----------
    // Same params the grid itself sends, so nav matches exactly what's listed.
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

    // ============================================================
    // 1094-C ALE MATRIX — a fixed grid, not a form field, so it needs its
    // own diff/highlight/render rather than the generic field-by-field pass.
    // ============================================================
    var CMP_MONTHS = ["All 12 Months", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
                      "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    var CMP_METRICS = { minCov: "Minimum Coverage", ft: "Full-Time", tot: "Total", agg: "Aggregate Group", sec: "Sec 4980H" };
    var ALE_METRICS = [["minCov", "Min Cov"], ["ft", "Full-Time"], ["tot", "Total"], ["agg", "Aggregate"], ["sec", "Sec 4980H"]];

    function cmpEsc(s) {
        return String(s == null ? "" : s)
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }
    // Matches BOTH the visible ALE table inputs (vis_*) and the hidden matrix-modal
    // duplicates (mx_*) — both fold into the ALE table, never listed field-by-field.
    // Used as the plugin's skipField so the generic engine's own field diff leaves
    // these cells alone; this file diffs and highlights them separately below.
    function matrixLabel(el) {
        var m = (el.id || "").match(/^(?:cmp__)?(?:vis|mx)_(minCov|ft|tot|agg|sec)_(\d+)$/);
        if (!m) return null;
        return CMP_METRICS[m[1]] + " — " + (CMP_MONTHS[parseInt(m[2], 10)] || ("M" + m[2]));
    }
    function aleCell(el, metric) {
        if (!el) return "";
        if (metric === "minCov" || metric === "agg") return el.checked ? "✓" : "—";
        var v = (el.value || "").trim();
        return v === "" ? "0" : v;
    }
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
    function highlightAle($shell, fieldCell) {
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
    function aleDiffCount(ale) {
        var n = 0;
        ale.rows.forEach(function (r) { r.cells.forEach(function (c) { if (c.diff) n++; }); });
        return n;
    }
    function renderAleMatrix(ale, activeYear, compareYear) {
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
              '<div class="cmp-ale-pane"><div class="cmp-ale-head cmp-ale-cur">' + cmpEsc(activeYear) + ' · current</div>' + tbl("cur") + '</div>' +
              '<div class="cmp-ale-pane"><div class="cmp-ale-head">' + cmpEsc(compareYear) + '</div>' + tbl("cmp") + '</div>' +
            '</div></div>';
    }

    // ============================================================
    // SERVICES TAB — a list of named items matched across years by name,
    // not a form field or a table row. "Different" includes a service
    // present in only one year, or a common one whose status changed.
    // ============================================================
    function svcName(el) {
        var $h = $(el).find("h6").first();
        return ($h.attr("title") || $h.text() || "").trim().toLowerCase();
    }
    function svcItems($pane) {
        return $pane.find(".service-item").toArray().map(function (el) { return { el: el, name: svcName(el) }; });
    }
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
    // Load a service's detail into its OWN pane's container. Self-contained (the
    // service partial's click handler is stripped from the compare clone, so we
    // can't rely on it). A re-diff runs when the detail load completes (see the
    // ajaxComplete handler below).
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
    // to both so its detail (month grid + status) is compared. Runs once all tabs have
    // loaded (cfg.afterAllTabsLoaded) — needs both years' service lists to be in the DOM.
    function syncCompareServices($shell, state) {
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
    // Trivial, stable DOM queries used only by this file's own Services-tab wiring
    // below — not worth routing through the engine for one line each.
    function paneTabBtns($pane) { return $pane.find('.nav-link[data-bs-toggle="tab"]'); }
    function tabPaneFor($pane, index) { var btn = paneTabBtns($pane).get(index); if (!btn) return $(); return $pane.find($(btn).attr("data-bs-target")); }

    // ============================================================
    // RECORD NAV — auto-pages the grid when Prev/Next lands on a row that
    // isn't on the currently visible page. Genuinely more than the engine's
    // generic highlightRow does, so it's supplied as cfg.highlightRow.
    // ============================================================
    var pendingSelectId = null;

    function domHighlightRow(id) {
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
    function revealRowInList(id, instance) {
        var idx = instance.state.navIds.indexOf(String(id));
        if (idx < 0) { domHighlightRow(id); return; }

        var st = (window.GridManager && GridManager.getState) ? GridManager.getState() : {};
        var pageSize = parseInt(st.pageSize, 10) || 10;
        var currentPage = parseInt(st.pageIndex, 10) || 1;
        var targetPage = Math.floor(idx / pageSize) + 1;

        if (targetPage === currentPage || !(window.GridManager && GridManager.setPageIndex)) {
            domHighlightRow(id);
        } else {
            // Highlight happens after the new page renders (ajaxComplete below).
            pendingSelectId = String(id);
            GridManager.setPageIndex(targetPage);
        }
    }

    // Open the first list row so the detail pane shows a real record instead of the
    // default placeholder. After a view switch to Split the list re-renders
    // asynchronously; wait for its rows, then open the first record if nothing is
    // selected yet. Only applies to the split view (.employer-link-am).
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
        if (!force && instance.state.recordId) return; // keep the open record
        var $first = $(LIST + " .employer-link-am").first();
        if ($first.length) {
            $first.trigger("click");
        } else if (force) {
            // Filtered to no results: reset to the default/empty view.
            $("#employerDetailToolbar, #cmpDetailToolbar").addClass("d-none");
            $("#commonDetailContainer").html(
                '<div class="cmp-empty"><i class="bx bx-folder-open bx-lg d-block mb-2 mx-auto"></i>' +
                'No employer records match the current filter.</div>'
            );
        }
    }

    // ============================================================
    // THE ENGINE INSTANCE
    // ============================================================
    var instance = CompareCore.create({
        apiName: "EmployerCompareInstance",
        container: "#commonDetailContainer",
        toolbar: "#cmpDetailToolbar",
        listContainer: LIST,
        rowSelector: ".employer-link-am",
        rowIdAttr: "data-employer-id",
        detailColumn: "#mainDetailColumn",

        detailUrl: "/Employer/GetCommonDetails", detailParam: "EmployerId", detailYearParam: "filingYear",
        compareUrl: "/Employer/GetCompareDetails", compareParam: "EmployerId", compareYearParam: "filingYear",
        yearsUrl: "/Employer/GetFilingYears",
        idsUrl: IDS_URL,
        navParams: currentListParams,
        // Not cfg.listReloadUrlMatch: this page needs more than the generic
        // "re-highlight the open record" reaction (a pending cross-page nav
        // target, and auto-select-first-on-fresh-filter). Handled below instead,
        // in the one ajaxComplete listener for EmployerListPartial.

        defaultSection: "Employer Profile",
        sectionIcons: {
            "Employer Profile": "bx-building-house", "ER Info": "bx-id-card",
            "1094 Details": "bx-file", "Service Info": "bx-cog", "Other Info": "bx-info-circle"
        },
        // Audit Logs + Notes are not field-comparable.
        hideSelectors: ['[id$="Employerauditlog"]', '[id$="auditLogContainer_Employer"]'],
        recordMarkerSelector: "#hdnEmployerId",

        // Auto-pages the grid to the record's page before highlighting it — the
        // generic engine assumes the row is already on the visible page.
        highlightRow: function (id) { revealRowInList(id, instance); },

        plugins: [{
            skipField: function (el) { return !!matrixLabel(el); },
            count: function ($shell) {
                var n = 0;
                var ale = buildAleMatrix($shell);
                if (ale) n += aleDiffCount(ale);
                n += serviceDiffRows($shell).length;
                return n;
            },
            tabChanged: function ($lp, $rp, $shell) {
                var changed = 0;
                if ($lp.find('[id$="vis_ft_0"], [id$="vis_minCov_0"]').length) {
                    changed += highlightAle($shell, function fieldCell(el) {
                        var td = el.closest("td");
                        if (td) return td;
                        var cell = el.closest('[class*="col-"], .mb-3, .form-group, .form-check');
                        return cell || el.closest(".input-group") || el.parentElement || el;
                    });
                }
                if ($lp.find('.service-item, [id$="serviceDetailContainer"]').length && serviceDiffRows($shell).length > 0) {
                    changed += 1;
                }
                return changed;
            },
            diffRows: function ($shell) {
                var svc = servicesTabInfo($shell);
                return serviceDiffRows($shell).map(function (r) {
                    return { section: svc.name, label: r.label, cur: r.cur, cmp: r.cmp };
                });
            },
            extraBlocks: function ($shell) {
                var ale = buildAleMatrix($shell);
                if (!ale) return [];
                return [{
                    section: "1094 Details",
                    html: renderAleMatrix(ale, instance.state.activeYear, instance.state.compareYear),
                    count: aleDiffCount(ale)
                }];
            }
        }],

        // Once every tab has loaded: mark services present in only one year, and
        // auto-open the first service common to both.
        afterAllTabsLoaded: function ($shell) { syncCompareServices($shell, instance.state); }
    });

    // ============================================================
    // EXTRA EVENT WIRING — genuinely Employer-specific, not part of what
    // the generic engine (or its plugin hooks) covers.
    // ============================================================

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

    // A service's detail (month grid) loads async after its tab, so re-diff the
    // Services tab whenever a service detail finishes loading inside the compare shell.
    $(document).ajaxComplete(function (evt, xhr, settings) {
        if (!settings || !settings.url || settings.url.indexOf("LoadServiceDetail") === -1) return;
        var $shell = $("#commonDetailContainer").find(".cmp-shell");
        if (!$shell.length) return;
        var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
        var idx = -1;
        paneTabBtns($left).each(function (i) {
            if (tabPaneFor($left, i).find('[id$="serviceDetailContainer"]').length) { idx = i; return false; }
        });
        if (idx >= 0) {
            // compare pane detail must stay read-only after its async load
            $right.find('[id$="serviceDetailContainer"]').find("input, select, textarea").prop("disabled", true);
            instance.diffActiveTab($left, $right, idx);
        }
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

    // Rebuild the whole-list nav ids whenever the employer list (re)loads via the grid
    // (search / filter / sort / page change), and react: consume a pending cross-page
    // nav target, auto-select the first row after a fresh search/filter, or just
    // re-highlight the current selection on a plain page change. refreshNavList() is
    // NOT awaited here — its response can arrive after the rest of this handler runs,
    // same as the pre-migration version; state.navIds a beat stale only matters if
    // Prev/Next is clicked in that same instant, and worst case it re-computes on the
    // next click.
    $(document).ajaxComplete(function (evt, xhr, settings) {
        if (!(settings && settings.url && settings.url.indexOf("EmployerListPartial") !== -1)) return;
        instance.refreshNavList();

        var pending = pendingSelectId;
        pendingSelectId = null;
        if (pending) { domHighlightRow(pending); return; }
        var st = (window.GridManager && GridManager.getState) ? GridManager.getState() : {};
        if (st.viewMode && st.viewMode !== "Split") return;  
        var m = /[?&]pageindex=(\d+)/i.exec(settings.url || "");
        var pageIndex = m ? parseInt(m[1], 10) : 1;
        if (pageIndex === 1) {
            autoSelectFirst(true);
        } else if (instance.state.recordId) {
            domHighlightRow(instance.state.recordId);
        }
    });

    // Initial build (the first list render is server-side, no ajax). Deferred so
    // GridManager.init() has run and getState() is available.
    $(function () {
        setTimeout(function () {
            if ($(LIST + " .employer-link-am").length) autoSelectFirst();
        }, 0);
    });

    // ---- public API — unchanged shape, so nothing outside this file changes ----
    window.EmployerCompare = {
        onDetailLoaded: instance.onDetailLoaded,
        highlightRow: instance.highlightRow,
        onViewChange: instance.onViewChange,
        selectFirst: function () { selectFirstWhenReady(0); }
    };
})();
