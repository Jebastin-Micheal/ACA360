// Dependency-free contract tests. These exercise the shipped client with a small
// DOM/DataTables adapter; they do not replace a browser or database smoke test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');
const root = path.resolve(__dirname, '..');
const script = fs.readFileSync(path.join(root, 'ACA360.Web/wwwroot/js/import-analysis.js'), 'utf8');
const source = fs.readFileSync(path.join(root, 'ACA360.Core/Models/ImportAnalysisCatalog.cs'), 'utf8');
const sheets = [...source.matchAll(/new\("(Employer|Plan|Premium|Employee|Dependent)", "([^"]+)", new ImportAnalysisColumn\[\]\s*\{([\s\S]*?)\n\s*\}\)/g)]
    .map(m => ({ entity: m[1], label: m[2], columns: [...m[3].matchAll(/new\("([^"]+)", "([^"]+)"\)/g)].map(c => ({ key: c[1], title: c[2] })) }));

function setup(fetchImpl) {
    const requests = [], values = new Map(), props = new Map(), events = [];
    const downloads = [], fetches = [], revoked = [];
    const modal = { dataset: { rowsUrl: '/rows', summaryUrl: '/summary', importUrl: '/import', exportUrl: '/export' }, classList: { contains: () => true } };
    let config, response;
    const wrap = selector => ({
        find: child => wrap(child), first() { return this; },
        val(value) { if (value !== undefined) { values.set(selector, value); return this; }
            if (String(selector).includes('__RequestVerificationToken')) return 'test-token';
            return values.get(selector) ?? 'All'; },
        prop(name, value) { if (value === undefined) return props.get(selector + ':' + name); props.set(selector + ':' + name, value); return this; },
        text(value) { if (value === undefined) return values.get(selector) || ''; values.set(selector, String(value)); return this; },
        html(value) { if (value !== undefined) { values.set(selector, value); return this; }
            return (values.get(selector) || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); },
        attr() { return this; }, css() { return this; }, addClass() { return this; }, removeClass() { return this; },
        toggleClass() { return this; }, empty() { values.delete(selector); return this; }, append() { return this; }, each() { return this; },
        on(name, child, handler) { events.push({ selector, name, child: typeof child === 'string' ? child : null, handler: handler || child }); return this; },
        DataTable(options) { config = options; return { destroy() {}, ajax: { reload() {} } }; }
    });
    const $ = selector => wrap(selector);
    $.fn = { DataTable: true };
    $.ajax = options => {
        const request = { options, aborted: false,
            done(callback) { this.success = callback; return this; },
            fail(callback) { this.failure = callback; return this; },
            abort() { this.aborted = true; this.failure?.({}, 'abort'); },
            resolve(data) { this.success?.(data); }, reject(xhr) { this.failure?.(xhr, 'error'); }
        };
        requests.push(request); return request;
    };
    const document = { getElementById: id => id === 'analysisModal' ? modal : { textContent: JSON.stringify(sheets) }, dispatchEvent() {},
        body: { appendChild() {} }, createElement: () => ({ click() { downloads.push(this.download); }, remove() {} }) };
    const window = { jQuery: $, confirm: () => false,
        fetch: (url, options) => { fetches.push({ url, options }); return fetchImpl(url, options); },
        URL: { createObjectURL: () => 'blob:test', revokeObjectURL: value => revoked.push(value) }, setTimeout: callback => callback() };
    vm.runInNewContext(script, { window, document, $, AbortController, URLSearchParams, bootstrap: { Modal: { getOrCreateInstance: () => ({ show() {}, hide() {} }) } }, CustomEvent: class {}, console });
    function event(selector, name, child, target) {
        const entry = events.find(e => e.selector === selector && e.name.startsWith(name) && e.child === child);
        assert.ok(entry, 'event registered: ' + name);
        return entry.handler.call(target);
    }
    return {
        requests, values, props, modal, window, downloads, fetches, revoked,
        download: () => event('#analysisDownload', 'click.ia', null),
        open: id => window.AcaImportAnalysis.open(id),
        grid: () => config,
        draw: (start = 0, length = 10, sort = 0, search = '') => {
            config.ajax({ draw: 7, start, length, order: [{ column: sort, dir: 'asc' }], search: { value: search } }, r => { response = r; });
            return requests.at(-1);
        },
        response: () => response,
        tab: entity => event(modal, 'click.ia', '[data-entity]', { dataset: { entity } }),
        mode: value => event(modal, 'change.ia', 'input[name="importMode"]', { value }),
        close: () => event(modal, 'hidden.bs.modal', null, modal)
    };
}

test('all 89 workbook columns are exposed in five tabs', () => {
    assert.deepEqual(sheets.map(s => s.columns.length), [16, 13, 9, 39, 12]);
    const app = setup(); app.open(42);
    assert.equal(app.grid().serverSide, true);
    assert.equal(app.grid().pageLength, 10);
    assert.equal(app.grid().columns.length, 20); // 16 source columns and four metadata/detail controls
    assert.equal(app.grid().columns[3].data, 'PrimaryEIN');
});
test('each page requests only the requested range and sends the anti-forgery header', () => {
    const app = setup(); app.open(42);
    const first = app.draw(); const next = app.draw(10, 10);
    assert.equal(first.aborted, true);
    const data = JSON.parse(next.options.data);
    assert.equal(data.fileLogId, 42); assert.equal(data.entity, 'Employer');
    assert.equal(data.start, 10); assert.equal(data.length, 10); assert.equal(data.draw, 7);
    assert.equal(next.options.headers.RequestVerificationToken, 'test-token');
    assert.equal(app.requests.filter(r => r.options.url === '/summary').length, 1);
});
test('tab changes create just that sheet and reject late previous responses', () => {
    const app = setup(); app.open(42); const old = app.draw();
    app.tab('Employee'); assert.equal(old.aborted, true);
    const current = app.draw();
    assert.equal(JSON.parse(current.options.data).entity, 'Employee');
    assert.equal(app.grid().columns.length, 43);
    old.resolve({ data: ['stale'] }); assert.equal(app.response(), undefined);
    current.resolve({ data: ['current'] }); assert.equal(app.response().data[0], 'current');
});
test('sort, search and both filters travel to SQL through the controller payload', () => {
    const app = setup(); app.open(42); app.tab('Premium');
    app.values.set('#analysisImpactFilter', 'INSERT'); app.values.set('#analysisValidationFilter', 'Warning');
    const request = JSON.parse(app.draw(20, 25, 10, 'medical').options.data);
    assert.equal(request.sortColumn, 'EEMonthlyContribution');
    assert.equal(request.search, 'medical'); assert.equal(request.impactFilter, 'INSERT');
    assert.equal(request.validationFilter, 'Warning'); assert.equal(request.length, 25);
});
test('mode switches request a new summary and discard a late response from the prior mode', () => {
    const app = setup(); app.open(42); const oldSummary = app.requests[0]; app.draw();
    app.mode('Overwrite'); assert.equal(oldSummary.aborted, true);
    const request = JSON.parse(app.draw().options.data); assert.equal(request.importMode, 'Overwrite');
    oldSummary.resolve({ success: true, data: {} }); // must be ignored before rendering missing fields
    assert.equal(app.props.get('#analysisConfirm:disabled'), true);
});
test('opening another file resets mode, cancels old requests and prevents stale rows', () => {
    const app = setup(); app.open(42); app.mode('Overwrite'); const old = app.draw();
    app.open(43); assert.equal(old.aborted, true);
    old.resolve({ data: ['wrong file'] }); assert.equal(app.response(), undefined);
    const data = JSON.parse(app.draw().options.data);
    assert.equal(data.fileLogId, 43); assert.equal(data.importMode, 'Merge'); assert.equal(data.start, 0);
});
test('cell renderers show uploaded HTML as text', () => {
    const app = setup(); app.open(42);
    const value = app.grid().columns[3].render('<img src=x onerror=alert(1)>');
    assert.equal(value.includes('<img'), false); assert.equal(value.includes('&lt;img'), true);
});
test('a failed grid request clears processing and exposes a retry message', () => {
    const app = setup(); app.open(42); app.draw().reject({ status: 500, responseJSON: { error: 'Retry this page.' } });
    assert.equal(app.response().data.length, 0);
    assert.equal(app.values.get('span'), 'Retry this page.');
});
test('closing the modal aborts work and invalidates delayed responses', () => {
    const app = setup(); app.open(42); const old = app.draw(); app.close();
    assert.equal(old.aborted, true); old.resolve({ data: ['late'] }); assert.equal(app.response(), undefined);
});
test('confirmation stays disabled for excluded rows and becomes available only after a ready summary', () => {
    const app = setup(); app.open(42);
    const data = { fileName: 'sample.xlsx', planYear: 2026, totalRows: 4, cleanRows: 3, warningRows: 1,
        errorRows: 0, informationRows: 0, excludedRows: 1, canImport: false,
        entityStats: sheets.map(s => ({ entityType: s.entity, total: 0, inserts: 0, updates: 0, skips: 0, removes: 0, excluded: 0 })), relatedRemovals: [] };
    app.requests[0].resolve({ success: true, data });
    assert.equal(app.props.get('#analysisConfirm:disabled'), true);
    assert.match(app.values.get('#analysisImportStatus'), /excluded/);
    app.open(43);
    app.requests.at(-1).resolve({ success: true, data: { ...data, excludedRows: 0, canImport: true } });
    assert.equal(app.props.get('#analysisConfirm:disabled'), false);
});

function readyForExport(app, canImport = true) {
    app.requests.at(-1).resolve({ success: true, data: {
        fileName: 'sample.xlsx', planYear: 2026, totalRows: 1, cleanRows: 1,
        warningRows: 0, errorRows: 0, informationRows: 0, excludedRows: 0,
        canImport, entityStats: [], relatedRemovals: []
    } });
}
const excelResponse = () => ({ ok: true,
    headers: { get: () => 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
    blob: async () => ({}) });

test('download exports the selected file/mode, ignoring grid filters and paging', async () => {
    const app = setup(async () => excelResponse()); app.open(42); app.mode('Overwrite'); readyForExport(app);
    app.tab('Employee'); app.values.set('#analysisImpactFilter', 'INSERT');
    await app.download();
    const request = app.fetches[0];
    assert.equal(request.url, '/export'); assert.equal(request.options.method, 'POST');
    assert.equal(request.options.headers.RequestVerificationToken, 'test-token');
    assert.deepEqual([...request.options.body.entries()], [['id', '42'], ['importMode', 'Overwrite']]);
    assert.deepEqual(app.downloads, ['Import-Analysis-42-Overwrite.xlsx']);
    assert.deepEqual(app.revoked, ['blob:test']);
    assert.equal(app.props.get('#analysisDownload:disabled'), false);
});
test('export is enabled for a reviewed file even when import is blocked', async () => {
    const app = setup(async () => excelResponse()); app.open(42);
    assert.equal(app.props.get('#analysisDownload:disabled'), true);
    readyForExport(app, false);
    assert.equal(app.props.get('#analysisDownload:disabled'), false);
    assert.equal(app.props.get('#analysisConfirm:disabled'), true);
    await app.download(); assert.equal(app.downloads.length, 1);
});
test('download errors restore the button and do not save JSON as Excel', async () => {
    const app = setup(async () => ({ ok: false, json: async () => ({ error: 'File is unavailable.' }) }));
    app.open(42); readyForExport(app); await app.download();
    assert.equal(app.downloads.length, 0);
    assert.equal(app.values.get('span'), 'File is unavailable.');
    assert.equal(app.props.get('#analysisDownload:disabled'), false);
    assert.equal(app.props.get('#analysisConfirm:disabled'), false);
});
test('a login HTML response is rejected rather than downloaded as a workbook', async () => {
    const app = setup(async () => ({ ok: true, headers: { get: () => 'text/html' } }));
    app.open(42); readyForExport(app); await app.download();
    assert.equal(app.downloads.length, 0);
    assert.match(app.values.get('span'), /did not return an Excel workbook/);
});
test('close cancels an export and discards its eventual response', async () => {
    let resolve;
    const app = setup(() => new Promise(r => { resolve = r; })); app.open(42); readyForExport(app);
    const pending = app.download();
    assert.equal(app.props.get('#analysisConfirm:disabled'), true);
    assert.equal(app.props.get('#analysisDownload:disabled'), true);
    await app.download(); assert.equal(app.fetches.length, 1);
    app.close(); assert.equal(app.fetches[0].options.signal.aborted, true);
    resolve(excelResponse()); await pending; assert.equal(app.downloads.length, 0);
});
test('opening another file prevents the previous export from changing its controls', async () => {
    let resolve;
    const app = setup(() => new Promise(r => { resolve = r; })); app.open(42); readyForExport(app);
    const pending = app.download(); app.open(43); readyForExport(app);
    resolve(excelResponse()); await pending;
    assert.equal(app.downloads.length, 0);
    assert.equal(app.props.get('#analysisDownload:disabled'), false);
});
