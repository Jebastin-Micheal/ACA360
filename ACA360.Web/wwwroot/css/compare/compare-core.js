/* =====================================================================
   Filing-Year Comparison — COMMON CORE engine
   ---------------------------------------------------------------------
   One reusable engine for Employer / Employee / Plan. A module calls
   CompareCore.create(cfg) with its URLs, list-row selector, id/year field
   names and (optionally) module-unique diff plugins. The engine provides:
     - the side-by-side split shell (current year editable, compare year
       read-only), built by cloning the loaded detail and namespacing ids
     - generic field diff + amber highlight (inputs/selects/textareas)
     - read-only display-field diff via data-cmp-field="<label>"
     - "Show only differences" modern card list + toolbar count pill
     - record Prev/Next nav + Up/Down keys, Esc to exit
     - the filing-year picker and per-tab diffing
   Module-unique comparisons (e.g. Employer 1094 ALE matrix, services)
   are supplied as cfg.plugins = [{ tabChanged, diffRows, count, extraBlocks }].
   ===================================================================== */
(function () {
    "use strict";

    function create(cfg) {
        cfg = cfg || {};
        var CONTAINER = cfg.container || "#commonDetailContainer";
        var TOOLBAR = cfg.toolbar || "#cmpDetailToolbar";
        var LIST = cfg.listContainer;                 // e.g. "#employeeListContainer"
        var ROW = cfg.rowSelector;                    // e.g. ".employee-item"
        var ROW_ID = cfg.rowIdAttr || "data-id";
        var plugins = cfg.plugins || [];
        var SKIP_IDS = cfg.skipFieldIds || ["FilingYear", "PlanYear"];

        var state = {
            recordId: null,
            activeYear: null,
            compareYear: null,
            yearsCache: null,
            yearsCurrent: null,
            yearsFilledFor: null,
            navIds: [],
            navFilingYear: null,
            pendingSelectId: null,
            pendingCompareYear: null
        };

        function $c() { return $(CONTAINER); }
        function $t() { return $(TOOLBAR); }
        function idOf(el) { var v = $(el).attr(ROW_ID); return v == null ? null : String(v); }

        // ---- comparable fields / diff helpers ---------------------------
        function comparableFields(root) {
            return $(root).find("input, select, textarea").filter(function () {
                var t = (this.type || "").toLowerCase();
                if (t === "hidden" || t === "button" || t === "submit" || t === "reset") return false;
                var bare = (this.id || "").replace(/^cmp__/, "");
                if (SKIP_IDS.indexOf(bare) >= 0) return false;
                return true;
            }).toArray();
        }
        function fieldValue(el) {
            var t = (el.type || "").toLowerCase(), v;
            if (t === "checkbox") v = el.checked ? "1" : "0";
            else if (t === "radio") v = el.checked ? (el.value || "on") : "";
            else if (el.tagName === "SELECT") { var opt = el.options[el.selectedIndex]; v = opt ? (opt.text || opt.value) : el.value; }
            else v = el.value;
            return (v == null ? "" : String(v)).trim();
        }
        function fieldDisplayValue(el) {
            var t = (el.type || "").toLowerCase();
            if (t === "checkbox") return el.checked ? "Yes" : "No";
            if (el.tagName === "SELECT") { var o = el.options[el.selectedIndex]; return o ? (o.text || o.value).trim() : ""; }
            return (el.value || "").trim();
        }
        // Compare values ignoring case + surrounding whitespace: some fields are
        // upper-cased for display only (not in the DB), so "JANUARY" vs "January"
        // is NOT a real year-over-year change and must not be flagged.
        function normCmp(s) { return String(s == null ? "" : s).replace(/\s+/g, " ").trim().toLowerCase(); }
        function fieldCell(el) {
            var td = el.closest("td");
            if (td) return td;
            var cell = el.closest('[class*="col-"], .mb-3, .form-group, .form-check');
            return cell || el.closest(".input-group") || el.parentElement || el;
        }

        // Namespace ids (+ references) in the read-only compare clone.
        function namespaceIds(rootEl, prefix) {
            var idMap = {};
            rootEl.querySelectorAll("[id]").forEach(function (el) { if (!el.id) return; idMap[el.id] = prefix + el.id; el.id = prefix + el.id; });
            function remap(attr, hash) {
                rootEl.querySelectorAll("[" + attr + "]").forEach(function (el) {
                    var val = el.getAttribute(attr); if (!val) return;
                    var raw = hash ? val.replace(/^#/, "") : val;
                    if (idMap[raw]) el.setAttribute(attr, (hash ? "#" : "") + idMap[raw]);
                });
            }
            remap("for", false); remap("href", true); remap("data-bs-target", true);
            remap("data-bs-parent", true); remap("aria-controls", false); remap("aria-labelledby", false);
        }

        // ---- year picker ------------------------------------------------
        function loadYears() {
            if (state.yearsCache) return $.Deferred().resolve(state.yearsCache).promise();
            return $.get(cfg.yearsUrl).then(function (resp) {
                state.yearsCache = (resp && resp.years) ? resp.years : [];
                if (resp && resp.current) state.yearsCurrent = String(resp.current);
                return state.yearsCache;
            });
        }
        function fillTablePicker() {
            loadYears().done(function (years) {
                var $sel = $t().find(".cmp-year-select");
                var cur = state.activeYear || state.yearsCurrent || "";
                $sel.empty().append('<option value="">Compare filing year…</option>');
                years.forEach(function (y) { if (String(y) !== String(cur)) $sel.append('<option value="' + y + '">' + y + '</option>'); });
                state.yearsFilledFor = null;
            });
        }
        function fillYearSelect() {
            if (state.yearsFilledFor === state.activeYear) return;
            loadYears().done(function (years) {
                var $sel = $t().find(".cmp-year-select");
                var active = String(state.activeYear);
                $sel.empty().append('<option value="">' + active + ' (current)</option>');
                years.forEach(function (y) { if (String(y) !== active) $sel.append('<option value="' + y + '">' + y + '</option>'); });
                $sel.val(state.compareYear ? String(state.compareYear) : "");
                state.yearsFilledFor = state.activeYear;
            });
        }

        // ---- record navigation -----------------------------------------
        function navParams() { return (typeof cfg.navParams === "function") ? cfg.navParams() : {}; }
        // Build the nav id list straight from the rendered list rows. Used when the
        // record id can't be re-derived server-side — e.g. Plan ids are AES-encrypted
        // with a random IV, so the same plan encrypts to a different string every call
        // and a server id list would never string-match the row the user clicked. The
        // DOM ids ARE the ones clicks/highlight use, so matching is exact.
        function navIdsFromDom() {
            if (!LIST || !ROW) return [];
            return $(LIST + " " + ROW).map(function () { return idOf(this); }).get().filter(function (v) { return v != null && v !== ""; });
        }
        function refreshNavList() {
            if (cfg.navFromDom) {
                state.navIds = navIdsFromDom();
                state.navFilingYear = state.activeYear || state.navFilingYear;
                updateNavCounter();
                return $.Deferred().resolve().promise();
            }
            if (!cfg.idsUrl) return $.Deferred().resolve().promise();
            return $.get(cfg.idsUrl, navParams()).done(function (resp) {
                var ids = (resp && resp.ids) ? resp.ids : (Array.isArray(resp) ? resp : []);
                state.navIds = ids.map(String);
                state.navFilingYear = (resp && resp.filingYear) ? String(resp.filingYear) : (state.activeYear || state.navFilingYear);
                updateNavCounter();
            });
        }
        function currentNavIndex() { return state.recordId ? state.navIds.indexOf(String(state.recordId)) : -1; }
        function updateNavCounter() {
            var n = state.navIds.length, idx = currentNavIndex();
            $("#cmpNavCounter").text((idx >= 0 && n) ? ((idx + 1) + " of " + n) : "");
            $("#cmpNavPrev").prop("disabled", idx <= 0);
            $("#cmpNavNext").prop("disabled", idx < 0 || idx >= n - 1);
        }
        function loadDetailDirect(id) {
            var year = state.activeYear || state.navFilingYear;
            $c().html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');
            if (typeof cfg.loadDetail === "function") { cfg.loadDetail(id, year, function () { onDetailLoaded(id, year); }); return; }
            var data = {}; data[cfg.detailParam] = id; if (cfg.detailYearParam) data[cfg.detailYearParam] = year;
            $.get(cfg.detailUrl, data)
                .done(function (html) { $c().html(html); onDetailLoaded(id, year); })
                .fail(function () { $c().html('<div class="alert alert-danger m-3">Failed to load record.</div>'); });
        }
        function highlightRow(id) {
            if (typeof cfg.highlightRow === "function") { cfg.highlightRow(id); return; }
            if (!LIST || !ROW) return;
            $(LIST + " " + ROW).removeClass("cmp-row-active");
            var $row = $(LIST + " " + ROW).filter(function () { return idOf(this) === String(id); }).first();
            if ($row.length) { $row.addClass("cmp-row-active"); $row[0].scrollIntoView({ block: "nearest" }); }
        }
        function loadRecord(id) { loadDetailDirect(id); highlightRow(id); }
        function goToNav(i) { if (i >= 0 && i < state.navIds.length) loadRecord(state.navIds[i]); }
        function goPrev() { goToNav(currentNavIndex() - 1); }
        function goNext() { goToNav(currentNavIndex() + 1); }
        // Modules that drive navigation with their OWN Prev/Next (e.g. Employee reuses
        // its #empNavRow) supply cfg.navPrev/navNext; otherwise use the core's list nav.
        function navPrevAction() { if (typeof cfg.navPrev === "function") cfg.navPrev(); else goPrev(); }
        function navNextAction() { if (typeof cfg.navNext === "function") cfg.navNext(); else goNext(); }

        // ---- diff -------------------------------------------------------
        function runDiff($left, $right) {
            var left = comparableFields($left), right = comparableFields($right);
            var n = Math.min(left.length, right.length), changed = 0;
            for (var i = 0; i < n; i++) {
                if (pluginSkipField(left[i])) continue;   // module handles it (e.g. ALE matrix)
                if (normCmp(fieldValue(left[i])) === normCmp(fieldValue(right[i]))) continue;
                changed++;
                left[i].classList.add("cmp-changed-field"); fieldCell(left[i]).classList.add("cmp-changed-cell");
                right[i].classList.add("cmp-changed-field"); fieldCell(right[i]).classList.add("cmp-changed-cell");
            }
            return { changed: changed };
        }
        function pluginSkipField(el) {
            for (var i = 0; i < plugins.length; i++) { if (plugins[i].skipField && plugins[i].skipField(el)) return true; }
            return false;
        }

        // read-only display fields (data-cmp-field="<label>")
        function roItems($scope) {
            return $scope.find("[data-cmp-field]").toArray().map(function (el) {
                return { el: el, label: ($(el).attr("data-cmp-field") || "").trim(), value: ($(el).text() || "").trim() };
            });
        }
        function roDiffPairs($l, $r) {
            var L = roItems($l), R = roItems($r), rmap = {}, out = [];
            R.forEach(function (x) { rmap[x.label] = x; });
            L.forEach(function (x) { var r = rmap[x.label]; if (r && normCmp(x.value) !== normCmp(r.value)) out.push({ label: x.label, cur: x.value, cmp: r.value, lEl: x.el, rEl: r.el }); });
            return out;
        }
        // The tight box to highlight for a read-only value. cfg.roCellSelector lets a
        // module name its own card (e.g. Employee's ".month-card") so the highlight lands
        // on that card rather than the whole row a generic .d-flex would climb to.
        function roCell(el) {
            var sel = (cfg.roCellSelector ? cfg.roCellSelector + ", " : "") + ".card.flex-fill, .d-flex";
            return el.closest(sel) || el.parentElement || el;
        }
        function highlightRoTab($lp, $rp) {
            var pairs = roDiffPairs($lp, $rp);
            pairs.forEach(function (p) { roCell(p.lEl).classList.add("cmp-changed-cell"); roCell(p.rEl).classList.add("cmp-changed-cell"); });
            return pairs.length;
        }

        function clearHighlights() {
            $c().find(".cmp-changed-cell").removeClass("cmp-changed-cell");
            $c().find(".cmp-changed-field").removeClass("cmp-changed-field");
            $c().find(".cmp-changed-row").removeClass("cmp-changed-row");
        }

        // ---- variable-length child tables (e.g. Plan banding) -----------
        // Positional field diff only compares rows present on BOTH sides; a row
        // added/removed in one year would slip through. cfg.rowDiffTables is a
        // pane-relative selector (class-based so it survives id-namespacing) for
        // tables whose EXTRA rows should be flagged and counted as differences.
        function rowDiffTablesIn($pane) {
            return cfg.rowDiffTables ? $pane.find(cfg.rowDiffTables).toArray() : [];
        }
        function nearestSectionTitle(el) {
            var $card = $(el).closest(".accordion-item, .card");
            var t = ($card.find(".card-header, .accordion-header").first().text() || "").trim();
            return t || (cfg.defaultSection || "Details");
        }
        function rowSummary(tr) {
            return $(tr).find("td").toArray().map(function (td) {
                var $t = $(td), sp = $t.find(".view-text");
                var v = sp.length ? (sp.text() || "").trim() : "";
                if (!v) { var inp = $t.find("input,select,textarea")[0]; if (inp) v = fieldDisplayValue(inp); }
                if (!v) v = ($t.text() || "").trim();
                return v;
            }).filter(function (v) { return v !== ""; }).join("  ·  ");
        }
        // Pair rowDiff tables left↔right by index; return each row present on one side only.
        function collectTableRowDiffs($shell) {
            if (!cfg.rowDiffTables) return [];
            var lts = rowDiffTablesIn($shell.find(".cmp-left")), rts = rowDiffTablesIn($shell.find(".cmp-right"));
            if (!rts.length) return [];                       // no compare-year table = no record
            var out = [], m = Math.min(lts.length, rts.length);
            for (var i = 0; i < m; i++) {
                var lr = $(lts[i]).find("tbody > tr").toArray(), rr = $(rts[i]).find("tbody > tr").toArray();
                for (var j = rr.length; j < lr.length; j++) out.push({ side: "cur", tr: lr[j], section: nearestSectionTitle(lts[i]) });
                for (var k = lr.length; k < rr.length; k++) out.push({ side: "cmp", tr: rr[k], section: nearestSectionTitle(rts[i]) });
            }
            return out;
        }
        function applyTableRowDiffs($shell) {
            var diffs = collectTableRowDiffs($shell);
            diffs.forEach(function (d) { $(d.tr).addClass("cmp-changed-row").find("td").addClass("cmp-changed-cell"); });
            return diffs.length;
        }

        // ---- summary + count -------------------------------------------
        function computeDiffCount($shell) {
            var left = comparableFields($shell.find(".cmp-left")), n = 0;
            for (var i = 0; i < left.length; i++) {
                if (!left[i].classList.contains("cmp-changed-field")) continue;
                if (pluginSkipField(left[i])) continue;
                n++;
            }
            n += roDiffPairs($shell.find(".cmp-left"), $shell.find(".cmp-right")).length;
            n += collectTableRowDiffs($shell).length;
            plugins.forEach(function (p) { if (p.count) n += (p.count($shell) || 0); });
            return n;
        }
        function updateSummaryCount() {
            var $shell = $c().find(".cmp-shell");
            showSummary({ changed: $shell.length ? computeDiffCount($shell) : 0 }, false);
        }
        function showSummary(result, missing) {
            var count = result ? result.changed : 0, $s = $(".cmp-summary");
            $s.removeClass("cmp-has-changes cmp-no-changes");
            if (missing) $s.addClass("cmp-no-changes").text("No " + state.compareYear + " record");
            else if (count === 0) $s.addClass("cmp-no-changes").text("In sync");
            else $s.addClass("cmp-has-changes").text(count + (count === 1 ? " difference" : " differences"));
            setDiffControls(count, missing);
        }
        function setDiffControls(count, missing) {
            if (missing) { $(".cmp-onlydiff").addClass("d-none"); $("#cmpOnlyDiff").prop("checked", false); $c().find(".cmp-difflist").remove(); return; }
            $(".cmp-onlydiff").toggleClass("d-none", !state.compareYear);
            reapplyOnlyDiff();
        }

        // ---- "Show only differences" list ------------------------------
        function cmpEsc(s) { return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }
        function fieldLabelText(el) {
            if (el.id) { var $lf = $('label[for="' + el.id.replace(/"/g, "") + '"]').first(); var tf = ($lf.text() || "").trim(); if (tf) return tf; }
            var scope = el.closest(".tab-pane, form, .cmp-left, .cmp-pane") || document;
            var labels = scope.querySelectorAll("label, .form-label, h4, h5, h6"), best = null;
            for (var i = 0; i < labels.length; i++) { if (labels[i].compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) best = labels[i]; else break; }
            if (best) { var tb = (best.textContent || "").trim(); if (tb) return tb; }
            return el.getAttribute("placeholder") || el.name || "Field";
        }
        function fieldSectionName(el, $pane) {
            var $tp = $(el).closest(".tab-pane");
            if (!$tp.length) return cfg.defaultSection || "Details";
            var pid = $tp.attr("id");
            if (pid) {
                var $btn = $pane.find('.nav-link[data-bs-toggle="tab"]').filter(function () {
                    var tgt = ($(this).attr("data-bs-target") || $(this).attr("href") || "").replace(/^#/, "");
                    return tgt === pid || $(this).attr("aria-controls") === pid;
                }).first();
                var tt = ($btn.text() || "").trim(); if (tt) return tt;
            }
            return cfg.defaultSection || "Details";
        }
        function buildDiffData($shell) {
            var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
            var left = comparableFields($left), right = comparableFields($right), rows = [];
            for (var i = 0; i < left.length; i++) {
                if (!left[i].classList.contains("cmp-changed-field")) continue;
                if (pluginSkipField(left[i])) continue;
                // Fields inside a repeating/dynamic row (cfg.gridRowSelector) are still
                // highlighted + counted, but a plugin renders them as a side-by-side grid
                // rather than one confusing positional chip row each.
                if (cfg.gridRowSelector && left[i].closest(cfg.gridRowSelector)) continue;
                rows.push({ section: fieldSectionName(left[i], $left), label: fieldLabelText(left[i]), cur: fieldDisplayValue(left[i]), cmp: right[i] ? fieldDisplayValue(right[i]) : "" });
            }
            roDiffPairs($left, $right).forEach(function (p) {
                // Cells marked data-cmp-nolist are still highlighted + counted, but a
                // plugin renders them as a custom block (e.g. the employee code table)
                // instead of one chip row each.
                if (p.lEl && p.lEl.getAttribute && p.lEl.getAttribute("data-cmp-nolist") != null) return;
                rows.push({ section: fieldSectionName(p.lEl, $left), label: p.label, cur: p.cur, cmp: p.cmp });
            });
            collectTableRowDiffs($shell).forEach(function (d) {
                var yr = d.side === "cur" ? state.activeYear : state.compareYear;
                rows.push({ section: d.section, label: "Row only in " + yr, cur: d.side === "cur" ? rowSummary(d.tr) : "", cmp: d.side === "cmp" ? rowSummary(d.tr) : "" });
            });
            var extras = [];
            plugins.forEach(function (p) {
                if (p.diffRows) p.diffRows($shell).forEach(function (r) { rows.push(r); });
                if (p.extraBlocks) p.extraBlocks($shell).forEach(function (b) { extras.push(b); });
            });
            return { rows: rows, extras: extras };
        }
        function sectionIcon(name) {
            var m = cfg.sectionIcons || {};
            return m[name] || "bx-detail";
        }
        function valChip(v, cls) { return v ? '<span class="cmp-chip ' + cls + '">' + cmpEsc(v) + '</span>' : '<span class="cmp-chip cmp-chip-empty">empty</span>'; }
        function renderDiffList(data) {
            var cur = cmpEsc(state.activeYear), cmp = cmpEsc(state.compareYear);
            if (!data.rows.length && !(data.extras && data.extras.length)) {
                return $('<div class="cmp-difflist"><div class="cmp-diff-empty"><i class="bx bx-check-circle"></i><div><strong>In sync</strong><span>No differences between ' + cur + ' and ' + cmp + '.</span></div></div></div>');
            }
            var order = [], groups = {};
            data.rows.forEach(function (r) { if (!groups[r.section]) { groups[r.section] = { rows: [], extras: [] }; order.push(r.section); } groups[r.section].rows.push(r); });
            (data.extras || []).forEach(function (b) { if (!groups[b.section]) { groups[b.section] = { rows: [], extras: [] }; order.push(b.section); } groups[b.section].extras.push(b); });

            var total = computeDiffCount($c().find(".cmp-shell"));
            var h = '<div class="cmp-difflist"><div class="cmp-diff-summary">' +
                '<span class="cmp-diff-count"><span class="cmp-diff-dot"></span>' + total + ' difference' + (total === 1 ? '' : 's') + '</span>' +
                '<span class="cmp-diff-legend"><span class="cmp-lg cmp-lg-cur">' + cur + ' · current</span><span class="cmp-lg cmp-lg-cmp">' + cmp + '</span></span></div>';
            order.forEach(function (sec) {
                var g = groups[sec], count = g.rows.length + g.extras.reduce(function (a, b) { return a + (b.count || 0); }, 0);
                h += '<div class="cmp-diffcard"><div class="cmp-diffcard-head"><i class="bx ' + sectionIcon(sec) + '"></i><span>' + cmpEsc(sec) + '</span><span class="cmp-diffcard-badge">' + count + '</span></div><div class="cmp-diffcard-body">';
                g.rows.forEach(function (r) { h += '<div class="cmp-diffitem"><div class="cmp-diffitem-label">' + cmpEsc(r.label) + '</div><div class="cmp-diffitem-vals">' + valChip(r.cur, "cmp-chip-cur") + valChip(r.cmp, "cmp-chip-cmp") + '</div></div>'; });
                g.extras.forEach(function (b) { h += b.html; });
                h += '</div></div>';
            });
            return $(h + '</div>');
        }
        function applyOnlyDiff(on) {
            var $shell = $c().find(".cmp-shell"); if (!$shell.length) return;
            $c().find(".cmp-difflist").remove();
            if (on) { $shell.after(renderDiffList(buildDiffData($shell))); $shell.addClass("d-none"); }
            else { $shell.removeClass("d-none"); }
        }
        function reapplyOnlyDiff() { if ($("#cmpOnlyDiff").is(":checked")) applyOnlyDiff(true); }

        // ---- enter / exit compare --------------------------------------
        // Command-bar chrome for compare mode: hide the view-mode toggle + Add
        // button, reveal the docked compare controls (diff pill / only-differences /
        // Return) in their place — same behaviour as the Employer screen.
        function setCompareChrome(on) {
            // The "default right" (view toggle + Add) is hidden and the compare
            // controls revealed only when they share the command bar (Plan/Employer).
            // Employee docks into its own quick-bar slot, so it leaves the command
            // bar's default-right alone (cfg.rightSlot points elsewhere).
            if (!cfg.rightSlot || cfg.rightSlot === "#gridToolbarCompareRight") {
                $("#gridToolbarDefaultRight").toggleClass("d-none", on);
                $("#gridToolbarCompareRight").toggleClass("d-none", !on);
            } else {
                $(cfg.rightSlot).toggleClass("d-none", !on);
            }
            $("#cmpToolbarDefaultRight").toggleClass("d-none", on);   // legacy fallback (undocked pages)
        }
        // Dock the compare toolbar + compare-mode controls into their slots. Plan and
        // Employer use the top command bar (center + right); Employee overrides the
        // slots (cfg.centerSlot / cfg.rightSlot) to sit in its quick-filter bar.
        function dockToolbar() {
            var slot = document.querySelector(cfg.centerSlot || "#gridToolbarCenterSlot");
            var bar = document.querySelector(TOOLBAR);
            if (slot && bar && bar.parentElement !== slot) { bar.classList.add("cmp-toolbar-inline"); slot.appendChild(bar); }
            var rslot = document.querySelector(cfg.rightSlot || "#gridToolbarCompareRight");
            var rctl = document.getElementById("cmpToolbarCompareRight");
            if (rslot && rctl && rctl.parentElement !== rslot) { rslot.appendChild(rctl); rctl.classList.remove("d-none"); }
            if (!cfg.idsUrl && !cfg.navFromDom) $(TOOLBAR).find(".cmp-nav").addClass("d-none");   // no record nav (Employee reuses its own)
        }
        function hideNonComparable($root) {
            $root.find(".notes-widget").closest(".card").addClass("cmp-hidden-section");
            (cfg.hideSelectors || []).forEach(function (sel) { $root.find(sel).closest(".card").addClass("cmp-hidden-section"); });
        }
        function removeNonComparable(rootEl) {
            var $r = $(rootEl);
            $r.find(".notes-widget").closest(".card").remove(); $r.find(".notes-widget").remove();
            (cfg.hideSelectors || []).forEach(function (sel) { $r.find(sel).closest(".card").remove(); });
        }
        function attachResizer($shell) {
            var resizer = $shell.find(".cmp-resizer")[0], left = $shell.find(".cmp-left")[0]; if (!resizer || !left) return;
            var dragging = false;
            resizer.addEventListener("pointerdown", function (e) { dragging = true; resizer.classList.add("dragging"); document.body.classList.add("cmp-resizing"); try { resizer.setPointerCapture(e.pointerId); } catch (x) {} });
            resizer.addEventListener("pointermove", function (e) { if (!dragging) return; var rect = $shell[0].getBoundingClientRect(); var pct = Math.max(25, Math.min(75, ((e.clientX - rect.left) / rect.width) * 100)); left.style.flex = "0 0 " + pct + "%"; });
            function end() { dragging = false; resizer.classList.remove("dragging"); document.body.classList.remove("cmp-resizing"); }
            resizer.addEventListener("pointerup", end); resizer.addEventListener("pointercancel", end);
            resizer.addEventListener("dblclick", function () { left.style.flex = "1 1 0"; });
        }
        function exitCompare(reopening) {
            state.compareYear = null; jumpIdx = -1; clearHighlights();
            $t().find(".cmp-summary").removeClass("cmp-has-changes cmp-no-changes").text("");
            $c().find(".cmp-difflist").remove(); $("#cmpOnlyDiff").prop("checked", false);
            var $shell = $c().find(".cmp-shell");
            if ($shell.length) {
                var $left = $shell.find(".cmp-left .cmp-pane-body").children();
                $shell.before($left); $shell.remove();
                $c().find(".cmp-hidden-section").removeClass("cmp-hidden-section");
            }
            if (!reopening) {
                $("#mainListColumn").closest(".row").removeClass("cmp-mode cmp-list-collapsed");
                resetListToggle();
                setCompareChrome(false);
            }
        }
        // Restore the list-header toggle to its default (expanded) icon/title.
        function resetListToggle() {
            $(".cmp-list-toggle").each(function () {
                $(this).find("i").attr("class", "bx bx-chevrons-left");
                $(this).attr("title", "Hide the list");
            });
        }
        function enterCompare(year) {
            state.compareYear = String(year); jumpIdx = -1; setCompareChrome(true);
            $c().find(".cmp-difflist").remove();
            var $detailNodes = $c().children().not(".cmp-shell, .cmp-difflist");
            var $shell = $(
                '<div class="cmp-shell cmp-active">' +
                  '<div class="cmp-pane cmp-left"><div class="cmp-pane-header"><span>' + state.activeYear + ' (current)</span></div><div class="cmp-pane-body"></div></div>' +
                  '<div class="cmp-resizer" role="separator" aria-orientation="vertical" aria-label="Resize panes" title="Drag to resize · double-click to reset"></div>' +
                  '<div class="cmp-pane cmp-right"><div class="cmp-pane-header"><span>' + state.compareYear + '</span><span class="cmp-head-right"><span class="cmp-readonly-tag"><i class="bx bx-lock-alt"></i> read-only</span><button type="button" class="cmp-close" title="Back to current year — close comparison"><i class="bx bx-x"></i></button></span></div>' +
                    '<div class="cmp-pane-body"><div class="text-center p-5"><div class="spinner-border text-primary"></div></div></div></div>' +
                '</div>'
            );
            $c().append($shell);
            $shell.find(".cmp-left .cmp-pane-body").append($detailNodes);
            hideNonComparable($shell.find(".cmp-left"));
            $("#mainListColumn").closest(".row").addClass("cmp-mode");
            attachResizer($shell);

            var $rightBody = $shell.find(".cmp-right .cmp-pane-body");
            var data = {}; data[cfg.compareParam || cfg.detailParam] = state.recordId; data[cfg.compareYearParam || "filingYear"] = state.compareYear;
            $.get(cfg.compareUrl, data)
                .done(function (html) {
                    var tmp = document.createElement("div"); tmp.innerHTML = html;
                    tmp.querySelectorAll("script").forEach(function (s) { s.remove(); });
                    removeNonComparable(tmp);
                    var hasRecord;
                    if (typeof cfg.hasRecord === "function") hasRecord = cfg.hasRecord(tmp);
                    else if (cfg.recordMarkerSelector) { var idEl = tmp.querySelector(cfg.recordMarkerSelector); hasRecord = !!(idEl && ((idEl.value || idEl.textContent || "").trim() !== "")); }
                    else hasRecord = !!tmp.querySelector("*");   // any content = a record; empty = none
                    if (!hasRecord) {
                        $rightBody.html('<div class="cmp-empty"><i class="bx bx-folder-open bx-lg d-block mb-2"></i>No record for ' + state.compareYear + '.</div>');
                        showSummary(null, true); return;
                    }
                    namespaceIds(tmp, "cmp__");
                    $rightBody.empty(); while (tmp.firstChild) $rightBody[0].appendChild(tmp.firstChild);
                    var $right = $shell.find(".cmp-right");
                    $right.find("input, select, textarea").prop("disabled", true);
                    $right.find("button, .btn").not(".nav-link, [data-bs-toggle='tab'], .cmp-close").hide();
                    $right.find("fieldset").prop("disabled", true);
                    $right.find(".nav-link, .nav-item").prop("disabled", false).removeClass("disabled");

                    // Module hook: the compare pane is a static clone with its scripts
                    // stripped, so anything the page normally builds with JS (e.g. an
                    // AJAX-filled dropdown) is re-populated here.
                    if (typeof cfg.afterCompareLoaded === "function") cfg.afterCompareLoaded($shell, $right);

                    // Diff the always-visible section immediately; tabs diff per-tab as they load.
                    runDiff($shell.find(".cmp-left"), $right);
                    highlightRoTab($shell.find(".cmp-left"), $right);
                    applyTableRowDiffs($shell);
                    updateSummaryCount();
                    loadAllTabsAndDiff($shell);
                    var $firstTab = $shell.find(".cmp-left .nav-link[data-bs-toggle='tab']").first();
                    if ($firstTab.length) $firstTab.trigger("click");
                })
                .fail(function () { $rightBody.html('<div class="cmp-empty text-danger">Failed to load ' + state.compareYear + ' record.</div>'); });
        }

        // ---- detail-loaded entry point ---------------------------------
        function onDetailLoaded(recordId, activeYear) {
            var keep = state.compareYear;
            state.recordId = recordId != null ? String(recordId) : null;
            state.activeYear = activeYear != null ? String(activeYear) : (state.activeYear || cfg.activeYear);
            state.compareYear = null;
            if (!state.recordId) { $t().addClass("d-none"); return; }
            $t().removeClass("d-none cmp-table-mode");
            // Rebuild the DOM-based nav list now the rows (and the clicked record) are present,
            // so "N of M" is correct on the very first load, not only after keyboard nav.
            if (cfg.navFromDom) { state.navIds = navIdsFromDom(); }
            fillYearSelect(); highlightRow(state.recordId); updateNavCounter();
            if (state.pendingCompareYear) {
                var pend = state.pendingCompareYear; state.pendingCompareYear = null;
                if (String(pend) !== String(state.activeYear)) { $t().find(".cmp-year-select").val(String(pend)); enterCompare(pend); return; }
            }
            if (keep && String(keep) !== String(state.activeYear)) { $t().find(".cmp-year-select").val(String(keep)); enterCompare(keep); }
            else { $t().find(".cmp-year-select").val(""); $t().find(".cmp-summary").text(""); setCompareChrome(false); }
        }

        // ---- tab sync + per-tab diff -----------------------------------
        function paneTabBtns($pane) { return $pane.find('.nav-link[data-bs-toggle="tab"]'); }
        function tabPaneFor($pane, index) { var btn = paneTabBtns($pane).get(index); if (!btn) return $(); return $pane.find($(btn).attr("data-bs-target")); }
        function tabReady($pane, index) {
            var btn = paneTabBtns($pane).get(index); if (!btn) return true;
            if (!$(btn).data("url")) return true;
            var $holder = tabPaneFor($pane, index).find('[id*="-content"]').first();
            return $holder.length ? $holder.children().length > 0 : true;
        }
        function markTabChanged($pane, index, changed) { var btn = paneTabBtns($pane).get(index); if (btn) $(btn).toggleClass("cmp-tab-changed", changed); }
        function diffActiveTab($left, $right, index) {
            var $lp = tabPaneFor($left, index), $rp = tabPaneFor($right, index); if (!$lp.length || !$rp.length) return;
            $lp.add($rp).find(".cmp-changed-cell").removeClass("cmp-changed-cell");
            $lp.add($rp).find(".cmp-changed-field").removeClass("cmp-changed-field");
            var res = runDiff($lp, $rp), changed = res.changed > 0;
            var $shell = $left.closest(".cmp-shell");
            plugins.forEach(function (p) { if (p.tabChanged) changed = (p.tabChanged($lp, $rp, $shell) > 0) || changed; });
            changed = highlightRoTab($lp, $rp) > 0 || changed;
            applyTableRowDiffs($shell);
            markTabChanged($left, index, changed); markTabChanged($right, index, changed);
            updateSummaryCount(); jumpIdx = -1;
        }
        function loadTabInto($pane, index, cb) {
            function done() { if (cb) cb(); }
            var btn = paneTabBtns($pane).get(index); if (!btn) { done(); return; }
            var $btn = $(btn), url = $btn.data("url"); if (!url) { done(); return; }
            var $holder = tabPaneFor($pane, index).find('[id*="-content"]').first();
            if (!$holder.length || $holder.children().length > 0) { done(); return; }
            $.get(url).done(function (html) {
                var t = document.createElement("div"); t.innerHTML = html;
                t.querySelectorAll("script").forEach(function (s) { s.remove(); });
                t.querySelectorAll(".notes-widget").forEach(function (el) { el.remove(); });
                if ($pane.hasClass("cmp-right")) namespaceIds(t, "cmp__");
                $holder.empty(); while (t.firstChild) $holder[0].appendChild(t.firstChild);
                $holder.data("loaded", true);
                if ($pane.hasClass("cmp-right")) $holder.find("input, select, textarea").prop("disabled", true);
            }).always(done);
        }
        function loadAllTabsAndDiff($shell) {
            var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
            var count = paneTabBtns($left).length, pending = 0;
            function afterEach() { if (--pending > 0) return; for (var k = 0; k < count; k++) diffActiveTab($left, $right, k); updateSummaryCount(); }
            for (var i = 0; i < count; i++) { pending += 2; loadTabInto($left, i, afterEach); loadTabInto($right, i, afterEach); }
            if (pending === 0) { for (var k = 0; k < count; k++) diffActiveTab($left, $right, k); updateSummaryCount(); }
        }

        // ---- keyboard jump between changed fields -----------------------
        var jumpIdx = -1;

        // ---- event wiring (per-instance, namespaced) --------------------
        // Split view is a LAYOUT state, not a row-count. Both the table and the split
        // list partials render the same row markup (e.g. .employee-item / .plan-item), so a
        // row-presence test reads "split" in table view too — which made a compare started
        // from table view build the shell in the still-hidden detail column instead of first
        // switching to split. The GridManager marks the detail column with .d-none in table
        // view (the app's canonical check), so read that; fall back to rows for pages without it.
        function viewIsSplit() {
            var $dc = $(cfg.detailColumn || "#mainDetailColumn");
            if ($dc.length) return !$dc.hasClass("d-none");
            return !!(LIST && ROW && $(LIST + " " + ROW).length > 0);
        }

        // Once the grid has switched to split view and reloaded the list, open the first
        // record so onDetailLoaded consumes state.pendingCompareYear and the comparison
        // appears. A plain view switch doesn't change the filter signature, so the page's
        // own onLoad auto-select doesn't fire here — we drive it. Polls briefly for the
        // reloaded split list; the guard on pendingCompareYear stops it double-selecting
        // if the page auto-selected first.
        function openFirstRecordForPending(tries) {
            if (!state.pendingCompareYear) return;                 // already handled
            if (!viewIsSplit() || !LIST || !ROW) { if (tries < 50) setTimeout(function () { openFirstRecordForPending(tries + 1); }, 60); return; }
            var $first = $(LIST + " " + ROW).first();
            if ($first.length) { $first.trigger("click"); return; }
            if (tries < 50) setTimeout(function () { openFirstRecordForPending(tries + 1); }, 60);
        }

        $(document).on("change", TOOLBAR + " .cmp-year-select", function () {
            var val = $(this).val();
            if (!viewIsSplit()) {
                if (val) { state.pendingCompareYear = val; $('.grid-view-btn[data-mode="Split"]').trigger("click"); openFirstRecordForPending(0); }
                return;
            }
            var go = val && String(val) !== String(state.activeYear);
            exitCompare(go); if (go) enterCompare(val);
        });
        $(document).on("change", "#cmpOnlyDiff", function () { applyOnlyDiff(this.checked); });
        $(document).on("click", "#cmpReturnBtn, .cmp-close", function () { $t().find(".cmp-year-select").val(""); exitCompare(false); });
        $(document).on("click", "#cmpNavPrev", function () { navPrevAction(); });
        $(document).on("click", "#cmpNavNext", function () { navNextAction(); });
        $(document).on("click", ".cmp-list-toggle, .emp-list-rail", function () {
            var $row = $("#mainListColumn").closest(".row");
            var collapsed = $row.toggleClass("cmp-list-collapsed").hasClass("cmp-list-collapsed");
            $(".cmp-list-toggle").each(function () {
                $(this).find("i").attr("class", collapsed ? "bx bx-chevrons-right" : "bx bx-chevrons-left");
                $(this).attr("title", collapsed ? "Show the list" : "Hide the list");
            });
        });
        $(document).on("keydown", function (e) {
            if (e.altKey || e.ctrlKey || e.metaKey) return;
            if (e.key === "Escape") { if (state.compareYear) { e.preventDefault(); $t().find(".cmp-year-select").val(""); exitCompare(false); } return; }
            if (e.key !== "ArrowUp" && e.key !== "ArrowDown") return;
            var el = e.target, tag = (el.tagName || "").toLowerCase();
            if (tag === "textarea" || el.isContentEditable) return;
            if (!state.recordId || !viewIsSplit()) return;
            e.preventDefault(); if (e.key === "ArrowDown") navNextAction(); else navPrevAction();
        });
        // Re-diff a tab whose lazy content just arrived inside this shell.
        $(document).on("shown.bs.tab", '.cmp-shell .nav-link[data-bs-toggle="tab"]', function () {
            var $btn = $(this), $shell = $btn.closest(".cmp-shell"), $pane = $btn.closest(".cmp-pane");
            var index = paneTabBtns($pane).index(this);
            if (tabSyncIndex === index) return;
            tabSyncIndex = index;
            var $left = $shell.find(".cmp-left"), $right = $shell.find(".cmp-right");
            var $other = $pane.hasClass("cmp-left") ? $right : $left, otherBtn = paneTabBtns($other).get(index);
            if (otherBtn) $(otherBtn).trigger("click");
            var tries = 0;
            (function waitDiff() {
                if ((tabReady($left, index) && tabReady($right, index)) || tries++ > 25) { diffActiveTab($left, $right, index); tabSyncIndex = null; }
                else setTimeout(waitDiff, 100);
            })();
        });
        var tabSyncIndex = null;

        // ---- public API + init -----------------------------------------
        var api = {
            onDetailLoaded: onDetailLoaded,
            highlightRow: highlightRow,
            refreshNavList: refreshNavList,
            enterCompare: enterCompare,
            exitCompare: exitCompare,
            onViewChange: function (mode) {
                if (mode === "Table") { if (state.compareYear) exitCompare(false); state.recordId = null; $t().removeClass("d-none").addClass("cmp-table-mode"); fillTablePicker(); }
                else { $t().removeClass("cmp-table-mode"); }
            },
            state: state,
            cfg: cfg
        };
        if (cfg.apiName) window[cfg.apiName] = api;

        // Keep record nav in sync when the list re-renders via the grid
        // (search / filter / sort / page change). cfg.listReloadUrlMatch is a
        // substring of the module's list-partial URL (e.g. "PlanList").
        if (cfg.listReloadUrlMatch) {
            $(document).ajaxComplete(function (evt, xhr, settings) {
                if (!settings || !settings.url || settings.url.indexOf(cfg.listReloadUrlMatch) < 0) return;
                refreshNavList().always(function () { if (state.recordId) highlightRow(state.recordId); });
            });
        }

        $(function () {
            setTimeout(function () {
                dockToolbar();
                if (typeof cfg.dock === "function") cfg.dock();
                refreshNavList();
                if (!viewIsSplit()) { $t().removeClass("d-none").addClass("cmp-table-mode"); fillTablePicker(); }
            }, 0);
        });

        return api;
    }

    window.CompareCore = { create: create };
})();
