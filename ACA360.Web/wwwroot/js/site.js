
$(document).ajaxSend(function (event, jqxhr, settings) {
    if (settings.type.toUpperCase() !== "GET") {
        var token = $('input[name="__RequestVerificationToken"]').val();
        if (token) {
            jqxhr.setRequestHeader("RequestVerificationToken", token);
        }
    }
});
function openNotes(entityType, entityId) {
    var modalEl = document.getElementById('notesModal');
    if (!modalEl) return;

    // (Re)initialize the categorized notes widget for this entity
    var root = modalEl.querySelector('.notes-widget');
    if (root && window.NotesWidget) {
        var titleEl = root.querySelector('.notes-panel-title');
        if (titleEl) titleEl.textContent = entityType + ' Notes';
        NotesWidget.openFor(root, entityType, entityId);
    }

    bootstrap.Modal.getOrCreateInstance(modalEl).show();
}

var currentFileId = 0; // Store ID for the "Confirm" action

function runAnalysis(fileId) {
    currentFileId = fileId;

    // 1. Reset UI
    $('#analysisModal').modal('show');
    $('#analysisLoading').removeClass('d-none');
    $('#analysisContent').addClass('d-none');
    $('#analysisTableBody').empty();

    // 2. Fetch Data
    $.ajax({
        url: '/FileUpload/Analyze?id=' + fileId, // Ensure this matches your Controller Route
        type: 'GET',
        success: function (response) {
            if (response.success) {
                renderAnalysis(response.data);
            } else {
                alert("Analysis failed: " + response.message);
                $('#analysisModal').modal('hide');
            }
        },
        error: function () {
            alert("Server error occurred during analysis.");
            $('#analysisModal').modal('hide');
        }
    });
}

function renderAnalysis(data) {
    // 1. Populate Summary Table
    const summaryBody = $('#analysisSummaryBody');
    summaryBody.empty();

    data.summary.forEach(stat => {
        // Highlight rows with Updates in yellow to warn user
        const rowClass = stat.updates > 0 ? 'table-warning' : '';

        const tr = `
            <tr class="${rowClass}">
                <td class="text-start fw-bold">${stat.entityType}</td>
                <td>${stat.total}</td>
                <td class="text-success">${stat.inserts > 0 ? '+' + stat.inserts : '-'}</td>
                <td class="text-warning fw-bold">${stat.updates > 0 ? stat.updates : '-'}</td>
                <td class="text-muted">${stat.skips}</td>
            </tr>
        `;
        summaryBody.append(tr);
    });

    // 2. Populate Details Grid
    const detailsBody = $('#analysisDetailsBody');
    detailsBody.empty();

    if (data.details.length === 0) {
        detailsBody.append('<tr><td colspan="4" class="text-center text-muted py-3">No critical changes detected.</td></tr>');
    } else {
        data.details.forEach(row => {
            let badge = row.actionType === 'INSERT'
                ? '<span class="badge bg-label-success">NEW</span>'
                : '<span class="badge bg-label-warning">UPDATE</span>';

            let changeHtml = formatChanges(row.changesJson); // Use the same helper from before

            const tr = `
                <tr>
                    <td><small class="text-uppercase text-muted">${row.entityType}</small></td>
                    <td>
                        <div class="fw-bold">${row.identifier}</div>
                        <div class="small text-muted">${row.subIdentifier || ''}</div>
                    </td>
                    <td>${badge}</td>
                    <td class="small">${changeHtml}</td>
                </tr>
            `;
            detailsBody.append(tr);
        });
    }

    // 3. Show Content
    $('#analysisLoading').addClass('d-none');
    $('#analysisContent').removeClass('d-none');
}

// -- HELPER: Parses the JSON from SQL and formats it --
function formatChanges(jsonString) {
    if (!jsonString) return '--';

    try {
        const changes = JSON.parse(jsonString);
        let html = '<ul class="list-unstyled mb-0">';

        // Loop through keys (e.g., "LastName", "HireDate")
        for (const [field, val] of Object.entries(changes)) {
            // Formatting dates or nulls for readability
            const oldVal = val.Old === null ? '<em>Empty</em>' : val.Old;
            const newVal = val.New === null ? '<em>Empty</em>' : val.New;

            html += `
                <li class="mb-1">
                    <strong class="text-dark">${field}:</strong> 
                    <span class="text-decoration-line-through text-muted me-1">${oldVal}</span> 
                    <i class="bx bx-right-arrow-alt text-muted" style="font-size:10px"></i> 
                    <span class="text-success fw-bold">${newVal}</span>
                </li>
            `;
        }
        html += '</ul>';
        return html;

    } catch (e) {
        console.error("JSON Parse Error", e);
        return '<span class="text-danger">Error parsing changes</span>';
    }
}





