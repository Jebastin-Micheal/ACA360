/* =====================================================================
   Employee filing-year comparison — thin config over compare-core.js.
   Employee differs from Plan/Employer in placement only:
     - the compare controls dock into the page's quick-filter bar
       (cfg.centerSlot / cfg.rightSlot), not the grid command bar;
     - it has NO idsUrl, so the core's own Prev/Next stays hidden and the
       page's existing #empNavRow drives record navigation — every detail
       load (click or nav) flows through window.loadEmpDetail, which calls
       EmployeeCompare.onDetailLoaded so the comparison re-runs.
   window.__cmpEmployeeYear is set by the Index view to the current year.
   ===================================================================== */
(function () {
    if (!window.CompareCore) return;

    // ---- shared helpers for the "Differences Only" side-by-side blocks ----------
    function esc(s) { return String(s == null ? "" : s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
    function years($shell) {
        return {
            cur: ($shell.find(".cmp-left .cmp-pane-header span").first().text() || "").trim().split(" ")[0] || "Current",
            cmp: ($shell.find(".cmp-right .cmp-pane-header span").first().text() || "").trim().split(" ")[0] || "Compare"
        };
    }
    // Two side-by-side tables (current | compare), a shared header row, one body row
    // per entry; cells flagged in diffMap[r][c] get the amber highlight.
    function sideBySide($shell, headers, firstCol, lRows, rRows, diffMap) {
        var y = years($shell), maxR = Math.max(lRows.length, rRows.length);
        function tbl(rowsArr, side) {
            var h = '<table class="cmp-code-tbl"><thead><tr><th>' + esc(firstCol) + '</th>';
            headers.forEach(function (hd) { h += '<th>' + esc(hd.label) + '</th>'; });
            h += '</tr></thead><tbody>';
            for (var r = 0; r < maxR; r++) {
                var row = rowsArr[r];
                h += '<tr><td class="cmp-code-mo">' + esc(row ? row.__label : (r + 1)) + '</td>';
                headers.forEach(function (hd, c) {
                    var v = row ? (row[hd.key] || "—") : "—";
                    h += '<td class="' + (diffMap[r] && diffMap[r][c] ? "cmp-code-diff" : "") + '">' + esc(v) + '</td>';
                });
                h += '</tr>';
            }
            return h + '</tbody></table>';
        }
        return '<div class="cmp-code-block"><div class="cmp-code-wrap">' +
            '<div class="cmp-code-pane"><div class="cmp-code-head cmp-code-cur">' + esc(y.cur) + ' · current</div>' + tbl(lRows, "cur") + '</div>' +
            '<div class="cmp-code-pane"><div class="cmp-code-head">' + esc(y.cmp) + '</div>' + tbl(rRows, "cmp") + '</div>' +
            '</div></div>';
    }

    // ---- repeating/dynamic section helpers (Dependents, Status, Payroll, …) ------
    // All share the same markup: a container of .dynamic-row rows, each holding
    // .data-field[data-key] fields. Rows are matched by POSITION within a section.
    function humanize(k) { return String(k || "").replace(/([a-z0-9])([A-Z])/g, "$1 $2").replace(/_/g, " ").replace(/\b\w/g, function (c) { return c.toUpperCase(); }).trim(); }
    function fieldValOf(el) {
        if (!el) return "";
        if (el.tagName === "SELECT") { var o = el.options[el.selectedIndex]; return o ? (o.text || o.value).trim() : ""; }
        return (el.value || "").trim();
    }
    function dynContainers($pane) {
        var seen = [], out = [];
        $pane.find(".dynamic-row").each(function () { var p = this.parentElement; if (seen.indexOf(p) < 0) { seen.push(p); out.push(p); } });
        return out;
    }
    function dynHeaders(cont) {
        var first = cont.querySelector(".dynamic-row"); if (!first) return [];
        var out = [], seen = {};
        first.querySelectorAll(".data-field").forEach(function (f) {
            if ((f.type || "").toLowerCase() === "hidden") return;
            var k = f.getAttribute("data-key"); if (!k || seen[k]) return; seen[k] = 1;
            out.push({ key: k, label: humanize(k) });
        });
        return out;
    }
    function dynFieldIn(row, key) { return row ? row.querySelector('.data-field[data-key="' + key + '"]:not([type="hidden"])') : null; }
    function dynSectionName(cont) {
        var t = ($(cont).closest(".tab-pane, .card, [id^='section_']").find("h6, h5, .section-header").first().text() || "").trim();
        return t.split("\n")[0].trim() || "Records";
    }
    // Single source of truth: walk paired containers/rows/cells; for each differing cell
    // call onDiff(lField, rField). Returns the diff count. Used for count AND highlight so
    // the top total, the section badges and the amber cells always agree.
    function dynamicWalk($l, $r, onDiff) {
        var lC = dynContainers($l), rC = dynContainers($r), n = Math.min(lC.length, rC.length), count = 0;
        for (var i = 0; i < n; i++) {
            var headers = dynHeaders(lC[i]); if (!headers.length) continue;
            var lrows = lC[i].querySelectorAll(".dynamic-row"), rrows = rC[i].querySelectorAll(".dynamic-row");
            var maxR = Math.max(lrows.length, rrows.length);
            for (var r = 0; r < maxR; r++) {
                var lrow = lrows[r], rrow = rrows[r];
                for (var c = 0; c < headers.length; c++) {
                    var lf = dynFieldIn(lrow, headers[c].key), rf = dynFieldIn(rrow, headers[c].key);
                    if ((!lrow || !rrow) || fieldValOf(lf).toLowerCase() !== fieldValOf(rf).toLowerCase()) {
                        count++;
                        if (onDiff) onDiff(lf, rf);
                    }
                }
            }
        }
        return count;
    }
    function dynamicCount($l, $r) { return dynamicWalk($l, $r, null); }
    function dynamicHighlight($l, $r) {
        return dynamicWalk($l, $r, function (lf, rf) {
            if (lf) { lf.classList.add("cmp-changed-field"); var lc = lf.closest("td") || lf.parentElement; if (lc) lc.classList.add("cmp-changed-cell"); }
            if (rf) { rf.classList.add("cmp-changed-field"); var rc = rf.closest("td") || rf.parentElement; if (rc) rc.classList.add("cmp-changed-cell"); }
        });
    }
    // Side-by-side grid blocks for the "Differences Only" list — same diff logic as above.
    function dynamicGridBlocks($shell) {
        function rowsOf(cont, headers) {
            return Array.prototype.map.call(cont.querySelectorAll(".dynamic-row"), function (row, i) {
                var v = { __label: i + 1 };
                headers.forEach(function (hd) { v[hd.key] = fieldValOf(dynFieldIn(row, hd.key)); });
                return v;
            });
        }
        var $l = $shell.find(".cmp-left"), $r = $shell.find(".cmp-right");
        var lC = dynContainers($l), rC = dynContainers($r), out = [], n = Math.min(lC.length, rC.length);
        for (var i = 0; i < n; i++) {
            var headers = dynHeaders(lC[i]); if (!headers.length) continue;
            var lRows = rowsOf(lC[i], headers), rRows = rowsOf(rC[i], headers);
            var maxR = Math.max(lRows.length, rRows.length), diffMap = [], count = 0;
            for (var r = 0; r < maxR; r++) {
                var lr = lRows[r], rr = rRows[r], row = [];
                headers.forEach(function (hd) {
                    var d = (!lr || !rr) || ((lr[hd.key] || "").toLowerCase() !== (rr[hd.key] || "").toLowerCase());
                    row.push(d); if (d) count++;
                });
                diffMap.push(row);
            }
            if (!count) continue;
            out.push({ section: dynSectionName(lC[i]), html: sideBySide($shell, headers, "#", lRows, rRows, diffMap), count: count });
        }
        return out;
    }

    window.EmployeeCompareInstance = CompareCore.create({
        apiName: "EmployeeCompare",
        container: "#commonDetailContainer",
        toolbar: "#cmpDetailToolbar",
        listContainer: "#employeeListContainer",
        rowSelector: ".employee-item",
        rowIdAttr: "data-id",

        detailUrl: "/Employee/GetEmployeeBasicDetails", detailParam: "employeeId",
        compareUrl: "/Employee/GetEmployeeCompareDetails", compareParam: "employeeId", compareYearParam: "filingYear",
        yearsUrl: "/Employee/GetEmployeeYears",
        // no idsUrl → the core's own record nav stays hidden; the page's
        // existing #empNavRow Prev/Next drives navigation instead.

        // Placement: employee sits in the quick-filter bar, not the command bar.
        centerSlot: "#employeeCompareSlot",
        rightSlot: "#employeeCompareRightSlot",
        // Up/Down keys drive the page's own Prev/Next (which loads the record and
        // re-runs the comparison) instead of the core's unused list nav.
        navPrev: function () { $("#btnEmpPrev").trigger("click"); },
        navNext: function () { $("#btnEmpNext").trigger("click"); },

        activeYear: window.__cmpEmployeeYear || null,
        defaultSection: "Employee",
        // Flags / Notes / Audit Logs aren't year-comparable — drop that whole tab card
        // from each pane. The three tabs share one card; the flags pane's closest .card
        // is it. The id suffix is stable regardless of the razor tabPrefix / clone prefix.
        hideSelectors: [".tab-pane[id$='_flags']"],
        // The employer is the same business across years (different per-year id) — never a
        // year-over-year change, so keep it out of the diff. Its <select> is AJAX-filled,
        // so we also re-populate it in the clone below (scripts are stripped from the clone).
        skipFieldIds: ["FilingYear", "PlanYear", "employerDropdown"],
        // Highlight the whole differing MONTH CARD (a month that changed in any line lights up).
        roCellSelector: ".month-card",
        // Fields in a repeating .dynamic-row (Dependents, Status, Payroll, Medical, COBRA,
        // Union) are shown as a side-by-side grid in "Differences Only", not chip rows.
        gridRowSelector: ".dynamic-row",
        afterCompareLoaded: function ($shell, $right) {
            var $left = $shell.find(".cmp-left");

            // --- Employee Codes: make the 12 month cards comparable -----------------
            // The real code values live in hidden inputs (server-rendered in BOTH panes);
            // the visible cards are JS-filled and COLLAPSE to an "ALL months" card when
            // every month matches, so the two years display inconsistently and the
            // read-only diff can't see them. Re-fill each month card from its OWN hidden
            // inputs in both panes (bypassing the collapse) so the years line up and the
            // data-cmp-field cells diff. Runs before the core's read-only diff pass.
            var MONTHS = ["JAN","FEB","MAR","APR","MAY","JUN","JUL","AUG","SEP","OCT","NOV","DEC"];
            [$left, $right].forEach(function ($p) {
                MONTHS.forEach(function (m) {
                    var $card = $p.find('.month-card[data-month="' + m + '"]');
                    if (!$card.length) return;
                    function hid(k) { return ($p.find('input[id$="_' + m + '_' + k + '"]').val() || "").trim(); }
                    function setv(k, txt) { $p.find('[id*="view_' + m + '_' + k + '"]').text(txt); }
                    var lcmp = parseFloat(hid("LCMP"));
                    setv("COC", hid("COC") || "-");
                    setv("LCMP", lcmp > 0 ? "$" + lcmp.toFixed(2) : "");
                    setv("SHC", hid("SHC") || "-");
                    setv("ZIP", hid("ZIP"));
                    $card.removeClass("opacity-50");
                });
            });

            // --- Employer name: mirror the LEFT pane's name into the read-only clone ---
            // Its <select> is AJAX-filled by id, but the compare-year employer has a
            // DIFFERENT per-year id than the dropdown lists, so it can't self-select.
            // Same business (same Tax ID), so mirror what the left already shows.
            var $rsel = $right.find("select[name='EmployerId']");
            if ($rsel.length) {
                (function mirror(tries) {
                    var txt = ($left.find("select[name='EmployerId'] option:selected").first().text() || "").trim();
                    if (txt && txt.toLowerCase().indexOf("select") === -1) {
                        $rsel.empty().append($("<option></option>").text(txt)).prop("disabled", true);
                    } else if (tries < 20) {
                        setTimeout(function () { mirror(tries + 1); }, 150);
                    }
                })(0);
            }
        },
        sectionIcons: {
            "Basic Info": "bx-id-card", "Hire Info": "bx-briefcase", "Status": "bx-user-check",
            "Payroll": "bx-dollar", "Medical": "bx-plus-medical", "Codes": "bx-code-alt",
            "Flags": "bx-flag", "Dependents": "bx-group", "COBRA": "bx-shield", "Union": "bx-been-here"
        },

        // Keep a single highlight source — reuse the page's own row highlighter.
        highlightRow: function (id) {
            if (typeof window.highlightListRow === "function") { window.highlightListRow(id); return; }
            var $rows = $("#employeeListContainer .employee-item");
            $rows.removeClass("cmp-row-active");
            $rows.filter(function () { return String($(this).data("id")) === String(id); }).addClass("cmp-row-active");
        },

        // In "Differences Only", render the month-code diffs AND every repeating/dynamic
        // section (Dependents, Status, Payroll, …) as SIDE-BY-SIDE tables (current | compare,
        // differing cells highlighted) — the same layout as the employer ALE matrix — instead
        // of confusing positional chip rows. The underlying fields are still highlighted +
        // counted by the core; these blocks are just the presentation.
        plugins: [{
            // Repeating-row fields are matched by POSITION within their section, not by
            // the core's whole-pane positional diff (which mis-aligns and mis-counts when
            // the two years have different row counts). Exclude them from the core diff…
            skipField: function (el) { return !!(el.closest && el.closest(".dynamic-row")); },
            // …and count + highlight them here instead (one shared walker, so the top total,
            // the section badges and the amber cells all agree).
            count: function ($shell) { return dynamicCount($shell.find(".cmp-left"), $shell.find(".cmp-right")); },
            tabChanged: function ($lp, $rp) { return dynamicHighlight($lp, $rp); },
            extraBlocks: function ($shell) {
                var blocks = [];

                // Month codes: months as rows, Lines 14–17 as columns.
                var MONTHS = ["JAN","FEB","MAR","APR","MAY","JUN","JUL","AUG","SEP","OCT","NOV","DEC"];
                var LINES = [["14","Line 14"], ["15","Line 15"], ["16","Line 16"], ["17","Line 17"]];
                var $l = $shell.find(".cmp-left"), $r = $shell.find(".cmp-right");
                var headers = LINES.map(function (ln) { return { key: ln[0], label: ln[1] }; });
                var lRows = [], rRows = [], diffMap = [], codeCount = 0;
                MONTHS.forEach(function (m) {
                    var lr = { __label: m }, rr = { __label: m }, drow = [], any = false;
                    LINES.forEach(function (ln) {
                        var lbl = m + " Line " + ln[0];
                        var lv = ($l.find('[data-cmp-field="' + lbl + '"]').first().text() || "").trim();
                        var rv = ($r.find('[data-cmp-field="' + lbl + '"]').first().text() || "").trim();
                        lr[ln[0]] = lv; rr[ln[0]] = rv;
                        var d = lv.toLowerCase() !== rv.toLowerCase();
                        drow.push(d); if (d) { any = true; codeCount++; }
                    });
                    if (any) { lRows.push(lr); rRows.push(rr); diffMap.push(drow); }
                });
                if (lRows.length) blocks.push({ section: "Codes", html: sideBySide($shell, headers, "Month", lRows, rRows, diffMap), count: codeCount });

                // Every repeating/dynamic section.
                dynamicGridBlocks($shell).forEach(function (b) { blocks.push(b); });
                return blocks;
            }
        }]
    });
})();
