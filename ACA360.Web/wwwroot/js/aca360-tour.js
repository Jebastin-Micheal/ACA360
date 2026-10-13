/* Guided tours (Driver.js, assets/vendor/libs/driver).
   - One tour per page: the home dashboard (/Dashboard) and the pages marked with
     [data-tour-page] (Employers, Tracker, Tracker employer dashboard, Files, Error Triage).
   - Each starts by itself the first time a user opens that page. "Seen" is kept on the server
     (TourController, one id per tour, e.g. "home:1"), so it does not come back on another device.
   - Profile menu > "Take the tour" ([data-tour-start]) replays the tour of the current page; on
     a page without one it opens the dashboard with ?tour=1.
   - Steps point at [data-tour] markers, the dashboard areas ([data-section]) and page ids; a step
     whose element is missing or hidden (role, permissions, screen size, layout) is skipped.
   - Never starts by itself while viewing as someone, or while another dialog is open. */
(function () {
    'use strict';

    var me = document.currentScript;
    var cfg = {
        role: (me && me.getAttribute('data-role')) || '',
        homeUrl: (me && me.getAttribute('data-home-url')) || '/Dashboard',
        statusUrl: (me && me.getAttribute('data-status-url')) || '/Tour/Status',
        seenUrl: (me && me.getAttribute('data-seen-url')) || '/Tour/Seen'
    };

    // The role's own area on the dashboard (Views/Shared/Dashboard/Roles).
    var ROLE_TEXT = {
        'SuperAdmin': 'Your admin cards: active users, who is signed in now, files this month, temporary passwords, critical alerts, the IRS filing overview and shortcuts to the administration pages.',
        'Admin': 'Your admin cards: active users, who is signed in now, files this month, temporary passwords, critical alerts, the IRS filing overview and shortcuts to the administration pages.',
        'ACA Director': 'The IRS filing overview, critical alerts and user management for your team.',
        'Account Manager Supervisor': 'Employers by account manager, your team\'s workload and user management.',
        'Account Manager': 'Your IRS filing overview and the rejections you need to fix.',
        'DA Supervisor': 'File results for all analysts (accepted, rejected, processing, critical errors), files by analyst, and files still waiting for an analyst.',
        'Data Analyst': 'Your files (accepted, rejected, processing, critical errors), your priority queue and the employers you work on.'
    };

    function viewingAs() {
        var m = document.querySelector('meta[name="aca360-view-as"]');
        return !!m && m.content === '1';
    }
    function onHomeDashboard() {
        var d = window.ACA360_DASHBOARD;
        return !!(d && d.config && d.config.dashboardKey === 'home');
    }
    function visible(el) {
        return !!el && el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
    }
    function first(selector) {
        var all = document.querySelectorAll(selector);
        for (var i = 0; i < all.length; i++) if (visible(all[i])) return all[i];
        return null;
    }
    function closest(selector, ancestor) {
        var el = first(selector);
        return el && el.closest(ancestor);
    }
    function text(el) { return el ? (el.textContent || '').replace(/\s+/g, ' ').trim() : ''; }

    // A list of steps: add() keeps a step only when its element is on screen.
    function stepList() {
        var steps = [];
        return {
            intro: function (title, description) { steps.push({ popover: { title: title, description: description } }); },
            add: function (target, title, description) {
                var el = typeof target === 'string' ? first(target) : target;
                if (visible(el)) steps.push({ element: el, popover: { title: title, description: description } });
            },
            done: function (description) {
                steps.push({ popover: { title: 'You are all set', description: description ||
                    'Replay this tour any time: click your profile picture, then Take the tour.' } });
                return steps;
            }
        };
    }

    // Search, filters, refresh, page size, view and actions (Views/Shared/_GridToolbarCommon).
    function gridToolbar(s, actionsText) {
        s.add('[data-tour="grid-search"]', 'Search', 'Type to search the list. Where there is a magnifier menu, choose which columns to search in.');
        s.add('[data-tour="grid-filter"]', 'Filters', 'Narrow the list with filters. A red number shows how many are active.');
        s.add('[data-tour="grid-refresh"]', 'Refresh', 'Reload the list with the latest data.');
        s.add('[data-tour="grid-pagesize"]', 'Rows per page', 'How many rows each page shows.');
        s.add('[data-tour="grid-view"]', 'Table or split view', 'Table shows the full list. Split view keeps the list on the left and opens the details of the row you click on the right.');
        if (actionsText) s.add('[data-tour="grid-actions"]', 'Actions', actionsText);
    }

    var TOURS = {
        home: { id: 'home:1', steps: function () {
            var s = stepList();
            s.intro('Welcome to ACA360', 'A short look around your dashboard and the menus. Use Next and Back, or press Esc to close. You can replay it any time from your profile menu.');
            s.add('#layout-menu', 'Main menu', 'Every page you can use. What is listed depends on your role and permissions.');
            s.add('[data-tour="employer"]', 'Selected employer', 'The employer you are working on. Click to switch; pages and reports follow this choice.');
            s.add('[data-tour="plan-year"]', 'Plan year', 'The filing year you are working in. Change it here and the dashboard and pages follow.');
            s.add('[data-tour="shortcuts"]', 'Shortcuts', 'Keep the pages you use most one click away. Use + to add a page.');
            s.add('[data-tour="notifications"]', 'Notifications', 'Alerts about files, filings and tasks. A red dot means something new.');
            s.add('[data-tour="email"]', 'Email', 'Messages sent and received in ACA360.');
            s.add('[data-tour="theme"]', 'Theme', 'Switch between light and dark.');
            s.add('[data-tour="profile"]', 'Your account', 'Your profile and settings, the FAQ, installing the ACA360 app, this tour, and Log out.');
            var role = first('[data-section="role"]');
            if (role) {
                s.add(role, text(role.querySelector('.scope-bar h2')) || 'Your area',
                    (!viewingAs() && ROLE_TEXT[cfg.role]) || 'The tiles for this role.');
            }
            s.add('[data-section="portfolio"]', 'My book of business',
                'Totals across every employer (EIN) you can access for the plan year: clients, forms mailed, e-filed, penalties and past-due clients, and the process step each client is at.');
            s.add('[data-section="employer"]', 'Employer detail',
                'A closer look at one employer: penalty exposure, deadlines, process step, workforce and coverage charts.');
            s.add('[data-tour="view-as"]', 'View as',
                'See ACA360 exactly as a broker, an employer or a team member sees it. It is read-only, and "Return to my view" brings you back.');
            s.add('[data-tour="dash-actions"]', 'Employer actions',
                'Run an AI data scan, export this dashboard as a PDF, download reports and use quick actions for the selected employer.');
            s.add('.scope .scope-toggle', 'Hide or show an area', 'Fold away a whole area you do not need. ACA360 remembers it for you.');
            s.add('#btnEdit', 'Customize layout', 'Drag tiles to reorder them, hide tiles with × and bring them back, then Save layout.');
            return s.done();
        } },

        employers: { id: 'employers:1', steps: function () {
            var s = stepList();
            s.intro('Employers', 'Every employer you can access, with their details, forms and notes.');
            gridToolbar(s, 'Add a new employer.');
            s.add('#employerTable thead', 'The list', 'Click a column heading to sort. Click an employer to open its details.');
            s.add('#employerTable .action-dots-btn', 'Row actions', 'Notes, recalculating codes, the 1094-B / 1094-C forms and more for that employer.');
            return s.done();
        } },

        tracker: { id: 'tracker:1', steps: function () {
            var s = stepList();
            s.intro('Employer tracking', 'Track each employer and the services you provide to them.');
            gridToolbar(s, 'Export the tracker to Excel, or add an employer.');
            s.add('#listContainer', 'Employers', 'Click an employer to see its services on the right. The row icons open its tracking dashboard, its employees, or the edit form.');
            s.add('.svc-panel', 'Services', 'The services of the employer you selected. Use Add to record a new one.');
            return s.done();
        } },

        'tracker-employer': { id: 'tracker-employer:1', steps: function () {
            var s = stepList();
            s.intro('Employer tracking dashboard', 'Everything tracked for one employer. Double-click a field to unlock and edit it.');
            s.add('.emp-tab-nav', 'Sections', 'Employer details, affiliates, broker and other details, one tab each.');
            s.add('#btnSave', 'Save', 'Saves the changes you made on these tabs.');
            s.add('#svcPanelBody', 'Selected service', 'The details of the service you pick from the grid below.');
            s.add('#svcGrid', 'Services', 'All services for this employer. Click a row to open it above.');
            s.add('#btnFullScreen', 'Full screen', 'Use the whole screen for this dashboard. The export button beside it downloads the employer to Excel.');
            return s.done();
        } },

        files: { id: 'files:1', steps: function () {
            var s = stepList();
            s.intro('File management', 'Upload employee data files and follow them through validation and import.');
            s.add(closest('#cardTotal', '.row'), 'At a glance', 'Files in this view, files that need action (assign, fix or import), files processing and files completed.');
            s.add('#viewModeTabs', 'Active, archived, recycle bin', 'Switch between current files, archived files and deleted files.');
            s.add(closest('#searchInput', '.row'), 'Find files', 'Search by file, employer or EIN, filter by date, or show only your files.');
            s.add('[data-bs-target="#uploadModal"]', 'Upload', 'Upload a new data file for an employer.');
            var chip = first('.filter-chip');
            s.add(chip && chip.parentElement, 'Status', 'Show files in one status only.');
            s.add('#fileTableContainer', 'Files', 'Each file’s pipeline status, who it is assigned to and its data quality. Use the actions on the right to claim, assign, review errors or import.');
            return s.done();
        } },

        triage: { id: 'triage:1', steps: function () {
            var s = stepList();
            s.intro('Error triage', 'Fix the problems found in an uploaded file before it is imported.');
            s.add(closest('#healthChart', '.row'), 'File health', 'Errors block the import; warnings are allowed. The chart shows how clean the file is.');
            s.add('#btnReadyImport', 'Ready to import', 'Available once every error is fixed. It takes you back to File management to preview and import.');
            s.add(closest('.filter-btn', '.btn-group'), 'Show', 'All issues, or only errors, warnings or information.');
            s.add('#triageSearch', 'Search', 'Search all columns, or pick one: field name, error message or table.');
            s.add('#ErrorTriageListContainer', 'Issues', 'Each row is a problem in the file. Edit Data fixes the value; warnings can be acknowledged with a reason, one by one or several at once; later files for the employer keep the acknowledgement.');
            return s.done();
        } }
    };

    /* Other list pages built on the shared grid toolbar: intro, toolbar, the list and the first
       row's actions. key = the page's data-tour-page value (also the tour id). */
    var LIST_PAGES = {
        employees: { title: 'Employees', list: '#employeeListContainer',
            intro: 'The employees of the selected employer for the plan year, with their coverage codes and forms.',
            actions: 'Add an employee.', row: 'Edit or delete the employee, and manage electronic delivery consent and portal access.' },
        plans: { title: 'Plans', list: '#planListContainer',
            intro: 'The health plans the selected employer offers.',
            actions: 'Add a plan.', row: 'Edit the plan, add notes, or delete it.' },
        distribution: { title: 'Distribution center', list: '#DistributionListContainer',
            intro: 'Deliver 1095-C forms to employees, electronically or on paper.',
            actions: 'Show employees by delivery status (electronic, paper, sent, pending), e-mail the ones you selected, or export the list.',
            row: 'Download that employee’s form as a PDF.' },
        filing: { title: 'Filing center', list: '#FilingListContainer',
            intro: 'Each employer’s generated forms and filing files.',
            row: 'Download the filing package, see the timeline, regenerate, finalize or unlock the filing, and archive it.' },
        flags: { title: 'System flags', list: '#gridContainer',
            intro: 'Flags raised on employer data, and where to fix them.',
            actions: 'Add a flag, or print and export the list.', row: 'Edit the flag.' },
        menus: { title: 'Menus', list: '#menuListContainer',
            intro: 'The menu items in ACA360: their pages, icons and order.',
            actions: 'Add a menu item, or print and export the list.', row: 'Edit or delete the menu item.' },
        firms: { title: 'Firms and brokers', list: '#firmGridContainer',
            intro: 'Broker firms and the brokers who work for them.',
            actions: 'Add a firm.', row: 'Edit or delete the firm.' },
        'process-steps': { title: 'Process steps', list: '#gridContainer',
            intro: 'The steps every employer’s filing moves through.',
            actions: 'Add a process step.', row: 'Edit or delete the step.' },
        roles: { title: 'Roles', list: '#RoleListContainer',
            intro: 'User roles and what each one may do.',
            actions: 'Add a role, or print and export the list.', row: 'Edit the role, set its permissions, or delete it.' },
        rules: { title: 'Validation rules', list: '#RulesListContainer',
            intro: 'The rules uploaded data files are checked against.',
            actions: 'Add a rule, import rules from a file, or print and export the list.', row: 'Edit or delete the rule.' },
        'sales-reps': { title: 'Sales representatives', list: '#gridContainer',
            intro: 'The sales representatives you can assign to employers.',
            actions: 'Add a sales representative.', row: 'Edit or delete the sales representative.' },
        services: { title: 'Service catalog', list: '#gridContainer',
            intro: 'The services ACA360 provides to employers.',
            actions: 'Add a service.', row: 'Edit or delete the service.' }
    };

    // The actions cell of the first real row (not a "no records" row).
    function firstRowActions(container) {
        var rows = container ? container.querySelectorAll('tbody tr') : [];
        for (var i = 0; i < rows.length; i++) {
            var cells = rows[i].querySelectorAll('td');
            if (cells.length > 1 && visible(rows[i])) return cells[cells.length - 1];
        }
        return null;
    }

    Object.keys(LIST_PAGES).forEach(function (key) {
        var page = LIST_PAGES[key];
        TOURS[key] = { id: key + ':1', steps: function () {
            var s = stepList();
            s.intro(page.title, page.intro);
            gridToolbar(s, page.actions);
            var list = first(page.list);
            s.add(list, 'The list', 'Click a column heading to sort, and a row to open it.');
            s.add(firstRowActions(list), 'Row actions', page.row);
            if (key === 'firms')
                s.add('#brokerGridContainer', 'Brokers', 'The brokers of the firm you selected. Select a firm first to add a broker.');
            return s.done();
        } };
    });

    function currentTourKey() {
        if (onHomeDashboard()) return 'home';
        var el = document.querySelector('[data-tour-page]');
        var key = el && el.getAttribute('data-tour-page');
        return key && TOURS[key] ? key : null;
    }

    function markSeen(tourId) {
        if (viewingAs()) return;                    // View As is read-only
        var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        var token = tokenEl ? tokenEl.value : '';
        var body = new URLSearchParams({ tour: tourId, __RequestVerificationToken: token });
        fetch(cfg.seenUrl, {
            method: 'POST', credentials: 'same-origin', body: body,
            headers: { 'RequestVerificationToken': token, 'X-Requested-With': 'XMLHttpRequest' }
        }).catch(function () { });
    }

    var running = false;
    function start(key) {
        key = key || currentTourKey();
        var def = key && TOURS[key];
        if (!def || running || !window.driver || !window.driver.js) return;
        running = true;
        var tour = window.driver.js.driver({
            steps: def.steps(),
            showProgress: true,
            progressText: '{{current}} of {{total}}',
            nextBtnText: 'Next',
            prevBtnText: 'Back',
            doneBtnText: 'Done',
            popoverClass: 'aca360-tour',
            overlayOpacity: 0.55,
            stagePadding: 6,
            stageRadius: 8,
            smoothScroll: true,
            // Done, the close button, Esc or a click outside. Not onDestroyed: Driver.js skips that
            // one when Done is clicked on a last step without an element (ours).
            onDestroyStarted: function () {
                running = false;
                markSeen(def.id);
                tour.destroy();
            }
        });
        tour.drive();
    }
    window.ACA360StartTour = start;

    // Wait until no other dialog (e.g. the Install app popup) is open.
    function whenNoDialog(fn) {
        if (!document.querySelector('.modal.show')) { fn(); return; }
        document.addEventListener('hidden.bs.modal', function again() {
            if (document.querySelector('.modal.show')) return;
            document.removeEventListener('hidden.bs.modal', again);
            setTimeout(fn, 400);
        });
    }

    document.addEventListener('click', function (e) {
        var trigger = e.target.closest ? e.target.closest('[data-tour-start]') : null;
        if (!trigger) return;
        e.preventDefault();
        if (currentTourKey()) start();
        else window.location.href = cfg.homeUrl + (cfg.homeUrl.indexOf('?') < 0 ? '?' : '&') + 'tour=1';
    });

    document.addEventListener('DOMContentLoaded', function () {
        var key = currentTourKey();
        if (!key) return;

        var params = new URLSearchParams(window.location.search);
        if (key === 'home' && params.get('tour') === '1') {   // asked for from another page
            params.delete('tour');
            var rest = params.toString();
            history.replaceState(null, '', window.location.pathname + (rest ? '?' + rest : '') + window.location.hash);
            setTimeout(function () { whenNoDialog(function () { start('home'); }); }, 600);
            return;
        }

        if (viewingAs()) return;
        fetch(cfg.statusUrl + '?tour=' + encodeURIComponent(TOURS[key].id), {
            credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
            .then(function (r) { return r.ok ? r.json() : { seen: true }; })
            .then(function (s) {
                // Give the page (lists load after it) and the Install app popup time to settle.
                if (s && s.seen === false) setTimeout(function () { whenNoDialog(function () { start(key); }); }, 2500);
            })
            .catch(function () { });
    });
})();
