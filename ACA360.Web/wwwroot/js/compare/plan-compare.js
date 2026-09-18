/* =====================================================================
   Plan filing-year comparison — thin config over compare-core.js.
   No module-unique plugins: all plan fields are compared generically.
   window.__cmpPlanYear is set by the Index view to the current filing year.
   Plan ids are encrypted; they flow through unchanged (the server decrypts).
   ===================================================================== */
(function () {
    if (!window.CompareCore) return;

    window.PlanCompareInstance = CompareCore.create({
        apiName: "PlanCompare",
        container: "#commonDetailContainer",
        toolbar: "#cmpDetailToolbar",
        listContainer: "#planListContainer",
        rowSelector: ".plan-item",
        rowIdAttr: "data-id",

        detailUrl: "/Plan/PlanFetch", detailParam: "planId",
        compareUrl: "/Plan/GetPlanCompareDetails", compareParam: "planId", compareYearParam: "filingYear",
        yearsUrl: "/Plan/GetPlanYears",
        // Record Prev/Next through the current employer's plans. Plan ids are AES-
        // encrypted with a random IV (non-deterministic), so a server id list can't be
        // matched to the clicked row — build the nav list from the rendered rows instead.
        navFromDom: true,
        listReloadUrlMatch: "PlanList",

        activeYear: window.__cmpPlanYear || null,
        defaultSection: "Plan",
        sectionIcons: { "Plan": "bx-clipboard", "Benefits": "bx-dollar-circle", "BANDING DETAILS": "bx-table" },

        // Notes + Audit Logs are not year-comparable — drop the tab card from each pane.
        hideSelectors: ["#plan_notes", "#plan_auditlogs"],
        // Banding rows are a variable-length child table: diff whole rows, so a band
        // present in one year but not the other is highlighted + listed as a difference.
        rowDiffTables: "table.cmp-rowdiff",

        highlightRow: function (id) {
            var $rows = $("#planListContainer .plan-item");
            $rows.removeClass("cmp-row-active");
            $rows.filter(function () { return String($(this).data("id")) === String(id); }).addClass("cmp-row-active");
        }
    });
})();
