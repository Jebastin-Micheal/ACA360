(function (window, $) {
    'use strict';
    const modal = document.getElementById('analysisModal');
    if (!modal || !$) return;
    const $modal = $(modal);
    const sheets = JSON.parse(document.getElementById('analysisSheetCatalog').textContent);
    const impacts = {
        INSERT: ['New', 'ia-insert'], UPDATE: ['Updated', 'ia-update'],
        NOCHANGE: ['No change', 'ia-neutral'], REMOVE: ['Removed on import', 'ia-remove'],
        EXCLUDED: ['Excluded', 'ia-error']
    };
    const validations = { Clean: 'bg-label-success', Warning: 'bg-label-warning', Error: 'bg-label-danger', Information: 'bg-label-info' };
    let fileId = 0, entity = 'Employer', mode = 'Merge', summary = null;
    let grid = null, gridRequest = null, summaryRequest = null, apexChart = null;
    let exporting = false, exportController = null;
    let session = 0, gridVersion = 0, confirming = false, importing = false;
    const number = value => (Number(value) || 0).toLocaleString();
    const escape = value => $('<div>').text(value == null ? '' : String(value)).html();
    const token = () => $modal.find('input[name="__RequestVerificationToken"]').val();
    const badge = (label, css) => '<span class="ia-tag ' + css + '">' + escape(label) + '</span>';
    function message(selector, text) {
        const box = $modal.find(selector);
        box.toggleClass('d-none', !text).find('span').first().text(text || '');
    }
    function errorMessage(xhr, fallback) {
        if (xhr.status === 401 || xhr.status === 403) return 'Your session or permission has changed. Sign in again to continue.';
        return xhr.responseJSON?.error || xhr.responseJSON?.message || fallback;
    }
    function isCurrent(context) {
        return context.session === session && context.fileId === fileId && context.mode === mode;
    }
    function context() { return { session, fileId, mode }; }
    function updateConfirm() {
        $('#analysisConfirm').prop('disabled', importing || confirming || exporting || !summary?.canImport);
        $('#analysisDownload').prop('disabled', importing || confirming || exporting || !summary)
            .attr('aria-busy', String(exporting));
        $('#analysisDownloadText').text(exporting ? 'Preparing Excel…' : 'Download Excel');
    }
    function cancelRequests() {
        exportController?.abort(); exportController = null; exporting = false;
        if (!importing) $modal.find('input[name="importMode"]').prop('disabled', false);
        summaryRequest?.abort(); summaryRequest = null;
        gridRequest?.abort(); gridRequest = null;
    }
    function destroyGrid() {
        gridVersion++;
        gridRequest?.abort(); gridRequest = null;
        if (grid) { grid.destroy(); grid = null; }
        $('#analysisGrid').empty();
    }
    function issues(row) {
        if (!row._issues) {
            try { row._issues = JSON.parse(row.issuesJson || '[]'); }
            catch (_) { row._issues = []; }
        }
        return row._issues;
    }
    function rowDetails(row) {
        const list = issues(row);
        let html = '<div class="ia-details"><p><strong>' + (row.rowSource === 'Database' ? 'Existing database record' : 'Uploaded row ' + escape(row.rowNumber)) + '</strong></p>';
        if (row.actionNote) html += '<p>' + escape(row.actionNote) + '</p>';
        if (list.length) {
            html += '<ul>' + list.map(item => '<li>' + badge(item.severity, validations[item.severity] || 'ia-neutral') +
                ' <strong>' + escape(item.column) + '</strong>: ' + escape(item.message) + '</li>').join('') + '</ul>';
        } else if (row.errorMessages) html += '<p>' + escape(row.errorMessages) + '</p>';
        else if (!row.actionNote) html += '<p>No open validation issues for this row.</p>';
        return html + '</div>';
    }
    // "What changed" (script 057): a server-rendered panel in place of the sheet grid.
    let changes = { kind: '', search: '', page: 1 }, changesRequest = null;
    function showChanges(show) {
        $('#analysisGridPanel, .ia-filters, .ia-grid-legend, .ia-count-note').toggleClass('d-none', show);
        $('#analysisChangesPanel').toggleClass('d-none', !show);
    }
    function loadChanges() {
        changesRequest?.abort();
        const captured = context();
        const $panel = $('#analysisChangesPanel').attr('aria-busy', 'true');
        if (!$panel.children().length) $panel.html('<p class="text-muted py-4 text-center">Comparing the file with what the system holds…</p>');
        changesRequest = $.ajax({ url: modal.dataset.changesUrl, cache: false,
            data: { id: fileId, kind: changes.kind, search: changes.search, pageIndex: changes.page }
        }).done(function (html) {
            if (isCurrent(captured)) $panel.html(html);
        }).fail(function (xhr, status) {
            if (status !== 'abort' && isCurrent(captured))
                $panel.html('<div class="alert alert-danger">' + escape(errorMessage(xhr, 'Could not work out what changed. Please retry.')) + '</div>');
        }).always(() => $panel.attr('aria-busy', 'false'));
    }
    function createGrid() {
        destroyGrid();
        message('#analysisGridError', '');
        if (entity === 'Changes') { showChanges(true); loadChanges(); return; }
        showChanges(false);
        if (!$.fn.DataTable) {
            message('#analysisGridError', 'DataTables could not load. Refresh the page and check the shared scripts.');
            return;
        }
        const sheet = sheets.find(s => s.entity === entity);
        const version = gridVersion;
        const captured = context();
        const tableColumns = [
            { data: 'rowNumber', title: 'Source row', className: 'ia-control', render: value => value == null ? 'Database' : escape(value) },
            { data: 'actionType', title: 'Change', render: value => badge(...(impacts[value] || ['Unknown', 'ia-neutral'])) },
            { data: 'validationStatus', title: 'Validation', render: value => badge(value, validations[value] || 'ia-neutral') },
            ...sheet.columns.map(column => ({ data: column.key, title: escape(column.title), defaultContent: '', render: value => escape(value) })),
        ];
        $('#analysisGrid').append($('<caption>').addClass('visually-hidden').text(sheet.label + ' upload values'));
        grid = $('#analysisGrid').DataTable({
            processing: true, serverSide: true, pageLength: 10, lengthMenu: [10, 25, 50, 100],
            searchDelay: 450, order: [[0, 'asc']], orderMulti: false,
            scrollX: true, scrollY: '36vh', scrollCollapse: true,
            autoWidth: false, stateSave: false, deferRender: true,
            ordering: false,
            pagingType: 'full_numbers',
            dom: '<"d-flex flex-wrap justify-content-between align-items-center gap-2"lf>rt<"d-flex flex-wrap justify-content-between align-items-center gap-2"ip>',
            language: { emptyTable: 'No rows in this sheet.', zeroRecords: 'No rows match the selected filters.',
                processing: 'Loading rows…', search: 'Search values:', lengthMenu: 'Show _MENU_ rows',
                info: 'Showing _START_–_END_ of _TOTAL_ rows', infoEmpty: 'Showing 0 rows',
                paginate: {
                    first: 'First',
                    previous: '‹',
                    next: '›',
                    last: 'Last'
                }
},

            columns: tableColumns,
            drawCallback: function (settings) {
                const api = this.api();
                const $container = $(api.table().container());

                $container.find('.dataTables_scrollHead th').each(function () {
                    if ($(this).find('.ia-col-resizer').length === 0) {
                        const $resizer = $('<div class="ia-col-resizer"></div>');
                        $(this).append($resizer);

                        let startX, startWidth;
                        $resizer.on('mousedown', function (e) {
                            e.preventDefault();
                            startX = e.pageX;
                            startWidth = $(this).parent().outerWidth();
                            $(this).addClass('active');

                            $(document).on('mousemove.ia-resize', function (e) {
                                const newWidth = Math.max(50, startWidth + (e.pageX - startX));
                                $resizer.parent().css({ 'min-width': newWidth, 'width': newWidth });


                                const colIdx = $resizer.parent().index();
                                $container.find('.dataTables_scrollBody tbody tr').each(function () {
                                    $(this).children('td').eq(colIdx).css({ 'min-width': newWidth, 'max-width': newWidth, 'width': newWidth });
                                });
                            });

                            $(document).on('mouseup.ia-resize', function () {
                                $('.ia-col-resizer').removeClass('active');
                                $(document).off('mousemove.ia-resize mouseup.ia-resize');
                                api.columns.adjust();
                            });
                        });
                    }
                });
            },
            ajax: function (dt, callback) {
                gridRequest?.abort();
                message('#analysisGridError', '');
                const order = dt.order[0] || { column: 0, dir: 'asc' };
                gridRequest = $.ajax({
                    url: modal.dataset.rowsUrl, method: 'POST', contentType: 'application/json', dataType: 'json',
                    headers: { RequestVerificationToken: token() },
                    data: JSON.stringify({
                        fileLogId: captured.fileId, entity: sheet.entity, importMode: captured.mode,
                        draw: dt.draw, start: dt.start, length: dt.length, search: dt.search.value,
                        sortColumn: tableColumns[order.column]?.data || 'rowNumber', sortDirection: order.dir,
                        impactFilter: $('#analysisImpactFilter').val(), validationFilter: $('#analysisValidationFilter').val()
                    })
                }).done(function (result) {
                    if (isCurrent(captured) && version === gridVersion) callback(result);
                }).fail(function (xhr, status) {
                    if (status === 'abort' || !isCurrent(captured) || version !== gridVersion) return;
                    callback({ draw: dt.draw, recordsTotal: 0, recordsFiltered: 0, data: [] });
                    message('#analysisGridError', errorMessage(xhr, 'Could not load rows. Please retry.'));
                });
            },
            createdRow: function (tr, row) {
                let state = row.actionType === 'REMOVE' ? 'remove' : row.actionType.toLowerCase();
                if (row.validationStatus === 'Error') state = 'error';
                else if (row.validationStatus === 'Warning') state = 'warning';
                $(tr).addClass('ia-row-' + state);
                sheet.columns.forEach((column, index) => {
                    const fieldIssues = issues(row).filter(i => i.column?.toLowerCase() === column.key.toLowerCase());
                    if (!fieldIssues.length) return;
                    const severe = fieldIssues.some(i => ['Error', 'Critical', 'Fatal'].includes(i.severity));
                    $(tr).children('td').eq(index + 3).addClass(severe ? 'ia-cell-error' : 'ia-cell-warning')
                        .attr('title', fieldIssues.map(i => i.message).join('\n'));
                });
            }
        });
        $('#analysisGridPanel').attr('aria-labelledby', 'ia-tab-' + entity);
    }
   
    let apexDonutChart = null;

    function renderSummary(data) {
        summary = data;
        $('#analysisFileName').html('<i class="bx bx-file me-1" data-bs-toggle="tooltip" data-bs-placement="top" title="Uploaded Excel File"></i>' + escape(data.fileName) + ' <span class="mx-1"> | </span> <i class="bx bx-calendar me-1" data-bs-toggle="tooltip" data-bs-placement="top" title="Filing Year" aria-hidden="true"></i>' + escape(data.planYear || '—'));
        $('#analysisRowCount').text(number(data.totalRows));
        $('#analysisModeLabel').text(mode);

        const quality = [
            ['Clean', data.cleanRows, '#71dd37', 'Ready'],
            ['Warning', data.warningRows, '#eeb436', 'Review'],
            ['Error', data.errorRows, '#df4e59', 'Resolve'],
            ['Information', data.informationRows, '#5987c8', 'Review']
        ];

       
        const seriesData = [data.cleanRows || 0, data.warningRows || 0, data.errorRows || 0, data.informationRows || 0];

        if (apexChart) {
           
            apexChart.updateSeries(seriesData);
        } else {
           
            $('#impactChart').empty();
            const options = {
                series: seriesData,
                labels: ['Clean', 'Warnings', 'Errors', 'Info'],
                chart: {
                    type: 'donut',
                    height: 130,
                    sparkline: { enabled: true }
                },
                colors: ['#71dd37', '#eeb436', '#df4e59', '#5987c8'],
                plotOptions: {
                    pie: {
                        donut: {
                            size: '75%',
                            labels: {
                                show: true,
                                name: {
                                    show: true,
                                    fontSize: '12px',
                                    offsetY: 20,
                                    color: '#6b7280'
                                },
                                value: {
                                    show: true,
                                    fontSize: '22px',
                                    fontWeight: 700,
                                    color: '#111827',
                                    offsetY: -10, 
                                    formatter: val => number(val)
                                },
                                total: {
                                    show: true,
                                    showAlways: true,
                                    label: 'uploaded rows', 
                                    fontSize: '12px',
                                    color: '#111827',
                                    formatter: function (w) {
                                       
                                        return number(w.globals.seriesTotals.reduce((a, b) => a + b, 0));
                                    }
                                }
                            }
                        }
                    }
                },
                dataLabels: { enabled: false },
                stroke: { show: false },
                tooltip: {
                    enabled: true,
                    theme: 'light',
                    y: { formatter: val => number(val) + " rows" }
                }
            };
            apexDonutChart = new ApexCharts(document.querySelector("#impactChart"), options);
            apexDonutChart.render();
        }
        // ---------------------------------------------------------

        $('#analysisValidationBody').html(quality.map(q => '<tr><td>' + badge(q[0], validations[q[0]]) +
            '</td><td>' + number(q[1]) + '</td><td>' + q[3] + '</td></tr>').join(''));

        const entityIcons = {
            'Employer': 'bx-building text-primary',
            'Plan': 'bx-clipboard text-info',
            'Premium': 'bx-money text-success',
            'Employee': 'bx-user text-warning',
            'Dependent': 'bx-group text-danger'
        };

        $('#analysisEntityBody').html(data.entityStats.map(item => {
            $('[data-count="' + item.entityType + '"]', modal).text(number(item.total));

            const icon = entityIcons[item.entityType] || 'bx-layer';
            const displayName = escape(item.entityType === 'Dependent' ? 'Dependents' : item.entityType);

            return '<tr>' +
                '<td><i class="bx ' + icon + ' me-2 fs-6 align-middle"></i>' + displayName + '</td>' +
                '<td><span class="badge bg-label-success">' + number(item.inserts) + '</span></td>' + // New
                '<td><span class="badge bg-label-info">' + number(item.updates) + '</span></td>' +   // Update
                '<td>' + number(item.skips) + '</td>' +                                              // No change
                '<td><span class="badge bg-label-danger">' + number(item.removes) + '</span></td>' + // Remove
                '<td><span class="badge bg-label-warning">' + number(item.excluded) + '</span></td>' +// Excluded
                '<td><strong>' + number(item.total) + '</strong></td>' +                             // Upload
                '</tr>';
        }).join(''));

        const removes = data.entityStats.reduce((sum, item) => sum + item.removes, 0);
        const related = data.relatedRemovals.filter(r => r.removes > 0).map(r => number(r.removes) + ' ' + r.entityType.toLowerCase());

        $('#analysisRemovalNotice').toggleClass('d-none', mode !== 'Overwrite').text(
            'Overwrite removes ' + number(removes) + ' existing premium/dependent records in the parent employer scope' +
            (related.length ? ', plus ' + related.join(', ') : '') + '. Employee and Plan master records remain.');

        $('#analysisImportStatus').text(data.canImport ? 'Ready to import.' :
            data.errorRows || data.excludedRows ? 'Resolve errors and excluded rows before importing.' : 'This file is not ready for import, or your role cannot import.');

        updateConfirm();
    }
    function loadSummary() {
        summaryRequest?.abort(); summary = null; updateConfirm();
        message('#analysisError', '');
        $('#analysisImportStatus').text('Loading analysis…');
        $('#analysisModeLabel').text(mode);
        $('#analysisValidationBody').html('<tr><td colspan="3">Loading summary…</td></tr>');
        $('#analysisEntityBody').html('<tr><td colspan="7">Loading summary…</td></tr>');
        $('#analysisRemovalNotice').addClass('d-none').empty();
        const captured = context();
        summaryRequest = $.ajax({ url: modal.dataset.summaryUrl, dataType: 'json', cache: false,
            data: { id: fileId, importMode: mode }
        }).done(function (result) {
            if (!isCurrent(captured)) return;
            if (result.success) renderSummary(result.data);
            else { message('#analysisError', result.message || 'Analysis failed.'); $('#analysisImportStatus').text('Analysis is unavailable.'); }
        }).fail(function (xhr, status) {
            if (status === 'abort' || !isCurrent(captured)) return;
            message('#analysisError', errorMessage(xhr, 'Could not load the analysis. Please retry.'));
            $('#analysisImportStatus').text('Analysis is unavailable.');
        });
    }
    function open(id) {
        if (importing) return;
        const parsed = Number(id);
        if (!Number.isSafeInteger(parsed) || parsed <= 0) return;
        session++; cancelRequests(); destroyGrid();
        fileId = parsed; entity = 'Employer'; mode = 'Merge'; summary = null; confirming = false;
        changes = { kind: '', search: '', page: 1 }; $('#analysisChangesPanel').empty(); showChanges(false);
        $('#analysisConfirm').text('Confirm & Import'); updateConfirm();
        $modal.find('input[name="importMode"][value="Merge"]').prop('checked', true);
        $modal.find('[role="tab"]').each(function () {
            const selected = this.dataset.entity === entity;
            $(this).toggleClass('active', selected).attr({ 'aria-selected': String(selected), tabindex: selected ? 0 : -1 });
        });
        $('#analysisImpactFilter, #analysisValidationFilter').val('All');
        $('#analysisFileName').text('Loading file…');
        $('#analysisRowCount').text('—');
        $('#impactChart').css('background', 'transparent').attr('aria-label', 'Validation summary is loading');
        $modal.find('[data-count]').text('—');
        message('#analysisGridError', '');
        const shown = modal.classList.contains('show');
        bootstrap.Modal.getOrCreateInstance(modal).show();
        loadSummary();
        if (shown) createGrid();
    }
    async function confirmImport() {
        if (!summary?.canImport || importing || confirming || exporting) return;
        const captured = context(), data = summary;
        confirming = true; updateConfirm();
        try {
            const yearText = data.planYear !== new Date().getFullYear() ? ' Filing year: ' + data.planYear + '.' : '';
            const question = captured.mode === 'Overwrite'
                ? 'Overwrite will delete the existing records shown in the removal preview before importing.' + yearText + ' Continue?'
                : 'Import the reviewed file using Update / Merge?' + yearText;
            const accepted = typeof window.showConfirm === 'function'
                ? await window.showConfirm('Confirm import', question, captured.mode === 'Overwrite' ? 'Overwrite & Import' : 'Import', captured.mode === 'Overwrite' ? 'btn-danger' : 'btn-primary')
                : window.confirm(question);
            if (!accepted || !isCurrent(captured) || !summary?.canImport) return;
            importing = true;
            $('#analysisConfirm').text('Starting import…');
            $modal.find('input[name="importMode"], [data-bs-dismiss="modal"]').prop('disabled', true);
            await $.ajax({ url: modal.dataset.importUrl, method: 'POST', dataType: 'json',
                data: { FileLogId: captured.fileId, importMode: captured.mode, __RequestVerificationToken: token() }
            }).done(function (result) {
                if (!result.success) { message('#analysisError', result.message || 'Import could not start.'); return; }
                importing = false;
                bootstrap.Modal.getOrCreateInstance(modal).hide();
                document.dispatchEvent(new CustomEvent('import-analysis:queued', { detail: { fileLogId: captured.fileId, message: result.message } }));
            });
        } catch (error) {
            message('#analysisError', errorMessage(error, 'Could not start the import. Please retry.'));
        } finally {
            if (isCurrent(captured)) {
                confirming = false; importing = false;
                $('#analysisConfirm').text('Confirm & Import');
                $modal.find('input[name="importMode"], [data-bs-dismiss="modal"]').prop('disabled', false);
                updateConfirm();
            }
        }
    }
    async function downloadExcel() {
        if (!summary || !fileId || exporting || importing || confirming) return;
        const captured = context();
        const controller = new AbortController();
        exportController = controller; exporting = true; updateConfirm();
        message('#analysisExportError', '');
        $('#analysisExportStatus').text('Preparing all five sheets and chart summaries. You can close the modal to cancel.');
        $modal.find('input[name="importMode"]').prop('disabled', true);
        try {
            // Send only the selected file/mode. The server reads every sheet from SQL;
            // the current DataTable page, tab, search and filters do not limit the export.
            const response = await window.fetch(modal.dataset.exportUrl, {
                method: 'POST', credentials: 'same-origin', signal: controller.signal,
                headers: { RequestVerificationToken: token() },
                body: new URLSearchParams({ id: String(captured.fileId), importMode: captured.mode })
            });
            if (!response.ok) {
                let detail;
                try { detail = await response.json(); } catch (_) { /* non-JSON error */ }
                throw new Error(detail?.error || 'Could not download Excel. Check your session and retry.');
            }
            if (!response.headers.get('content-type')?.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'))
                throw new Error('The server did not return an Excel workbook. Sign in again and retry.');
            const blob = await response.blob();
            if (!isCurrent(captured) || controller.signal.aborted) return;
            const url = window.URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = 'Import-Analysis-' + captured.fileId + '-' + captured.mode + '.xlsx';
            document.body.appendChild(link);
            try { link.click(); }
            finally { link.remove(); window.setTimeout(() => window.URL.revokeObjectURL(url), 60000); }
            $('#analysisExportStatus').text('Excel download started.');
        } catch (error) {
            if (error.name !== 'AbortError' && isCurrent(captured) && !controller.signal.aborted) {
                message('#analysisExportError', error.message || 'Could not download Excel. Please retry.');
                $('#analysisExportStatus').text('Excel export failed. Select Download Excel to retry.');
            }
        } finally {
            if (exportController === controller) {
                exportController = null; exporting = false;
                $modal.find('input[name="importMode"]').prop('disabled', false);
                updateConfirm();
            }
        }
    }
    $modal.on('shown.bs.modal.ia', function () { if (fileId) createGrid(); });
    $modal.on('hide.bs.modal.ia', function (event) { if (importing) event.preventDefault(); });
    $modal.on('hidden.bs.modal.ia', function () {
        session++; cancelRequests(); destroyGrid(); fileId = 0; summary = null; updateConfirm();
       
        if (apexChart) {
            apexChart.destroy();
            apexChart = null;
        }
    });
    $modal.on('click.ia', '[data-entity]', function () {
        if (entity === this.dataset.entity) return;
        entity = this.dataset.entity;
        $modal.find('[role="tab"]').removeClass('active').attr({ 'aria-selected': 'false', tabindex: -1 });
        $(this).addClass('active').attr({ 'aria-selected': 'true', tabindex: 0 });
        createGrid();
    });
    $modal.on('keydown.ia', '[role="tab"]', function (event) {
        if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const tabs = $modal.find('[role="tab"]'), index = tabs.index(this);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? tabs.length - 1 :
            (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
        tabs.eq(next).trigger('click').trigger('focus');
    });
    $modal.on('change.ia', 'input[name="importMode"]', function () {
        mode = this.value; session++; loadSummary(); createGrid();
    });
    $modal.on('click.ia', '.ia-change-kind', function () {
        changes.kind = this.dataset.kind || ''; changes.page = 1; loadChanges();
    });
    $modal.on('click.ia', '.ia-change-page', function () {
        changes.page = Number(this.dataset.page) || 1; loadChanges();
    });
    let changeSearchTimer;
    $modal.on('input.ia', '.ia-change-search', function () {
        clearTimeout(changeSearchTimer);
        const value = this.value;
        changeSearchTimer = setTimeout(() => { changes.search = value; changes.page = 1; loadChanges(); }, 450);
    });
    $('#analysisImpactFilter, #analysisValidationFilter').on('change.ia', () => grid?.ajax.reload(null, true));
    $('#analysisRetry').on('click.ia', loadSummary);
    $('#analysisGridRetry').on('click.ia', () => grid ? grid.ajax.reload(null, false) : createGrid());
    $('#analysisConfirm').on('click.ia', confirmImport);
    $('#analysisDownload').on('click.ia', downloadExcel);
    $modal.on('click.ia', '.ia-row-details', function () {
        const row = grid.row($(this).closest('tr'));
        if (row.child.isShown()) { row.child.hide(); $(this).attr('aria-expanded', 'false'); }
        else { row.child(rowDetails(row.data())).show(); $(this).attr('aria-expanded', 'true'); }
    });
    window.AcaImportAnalysis = { open };
})(window, window.jQuery);
