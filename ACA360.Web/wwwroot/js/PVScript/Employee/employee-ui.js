// Location: wwwroot/assets/js/employee-ui.js
const EmployeeUi = (function () {
    return {
        showButtonSpinner: function ($button) {
            $button.prop('disabled', true)
                .data('original-text', $button.html())
                .html('<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>Saving...');
        },

        resetButton: function ($button) {
            $button.prop('disabled', false)
                .html($button.data('original-text'));
        },

        updatePartialContainer: function (containerSelector, htmlContent) {
            $(containerSelector).html(htmlContent);
        },

        safelyCloseModalAndAlert: function (modalId, successMessage) {
            const modalEl = document.getElementById(modalId);
            if (!modalEl) return;

            // Wait for the modal fade out transition to finish before showing SweetAlert
            $(modalEl).one('hidden.bs.modal', function () {
                Swal.fire({
                    title: 'Success!',
                    text: successMessage,
                    icon: 'success',
                    customClass: {
                        confirmButton: 'btn btn-primary'
                    },
                    buttonsStyling: false
                });
            });

            // Trigger the hide
            const bsModal = bootstrap.Modal.getInstance(modalEl);
            if (bsModal) {
                bsModal.hide();
            } else {
                // Fallback if instance isn't tracked properly
                $(modalEl).modal('hide');
            }
        },

        disposeModalsInContainer: function (containerSelector) {
            // Destroy old modal instances in memory before replacing DOM
            $(containerSelector).find('.modal').each(function () {
                const bsModal = bootstrap.Modal.getInstance(this);
                if (bsModal) bsModal.dispose();
            });
        },

        initModalGuards: function () {
            // Prevent child modals from closing parent modals
            $(document).on('hide.bs.modal', '.child-modal', function (e) {
                e.stopPropagation();
            });
        }
    };
})();




// ==========================================
// GLOBAL UI TOGGLE FUNCTIONS (For Inline HTML OnClick Events)
// ==========================================

window.toggleSection = function (sectionId, isEditing, isCancel) {
    // Prevent accidental page reloads if clicking the button triggers a form submit
    if (window.event) { window.event.preventDefault(); }

    var $section = $("#" + sectionId);

    // CRITICAL FIX: If the modal is open, strictly target the section INSIDE the modal
    if ($('#basicModal').hasClass('show')) {
        $section = $('#basicModal').find("#" + sectionId);
    } else if ($('.modal.show').length > 0) {
        $section = $('.modal.show').find("#" + sectionId);
    }

    // Fallback
    if ($section.length === 0) $section = $("#" + sectionId).last();

    // 1. Toggle UI states
    $section.find('.btn-edit').toggleClass('d-none', isEditing);
    $section.find('.action-buttons').toggleClass('d-none', !isEditing);
    $section.find('.delete-btn').toggleClass('d-none', !isEditing);

    // 2. Enable/Disable Inputs safely
    if (isEditing) {
        // Keeps Readonly fields and the Covered Individuals table locked
        $section.find('input, select, textarea')
            .not('[readonly]')
            .not('#coveredIndividualsTable input')
            .prop('disabled', false);
    } else {
        $section.find('input, select, textarea').prop('disabled', true);
    }

    // 3. Handle Revert / Commit Logic for Basic Info
    if (sectionId === 'section_basic' && !isEditing) {
        if (isCancel === true) {
            $section.find('input:not(:checkbox):not(:radio), textarea').each(function () { this.value = this.defaultValue; });
            $section.find('input[type="checkbox"], input[type="radio"]').each(function () { this.checked = this.defaultChecked; });
            $section.find('select').each(function () { $(this).find('option').each(function () { this.selected = this.defaultSelected; }); });
            $section.find('.select2, .select2-aca').trigger('change.select2');
            $section.find('select:not(.select2):not(.select2-aca)').trigger('change');
        } else {
            $section.find('input:not(:checkbox):not(:radio), textarea').each(function () { this.defaultValue = this.value; });
            $section.find('input[type="checkbox"], input[type="radio"]').each(function () { this.defaultChecked = this.checked; });
            $section.find('select').each(function () { var currentVal = $(this).val(); $(this).find('option').each(function () { this.defaultSelected = (this.value === currentVal); }); });
        }
    }
};

window.removeRow = function (button) {
    const $row = $(button).closest('.dynamic-row');
    const $container = $row.closest('[id^="container_"]');

    // Prevent deleting the very last row so the user always has at least one row to edit
    if ($container.find('.dynamic-row').length > 1) {
        $row.remove();
    } else {
        // If it's the last row, just clear the values instead of destroying the HTML
        $row.find('input, select, textarea').not('[readonly]').val('');
        $row.find('input[type="checkbox"]').prop('checked', false);

        // Set hidden Id fields to 0 or empty so the backend knows it is empty/deleted
        $row.find('input[data-key="Id"], input[name="Id"]').val('0');
    }
};

window.addDynamicRow = function (sectionType) {
    const $container = $('#container_' + sectionType);
    const $lastRow = $container.find('.dynamic-row').last();

    if ($lastRow.length > 0) {
        // Clone the last row
        const $newRow = $lastRow.clone();

        // Clear all values in the new cloned row
        $newRow.find('input, select, textarea').not('[readonly]').val('');
        $newRow.find('input[type="checkbox"]').prop('checked', false);

        // Ensure the internal Database ID is reset to 0 so the C# backend treats it as a new INSERT instead of an UPDATE
        $newRow.find('input[data-key="Id"]').val('0');

        // Append it to the container
        $container.append($newRow);
    }
};