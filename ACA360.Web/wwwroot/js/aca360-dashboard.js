/* =============================================================================
   ACA360 — Dashboard behaviour
   Target: ACA360.Web/wwwroot/js/aca360-dashboard.js

   Charts are drawn as inline SVG here because the design mockup had to be
   self-contained. In the application you may swap the four chart functions for
   Chart.js and keep everything else; the layout, tray and View As code does not
   depend on them.

   Server data arrives on window.ACA360_DASHBOARD (see Dashboard.cshtml). The
   sample arrays below are only used when that object is absent, so the file can
   still be opened on its own while styling.
   ============================================================================= */

(function () {
    "use strict";

    var MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    var S = ["--s1", "--s2", "--s3", "--s4", "--s5"];
    function tok(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }

    /* ------------------------------------------------------------------ data
       Dashboard.cshtml emits window.ACA360_DASHBOARD from the view model. The
       literals below are the fallback used when this file is opened outside the
       application (styling, design review) — they are never reached in the app. */
    var SRV = window.ACA360_DASHBOARD || {};
    var CFG = SRV.config || {};
    /* Named "AI Data Scan" in the UI, but it runs four deterministic SQL checks —
uniformity, offer-code mismatch, age outliers, penalty exposure. Worth knowing
when someone asks what the AI is doing. */
    window.runHealthScan = function () {
        if (!CFG.anomalyUrl) return;
        var modal = document.getElementById("scanModal");
        var body = document.getElementById("scanResultBody");
        if (body) body.innerHTML = '<div class="tray-empty">Scanning…</div>';
        if (window.bootstrap && modal) new bootstrap.Modal(modal).show();

        fetch(CFG.anomalyUrl)
            .then(function (r) { return r.text(); })
            .then(function (html) {
                if (body && window.jQuery) {
                    jQuery(body).html(html);
                } else if (body) {
                    body.innerHTML = html;
                }
            })
            .catch(function () { if (body) body.innerHTML = '<div class="tray-empty">The scan could not be completed.</div>'; });
    };
    /* The server sends MonthlyDashboardRow objects; the charts want parallel
   arrays. Convert once here rather than teaching six chart functions about the
   row shape. Month numbers are matched explicitly so a gap in the data leaves a
   zero rather than shifting every later month left by one. */
    function shapeMonthly(rows) {
        var out = { enrolled: [], waived: [], ft: [], total: [], cobra: [], retiree: [], union: [] };
        var byMonth = {};
        (rows || []).forEach(function (r) {
            var m = r.monthNum != null ? r.monthNum : r.MonthNum;
            if (m) byMonth[m] = r;
        });
        function pick(r, a, b) {
            if (!r) return 0;
            var v = r[a] != null ? r[a] : r[b];
            return v == null ? 0 : v;
        }
        for (var m = 1; m <= 12; m++) {
            var r = byMonth[m];
            out.enrolled.push(pick(r, "enrolled", "Enrolled"));
            out.waived.push(pick(r, "waived", "Waived"));
            out.ft.push(pick(r, "fullTime", "FullTime"));
            out.total.push(pick(r, "totalEmployed", "TotalEmployed"));
            out.cobra.push(pick(r, "cobra", "Cobra"));
            out.retiree.push(pick(r, "retiree", "Retiree"));
            out.union.push(pick(r, "union", "Union"));
        }
        return out;
    }

    var DATA = SRV.monthly && SRV.monthly.length
        ? shapeMonthly(SRV.monthly)
        : { enrolled: [], waived: [], ft: [], total: [], cobra: [], retiree: [], union: [] };

    DATA.coverage = (SRV.coverage && SRV.coverage.length === 12) ? SRV.coverage : [];
    DATA.riskMonths = SRV.riskMonths || [];


    /* ------------------------------------------------- tooltip (shared layer) */
    var tip = document.getElementById("tip");
    function showTip(evt, html) {
        tip.innerHTML = html;
        tip.classList.add("on");
        var pad = 14, r = tip.getBoundingClientRect();
        var x = evt.clientX + pad, y = evt.clientY + pad;
        if (x + r.width > window.innerWidth - 8) x = evt.clientX - r.width - pad;
        if (y + r.height > window.innerHeight - 8) y = evt.clientY - r.height - pad;
        tip.style.left = x + "px"; tip.style.top = y + "px";
    }
    function hideTip() { tip.classList.remove("on"); }

    function svg(tag, attrs) {
        var el = document.createElementNS("http://www.w3.org/2000/svg", tag);
        for (var k in attrs) el.setAttribute(k, attrs[k]);
        return el;
    }
    function swatch(c) { return '<i style="background:' + c + '"></i>'; }
    /* append to aca360-dashboard.js — recompute on demand */
    window.loadPenaltyRisk = function () {
        if (!CFG.riskAnalysisUrl) return;
        var el = document.getElementById("totalRiskDisplay");
        if (el) el.textContent = "…";

        var money = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });

        fetch(CFG.riskAnalysisUrl)
            .then(function (r) { return r.json(); })
            .then(function (d) {
                var total = d.totalEstimatedPenalty || 0;
                if (el) {
                    el.textContent = money.format(total);
                    el.classList.toggle("is-bad", total > 0);
                    el.classList.toggle("is-good", total === 0);
                }
                var badge = document.getElementById("typeABadge");
                if (badge) {
                    badge.className = "pill " + (d.typeA_Triggered ? "pill-crit" : "pill-good");
                    badge.innerHTML = '<span class="dot"></span>' + (d.typeA_Triggered ? "Triggered" : "MEC met (95%)");
                }
                var amt = document.getElementById("typeAAmount");
                if (amt) {
                    amt.textContent = money.format(d.typeA_TotalExposure || 0);
                    amt.classList.toggle("d-none", !d.typeA_Triggered);
                }
                var bc = document.getElementById("typeBCount");
                if (bc) {
                    bc.textContent = d.typeB_TotalViolations || 0;
                    bc.classList.toggle("is-bad", (d.typeB_TotalViolations || 0) > 0);
                }
            })
            .catch(function () { if (el) el.textContent = "Unavailable"; });
    };
    /* --------------------------------------------------------- static tables */
    /* Live countdown. Builds the four tiles once, then each tick animates only the
   units whose value actually changed — otherwise the seconds tile drags the
   day tile through a slide every second. */
    function initLiveClock(elementId, deadlineIso, railId) {
        var host = document.getElementById(elementId);
        if (!host) return null;

        var targetTime = new Date(deadlineIso).getTime();
        var UNITS = [["d", "Days"], ["h", "Hours"], ["m", "Mins"], ["s", "Secs"]];

        host.innerHTML = UNITS.map(function (u, i) {
            return '<div class="fd-tile">' +
                '<div class="fd-cap"></div>' +
                '<div class="fd-num"><div class="cd-slot" data-u="' + u[0] + '"><span class="cd-val">--</span></div></div>' +
                '<div class="fd-lbl">' + u[1] + '</div>' +
                '</div>';
        }).join("");

        var rail = document.getElementById(railId);

        function paintRail() {
            if (!rail || isNaN(targetTime)) return;
            // Window = 1 Jan of the deadline's year through the deadline itself.
            var start = new Date(new Date(targetTime).getFullYear(), 0, 1).getTime();
            var pct = Math.max(0, Math.min(100, ((Date.now() - start) / (targetTime - start)) * 100));
            rail.style.width = pct.toFixed(1) + "%";
        }

        function slide(slot, text) {
            var cur = slot.querySelector(".cd-val:not(.cd-exit)");
            if (cur && cur.textContent === text) return;
            var next = document.createElement("span");
            next.className = "cd-val cd-enter";
            next.textContent = text;
            slot.appendChild(next);
            if (cur) {
                cur.classList.add("cd-exit");
                setTimeout(function () { cur.remove(); }, 500);
            }
            requestAnimationFrame(function () {
                requestAnimationFrame(function () { next.classList.remove("cd-enter"); });
            });
        }

        var clockInterval = null;
        function stop() { if (clockInterval) { clearInterval(clockInterval); clockInterval = null; } }

        function showClosed(msg) {
            host.innerHTML = '<div class="cd-done">' + msg + "</div>";
            if (rail) rail.style.width = "100%";
            stop();
            refreshDatesState();   // a deadline just passed — restate the header
        }

        function tick() {
            if (isNaN(targetTime)) { showClosed("Deadline not set"); return false; }
            var remainder = targetTime - Date.now();
            if (remainder < 0) { showClosed("Filing window closed"); return false; }

            var v = {
                d: Math.floor(remainder / 86400000),
                h: Math.floor((remainder % 86400000) / 3600000),
                m: Math.floor((remainder % 3600000) / 60000),
                s: Math.floor((remainder % 60000) / 1000)
            };
            UNITS.forEach(function (u) {
                var slot = host.querySelector('.cd-slot[data-u="' + u[0] + '"]');
                if (slot) slide(slot, String(v[u[0]]).padStart(2, "0"));
            });
            paintRail();
            return true;
        }

        // Paint immediately so there is no blank first second.
        if (tick()) clockInterval = setInterval(tick, 1000);
        return clockInterval;
    }
    /* The header is painted server-side so there is no flash on load. This keeps it
   honest for a page left open across a deadline — the clock is already ticking,
   so it may as well tell the tile. */
    function refreshDatesState() {
        var tile = document.getElementById("datesTile");
        if (!tile) return;

        var dl = SRV.deadlines || {};
        var now = Date.now();
        var mailPassed = dl.mailing && new Date(dl.mailing).getTime() < now;
        var efilePassed = dl.efiling && new Date(dl.efiling).getTime() < now;

        var bar, pill, label;
        if (mailPassed && efilePassed) { bar = "hb-closed"; pill = "pill-info"; label = "Filing window closed"; }
        else if (mailPassed || efilePassed) { bar = "hb-crit"; pill = "pill-crit"; label = "Deadline passed"; }
        else { bar = "hb-amber"; pill = "pill-warn"; label = "Open"; }

        tile.classList.remove("hb-amber", "hb-crit", "hb-closed", "ac-warn", "ac-danger", "ac-navy");
        tile.classList.add(bar, bar === "hb-crit" ? "ac-danger" : bar === "hb-closed" ? "ac-navy" : "ac-warn");

        var el = document.getElementById("datesStatePill");
        if (el) {
            el.className = "pill " + pill;
            el.innerHTML = '<span class="dot"></span>' + label;
        }
    }
    var mailingTimer = null, efilingTimer = null;

    function renderDeadlines() {
        var dl = SRV.deadlines || {};
        function fmt(iso) {
            var d = new Date(iso);
            return isNaN(d.getTime()) ? "Not set"
                : d.toLocaleDateString("en-US", { day: "2-digit", month: "short", year: "numeric" });
        }
        var m = document.getElementById("txtMailDate");
        var e = document.getElementById("txtEfileDate");
        if (m) m.textContent = fmt(dl.mailing);
        if (e) e.textContent = fmt(dl.efiling);

        if (mailingTimer) clearInterval(mailingTimer);
        if (efilingTimer) clearInterval(efilingTimer);
        mailingTimer = initLiveClock("mailCd", dl.mailing, "mailRail");
        efilingTimer = initLiveClock("efileCd", dl.efiling, "efileRail");
        refreshDatesState();
    }
    function renderCoverage() {
        var box = document.getElementById("covMap");
        if (!box) return;
        if (!DATA.coverage || DATA.coverage.length !== 12) {
            box.innerHTML = '<div class="tray-empty">Coverage has not been calculated for this plan year.</div>';
            return;
        }
        box.innerHTML = DATA.coverage.map(function (v, i) {
            var cls = v >= 100 ? "cov-ok" : v >= 90 ? "cov-part" : "cov-bad";
            var label = v + "%";
            return '<div class="cov-cell ' + cls + '" title="' + MONTHS[i] + '">' +
                '<div class="m">' + MONTHS[i] + '</div><div class="v">' + label + '</div></div>';
        }).join("");
    }

    /* ------------------------------------------------------------ chart: bars */
    // Grouped vertical bars. 2px gap between adjacent fills, 4px rounded tops
    // anchored to the baseline, recessive gridlines, selective end labels.
    /* Series visibility per chart, so a filter survives a redraw (theme change,
   resize, drag). Keyed by the chartId passed in opts. */
    var SERIES_ON = {};

    function grouped(svgEl, series, opts) {
        opts = opts || {};
        var chartId = opts.chartId || svgEl.id;

        if (!SERIES_ON[chartId]) SERIES_ON[chartId] = series.map(function () { return true; });
        var on = SERIES_ON[chartId];
        var shown = series.filter(function (s, i) { return on[i]; });

        if (!shown.length) {
            emptyState(svgEl, opts.legendEl, "All series hidden — use the filter to show one");
            buildSeriesFilter(svgEl, chartId, series, opts);
            return;
        }

        var W = svgEl.clientWidth || svgEl.parentNode.clientWidth || 520;
        var H = +svgEl.getAttribute("height");
        // Extra headroom at the top: the value labels sit above the tallest bar.
        var padL = 42, padR = 8, padB = 22, padT = 20;
        var iw = W - padL - padR, ih = H - padB - padT;

        var max = 0;
        shown.forEach(function (s) {
            s.values.forEach(function (v) { if (v > max) max = v; });
        });
        var mag = Math.pow(10, Math.floor(Math.log10(max || 1)));
        max = Math.ceil((max * 1.15) / (mag / 2)) * (mag / 2) || 10;

        svgEl.setAttribute("viewBox", "0 0 " + W + " " + H);
        svgEl.innerHTML = "";

        [0, .25, .5, .75, 1].forEach(function (f) {
            var y = padT + ih - ih * f;
            svgEl.appendChild(svg("line", { x1: padL, x2: W - padR, y1: y, y2: y, class: "gridline" }));
            var t = svg("text", { x: padL - 6, y: y + 3.5, "text-anchor": "end", class: "axis-lbl" });
            t.textContent = Math.round(max * f).toLocaleString();
            svgEl.appendChild(t);
        });

        var slot = iw / 12, n = shown.length;
        var bw = Math.max(3, (slot - 8) / n - 2);
        // Below ~13px a four-digit label will not fit inside the bar pitch.
        var showLabels = bw >= 13;

        for (var m = 0; m < 12; m++) {
            (function (m) {
                var x0 = padL + slot * m + 4;
                shown.forEach(function (s, si) {
                    var v = s.values[m] || 0;
                    var h = Math.max(v > 0 ? 2 : 0, (v / max) * ih);
                    var x = x0 + si * (bw + 2);
                    var y = padT + ih - h;

                    var rect = svg("rect", { x: x, y: y, width: bw, height: h, rx: 3, fill: s.color });
                    rect.style.cursor = "pointer";
                    rect.addEventListener("mousemove", function (e) {
                        showTip(e, '<div class="tt">' + MONTHS[m] + '</div>' +
                            shown.map(function (ss) {
                                return '<div class="tr"><span>' + swatch(ss.color) + ss.name + '</span><b>' +
                                    (ss.values[m] || 0).toLocaleString() + '</b></div>';
                            }).join(""));
                    });
                    rect.addEventListener("mouseleave", hideTip);
                    svgEl.appendChild(rect);

                    // Counts always on, as on the previous dashboard.
                    if (showLabels) {
                        var lbl = svg("text", { x: x + bw / 2, y: y - 4, "text-anchor": "middle", class: "val-lbl" });
                        lbl.textContent = v.toLocaleString();
                        svgEl.appendChild(lbl);
                    }
                });
                var ml = svg("text", { x: x0 + (slot - 8) / 2, y: H - 6, "text-anchor": "middle", class: "axis-lbl" });
                ml.textContent = MONTHS[m];
                svgEl.appendChild(ml);
            })(m);
        }

        if (opts.legendEl) {
            opts.legendEl.innerHTML = shown.map(function (s) {
                var total = s.values.reduce(function (a, b) { return a + (b || 0); }, 0);
                return '<span>' + swatch(s.color) + s.name +
                    ' <span class="lv">' + total.toLocaleString() + ' total</span></span>';
            }).join("");
        }

        buildSeriesFilter(svgEl, chartId, series, opts);
    }
    /* A checkbox per series in the tile's ⋮ menu. Built once per chart; the state
   lives in SERIES_ON so it survives every redraw. */
    function buildSeriesFilter(svgEl, chartId, series, opts) {
        var tile = svgEl.closest(".tile");
        if (!tile) return;

        var head = tile.querySelector(".tile-head");
        if (!head || head.querySelector(".series-filter")) return;   // already built

        var wrap = document.createElement("div");
        wrap.className = "series-filter dropdown";
        wrap.innerHTML =
            '<button class="icon-btn" type="button" data-bs-toggle="dropdown" aria-expanded="false" aria-label="Filter series">&#8942;</button>' +
            '<ul class="dropdown-menu dropdown-menu-end">' +
            '<li><h6 class="dropdown-header">Show</h6></li>' +
            series.map(function (s, i) {
                return '<li><label class="dropdown-item series-opt">' +
                    '<input type="checkbox" data-i="' + i + '"' + (SERIES_ON[chartId][i] ? " checked" : "") + '> ' +
                    swatch(s.color) + s.name +
                    '</label></li>';
            }).join("") +
            '</ul>';

        wrap.addEventListener("change", function (e) {
            var i = e.target.getAttribute("data-i");
            if (i === null) return;
            SERIES_ON[chartId][+i] = e.target.checked;
            drawAll();
        });
        wrap.addEventListener("click", function (e) {
            if (e.target.closest(".series-opt")) e.stopPropagation();   // keep the menu open
        });

        var spacer = head.querySelector(".th-spacer");
        if (spacer && spacer.nextSibling) head.insertBefore(wrap, spacer.nextSibling);
        else head.appendChild(wrap);
    }

    /* Browser print-to-PDF rather than html2canvas. The charts are SVG, so print
   keeps them vector and the text selectable; the previous canvas-based export
   produced a 480-page, 46 MB file. The @media print rules in the stylesheet
   already drop the edit controls and action buttons. */
    window.downloadDashboardPDF = async function () {
        const element = document.getElementById('dashboard-content');
        if (!element) {
            console.error('Target element #dashboard-content not found.');
            return;
        }

        const elementsToHide = element.querySelectorAll('button, .dropdown, .modal-trigger');

        Swal.fire({
            title: 'Generating PDF...',
            text: 'Capturing dashboard data and charts. Please wait.',
            allowOutsideClick: false,
            didOpen: () => {
                Swal.showLoading();
            }
        });

        try {
            // Hide UI elements during render
            elementsToHide.forEach(el => el.style.visibility = 'hidden');

            // Scroll to top to prevent canvas cut-offs
            window.scrollTo(0, 0);

            // Capture element as canvas
            const canvas = await html2canvas(element, {
                scale: 2,
                useCORS: true,
                backgroundColor: '#ffffff',
                windowWidth: element.scrollWidth,
                windowHeight: element.scrollHeight,
                scrollY: 0,
                onclone: function (clonedDoc) {
                    // FIX: Overwrite unsupported CSS color functions for html2canvas compatibility
                    const allElements = clonedDoc.querySelectorAll('*');
                    allElements.forEach(el => {
                        const style = window.getComputedStyle(el);
                        const propsToCheck = ['color', 'background-color', 'border-color', 'fill', 'stroke', 'text-decoration-color'];

                        propsToCheck.forEach(prop => {
                            const val = style.getPropertyValue(prop);
                            if (val && (val.includes('color(') || val.includes('color-mix('))) {
                                el.style.setProperty(prop, 'rgba(0, 0, 0, 1)', 'important');
                            }
                        });
                    });
                }
            });

            // Initialize jsPDF
            const { jsPDF } = window.jspdf;
            const pdf = new jsPDF('p', 'mm', 'a4');

            const pdfWidth = pdf.internal.pageSize.getWidth();
            const pdfHeight = (canvas.height * pdfWidth) / canvas.width;

            let heightLeft = pdfHeight;
            let position = 0;
            const imgData = canvas.toDataURL('image/png');

            pdf.addImage(imgData, 'PNG', 0, position, pdfWidth, pdfHeight);
            heightLeft -= 297; // A4 page height in mm

            while (heightLeft > 0) {
                position = heightLeft - pdfHeight;
                pdf.addPage();
                pdf.addImage(imgData, 'PNG', 0, position, pdfWidth, pdfHeight);
                heightLeft -= 297;
            }

            // Get employer name dynamically from DOM data-attribute
            const pdfBtn = document.getElementById('exportPdfBtn');
            const rawEmployerName = pdfBtn ? (pdfBtn.getAttribute('data-employer-name') || 'Employer') : 'Employer';

            // Clean non-alphanumeric characters in JavaScript
            const employerName = rawEmployerName.replace(/[^a-zA-Z0-9]/g, '_');
            const year = $('#hdnReportingYear').val() || '';
            const fileName = `Dashboard_${employerName}_${year}.pdf`;

            pdf.save(fileName);

            Swal.close();
            Swal.fire({
                icon: 'success',
                title: 'Success!',
                text: 'Dashboard PDF downloaded successfully.',
                timer: 2000,
                showConfirmButton: false
            });

        } catch (error) {
            console.error('PDF Generation failed:', error);
            Swal.fire({
                icon: 'error',
                title: 'Failed',
                text: 'Could not generate the PDF. Please try again.'
            });
        } finally {
            // Always restore hidden elements regardless of success or failure
            elementsToHide.forEach(el => el.style.visibility = 'visible');
        }
    };
    /* -------------------------------------------------- chart: stacked single */
    function stackedBar(svgEl, parts, legendEl) {
        var W = svgEl.clientWidth || svgEl.parentNode.clientWidth || 520;
        var H = +svgEl.getAttribute("height");
        var total = parts.reduce(function (a, p) { return a + p.value; }, 0);
        var barY = 22, barH = 42, padX = 2;
        var iw = W - padX * 2;

        svgEl.setAttribute("viewBox", "0 0 " + W + " " + H);
        svgEl.innerHTML = "";

        var x = padX;
        parts.forEach(function (p, i) {
            var w = (p.value / total) * iw - (i < parts.length - 1 ? 2 : 0); // 2px surface gap
            var g = svg("g", {});
            var r = svg("rect", {
                x: x, y: barY, width: Math.max(w, 1), height: barH,
                fill: p.color,
                rx: (i === 0 || i === parts.length - 1) ? 4 : 0
            });
            r.style.cursor = "pointer";
            r.addEventListener("mousemove", function (e) {
                showTip(e, '<div class="tt">' + p.name + '</div><div class="tr"><span>Employees</span><b>' +
                    p.value + '</b></div><div class="tr"><span>Share</span><b>' +
                    Math.round(p.value / total * 100) + '%</b></div>');
            });
            r.addEventListener("mouseleave", hideTip);
            g.appendChild(r);

            // direct label — the relief rule for series below 3:1 on the light surface
            if (w > 46) {
                var t = svg("text", { x: x + w / 2, y: barY + barH / 2 + 4, "text-anchor": "middle", class: "val-lbl" });
                t.setAttribute("fill", "#fff");
                t.textContent = p.value;
                g.appendChild(t);
            }
            var cap = svg("text", { x: x, y: barY - 7, class: "axis-lbl" });
            cap.textContent = p.short;
            g.appendChild(cap);

            svgEl.appendChild(g);
            x += (p.value / total) * iw;
        });

        var base = svg("text", { x: padX, y: H - 6, class: "axis-lbl" });
        base.textContent = total + " active employees · " + parts.length + " segments";
        svgEl.appendChild(base);

        legendEl.innerHTML = parts.map(function (p) {
            return '<span>' + swatch(p.color) + p.name + ' <span class="lv">' + p.value + '</span></span>';
        }).join("");
    }

    /* --------------------------------------------------------- chart: donut */
    function donut(svgEl, pct) {
        var W = svgEl.clientWidth || svgEl.parentNode.clientWidth || 240;
        var H = +svgEl.getAttribute("height");
        var cx = W / 2, cy = H / 2 - 4, r = Math.min(W, H) / 2 - 26, sw = 17;
        var C = 2 * Math.PI * r;

        svgEl.setAttribute("viewBox", "0 0 " + W + " " + H);
        svgEl.innerHTML = "";
        svgEl.appendChild(svg("circle", { cx: cx, cy: cy, r: r, fill: "none", stroke: tok("--grid"), "stroke-width": sw }));
        var arc = svg("circle", {
            cx: cx, cy: cy, r: r, fill: "none", stroke: tok("--s1"), "stroke-width": sw,
            "stroke-dasharray": (C * pct / 100) + " " + C,
            "stroke-linecap": "round",
            transform: "rotate(-90 " + cx + " " + cy + ")"
        });
        svgEl.appendChild(arc);

        var v = svg("text", { x: cx, y: cy + 2, "text-anchor": "middle" });
        v.setAttribute("style", "font-size:27px;font-weight:800;letter-spacing:-.03em");
        v.setAttribute("fill", tok("--ink"));
        v.textContent = pct + "%";
        svgEl.appendChild(v);

        var s = svg("text", { x: cx, y: cy + 20, "text-anchor": "middle", class: "axis-lbl" });
        s.textContent = "complete";
        svgEl.appendChild(s);

        var f = svg("text", { x: cx, y: H - 4, "text-anchor": "middle", class: "axis-lbl" });
        f.textContent = "Step 8 of 13 · " + (100 - pct) + "% remaining";
        svgEl.appendChild(f);
    }

    /* ------------------------------------------------------- chart: ALE line */
    // One axis only. Headcount is the series; the ALE threshold is a reference
    // rule, and penalty-risk months are marked with a status color plus a label —
    // never color alone.
    function aleLine(svgEl, legendEl) {
        var W = svgEl.clientWidth || svgEl.parentNode.clientWidth || 520;
        var H = +svgEl.getAttribute("height");
        var padL = 38, padR = 10, padT = 12, padB = 22;
        var iw = W - padL - padR, ih = H - padT - padB;
        var vals = DATA.ft || [];
        var THRESH = 50;

        // Scale to the data, not to a fixed ceiling. A hardcoded max silently draws
        // above the plot area once headcount exceeds it, and with the SVG unclipped
        // that lands on top of the chart above.
        var peak = Math.max(THRESH, Math.max.apply(null, vals.concat([0])));
        function niceMax(v) {
            if (v <= 0) return 10;
            var mag = Math.pow(10, Math.floor(Math.log10(v)));
            var step = mag / 2;
            return Math.ceil((v * 1.12) / step) * step;
        }
        var max = niceMax(peak), min = 0;

        svgEl.setAttribute("viewBox", "0 0 " + W + " " + H);
        svgEl.innerHTML = "";

        function X(i) { return padL + (iw / 11) * i; }
        function Y(v) { return padT + ih - ((v - min) / (max - min)) * ih; }

        // Five evenly spaced ticks derived from the scale.
        [0, .25, .5, .75, 1].forEach(function (f) {
            var g = Math.round(max * f);
            svgEl.appendChild(svg("line", { x1: padL, x2: W - padR, y1: Y(g), y2: Y(g), class: "gridline" }));
            var t = svg("text", { x: padL - 6, y: Y(g) + 3.5, "text-anchor": "end", class: "axis-lbl" });
            t.textContent = g;
            svgEl.appendChild(t);
        });

        // Draw the ALE threshold only when it is inside the plotted range.
        if (THRESH > min && THRESH < max) {
            svgEl.appendChild(svg("line", {
                x1: padL, x2: W - padR, y1: Y(THRESH), y2: Y(THRESH),
                stroke: tok("--ink-3"), "stroke-width": 1.5, "stroke-dasharray": "5 4"
            }));
            var thl = svg("text", { x: W - padR, y: Y(THRESH) - 6, "text-anchor": "end", class: "axis-lbl" });
            thl.textContent = "ALE threshold — 50 FT";
            svgEl.appendChild(thl);
        }

        var d = vals.map(function (v, i) { return (i ? "L" : "M") + X(i) + " " + Y(v); }).join(" ");
        var area = d + " L" + X(11) + " " + Y(0) + " L" + X(0) + " " + Y(0) + " Z";
        svgEl.appendChild(svg("path", { d: area, fill: tok("--s1"), opacity: ".10" }));
        svgEl.appendChild(svg("path", { d: d, fill: "none", stroke: tok("--s1"), "stroke-width": 2, "stroke-linejoin": "round" }));

        vals.forEach(function (v, i) {
            var risk = DATA.riskMonths.indexOf(i) >= 0;
            var c = svg("circle", {
                cx: X(i), cy: Y(v), r: risk ? 5.5 : 4,
                fill: risk ? tok("--warn") : tok("--s1"),
                stroke: tok("--card"), "stroke-width": 2
            });
            c.style.cursor = "pointer";
            c.addEventListener("mousemove", function (e) {
                showTip(e, '<div class="tt">' + MONTHS[i] + ' 2026</div>' +
                    '<div class="tr"><span>Full-time</span><b>' + v + '</b></div>' +
                    '<div class="tr"><span>Total</span><b>' + DATA.total[i] + '</b></div>' +
                    (risk ? '<div class="tr" style="color:#ffd79a"><span>&sect;4980H(b) risk</span><b>Yes</b></div>' : ''));
            });
            c.addEventListener("mouseleave", hideTip);
            svgEl.appendChild(c);

            if (risk) {
                var rl = svg("text", { x: X(i), y: Y(v) - 11, "text-anchor": "middle", class: "val-lbl" });
                rl.setAttribute("fill", tok("--warn"));
                rl.textContent = "risk";
                svgEl.appendChild(rl);
            }
            if (i % 2 === 0) {
                var t = svg("text", { x: X(i), y: H - 6, "text-anchor": "middle", class: "axis-lbl" });
                t.textContent = MONTHS[i];
                svgEl.appendChild(t);
            }
        });

        var lastVal = vals.length ? vals[vals.length - 1] : 0;
        var riskNames = (DATA.riskMonths || []).map(function (i) { return MONTHS[i]; });

        legendEl.innerHTML =
            '<span>' + swatch(tok("--s1")) + 'Full-time headcount <span class="lv">' + lastVal + ' in Dec</span></span>' +
            (riskNames.length
                ? '<span>' + swatch(tok("--warn")) + 'Month with &sect;4980H(b) risk <span class="lv">' + riskNames.join(", ") + '</span></span>'
                : '');
    }

    /* ----------------------------------------------------------- draw it all */
    function hasData(arr) { return arr && arr.length && arr.some(function (v) { return v > 0; }); }

    /* An empty chart box tells the reader nothing about why it is empty. Say it. */
    function emptyState(svgEl, legendEl, msg) {
        var W = svgEl.clientWidth || svgEl.parentNode.clientWidth || 400;
        var H = +svgEl.getAttribute("height");
        svgEl.setAttribute("viewBox", "0 0 " + W + " " + H);
        svgEl.innerHTML = "";
        var t = svg("text", { x: W / 2, y: H / 2, "text-anchor": "middle", class: "axis-lbl" });
        t.textContent = msg || "No data for this plan year";
        svgEl.appendChild(t);
        if (legendEl) legendEl.innerHTML = "";
    }

    function drawAll() {
        var wf = SRV.workforce || {};
        var comp = [
            { name: "Full-time", short: "Full-time", value: wf.fullTime || 0, color: tok("--s1") },
            { name: "Part-time", short: "Part-time", value: wf.partTime || 0, color: tok("--s2") },
            { name: "Variable hours", short: "Variable", value: wf.variable || 0, color: tok("--s3") },
            { name: "COBRA", short: "COBRA", value: wf.cobra || 0, color: tok("--s4") }
        ].filter(function (p) { return p.value > 0; });

        if (comp.length)
            stackedBar(document.getElementById("chWorkforce"), comp, document.getElementById("lgWorkforce"));
        else
            emptyState(document.getElementById("chWorkforce"), document.getElementById("lgWorkforce"), "No workforce breakdown available");

        donut(document.getElementById("chDonut"), SRV.processPercent || 0);

        if (hasData(DATA.enrolled) || hasData(DATA.waived))
            grouped(document.getElementById("chEnrWaived"), [
                { name: "Enrolled coverage", values: DATA.enrolled, color: tok("--ch-enrolled") },
                { name: "Waived coverage", values: DATA.waived, color: tok("--ch-waived") }
            ], { legendEl: document.getElementById("lgEnrWaived"), chartId: "enrWaived" });
        else
            emptyState(document.getElementById("chEnrWaived"), document.getElementById("lgEnrWaived"));

        if (hasData(DATA.ft) || hasData(DATA.total))
            grouped(document.getElementById("chFtTotal"), [
                { name: "Full-time employees", values: DATA.ft, color: tok("--ch-ft") },
                { name: "Total employed workforce", values: DATA.total, color: tok("--ch-total") }
            ], { legendEl: document.getElementById("lgFtTotal"), chartId: "ftTotal" });
        else
            emptyState(document.getElementById("chFtTotal"), document.getElementById("lgFtTotal"));

        if (hasData(DATA.cobra) || hasData(DATA.retiree) || hasData(DATA.union))
            grouped(document.getElementById("chCru"), [
                { name: "COBRA", values: DATA.cobra, color: tok("--ch-cobra") },
                { name: "Retiree", values: DATA.retiree, color: tok("--ch-retiree") },
                { name: "Union", values: DATA.union, color: tok("--ch-union") }
            ], { legendEl: document.getElementById("lgCru"), chartId: "cru" });
        else
            emptyState(document.getElementById("chCru"), document.getElementById("lgCru"), "No COBRA, retiree or union participants");

        if (SRV.aleTrend && SRV.aleTrend.length)
            aleLine(document.getElementById("chAle"), document.getElementById("lgAle"));
        else
            emptyState(document.getElementById("chAle"), document.getElementById("lgAle"));
    }

    /* --------------------------------------------------- customize / layout */
    // Two independent grids so the portfolio band and the employer panel keep
    // separate arrangements. In the real build each saves under its own
    // DashboardKey rather than sharing one row per user.
    var GRIDS = [
        { grid: "gridPortfolio", tray: "trayPortfolio" },
        { grid: "gridWorkforce", tray: "trayEmployer" },
        { grid: "gridStatus", tray: "trayEmployer" },
        { grid: "gridTrends", tray: "trayEmployer" },
        { grid: "gridDetail", tray: "trayEmployer" }
    ];
    var LABELS = {};
    var snapshot = null;
    var defaultLayout = null;

    function eachTile(fn) {
        GRIDS.forEach(function (g) {
            var el = document.getElementById(g.grid);
            if (!el) return;
            Array.prototype.forEach.call(el.children, function (t) { fn(t, g); });
        });
    }

    function buildTools() {
        eachTile(function (t) {
            LABELS[t.dataset.w] = t.querySelector("h4").textContent;
            var tools = t.querySelector(".tile-tools");
            if (!tools) return;
            tools.innerHTML =
                '<button class="icon-btn" title="Drag to reorder" aria-label="Drag to reorder">&#8942;&#8942;</button>' +
                '<button class="icon-btn js-hide" title="Hide this tile" aria-label="Hide this tile">&times;</button>';
            tools.querySelector(".js-hide").addEventListener("click", function (e) {
                e.stopPropagation();
                t.classList.add("hidden-widget");
                refreshTrays();
            });
            t.setAttribute("draggable", "false");
        });
    }

    function refreshTrays() {
        var byTray = {};
        GRIDS.forEach(function (g) { byTray[g.tray] = byTray[g.tray] || []; });
        eachTile(function (t, g) {
            if (t.classList.contains("hidden-widget")) byTray[g.tray].push(t);
        });
        Object.keys(byTray).forEach(function (trayId) {
            var box = document.getElementById(trayId).querySelector(".tray-items");
            var items = byTray[trayId];
            if (!items.length) { box.innerHTML = '<span class="tray-empty">Nothing hidden.</span>'; return; }
            box.innerHTML = "";
            items.forEach(function (t) {
                var b = document.createElement("button");
                b.className = "tray-chip";
                b.textContent = "+ " + LABELS[t.dataset.w];
                b.addEventListener("click", function () { t.classList.remove("hidden-widget"); refreshTrays(); });
                box.appendChild(b);
            });
        });
    }

    var dragged = null;
    function wireDrag() {
        eachTile(function (t) {
            t.addEventListener("dragstart", function (e) {
                dragged = t; t.classList.add("dragging");
                e.dataTransfer.effectAllowed = "move";
                try { e.dataTransfer.setData("text/plain", t.dataset.w); } catch (err) { }
            });
            t.addEventListener("dragend", function () {
                t.classList.remove("dragging");
                eachTile(function (x) { x.classList.remove("drop-target"); });
                dragged = null;
            });
            t.addEventListener("dragover", function (e) {
                if (!dragged || dragged === t || dragged.parentNode !== t.parentNode) return;
                e.preventDefault(); t.classList.add("drop-target");
            });
            t.addEventListener("dragleave", function () { t.classList.remove("drop-target"); });
            t.addEventListener("drop", function (e) {
                if (!dragged || dragged.parentNode !== t.parentNode) return;
                e.preventDefault(); t.classList.remove("drop-target");
                var kids = Array.prototype.slice.call(t.parentNode.children);
                if (kids.indexOf(dragged) < kids.indexOf(t)) t.parentNode.insertBefore(dragged, t.nextSibling);
                else t.parentNode.insertBefore(dragged, t);
                drawAll();
            });
        });
    }
    function captureLayout() {
        return GRIDS.map(function (g) {
            var el = document.getElementById(g.grid);
            if (!el) return [];
            return Array.prototype.map.call(el.children, function (t) {
                return { w: t.dataset.w, hidden: t.classList.contains("hidden-widget") };
            });
        });
    }
    function applyLayoutSnapshot(snap) {
        if (!snap) return;
        GRIDS.forEach(function (g, gi) {
            var grid = document.getElementById(g.grid);
            if (!grid) return;
            (snap[gi] || []).forEach(function (rec) {
                var el = grid.querySelector('[data-w="' + rec.w + '"]');
                if (!el) return;
                grid.appendChild(el);
                el.classList.toggle("hidden-widget", rec.hidden);
            });
        });
    }
    function takeSnapshot() {
        snapshot = GRIDS.map(function (g) {
            var el = document.getElementById(g.grid);
            if (!el) return [];
            return Array.prototype.map.call(el.children, function (t) {
                return { w: t.dataset.w, hidden: t.classList.contains("hidden-widget") };
            });
        });
    }
    function restoreSnapshot() {
        if (!snapshot) return;
        GRIDS.forEach(function (g, gi) {
            var grid = document.getElementById(g.grid);
            snapshot[gi].forEach(function (rec) {
                var el = grid.querySelector('[data-w="' + rec.w + '"]');
                if (!el) return;
                grid.appendChild(el);
                el.classList.toggle("hidden-widget", rec.hidden);
            });
        });
    }

    var btnEdit = document.getElementById("btnEdit"),
        btnSave = document.getElementById("btnSave"),
        btnCancel = document.getElementById("btnCancel"),
        btnReset = document.getElementById("btnReset");

    function setEditing(on) {
        document.body.classList.toggle("editing", on);
        btnEdit.hidden = on; btnSave.hidden = !on; btnCancel.hidden = !on; btnReset.hidden = !on;
        eachTile(function (t) { t.setAttribute("draggable", on ? "true" : "false"); });
        if (on) refreshTrays();
        drawAll();
    }

    /* ------------------------------------------------- layout persistence */
    // Saved per user PER DASHBOARD. UserDashboardLayout gained a DashboardKey in
    // Pack 3 Part 1 precisely so the Broker and Admin dashboards do not overwrite
    // this one. localStorage is only a paint-time cache; the server row is truth.
    var LS_KEY = "aca360_layout_" + (CFG.dashboardKey || "employer");

    function currentLayout() {
        var order = [], hidden = [];
        eachTile(function (t) {
            order.push(t.dataset.w);
            if (t.classList.contains("hidden-widget")) hidden.push(t.dataset.w);
        });
        return { order: order, hidden: hidden };
    }

    function applyLayout(state) {
        if (!state || !state.order) return;
        var hidden = state.hidden || [];
        GRIDS.forEach(function (g) {
            var grid = document.getElementById(g.grid);
            state.order.forEach(function (key) {
                var el = grid.querySelector('[data-w="' + key + '"]');
                if (el) grid.appendChild(el);
            });
        });
        eachTile(function (t) {
            t.classList.toggle("hidden-widget", hidden.indexOf(t.dataset.w) >= 0);
        });
    }

    function saveLayout() {
        var state = currentLayout();
        try { localStorage.setItem(LS_KEY, JSON.stringify(state)); } catch (e) { }

        if (!CFG.saveLayoutUrl) return;
        var body = new URLSearchParams();
        body.append("order", JSON.stringify(state.order));
        body.append("hidden", JSON.stringify(state.hidden));
        body.append("dashboardKey", CFG.dashboardKey || "employer");

        fetch(CFG.saveLayoutUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                "RequestVerificationToken": CFG.antiForgeryToken || ""
            },
            body: body.toString()
        }).catch(function () { /* the local cache already holds it */ });
    }

    function loadLayout() {
        try {
            var cached = JSON.parse(localStorage.getItem(LS_KEY) || "null");
            if (cached) applyLayout(cached);
        } catch (e) { }

        if (!CFG.getLayoutUrl) return;
        fetch(CFG.getLayoutUrl + "?dashboardKey=" + encodeURIComponent(CFG.dashboardKey || "employer"))
            .then(function (r) { return r.json(); })
            .then(function (res) {
                if (!res || !res.success || !res.order || !res.order.length) return;
                applyLayout({ order: res.order, hidden: res.hidden || [] });
                try { localStorage.setItem(LS_KEY, JSON.stringify({ order: res.order, hidden: res.hidden || [] })); } catch (e) { }
                refreshTrays(); drawAll();
            })
            .catch(function () { });
    }

    btnEdit.addEventListener("click", function () { takeSnapshot(); setEditing(true); });
    btnSave.addEventListener("click", function () { saveLayout(); setEditing(false); });
    btnCancel.addEventListener("click", function () { restoreSnapshot(); setEditing(false); });
    //btnReset.addEventListener("click", function () {
    //    eachTile(function (t) { t.classList.remove("hidden-widget"); });
    //    refreshTrays(); drawAll();
    //});

    btnReset.addEventListener("click", function () {
        applyLayoutSnapshot(defaultLayout);
        refreshTrays(); drawAll();
    });

    /* ------------------------------------------------------- View As
       The picker sends a mode and a target id and nothing else. The server derives
       the effective role and scope from that target and re-checks entitlement — a
       value forged here buys nothing. The page reloads on success because every
       widget's scope changes, and a partial refresh would leave stale numbers on
       screen under a banner claiming a different identity. */
    var viewAs = document.getElementById("viewAs");
    var impExit = document.getElementById("impExit");

    function postViewAs(url, params) {
        var body = new URLSearchParams();
        Object.keys(params || {}).forEach(function (k) { body.append(k, params[k]); });
        return fetch(url, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                "RequestVerificationToken": CFG.antiForgeryToken || ""
            },
            body: body.toString()
        }).then(function (r) { return r.json().catch(function () { return { success: false }; }); });
    }

    if (viewAs) {
        viewAs.addEventListener("change", function () {
            var v = viewAs.value;            // "" | "Broker:12" | "Employer:840"
            if (!v) { return endViewAs(); }

            var parts = v.split(":");
            viewAs.disabled = true;
            postViewAs(CFG.beginViewAsUrl, { mode: parts[0], targetId: parts[1] })
                .then(function (res) {
                    if (res && res.success) { window.location.reload(); return; }
                    viewAs.disabled = false;
                    viewAs.value = "";
                    alert((res && res.message) || "That account is not available to you.");
                })
                .catch(function () {
                    viewAs.disabled = false;
                    viewAs.value = "";
                });
        });
    }

    function endViewAs() {
        var body = new URLSearchParams();
        body.append("returnUrl", window.location.pathname + window.location.search);
        return fetch(CFG.endViewAsUrl, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded",
                "RequestVerificationToken": CFG.antiForgeryToken || ""
            },
            body: body.toString()
        }).then(function () { window.location.reload(); });
    }
    if (impExit) impExit.addEventListener("click", endViewAs);

    /* ---------------------------------------------------------------- init */
    renderDeadlines(); renderCoverage();
    Array.prototype.forEach.call(document.querySelectorAll('[data-w="process-table"]'), initTableTools);
    buildTools(); wireDrag();
    defaultLayout = captureLayout();
    loadLayout();
    refreshTrays();
    drawAll();

    /* Search, status filter and click-to-sort for any table.tbl-interactive.
   Deliberately not DataTables: it injects its own wrapper, pager and Bootstrap
   styling, which fights the tile's header bar for a six-column table. */
    function initTableTools(root) {
        var table = root.querySelector("table.tbl-interactive");
        if (!table) return;

        var tbody = table.querySelector("tbody");
        var rows = Array.prototype.slice.call(tbody.querySelectorAll("tr"));
        var search = root.querySelector(".tbl-search");
        var filter = root.querySelector(".tbl-filter");
        var foot = root.querySelector(".tbl-foot");
        var total = rows.length;

        // Controls live inside a tile that becomes draggable in edit mode; without
        // this a click on the input starts a drag instead of focusing it.
        Array.prototype.forEach.call(root.querySelectorAll(".tbl-tools *"), function (el) {
            el.addEventListener("mousedown", function (e) { e.stopPropagation(); });
        });

        function cellText(row, i) {
            var td = row.cells[i];
            return td ? (td.textContent || "").trim() : "";
        }

        function sortKey(row, i, type) {
            var td = row.cells[i];
            if (!td) return type === "num" ? 0 : "";
            var raw = td.getAttribute("data-sort");
            var val = raw !== null ? raw : (td.textContent || "").trim();
            return type === "num" ? (parseFloat(val) || 0) : val.toLowerCase();
        }

        function apply() {
            var q = (search ? search.value : "").trim().toLowerCase();
            var status = filter ? filter.value : "";
            var col = filter ? parseInt(filter.getAttribute("data-filter-col"), 10) : -1;
            var shown = 0;

            rows.forEach(function (row) {
                var hit = !q || (row.textContent || "").toLowerCase().indexOf(q) >= 0;
                if (hit && status && col >= 0) {
                    hit = cellText(row, col).toLowerCase().indexOf(status.toLowerCase()) >= 0;
                }
                row.classList.toggle("row-hidden", !hit);
                if (hit) shown++;
            });

            if (foot) {
                foot.textContent = shown === total
                    ? total + (total === 1 ? " employer" : " employers")
                    : "Showing " + shown + " of " + total;
            }

            var empty = tbody.querySelector("tr.tbl-empty-row");
            if (!shown && !empty) {
                var tr = document.createElement("tr");
                tr.className = "tbl-empty-row";
                tr.innerHTML = '<td class="tbl-empty" colspan="' + table.tHead.rows[0].cells.length + '">No employers match that search.</td>';
                tbody.appendChild(tr);
            } else if (shown && empty) {
                empty.remove();
            }
        }

        Array.prototype.forEach.call(table.tHead.rows[0].cells, function (th, i) {
            var type = th.getAttribute("data-sort");
            if (!type) return;

            th.addEventListener("click", function () {
                var asc = !th.classList.contains("sort-asc");

                Array.prototype.forEach.call(table.tHead.rows[0].cells, function (o) {
                    o.classList.remove("sort-asc", "sort-desc");
                });
                th.classList.add(asc ? "sort-asc" : "sort-desc");

                rows.sort(function (a, b) {
                    var x = sortKey(a, i, type), y = sortKey(b, i, type);
                    if (x < y) return asc ? -1 : 1;
                    if (x > y) return asc ? 1 : -1;
                    return 0;
                });

                var stale = tbody.querySelector("tr.tbl-empty-row");
                if (stale) stale.remove();
                rows.forEach(function (r) { tbody.appendChild(r); });
                apply();
            });
        });

        if (search) search.addEventListener("input", apply);
        if (filter) filter.addEventListener("change", apply);
        apply();
    }
    // Bind click event to all links with the 'download-report' class
    $('.download-report').on('click', function (e) {
        // 1. Stop the default link click so the global loader doesn't fire
        e.preventDefault();

        // 2. Get the URL from the clicked link
        var downloadUrl = $(this).attr('href');

        // 3. Show your SweetAlert toast
        Swal.fire({
            title: 'Downloading...',
            icon: 'success',
            timer: 2000,
            timerProgressBar: true,
            showConfirmButton: false,
            toast: true,
            position: 'top-end'
        });

        // 4. Trigger the download safely using a hidden iframe
        var iframe = document.createElement('iframe');
        iframe.style.display = 'none';
        iframe.src = downloadUrl;
        document.body.appendChild(iframe);

        // 5. Clean up the iframe from the DOM after 5 seconds
        setTimeout(function () {
            document.body.removeChild(iframe);
        }, 5000);
    });

    var rt;
    window.addEventListener("resize", function () { clearTimeout(rt); rt = setTimeout(drawAll, 140); });
    if (window.matchMedia) {
        var mq = window.matchMedia("(prefers-color-scheme: dark)");
        (mq.addEventListener ? mq.addEventListener.bind(mq, "change") : mq.addListener.bind(mq))(function () {
            setTimeout(drawAll, 60);
        });
    }
    new MutationObserver(function () { setTimeout(drawAll, 60); })
        .observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
})();