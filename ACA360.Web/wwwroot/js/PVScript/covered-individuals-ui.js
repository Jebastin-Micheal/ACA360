const CoveredIndividualsUI = (function ($) {
    let modalInstance = null;

    let $modal = null;
    let $btnSave = null;
    let $chkAll = null;
    let $chkMonths = null;

    const init = function () {
        $modal = $('.editCoveredIndividualModal');

        if ($modal.length === 0) {
            console.error("Covered Individuals Modal not found in the DOM.");
            return;
        }

        modalInstance = new bootstrap.Modal($modal[0]);

        $btnSave = $('#btnSaveCI');
        $chkAll = $('#modal-chk-all');
        $chkMonths = $('.modal-chk-month');

        bindEvents();
    };

    const bindEvents = function () {
        $(document).off('click', '.edit-ci-row').on('click', '.edit-ci-row', function () {
            populateModal($(this));
            modalInstance.show();
        });

        $chkAll.off('change').on('change', function () {
            const isChecked = $(this).prop('checked');
            $chkMonths.prop('checked', isChecked);
        });

        $chkMonths.off('change').on('change', function () {
            const allChecked = $chkMonths.length === $chkMonths.filter(':checked').length;
            $chkAll.prop('checked', allChecked);
        });

        $btnSave.off('click').on('click', saveCoverageData);
    };

    const populateModal = function ($row) {
        $('#modal-ci-index').val($row.data('index'));
        $('#modal-ci-first').val($row.data('first'));
        $('#modal-ci-middle').val($row.data('middle'));
        $('#modal-ci-last').val($row.data('last'));
        $('#modal-ci-ssn').val($row.data('ssn'));
        $('#modal-ci-dob').val($row.data('dob'));

        // Populate disable coding safely[cite: 2]
        $('#CI_chk_disable_coding').prop('checked', String($row.data('disablecoding')).toLowerCase() === "true");
        $chkAll.prop('checked', String($row.data('all')).toLowerCase() === "true");

        $chkMonths.each(function () {
            const month = $(this).data('month');
            $(this).prop('checked', String($row.data(month)).toLowerCase() === "true");
        });
    };

    const saveCoverageData = function () {
        $btnSave.prop('disabled', true).html('<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Saving...');

        const rowIndex = $('#modal-ci-index').val();
        const $targetRow = $(`.edit-ci-row[data-index="${rowIndex}"]`);

        if ($targetRow.length === 0) {
            $btnSave.prop('disabled', false).text('Save');
            return;
        }

        const isAllChecked = $chkAll.prop('checked');
        const isDisableCodingChecked = $('#CI_chk_disable_coding').prop('checked');
        const monthData = {};

        $chkMonths.each(function () {
            const $chk = $(this);
            monthData[$chk.data('month')] = $chk.prop('checked');
        });

        const apiPayload = {
            Id: String($targetRow.data('id')), 
            FilingYear: String($targetRow.data('filingyear')), 
            CI_chk_disable_coding: isDisableCodingChecked,
            AllM: isAllChecked,
            Jan: monthData['jan'],
            Feb: monthData['feb'],
            Mar: monthData['mar'],
            Apr: monthData['apr'],
            May: monthData['may'],
            Jun: monthData['jun'],
            Jul: monthData['jul'],
            Aug: monthData['aug'],
            Sep: monthData['sep'],
            Oct: monthData['oct'],
            Nov: monthData['nov'],
            Dec: monthData['dec']
        };

        $.ajax({
            url: '/Employee/UpdateCoveredIndividualCoverage',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(apiPayload),
            headers: {
                "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val()
            },
            success: function (response) {
                if (response.success) {
                    
                    // Show success SweetAlert[cite: 2]
                    Swal.fire({
                        icon: 'success',
                        title: 'Saved!',
                        text: response.message || 'Coverage updated successfully.',
                        timer: 2000,
                        showConfirmButton: false
                    });

                    // Update dataset attributes 
                    $targetRow.data('all', isAllChecked.toString()).attr('data-all', isAllChecked.toString());
                    $targetRow.data('disablecoding', isDisableCodingChecked.toString()).attr('data-disablecoding', isDisableCodingChecked.toString());

                    for (const [month, isChecked] of Object.entries(monthData)) {
                        $targetRow.data(month, isChecked.toString()).attr(`data-${month}`, isChecked.toString());
                    }

                    // Update UI Table visually
                    const updateTableCheckbox = (colIndex, isChecked) => {
                        $targetRow.find(`td:eq(${colIndex}) input[type="checkbox"]`)
                            .prop('checked', isChecked)
                            .prop('disabled', true);
                    };

                    updateTableCheckbox(5, isAllChecked);
                    updateTableCheckbox(6, monthData['jan']);
                    updateTableCheckbox(7, monthData['feb']);
                    updateTableCheckbox(8, monthData['mar']);
                    updateTableCheckbox(9, monthData['apr']);
                    updateTableCheckbox(10, monthData['may']);
                    updateTableCheckbox(11, monthData['jun']);
                    updateTableCheckbox(12, monthData['jul']);
                    updateTableCheckbox(13, monthData['aug']);
                    updateTableCheckbox(14, monthData['sep']);
                    updateTableCheckbox(15, monthData['oct']);
                    updateTableCheckbox(16, monthData['nov']);
                    updateTableCheckbox(17, monthData['dec']);

                    modalInstance.hide();
                } else {
                    // Show warning SweetAlert if backend returned false for success[cite: 2]
                    Swal.fire({
                        icon: 'warning',
                        title: 'Warning',
                        text: response.message || 'Failed to save changes.'
                    });
                }
            },
            error: function (xhr) {
                console.error("API Error", xhr);
                
                // Attempt to parse server error string if available
                let errorMsg = "An error occurred while saving the data.";
                if(xhr.responseJSON && xhr.responseJSON.message) {
                    errorMsg = xhr.responseJSON.message;
                }

                // Show error SweetAlert[cite: 2]
                Swal.fire({
                    icon: 'error',
                    title: 'Error',
                    text: errorMsg
                });
            },
            complete: function () {
                $btnSave.prop('disabled', false).text('Save');
            }
        });
    };

    return {
        init: init
    };
})(jQuery);

$(document).ready(function () {
    CoveredIndividualsUI.init();
});