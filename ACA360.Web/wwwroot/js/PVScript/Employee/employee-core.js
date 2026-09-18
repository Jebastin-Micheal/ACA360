// Location: wwwroot/assets/js/employee-core.js
$(document).ready(function () {

    // 1. Initialize UI guards
    EmployeeUi.initModalGuards();

    // 2. Globally capture the Employee ID when the page loads
    const initialId = $('#HiddenEmployeeId').val();
    if (initialId) {
        EmployeeState.setEmployeeId(initialId);
    }

    // 3. Generic Form Submit Handler (Works for all partials)
    //$(document).on('submit', '.employee-section-form', function (e) {
    //    e.preventDefault();

    //    const $form = $(this);
    //    const $btn = $form.find('button[type="submit"]');
    //    const actionUrl = $form.attr('action');
    //    const sectionContainerId = $form.data('target-container');
    //    const modalId = $form.closest('.child-modal').attr('id');
    //    const listName = $form.data('list-name');

    //    if ($btn.prop('disabled')) return; // Race condition guard

    //    EmployeeUi.showButtonSpinner($btn);

    //    // --- STEP 1: Initialize the payload ---
    //    let payload = {
    //        EmployeeID: EmployeeState.getEmployeeId()
    //    };

    //    // --- STEP 2: Handle Standard Inputs (Basic Info, etc.) ---
    //    const $disabledInputs = $form.find(':input:disabled');
    //    $disabledInputs.prop('disabled', false); // Enable to read values

    //    const standardInputs = $form.serializeArray();

    //    $disabledInputs.prop('disabled', true); // Immediately disable again

    //    $.each(standardInputs, function () {
    //        // Convert empty strings to null to prevent C# [FromBody] parsing crashes
    //        let val = this.value.trim() === '' ? null : this.value;
    //        payload[this.name] = val;
    //    });

    //    // Capture standalone checkboxes missing from serializeArray()
    //    $form.find('input[type="checkbox"][name]').each(function () {
    //        payload[this.name] = $(this).is(':checked') ? 1 : 0;
    //    });

    //    // --- STEP 3: Handle Dynamic Rows (Medical, Payroll, Dependents, etc.) ---
    //    if (listName) {
    //        let listData = [];

    //        $form.find('.dynamic-row').each(function () {
    //            let rowObj = {};

    //            $(this).find('.data-field').each(function () {
    //                let key = $(this).data('key');
    //                let val;

    //                if ($(this).is(':checkbox')) {
    //                    val = $(this).is(':checked') ? 1 : 0;
    //                } else {
    //                    val = $(this).val();
    //                    val = (val && val.trim() === '') ? null : val;
    //                }

    //                if (key) {
    //                    rowObj[key] = val;
    //                }
    //            });

    //            listData.push(rowObj);
    //        });

    //        payload[listName] = listData;
    //    }

    //    // --- STEP 4: Send the formatted JSON payload to the API ---
    //    EmployeeApi.saveSection(actionUrl, payload)
    //        .then(function (updatedHtml) {
    //            // Swap out the old partial view with the fresh HTML from the server
    //            EmployeeUi.disposeModalsInContainer(sectionContainerId);
    //            EmployeeUi.updatePartialContainer(sectionContainerId, updatedHtml);

    //            if (modalId) {
    //                // Close the modal and show success
    //                EmployeeUi.safelyCloseModalAndAlert(modalId, 'Record updated successfully.');
    //            } else {
    //                // Show a toast/alert for inline sections (like Medical/Payroll)
    //                Swal.fire({
    //                    title: 'Saved!',
    //                    text: 'Record updated successfully.',
    //                    icon: 'success',
    //                    timer: 2000,
    //                    showConfirmButton: false,
    //                    customClass: { confirmButton: 'btn btn-primary' },
    //                    buttonsStyling: false
    //                });
    //            }
    //        })
    //        .catch(function (error) {
    //            Swal.fire({
    //                title: 'Error',
    //                text: 'Failed to update record. Please try again.',
    //                icon: 'error',
    //                customClass: { confirmButton: 'btn btn-primary' },
    //                buttonsStyling: false
    //            });
    //        })
    //        .finally(function () {
    //            EmployeeUi.resetButton($btn);
    //        });
    //});


    // 3. Generic Form Submit Handler (Works for all partials)
    $(document).on('submit', '.employee-section-form', function (e) {
        e.preventDefault();

        const $form = $(this);
        const $btn = $form.find('button[type="submit"]');
        const actionUrl = $form.attr('action');
        const sectionContainerId = $form.data('target-container');
        const modalId = $form.closest('.modal').attr('id');
        const listName = $form.data('list-name');

        if ($btn.prop('disabled')) return; // Race condition guard

        // --- STEP 1: Initialize the root payload ---
        let payload = {
            EmployeeID: parseInt(EmployeeState.getEmployeeId(), 10) || 0,
            EmployerId: parseInt($('#EmployerId').val(), 10) || 0,
            FilingYear: parseInt($('#FilingYear').val(), 10) || 0
        };

        function castFieldValue(key, val, type) {
            if (type === 'checkbox') return (val === true || val === 'on' || val === 'true' || val === 1) ? 1 : 0;
            if (key === 'Id' && String(val).indexOf('-') !== -1) return String(val);
            if (key && (key === 'Id' || key.endsWith('Id') || key.endsWith('ID'))) return parseInt(val, 10) || 0;
            if (type === 'number') return (val === '' || val == null) ? null : parseFloat(val);
            const boolIntFields = ['IsForeign', 'IsExPatriot', 'IsCorrected', 'IsMedicalEnrolled', 'IsCOBRAEnrolled', 'IsUnionMember', 'IsRetireeEnrolled'];
            if (boolIntFields.includes(key)) return parseInt(val, 10) || 0;
            return (val === '') ? null : val;
        }

        // --- STEP 2: Handle Standard Inputs ---
        const $disabledInputs = $form.find(':input:disabled');
        $disabledInputs.prop('disabled', false);
        const standardInputs = $form.serializeArray();
        $disabledInputs.prop('disabled', true);

        $.each(standardInputs, function () {
            let type = $form.find('[name="' + this.name + '"]').attr('type');
            payload[this.name] = castFieldValue(this.name, this.value, type);
        });

        $form.find('input[type="checkbox"][name]').each(function () {
            payload[this.name] = $(this).is(':checked') ? 1 : 0;
        });

        // --- STEP 3: Handle Dynamic Rows ---
        if (listName) {
            let listData = [];
            $form.find('.dynamic-row').each(function () {
                let rowObj = {};
                $(this).find('.data-field').each(function () {
                    let key = $(this).data('key');
                    let type = $(this).attr('type') || 'text';
                    let rawVal = $(this).is(':checkbox') ? $(this).is(':checked') : $(this).val();
                    if (key) rowObj[key] = castFieldValue(key, rawVal, type);
                });

                if (!rowObj.hasOwnProperty('EmployeeCodeId') || rowObj['EmployeeCodeId'] === 0) {
                    rowObj['EmployeeCodeId'] = parseInt($('#EmployeeCodeId').val(), 10) || 0;
                }
                listData.push(rowObj);
            });
            payload[listName] = listData;
        }

        // --- STEP 4: Confirmation Dialog & Save ---
        Swal.fire({
            title: 'Save Changes?',
            text: "Are you sure you want to save this record?",
            icon: 'question',
            showCancelButton: true,
            confirmButtonText: 'Yes, save it',
            cancelButtonText: 'Cancel',
            customClass: { confirmButton: 'btn btn-success me-2', cancelButton: 'btn btn-label-secondary' },
            buttonsStyling: false
        }).then((result) => {
            if (result.isConfirmed) {
                EmployeeUi.showButtonSpinner($btn); // Show spinner only after confirmed

                EmployeeApi.saveSection(actionUrl, payload)
                    .then(function (updatedHtml) {
                        EmployeeUi.disposeModalsInContainer(sectionContainerId);
                        EmployeeUi.updatePartialContainer(sectionContainerId, updatedHtml);

                        if (modalId) {
                            EmployeeUi.safelyCloseModalAndAlert(modalId, 'Record updated successfully.');
                        } else {
                            Swal.fire({
                                title: 'Saved!',
                                text: 'Record updated successfully.',
                                icon: 'success',
                                timer: 2000,
                                showConfirmButton: false
                            });
                        }
                    })
                    .catch(function (error) {
                        Swal.fire({
                            title: 'Error',
                            text: 'Failed to update record. Please try again.',
                            icon: 'error',
                            customClass: { confirmButton: 'btn btn-primary' },
                            buttonsStyling: false
                        });
                    })
                    .finally(function () {
                        EmployeeUi.resetButton($btn);
                    });
            }
        });
    });


    // 4. View Switching Handler (Table vs Split View)
    $(document).on('click', '.btn-switch-view', function () {
        const viewUrl = $(this).data('url');
        const targetContainer = '#mainViewContainer';

        EmployeeApi.loadPartialView(viewUrl)
            .then(function (html) {
                EmployeeUi.disposeModalsInContainer(targetContainer);
                EmployeeUi.updatePartialContainer(targetContainer, html);
            });
    });




    // ==========================================
    // EMPLOYEE LOADING, EDITING, AND DELETING
    // ==========================================

    // 1. Handle "Add" Employee click
    $(document).on('click', '.addEmployeeBtn', function (e) {
        e.stopPropagation();

        var token = $('input[name="__RequestVerificationToken"]').val();

        // Clear state for new employee
        EmployeeState.setEmployeeId('');

        var $modal = $("#basicModal");
        $modal.find(".modal-title").text("Add Employee");
        $modal.find(".modal-body").html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');
        bootstrap.Modal.getOrCreateInstance('#basicModal').show();

        $.ajax({
            url: '/Employee/Add_New',
            type: 'POST',
            data: { __RequestVerificationToken: token },
            success: function (html) {
                var $body = $modal.find(".modal-body");
                $body.html(html);
                loadEmployers_dropdwon();
                initModalUI($body);
            },
            error: function () {
                $modal.find(".modal-body").html('<p class="text-danger">Failed to load details.</p>');
            }
        });
    });

    // 2. Handle Split View Click (Table Row Click)
    $(document).on('click', '.employee-item', function () {
        var isSplitMode = !$('#mainDetailColumn').hasClass('d-none');

        if (isSplitMode) {
            $('.employee-active').removeClass('table-active');
            $(this).closest('.employee-active').addClass('table-active');

            var empId = $(this).data('id');

            // setEmployeeIdAndSync updates both the stored ID and the nav index
            if (typeof EmployeeState !== 'undefined') EmployeeState.setEmployeeIdAndSync(empId);

            var $splitContainer = $('#commonDetailContainer');
            $splitContainer.html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');

            $.ajax({
                url: '/Employee/GetEmployeeBasicDetails',
                type: 'GET',
                data: { employeeId: empId },
                success: function (data) {
                    $splitContainer.html(data);

                    // CRITICAL FIX: Run the calculations specifically on the Split Container
                    if (typeof window.calculateRiskAll === 'function') window.calculateRiskAll($splitContainer);
                    if (typeof window.fetchPenaltyDetails === 'function') window.fetchPenaltyDetails($splitContainer);
                    if (typeof window.refreshMonthCardDisplay === 'function') window.refreshMonthCardDisplay($splitContainer);

                    if (typeof initModalUI === 'function') initModalUI($splitContainer);

                    // Refresh nav bar to reflect the newly selected employee
                    if (typeof window.updateNavBar === 'function') window.updateNavBar();
                    if (window.EmployeeCompare) window.EmployeeCompare.onDetailLoaded(empId, window.__cmpEmployeeYear);
                }
            });
        }
    });

    // 3. Handle "Edit" and "View" icon click (Table View & Modal Triggers)
    $(document).on('click', '.employee-edit-btn, .employee-row', function (e) {
        e.stopPropagation();

        // Safely check for either data-employee-id or data-id depending on how the table is rendered
        var employeeId = $(this).data('employee-id') || $(this).data('id');
        if (!employeeId) {
            console.error("Employee ID is missing on the clicked row.");
            return;
        }

        var isEditMode = $(this).hasClass('employee-edit-btn');

        // Update the state manager
        if (typeof EmployeeState !== 'undefined') {
            EmployeeState.setEmployeeIdAndSync(employeeId);
        }

        var $modal = $("#basicModal");
        $modal.find(".modal-title").text(isEditMode ? "Edit Employee" : "View Employee");

        // Prevent DOM ID collision: If split view is open, empty it before opening the modal
        if (!$('#mainDetailColumn').hasClass('d-none')) {
            $('#commonDetailContainer').empty();
            $('.employee-active').removeClass('table-active');
        }

        $modal.find(".modal-body").html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');
        bootstrap.Modal.getOrCreateInstance('#basicModal').show();

        $.ajax({
            url: '/Employee/GetEmployeeBasicDetails', // MUST NOT HAVE 'Async'
            type: 'GET',
            data: { employeeId: employeeId },
            success: function (html) {
                var $body = $modal.find(".modal-body");
                $body.html(html);

                if (typeof window.calculateRiskAll === 'function') window.calculateRiskAll($body);
                if (typeof window.fetchPenaltyDetails === 'function') window.fetchPenaltyDetails($body);
                if (typeof window.refreshMonthCardDisplay === 'function') window.refreshMonthCardDisplay($body);

                if (typeof initModalUI === 'function') initModalUI($body);
                loadEmployers_dropdwon();
                // FIX: Use native .click() to trigger inline HTML handlers
                if (isEditMode) {
                    setTimeout(function () {
                        $body.find('.btn-edit').each(function () {
                            this.click(); // MUST be 'this.click()' not '$(this).click()'
                        });
                    }, 100);
                }
                // Refresh nav bar to show correct position
                if (typeof window.updateNavBar === 'function') window.updateNavBar();
            },
            error: function (xhr) {
                console.error("Failed to load modal details", xhr);
                $modal.find(".modal-body").html('<p class="text-danger text-center mt-3">Failed to load details.</p>');
            }
        });
    });

    // 4. Handle "Delete" click
    $(document).on('click', '.delete-employee-btn', function (e) {
        e.stopPropagation();

        var empId = $(this).data('employee-id');

        // CRITICAL: Must grab the token for POST requests
        var token = $('input[name="__RequestVerificationToken"]').val();

        Swal.fire({
            title: 'Are you sure?',
            text: "You won't be able to revert this!",
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, delete it!',
            customClass: {
                confirmButton: 'btn btn-danger me-2',
                cancelButton: 'btn btn-label-secondary'
            },
            buttonsStyling: false
        }).then((result) => {
            if (result.isConfirmed) {
                $.ajax({
                    url: '/Employee/DeleteEmployee',
                    type: 'POST',
                    data: {
                        id: empId,
                        __RequestVerificationToken: token // Attached token
                    },
                    success: function (response) {
                        if (response.success) {
                            Swal.fire({
                                title: 'Deleted!',
                                text: response.message,
                                icon: 'success',
                                customClass: { confirmButton: 'btn btn-primary' },
                                buttonsStyling: false
                            });
                            if (typeof GridManager !== 'undefined') GridManager.reload();
                        } else {
                            Swal.fire('Failed!', response.message, 'error');
                        }
                    },
                    error: function () {
                        Swal.fire('Error!', 'Could not delete the employee.', 'error');
                    }
                });
            }
        });
    });

    // 5. Shared UI Initializer
    window.initModalUI = function ($container) {
        $container = $container || $(document);

        // 1. Re-initialize Perfect Scrollbar on the new HTML
        const scrollElements = [
            { selector: '.perfectscroller_bottom', axis: { suppressScrollX: true } },
            { selector: '.perfectscroller_left', axis: { suppressScrollY: true } },
            { selector: '.perfectscroller .table-responsive', axis: { suppressScrollY: true } },
            { selector: '.perfectscroller_dependents', axis: { suppressScrollY: true } }
        ];

        scrollElements.forEach(item => {
            // Look inside the container, or check if the container itself is the scroll area
            let el = $container.find(item.selector)[0] || $container.filter(item.selector)[0];

            if (el && typeof PerfectScrollbar !== 'undefined') {
                new PerfectScrollbar(el, {
                    wheelSpeed: 1,
                    wheelPropagation: true,
                    minScrollbarLength: 20,
                    ...item.axis
                });
            }
        });
        // 2. Re-initialize Select2 (if explicitly used)
        if ($.fn.select2) {
            $container.find('.select-dropdown, .select2, .select2-aca').not('.swal2-select').each(function () {
                $(this).select2({
                    dropdownParent: $(this).closest('.modal').length ? $(this).closest('.modal') : $(document.body),
                    width: '100%'
                });
            });
        }

        // ==========================================
        // 3. FIX FOR SCRIPT-LOADED EMPLOYER DROPDOWN
        // ==========================================
        var $employerDropdown = $container.find('#employerDropdown');
        if ($employerDropdown.length > 0) {

            // 👉 IMPORTANT: If you have a specific function that fetches the options via AJAX, 
            loadEmployers_dropdwon(); 

            // Grab the ID we need to select from the HTML data attribute
            var selectedId = $employerDropdown.data('selected-id');

            if (selectedId) {
                // Use a slight delay to ensure your custom script has finished loading the <option> tags
                setTimeout(function () {
                    $employerDropdown.val(selectedId).trigger('change');
                }, 150);
            }
        }      
    };

    function loadEmployers_dropdwon() {
        $.ajax({
            url: '/Employee/drp_employer',
            type: 'GET',
            success: function (data) {
                var $dropdown = $('#employerDropdown');

                // Read the pre-selected ID from the HTML attribute we added
                var selectedEmployerId = String($dropdown.data('selected-id') || '');

                $dropdown.empty();
                $dropdown.append('<option value="">-- Select Employer --</option>');

                if (Array.isArray(data) && data.length > 0) {
                    $.each(data, function (index, item) {
                        var id = String(item.id || item.Id);
                        var name = item.name || item.Name;
                        var cleanName = name.replace(' (Primary)', '').replace(' (Affiliate)', '');

                        var $option = $('<option></option>').val(id).text(cleanName);

                        // Compare the current option's string ID with the model's string ID
                        if (id === selectedEmployerId && selectedEmployerId !== '') {
                            $option.prop('selected', true);
                        }

                        $dropdown.append($option);
                    });
                } else {
                    $dropdown.append('<option value="" disabled>No employers found</option>');
                }
            },
            error: function (xhr, status, error) {
                console.error("Error loading employers:", error);
            }
        });
    }


});

// Location: wwwroot/assets/js/employee-core.js

var monthList = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
var currentRiskMonth = "";

// ==========================================
// 0. DUPLICATE-ID GUARD
// ==========================================
function cssEscape(id) {
    return (window.CSS && CSS.escape) ? CSS.escape(id) : String(id).replace(/([ #;&,.+*~':"!^$[\]()=>|\/@])/g, '\\$1');
}

window.clearDuplicateIds = function ($scope) {
    $scope.find('[id]').each(function () {
        var id = this.id;
        if (!id) return;
        $('[id="' + cssEscape(id) + '"]').each(function () {
            if (this !== $scope[0] && !$.contains($scope[0], this)) {
                $(this).attr('data-dup-id', id).removeAttr('id');
            }
        });
    });
};

window.restoreDuplicateIds = function () {
    $('[data-dup-id]').each(function () {
        $(this).attr('id', $(this).attr('data-dup-id')).removeAttr('data-dup-id');
    });
};

window.safelyShowSubModal = function (modalId) {
    var modalEl = document.getElementById(modalId);
    if (!modalEl) return null;

    if (modalEl.parentElement && modalEl.parentElement.tagName !== 'BODY') {
        document.body.appendChild(modalEl);
    }

    var bsModal = bootstrap.Modal.getOrCreateInstance(modalEl);
    bsModal.show();
    return bsModal;
};

// ==========================================
// 1. UI TOGGLES (FIXED: Duplicate ID targeting)
// ==========================================
window.toggleSection = function (sectionId, isEditing, isCancel) {
    // CRITICAL FIX: Ensure we target the section INSIDE the open modal to avoid Split View ID collisions
    var $section = $("#" + sectionId);
    if ($('.modal.show').length > 0) {
        $section = $('.modal.show').find("#" + sectionId);
    }
    // Fallback if modal isn't open
    if ($section.length === 0) $section = $("#" + sectionId).last();

    // 1. Toggle UI states
    $section.find('.btn-edit').toggleClass('d-none', isEditing);
    $section.find('.action-buttons').toggleClass('d-none', !isEditing);
    $section.find('.delete-btn').toggleClass('d-none', !isEditing);

    // 2. Enable/Disable Inputs safely
    if (isEditing) {
        // Snapshot current values first so a later Cancel reverts to what is on
        // screen now (needed for dynamically-populated selects like the Employer
        // dropdown — see _snapshotSectionDefaults).
        if (typeof _snapshotSectionDefaults === 'function') _snapshotSectionDefaults($section);

        // Do not enable Readonly fields (like SSN) or Covered Individuals checkboxes
        $section.find('input, select, textarea')
            .not('[readonly]')
            .not('#coveredIndividualsTable input')
            .prop('disabled', false);
    } else {
        $section.find('input, select, textarea').prop('disabled', true);
        // Clear per-field edit highlights when the section locks.
        $section.find('.field-editing').removeClass('field-editing');
    }

    // 3. Handle Revert / Commit Logic for Basic Info
    if (sectionId === 'section_basic' && !isEditing) {
        if (isCancel === true) {
            $section.find('input:not(:checkbox):not(:radio), textarea').each(function () {
                this.value = this.defaultValue;
            });
            $section.find('input[type="checkbox"], input[type="radio"]').each(function () {
                this.checked = this.defaultChecked;
            });
            $section.find('select').each(function () {
                $(this).find('option').each(function () {
                    this.selected = this.defaultSelected;
                });
            });
            $section.find('.select2, .select2-aca').trigger('change.select2');
            $section.find('select:not(.select2):not(.select2-aca)').trigger('change');
        } else {
            $section.find('input:not(:checkbox):not(:radio), textarea').each(function () {
                this.defaultValue = this.value;
            });
            $section.find('input[type="checkbox"], input[type="radio"]').each(function () {
                this.defaultChecked = this.checked;
            });
            $section.find('select').each(function () {
                var currentVal = $(this).val();
                $(this).find('option').each(function () {
                    this.defaultSelected = (this.value === currentVal);
                });
            });
        }
    }
};

// ==========================================
// 2. SAVE GENERIC (FIXED: HTML Replacement)
// ==========================================
window.saveGeneric = function (sectionId, apiUrl, listKeyName, event) {
    // CRITICAL FIX: Stop the browser from refreshing the page and closing the modal
    if (event) { event.preventDefault(); }
    if (window.event) { window.event.preventDefault(); }

    listKeyName = listKeyName || null;
    var payload = {};

    // Target the visible section inside the modal
    var $section = $('#' + sectionId);
    if ($('.modal.show').length > 0) {
        $section = $('.modal.show').find("#" + sectionId);
    } else if ($section.length === 0) {
        $section = $("#" + sectionId).last();
    }

    var $form = $section.closest('form');
    if ($form.length === 0) $form = $section;

    var isValid = true;

    $section.find('.is-invalid').removeClass('is-invalid');
    $section.find('.invalid-feedback').remove();

    if (sectionId === 'section_basic' && !listKeyName) {
        var requiredFields = [
            { name: 'FirstName', label: 'First Name' },
            { name: 'LastName', label: 'Last Name' },
            { name: 'SSN', label: 'SSN' },
            { name: 'Birthday', label: 'Date of Birth' }
        ];

        $.each(requiredFields, function (index, field) {
            var $input = $form.find('[name="' + field.name + '"]');
            if ($input.length > 0) {
                var val = $input.val() ? $input.val().trim() : "";
                if (val === "") {
                    $input.addClass('is-invalid');
                    isValid = false;
                }
            }
        });
    }

    if (sectionId === 'section_basic' && isValid) {
        var $dobInput = $form.find('input[name="Birthday"]');
        var dobVal = $dobInput.val();

        if (dobVal) {
            var dobDate = new Date(dobVal + 'T00:00:00');
            var today = new Date();
            today.setHours(0, 0, 0, 0);

            if (dobDate > today) {
                $dobInput.addClass('is-invalid');
                $dobInput.after('<div class="invalid-feedback d-block">Date of Birth cannot be a future date.</div>');
                isValid = false;
            }
        }
    }

    if (!isValid) {
        var $firstError = $section.find('.is-invalid').first();
        if ($firstError.length > 0) {
            $('html, body').animate({ scrollTop: $firstError.offset().top - 200 }, 300);
            $firstError.focus();
        }
        return;
    }

    function toInt(val) {
        if (val === '' || val === null || val === undefined) return 0;
        var n = parseInt(val, 10);
        return isNaN(n) ? 0 : n;
    }

    function toFloat(val) {
        if (val === '' || val === null || val === undefined) return null;
        var n = parseFloat(val);
        return isNaN(n) ? null : n;
    }

    function castField($el) {
        var name = ($el.data('key') || $el.attr('name') || '').toString();
        var val = $el.val();
        var type = ($el.attr('type') || '').toLowerCase();

        if (type === 'checkbox') return $el.is(':checked') ? 1 : 0;
        if (name === 'Id' && String(val).indexOf('-') !== -1) return String(val);
        if (name === 'Id' || name.endsWith('Id') || name.endsWith('ID')) return toInt(val);
        if (type === 'number') return toFloat(val);

        var boolIntFields = ['IsForeign', 'IsExPatriot','IsCorrected', 'IsMedicalEnrolled', 'IsCOBRAEnrolled', 'IsUnionMember', 'IsRetireeEnrolled'];
        if (boolIntFields.indexOf(name) !== -1) {
            if (type === 'checkbox') return $el.is(':checked') ? 1 : 0;
            return toInt(val);
        }

        return (val === '') ? null : val;
    }

    // CASE A — List rows
    if (listKeyName) {
        var listData = [];
        var $rows = $section.find('.dynamic-row');
        if ($rows.length === 0) $rows = $section.find('tbody tr');

        $rows.each(function () {
            var rowObj = {};
            $(this).find('.data-field').each(function () {
                var key = $(this).data('key');
                if (key) rowObj[key] = castField($(this));
            });

            if (!rowObj.hasOwnProperty('EmployeeCodeId') || rowObj['EmployeeCodeId'] === 0) {
                rowObj['EmployeeCodeId'] = toInt($('#EmployeeCodeId').val());
            }

            if (rowObj['Id'] === '' || rowObj['Id'] == null) {
                rowObj['Id'] = "0";
            }

            listData.push(rowObj);
        });

        payload[listKeyName] = listData;
        payload['EmployeeID'] = $('#EmployeeID').val();
    }
    // CASE B — Flat form
    else {
        var $disabledInputs = $form.find(':input:disabled');
        $disabledInputs.prop('disabled', false);

        // BULLETPROOF FIX: Find all inputs directly. This works perfectly on both <form> and <div> wrappers.
        var formData = $form.find(':input').serializeArray();

        $.each(formData, function () {
            var val = this.value;
            var $input = $form.find('[name="' + this.name + '"]');

            if ($input.attr('type') === 'checkbox') {
                val = $input.is(':checked') ? 1 : 0;
            } else if ($input.attr('type') === 'number') {
                val = (val === "") ? null : parseFloat(val);
            } else if (this.name === "State" || this.name === "Country") {
                val = (val === "") ? null : parseInt(val, 10);
            } else if (this.name === "IsForeign" || this.name === "IsExPatriot" ||this.name === "ConsentedToElectronic" || this.name === "getForm" || this.name === "GetForm" || this.name === "IsCorrected") {
                val = (val === "" || val === null) ? 0 : parseInt(val, 10);
            } else if ($input.attr('type') === 'date' && val === "") {
                val = null;
            } else if (val === "") {
                val = null;
            }

            // 2. Map to Payload (Extracting both ID and Text for dropdowns)
            if (this.name === "State") {
                payload["StateId"] = val; // Sends the int (e.g., 34)
                // Extract the text of the selected option (e.g., "FL")
                var stateText = $input.find('option:selected').text();
                var isValidState = stateText && !stateText.toUpperCase().includes("SELECT");
                payload["State"] = isValidState ? stateText.trim() : null;

            } else if (this.name === "Country") {
                payload["CountryId"] = val; // Sends the int (e.g., 1)
                // Extract the text of the selected option (e.g., "USA")
                var countryText = $input.find('option:selected').text();
                var isValidCountry = countryText && !countryText.toUpperCase().includes("SELECT");
                payload["Country"] = isValidCountry ? countryText.trim() : null;
            }
            else {
                payload[this.name] = val;
            }
        });

        $disabledInputs.prop('disabled', true);

        // Fallback for unchecked checkboxes
        $form.find('input[type="checkbox"]').each(function () {
            if (!payload.hasOwnProperty(this.name)) {
                payload[this.name] = 0;
            }
        });

        // SINGLE-FIELD EDIT GUARD: declare which field(s) were opened via double-click so the
        // server keeps every other field at its stored value (an Inspect-enabled field is rejected).
        // Only fields carrying .field-editing count; full "Edit" mode has none → no restriction.
        var __editedNames = $section.find('.field-editing').map(function () {
            return $(this).attr('name') || $(this).data('key');
        }).get().filter(function (n) { return !!n; });
        if (__editedNames.length > 0) {
            payload['EditedFields'] = __editedNames;
        }
    }

    // --- NEW: Add the Confirmation Dialog here ---
    Swal.fire({
        title: 'Save Changes?',
        text: "Are you sure you want to save this record?",
        icon: 'question',
        showCancelButton: true,
        confirmButtonText: 'Yes, save it',
        cancelButtonText: 'Cancel',
        customClass: { confirmButton: 'btn btn-success me-2', cancelButton: 'btn btn-label-secondary' },
        buttonsStyling: false
    }).then((result) => {
        if (result.isConfirmed) {
            var $btn = $section.find('.btn-success');
            var orgHtml = $btn.html();
            $btn.prop('disabled', true).html('<span class="spinner-border spinner-border-sm"></span> Saving...');
            var token = $('input[name="__RequestVerificationToken"]').first().val();
            console.log(JSON.stringify(payload));

            $.ajax({
                url: apiUrl,
                type: 'POST',
                contentType: 'application/json',
                headers: { 'RequestVerificationToken': token },
                data: JSON.stringify(payload),
                success: function (r, textStatus, xhr) {
                    $btn.prop('disabled', false).html(orgHtml);

                    var contentType = xhr.getResponseHeader("content-type") || "";

                    // CRITICAL FIX: Inject HTML without closing the modal
                    if (contentType.indexOf("html") > -1) {
                        // 1. Replace the old HTML with the new HTML
                        $section.replaceWith(r);

                        // 2. CRITICAL FIX: Find the newly injected HTML on the screen
                        var $newSection = $('#' + sectionId);
                        if ($('.modal.show').length > 0) {
                            $newSection = $('.modal.show').find("#" + sectionId);
                        } else if ($newSection.length === 0) {
                            $newSection = $('#' + sectionId).last();
                        }

                        // 3. Lock the new section
                        window.toggleSection(sectionId, false, false);

                        // 4. Re-apply Perfect Scrollbars and Dropdowns to the new section
                        if (typeof window.initModalUI === 'function') {
                            window.initModalUI($newSection);
                        }

                        // 5. Notify the user (non-blocking toast)
                        Swal.fire({
                            title: 'Saved!',
                            text: 'Record updated successfully.',
                            icon: 'success',
                            timer: 2000,
                            showConfirmButton: false
                        });
                    } else if (r.success) {
                        Swal.fire({
                            title: 'Saved!',
                            text: 'Record updated successfully.',
                            icon: 'success',
                            timer: 2000,
                            showConfirmButton: false
                        }).then(() => {
                            window.toggleSection(sectionId, false, false);
                        });
                    } else {
                        Swal.fire({ title: 'Error!', text: r.message || 'Unknown error', icon: 'error' });
                    }
                },
                error: function (xhr) {
                    $btn.prop('disabled', false).html(orgHtml);
                    Swal.fire('Error', 'System error saving data. Check console for details.', 'error');
                }
            });
        }
    });
};

// ==========================================
// 3. DYNAMIC ROW TEMPLATES & LOGIC
// ==========================================
window.addDynamicRow = function (type) {
    var planData = (window.EmployeeConfig && window.EmployeeConfig.planData) ? window.EmployeeConfig.planData : [];
    var planOptionsHtml = planData.map(function (plan) {
        var isSelected = plan.Selected ? "selected" : "";
        var isDisabled = plan.Disabled ? "disabled" : "";
        return `<option value="${plan.Value}" ${isSelected} ${isDisabled}>${plan.Text}</option>`;
    }).join('');

    var rowTemplates = {
        'hire': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="row mt-0 g-3"><div class="col-md-6"><label class="form-label small start-date">Hire Date</label><input type="date" class="form-control data-field start-date" data-key="Hire_StartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-md-6"><label class="form-label small">Termination Date</label><input type="date" class="form-control data-field end-date" data-key="Hire_EndDate"></div></div></div>`,
        'status': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="StatusId" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-md-4"><label class="form-label small">Status</label><select class="form-select data-field" data-key="Status"><option value="">-- Select Status --</option><option value="0">FULL-TIME</option><option value="1">PART-TIME</option></select></div><div class="col-md-4"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="StatusStartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-md-4"><label class="form-label small">End Date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="StatusEndDate"></div></div></div>`,
        'payroll': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-md-4"><label class="form-label small">Hourly Pay Amount</label><input inputmode="decimal" class="form-control data-field" data-key="payPeriodHourlyAmount" oninput="this.value=this.value.replace(/[^0-9.]/g,'').replace(/(\..*)\./g,'$1').replace(/^(\d+)(\.\d{0,2})?.*$/,'$1$2')"></div><div class="col-md-4"><label class="form-label small">Salary Pay Amount</label><input inputmode="decimal" class="form-control data-field" data-key="payPeriodSalaryAmount" oninput="this.value=this.value.replace(/[^0-9.]/g,'').replace(/(\..*)\./g,'$1').replace(/^(\d+)(\.\d{0,2})?.*$/,'$1$2')"></div><div class="col-md-2"><label class="form-label small">Total Hours</label><input inputmode="decimal" class="form-control data-field" data-key="payPeriodTotalHours" oninput="this.value=this.value.replace(/[^0-9.]/g,'').replace(/(\..*)\./g,'$1').replace(/^(\d+)(\.\d{0,2})?.*$/,'$1$2')"></div><div class="col-md-2"><label class="form-label small">Additional</label><input inputmode="decimal" class="form-control data-field" data-key="payPeriodAdditional" oninput="this.value=this.value.replace(/[^0-9.]/g,'').replace(/(\..*)\./g,'$1').replace(/^(\d+)(\.\d{0,2})?.*$/,'$1$2')"></div><div class="col-md-6"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="payPeriodStartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-md-6"><label class="form-label small">End Date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="payPeriodEndDate"></div></div></div>`,
        'dependent': `<tr class="dynamic-row"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><td><input type="text" class="form-control form-control-sm data-field text-uppercase vw-8" data-key="FirstName" oninput="this.value=this.value.replace(/[^A-Za-z .'-]/g,'').replace(/^\s+/,'').replace(/\s{2,}/g,' ')" onblur="this.value=this.value.trim()" maxlength="100"></td><td><input type="text" class="form-control form-control-sm data-field text-uppercase vw-8" data-key="MiddleName" oninput="this.value=this.value.replace(/[^A-Za-z .'-]/g,'').replace(/^\s+/,'').replace(/\s{2,}/g,' ')" onblur="this.value=this.value.trim()" maxlength="100"></td><td><input type="text" class="form-control form-control-sm data-field text-uppercase vw-8" data-key="LastName" oninput="this.value=this.value.replace(/[^A-Za-z .'-]/g,'').replace(/^\s+/,'').replace(/\s{2,}/g,' ')" onblur="this.value=this.value.trim()" maxlength="100"></td><td><input type="text" class="form-control form-control-sm data-field text-uppercase vw-8" data-key="Suffix" oninput="this.value=this.value.replace(/[^A-Za-z0-9.]/g,'').replace(/\.{2,}/g,'.')" maxlength="50"></td><td><input type="text" class="form-control form-control-sm data-field vw-8" data-key="SSN" placeholder="XXX-XX-XXXX" maxlength="11" oninput="this.value=this.value.replace(/\\D/g,'').replace(/(\\d{3})(\\d{0,2})(\\d{0,4}).*/,function(_,a,b,c){return a+(b?'-'+b:'')+(c?'-'+c:'');})")"></td><td><input type="date" class="form-control text-uppercase form-control-sm data-field" onfocus="this.max=new Date().toISOString().split('T')[0]" data-key="Birthday"></td><td><input type="date" class="form-control text-uppercase form-control-sm data-field start-date" data-key="CoverageStartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></td><td><input type="date" class="form-control text-uppercase form-control-sm data-field end-date" data-key="CoverageEndDate"></td><td class="delete-btn"><button class="btn btn-sm btn-danger delete-btn" onclick="removeRow(this)"><i class="bx bx-x"></i></button></td></tr>`,
        'medical': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-12"><label class="form-label small">Plan Name</label><select class="form-select data-field text-uppercase" data-key="planId"><option value="">-- Select Plan --</option>${planOptionsHtml}</select></div><div class="col-6"><label class="form-label small">Offer Date</label><input type="date" class="form-control text-uppercase data-field" data-key="CoverageOfferDate"></div><div class="col-6 d-flex align-items-end"><div class="form-check mx-3"><input class="form-check-input data-field" type="checkbox" data-key="IsMedicalEnrolled"><label class="form-label small">Enrolled</label></div></div><div class="col-6"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="Medical_CoverageStartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-6"><label class="form-label small">End Date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="Medical_CoverageEndDate"></div></div></div>`,
        'cobra': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-12"><label class="form-label small">Plan Name</label><select class="form-select data-field text-uppercase" data-key="planId"><option value="">-- Select Plan --</option>${planOptionsHtml}</select></div><div class="col-4"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="COBRA_StartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-4"><label class="form-label small">End Date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="COBRA_EndDate"></div><div class="col-4 d-flex align-items-end"><div class="form-check mx-3 mb-0"><input class="form-check-input data-field" type="checkbox" data-key="IsCOBRAEnrolled"><label class="form-label small">COBRA Enrolled</label></div></div></div></div>`,
        'union': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-12"><label class="form-label small">Plan Name</label><select class="form-select data-field text-uppercase" data-key="planId"><option value="">-- Select Plan --</option>${planOptionsHtml}</select></div><div class="col-4"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="Union_ContributionStartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-4"><label class="form-label small">End Date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="Union_ContributionEndDate"></div><div class="col-4 d-flex align-items-end"><div class="form-check mx-3 mb-0"><input class="form-check-input data-field" type="checkbox" data-key="IsUnionMember"><label class="form-label small">Union Member</label></div></div></div></div>`,
        'retiree': `<div class="row g-3 mt-1 p-1 bg-label-secondary rounded dynamic-row align-items-end"><div class="text-end mb-n3 mt-0 ms-2"><button onclick="removeRow(this)" class="btn btn-sm bg-danger delete-btn float-end" type="button"><i class="bx bx-trash fs-6 text-white"></i></button></div><div class="row g-3 mt-0"><input type="hidden" class="data-field" data-key="Id" value="0" /><input type="hidden" class="data-field" data-key="EmployeeCodeId" value="${$('#EmployeeCodeId').val()}" /><div class="col-12"><label class="form-label small">Plan Name</label><select class="form-select data-field text-uppercase" data-key="planId"><option value="">-- Select Plan --</option>${planOptionsHtml}</select></div><div class="col-4"><label class="form-label small">Start Date</label><input type="date" class="form-control text-uppercase data-field start-date" data-key="Retiree_StartDate" onchange="var end=this.closest('.dynamic-row').querySelector('.end-date');end.min=this.value;if(end.value&&end.value<this.value)end.value='';"></div><div class="col-4"><label class="form-label small">End date</label><input type="date" class="form-control text-uppercase data-field end-date" data-key="Retiree_EndDate"></div><div class="col-4 d-flex align-items-end"><div class="form-check mx-3 mb-0"><input class="form-check-input data-field" type="checkbox" data-key="IsRetireeEnrolled"><label class="form-label small">Retiree</label></div></div></div></div>`
    };

    var targetContainer = $('#container_' + type);
    if ($('.modal.show').length > 0) targetContainer = $('.modal.show').find('#container_' + type);

    targetContainer.append(rowTemplates[type]);
};

window.removeRow = function (btn) {
    $(btn).closest($(btn).parents('tr').length ? 'tr' : '.dynamic-row').remove();
};

window.showAuditLogModal = function () {
    new bootstrap.Modal(document.getElementById('auditLogModal')).show();
};

// ==========================================
// 4. ACA CODE LOGIC
// ==========================================
window.openEditModal = function (monthClicked) {
    var $container = $('.modal.show').length > 0 ? $('.modal.show') : $(document);

    monthList.forEach(m => {
        $(`#modal_${m}_COC`).val($container.find(`#val_${m}_COC`).val()).trigger('change');
        $(`#modal_${m}_LCMP`).val($container.find(`#val_${m}_LCMP`).val());
        $(`#modal_${m}_SHC`).val($container.find(`#val_${m}_SHC`).val()).trigger('change');
        $(`#modal_${m}_ZIP`).val($container.find(`#val_${m}_ZIP`).val());
    });
    window.safelyShowSubModal('editCodeModal');
};

window.saveFromModal = function (e) {
    var codesData = {
        EmployeeCodeID: parseInt($("#EmployeeCodeId").val()) || 0,
        EmployeeId: parseInt($("#EmployeeID").val()) || 0,
        FilingYear: (window.EmployeeConfig && window.EmployeeConfig.filingYear) ? window.EmployeeConfig.filingYear : 0,
        IsLocked: $('#chk_islocked').is(':checked')
    };
    var btn = e ? e.currentTarget : window.event.currentTarget;
    var orgText = $(btn).html();
    $(btn).prop('disabled', true).html('<span class="spinner-border spinner-border-sm"></span> Saving...');

    monthList.forEach(m => {
        var coc = $(`#modal_${m}_COC`).val()?.toUpperCase() || null;
        var rawLcmp = $(`#modal_${m}_LCMP`).val();
        var lcmp = (rawLcmp === "") ? null : parseFloat(rawLcmp);
        var shc = $(`#modal_${m}_SHC`).val()?.toUpperCase() || null;

        codesData[`${m}_COC`] = coc;
        codesData[`${m}_LCMP`] = lcmp;
        codesData[`${m}_SHC`] = shc;
    });

    var token = $('input[name="__RequestVerificationToken"]').first().val();

    $.ajax({
        url: '/Employee/SaveEmployeeCodes',
        type: 'POST',
        contentType: 'application/json',
        headers: { "RequestVerificationToken": token },
        data: JSON.stringify(codesData),
        success: function (r, textStatus, xhr) {
            $(btn).prop('disabled', false).html(orgText);

            var modalElement = document.getElementById('editCodeModal');
            if (modalElement) {
                var modalInstance = bootstrap.Modal.getInstance(modalElement);
                if (modalInstance) modalInstance.hide();
            }

            setTimeout(function () {
                // CRITICAL FIX: Destroy the orphaned modal that got moved to the <body> 
                // and clean up any stuck backdrops so the next open works perfectly.
                $('body > #editCodeModal').remove();
                $('.modal-backdrop').remove();
                $('body').removeClass('modal-open').css('padding-right', '');

                var contentType = xhr.getResponseHeader("content-type") || "";
                if (contentType.indexOf("html") > -1) {

                    var $target = $('#employeeCodesSection');
                    if ($('.modal.show').length > 0) $target = $('.modal.show').find('#employeeCodesSection');

                    // Fallback if ID is missing on wrapper
                    if ($target.length === 0) $target = $('.month-card-container').parent();

                    $target.html(r);

                    // Re-initialize scrollbars on the new HTML
                    if (typeof window.initModalUI === 'function') {
                        window.initModalUI($target);
                    }
                }

                window.calculateRiskAll();
                window.refreshMonthCardDisplay();

                Swal.fire({
                    icon: 'success',
                    title: 'Locked & Saved',
                    text: 'Monthly codes have been updated successfully.',
                    timer: 2000,
                    showConfirmButton: false
                });
            }, 200);
        },
        error: function (xhr) {
            $(btn).prop('disabled', false).html(orgText);
            Swal.fire({
                icon: 'error',
                title: 'System Error',
                text: 'Could not connect to the server.',
                target: document.getElementById('editCodeModal')
            });
        }
    });
};

window.propagateMonth = function (startMonth, direction) {
    var months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
    var startIndex = months.indexOf(startMonth);

    if (startIndex === -1) return;

    var sourceCoc = $(`#modal_${startMonth}_COC`).val();
    var sourceLcmp = $(`#modal_${startMonth}_LCMP`).val();
    var sourceShc = $(`#modal_${startMonth}_SHC`).val();

    $('#modal_ALL_COC').val('').trigger('change.select2');
    $('#modal_ALL_LCMP').val('');
    $('#modal_ALL_SHC').val('').trigger('change.select2');

    function applyToMonth(m) {
        $(`#modal_${m}_COC`).val(sourceCoc).trigger('change.select2');
        $(`#modal_${m}_LCMP`).val(sourceLcmp);
        $(`#modal_${m}_SHC`).val(sourceShc).trigger('change.select2');
    }

    if (direction === 'left') {
        for (var i = 0; i <= startIndex; i++) { applyToMonth(months[i]); }
    } else if (direction === 'right') {
        for (var j = startIndex; j < months.length; j++) { applyToMonth(months[j]); }
    }
};

// ==========================================
// 5. RISK MODAL LOGIC
// ==========================================
window.showRiskDetails = function (clickedMonth) {
    currentRiskMonth = clickedMonth;
    var riskMonthsList = [];
    var container = $("#riskMonthsBadgeContainer");
    container.empty();
    var $sourceContainer = $('.modal.show').length > 0 ? $('.modal.show') : $(document);

    monthList.forEach(m => {
        let coc = $sourceContainer.find(`#val_${m}_COC`).val();
        let shc = $sourceContainer.find(`#val_${m}_SHC`).val();
        if (coc === "1H" && (shc !== "2A" && shc !== "2B" && shc !== "2G")) {
            riskMonthsList.push(m);
            var badgeHtml = `<span class="badge bg-danger rounded-1 px-3 py-2">${m}</span>`;
            container.append(badgeHtml);
        }
    });

    if (riskMonthsList.length === 0) {
        $("#riskMonthsSection").addClass("d-none");
    } else {
        $("#riskMonthsSection").removeClass("d-none");
    }
    window.safelyShowSubModal('riskAnalysisModal');
};

window.showAuditTimeline = function () {
    let employeeId = $("#EmployeeID").val();
    if (!employeeId) {
        alert("Employee ID not found.");
        return;
    }

    let url = (window.EmployeeConfig && window.EmployeeConfig.auditTimelineUrl) ? window.EmployeeConfig.auditTimelineUrl : '/Employee/GetEmployeeAuditTimeline';
    let filingYear = (window.EmployeeConfig && window.EmployeeConfig.filingYear) ? window.EmployeeConfig.filingYear : new Date().getFullYear();

    $('#auditTimelineBody').html('<div class="text-center p-4"><div class="spinner-border text-primary"></div></div>');
    window.safelyShowSubModal('auditTimelineModal');

    $.get(url, { employeeId: employeeId, year: filingYear }, function (data) {
        let html = '';
        let currentMonth = '';

        data.forEach(a => {
            if (a.monthCode !== currentMonth) {
                currentMonth = a.monthCode;
                html += `<h6 class="mt-3 text-primary">${currentMonth} ${filingYear}</h6>`;
            }

            let cls = a.changeSource === 'MANUAL' ? 'text-warning' : a.changeSource === 'AUTOFIX' ? 'text-info' : 'text-success';

            html += `
            <div class="audit-item mb-3 p-3 border rounded">
                <strong class="${cls}"><i class="bx bx-check-circle me-1"></i>${a.changeSource}</strong><br>
                <span class="text-muted">Line ${a.lineNumber}:</span> ${a.oldValue || '-'} <i class="bx bx-right-arrow-alt"></i> ${a.newValue || '-'}<br>
                <span class="audit-meta small text-muted d-block mt-1">
                    ${a.changeReason}<br>
                    <i class="bx bx-user me-1"></i>${a.changedByUserName || 'System'} &middot;
                    <i class="bx bx-time me-1"></i>${new Date(a.changedOn).toLocaleString()}
                </span>
            </div>`;
        });

        $('#auditTimelineBody').html(html);
    }).fail(function (xhr, status, error) {
        console.error("Audit API Error:", status, error);
        $('#auditTimelineBody').html('<p class="text-danger p-3">Error fetching audit history.</p>');
    });
};

window.fetchPenaltyDetails = function ($container) {
    $container = $container || ($('.modal.show').length > 0 ? $('.modal.show') : $(document));
    var empId = parseInt($("#EmployeeID").val(), 10) || 0;
    var year = (window.EmployeeConfig && window.EmployeeConfig.filingYear) ? window.EmployeeConfig.filingYear : new Date().getFullYear();

    if (empId <= 0) return;

    $.get(`/Employee/GetEmployeeRisk?id=${empId}&year=${year}`, function (data) {
        if (data.isAtRisk) {
            $container.find('#riskMonthsListText').append(` | Est. Penalty: $${data.penalty}`);
        }
    });
};

window.calculateRiskAll = function ($container) {
    $container = $container || ($('.modal.show').length > 0 ? $('.modal.show') : $(document));
    let riskCount = 0;
    let riskMonths = [];

    monthList.forEach(m => {
        let coc = $container.find(`#val_${m}_COC`).val();
        let shc = $container.find(`#val_${m}_SHC`).val();
        let card = $container.find(`#card_${m}`).removeClass('status-bar-safe status-bar-risk status-bar-neutral');
        let dot = card.find('.risk-indicator').addClass('d-none');

        if (coc === "1H" && (shc !== "2A" && shc !== "2B" && shc !== "2G")) {
            card.addClass('status-bar-risk');
            dot.removeClass('d-none');
            riskCount++;
            riskMonths.push(m);
        }
        else if (coc === "1A" || coc === "1E" || shc === "2C" || shc === "2G") {
            card.addClass('status-bar-safe');
        }
        else {
            card.addClass('status-bar-neutral');
        }
    });

    if (riskCount > 0) {
        $container.find('#headerRiskDisplay').removeClass('d-none');
        $container.find('#riskCountDisplay').text(riskCount);

        var listContainer = $container.find("#riskDisplay_List");
        listContainer.empty();

        riskMonths.forEach(m => {
            var badge = `<span class="badge bg-danger px-1" style="font-size: 0.65rem;">${m}</span>`;
            listContainer.append(badge);
        });

        var empId = parseInt($("#EmployeeID").val(), 10) || 0;
        var year = (window.EmployeeConfig && window.EmployeeConfig.filingYear) ? window.EmployeeConfig.filingYear : new Date().getFullYear();

        if (empId > 0) {
            $.get(`/Employee/GetEmployeeRiskData?id=${empId}&year=${year}`, function (data) {
                $container.find('#riskMonthsListText').append(` | Total Risk: $${data.estimatedPenalty}`);
            });
        }
    } else {
        $container.find('#headerRiskDisplay').addClass('d-none');
    }
};

window.applyAutoFix = function () {
    var codesData = {
        EmployeeCodeID: parseInt($("#EmployeeCodeId").val()) || 0,
        EmployeeId: parseInt($("#EmployeeID").val()) || 0,
        FilingYear: (window.EmployeeConfig && window.EmployeeConfig.filingYear) ? window.EmployeeConfig.filingYear : 0,
        IsLocked: $('#chk_islocked').is(':checked')
    };
    var token = $('input[name="__RequestVerificationToken"]').first().val();
    var defaultPremium = parseFloat($("#DefaultPlanPremium").val()) || 0;
    var $sourceContainer = $('.modal.show').length > 0 ? $('.modal.show') : $(document);

    monthList.forEach(m => {
        let currentCoc = $sourceContainer.find(`#val_${m}_COC`).val();
        if (currentCoc === "1H") {
            codesData[`${m}_COC`] = '1E';
            codesData[`${m}_LCMP`] = defaultPremium;
        } else {
            codesData[`${m}_COC`] = currentCoc;
            codesData[`${m}_LCMP`] = parseFloat($sourceContainer.find(`#val_${m}_LCMP`).val()) || null;
        }
        codesData[`${m}_SHC`] = '2G';
    });

    $.ajax({
        url: '/Employee/SaveEmployeeCodes',
        type: 'POST',
        contentType: 'application/json',
        headers: { "RequestVerificationToken": token },
        data: JSON.stringify(codesData),
        success: function (r, textStatus, xhr) {
            var riskModalEl = document.getElementById('riskAnalysisModal');
            if (riskModalEl) bootstrap.Modal.getOrCreateInstance(riskModalEl).hide();

            var contentType = xhr.getResponseHeader("content-type") || "";
            if (contentType.indexOf("html") > -1) {
                var $target = $('#employeeCodesSection');
                if ($('.modal.show').length > 0) $target = $('.modal.show').find('#employeeCodesSection');
                $target.html(r);
            }

            window.calculateRiskAll();
            window.refreshMonthCardDisplay();
            Swal.fire({ title: 'Auto-Fix Applied', text: 'Applied 1E (Offer) and 2G (Safe Harbor) to risk months.', icon: 'success', timer: 2000, showConfirmButton: false });
        },
        error: function (xhr, status, error) {
            Swal.fire('Error', 'An error occurred while applying the auto-fix.', 'error');
        }
    });
};

window.refreshMonthCardDisplay = function ($container) {
    $container = $container || ($('.modal.show').length > 0 ? $('.modal.show') : $(document));
    var months = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    var firstCoc = $container.find(`#val_JAN_COC`).val();
    var firstLcmp = $container.find(`#val_JAN_LCMP`).val();
    var firstShc = $container.find(`#val_JAN_SHC`).val();

    var isAllSame = true;
    for (var i = 1; i < months.length; i++) {
        var m = months[i];
        if ($container.find(`#val_${m}_COC`).val() !== firstCoc ||
            $container.find(`#val_${m}_LCMP`).val() !== firstLcmp ||
            $container.find(`#val_${m}_SHC`).val() !== firstShc) {
            isAllSame = false;
            break;
        }
    }

    if (isAllSame && firstCoc) {
        $container.find('#view_ALL_COC').text(firstCoc || "-");
        $container.find('#view_ALL_LCMP').text(firstLcmp > 0 ? "$" + parseFloat(firstLcmp).toFixed(2) : "");
        $container.find('#view_ALL_SHC').text(firstShc || "-");
        $container.find('#card_ALL').removeClass('opacity-50');

        months.forEach(m => {
            $container.find(`#view_${m}_COC`).text("-");
            $container.find(`#view_${m}_LCMP`).text("");
            $container.find(`#view_${m}_SHC`).text("-");
            $container.find(`#card_${m}`).addClass('opacity-50');
        });
    } else {
        $container.find('#view_ALL_COC').text("-");
        $container.find('#view_ALL_LCMP').text("");
        $container.find('#view_ALL_SHC').text("-");
        $container.find('#card_ALL').addClass('opacity-50');

        months.forEach(m => {
            var coc = $container.find(`#val_${m}_COC`).val();
            var lcmp = $container.find(`#val_${m}_LCMP`).val();
            var shc = $container.find(`#val_${m}_SHC`).val();

            $container.find(`#view_${m}_COC`).text(coc || "-");
            $container.find(`#view_${m}_LCMP`).text(lcmp > 0 ? "$" + parseFloat(lcmp).toFixed(2) : "");
            $container.find(`#view_${m}_SHC`).text(shc || "-");
            $container.find(`#card_${m}`).removeClass('opacity-50');
        });
    }
};

// ==========================================
// 6. COVERED INDIVIDUALS UI MODULE (FIXED: HTML Refresh)
// ==========================================
var CoveredIndividualsUI = (function ($) {

    const init = function () {
        bindEvents();
    };

    const bindEvents = function () {
        $(document).off('click', '.edit-ci-row').on('click', '.edit-ci-row', function () {
            var $row = $(this);
            var $modal = $('#editCoveredIndividualModal').last();
            if ($modal.length === 0) return;

            $('#modal-ci-index').val($row.data('index'));
            $('#modal-ci-first').val($row.data('first'));
            $('#modal-ci-middle').val($row.data('middle'));
            $('#modal-ci-last').val($row.data('last'));
            $('#modal-ci-ssn').val($row.data('ssn'));
            $('#modal-ci-dob').val($row.data('dob'));

            $('#CI_chk_disable_coding').prop('checked', String($row.data('disablecoding')).toLowerCase() === "true");
            $('#modal-chk-all').prop('checked', String($row.data('all')).toLowerCase() === "true");

            $('.modal-chk-month').each(function () {
                const month = $(this).data('month');
                $(this).prop('checked', String($row.data(month)).toLowerCase() === "true");
            });

            var bsModal = bootstrap.Modal.getOrCreateInstance($modal[0]);
            bsModal.show();
        });

        $(document).off('change', '#modal-chk-all').on('change', '#modal-chk-all', function () {
            const isChecked = $(this).prop('checked');
            $('.modal-chk-month').prop('checked', isChecked);
        });

        $(document).off('change', '.modal-chk-month').on('change', '.modal-chk-month', function () {
            const $chkMonths = $('.modal-chk-month');
            const allChecked = $chkMonths.length === $chkMonths.filter(':checked').length;
            $('#modal-chk-all').prop('checked', allChecked);
        });

        $(document).off('click', '#btnSaveCI').on('click', '#btnSaveCI', function () {
            saveCoverageData($(this));
        });
    };

    const saveCoverageData = function ($btnSave) {
        $btnSave.prop('disabled', true).html('<span class="spinner-border spinner-border-sm"></span> Saving...');

        const rowIndex = $('#modal-ci-index').val();
        const $targetRow = $(`.edit-ci-row[data-index="${rowIndex}"]`);

        if ($targetRow.length === 0) {
            $btnSave.prop('disabled', false).text('Save');
            return;
        }

        const isAllChecked = $('#modal-chk-all').prop('checked');
        const isDisableCodingChecked = $('#CI_chk_disable_coding').prop('checked');
        const monthData = {};

        $('.modal-chk-month').each(function () {
            monthData[$(this).data('month')] = $(this).prop('checked');
        });

        const apiPayload = {
            Id: String($targetRow.data('id')),
            EmployeeId: String($("#EmployeeID").val()),
            FilingYear: parseInt($targetRow.data('filingyear'), 10) || 0,
            CI_chk_disable_coding: isDisableCodingChecked,
            AllM: isAllChecked,
            Jan: monthData['jan'], Feb: monthData['feb'], Mar: monthData['mar'], Apr: monthData['apr'],
            May: monthData['may'], Jun: monthData['jun'], Jul: monthData['jul'], Aug: monthData['aug'],
            Sep: monthData['sep'], Oct: monthData['oct'], Nov: monthData['nov'], Dec: monthData['dec']
        };

        const token = $('input[name="__RequestVerificationToken"]').first().val();

        $.ajax({
            url: '/Employee/SaveCoveredIndividuals',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(apiPayload),
            headers: { "RequestVerificationToken": token },
            success: function (response, textStatus, xhr) {
                var contentType = xhr.getResponseHeader("content-type") || "";

                Swal.fire({
                    icon: 'success',
                    title: 'Saved!',
                    text: 'Coverage updated successfully.',
                    timer: 2000,
                    showConfirmButton: false
                });

                var $modal = $('#editCoveredIndividualModal').last();
                var bsModal = bootstrap.Modal.getInstance($modal[0]);
                if (bsModal) bsModal.hide();

                // CRITICAL FIX 2: Inject the HTML back into the DOM to refresh the UI
                if (contentType.indexOf("html") > -1) {
                    var $target = $('#coveredIndividualsSection');
                    if ($('.modal.show').length > 0) $target = $('.modal.show').find('#coveredIndividualsSection');

                    if ($target.length) {
                        $target.html(response);
                    } else {
                        // Fallback replacement if missing wrapper id
                        $('.perfectscroller_dependents').first().html(response);
                    }
                }
            },
            error: function (xhr) {
                Swal.fire({ icon: 'error', title: 'Error', text: "An error occurred while saving." });
            },
            complete: function () {
                $btnSave.prop('disabled', false).text('Save');
            }
        });
    };

    return { init: init };
})(jQuery);

// ==========================================
// 7. DOCUMENT READY & INIT
// ==========================================
$(document).ready(function () {
    var empId = parseInt($("#EmployeeID").val()) || 0;

    if (empId === 0) {
        $('.nav-tabs .nav-item:not(:first-child)').addClass('d-none');
        $('.btn-edit').addClass('d-none');
        $('.action-buttons').removeClass('d-none');
        $('input, select, textarea').prop('disabled', false);
    }

    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    const scrolls = ['.perfectscroller', '.perfectscroller_left', '.perfectscroller_dependents', '.perfectscroller_bottom'];
    scrolls.forEach(s => {
        let el = document.querySelector(s);
        if (el && typeof PerfectScrollbar !== 'undefined') new PerfectScrollbar(el, { wheelSpeed: 1, wheelPropagation: true });
    });

    $('#editCodeModal').on('shown.bs.modal', function () {
        if ($.fn.select2) $('.select2-aca').select2({ dropdownParent: $('#editCodeModal'), width: '100%' });
    });

    $('#basicModal').on('hidden.bs.modal', function (e) {
        if (e.target !== this) return;
        window.restoreDuplicateIds();
        $(this).find('.modal-body').empty();
        $('body > #editCodeModal, body > #auditTimelineModal, body > #riskAnalysisModal, body > #editCoveredIndividualModal').remove();
    });

    window.calculateRiskAll();
    window.fetchPenaltyDetails();
    window.refreshMonthCardDisplay();

    if (typeof CoveredIndividualsUI !== 'undefined') {
        CoveredIndividualsUI.init();
    }
});


// ==========================================
// 8. PREV / NEXT RECORD NAVIGATION
// ==========================================

/**
 * Sync the nav list from the currently rendered grid rows and
 * update the navigation bar state.
 * Called after every GridManager reload (see Index.cshtml onLoad hook).
 */
window.syncNavList = function () {
    var ids = EmployeeApi.getFilteredIdsFromDom();
    EmployeeState.setFilteredList(ids);
    window.updateNavBar();
};

/**
 * Refresh Prev/Next buttons (#empNavRow in quick filter bar)
 * and the counter badge (#empNavCounter in _EmployeeBasicInfo card-header).
 *
 * #empNavRow lives in quickFilterBar (same line as filter badges).
 * Only shown in split view when a valid employee is selected.
 * In table view it stays hidden (no persistent detail panel).
 */
window.updateNavBar = function () {
    var $row     = $('#empNavRow');
    var $prev    = $('#btnEmpPrev');
    var $next    = $('#btnEmpNext');
    var $counter = $('.emp-nav-counter');   // middle btn span + card-header badge (both, by class)

    var total        = EmployeeState.getTotalCount();
    var idx          = EmployeeState.getCurrentIndex();
    var hasPrev      = EmployeeState.hasPrev();
    var hasNext      = EmployeeState.hasNext();
    var hasSelection = total > 0 && idx >= 0;
    var label        = hasSelection ? (idx + 1) + ' of ' + total : '— of —';

    // Show nav row only in split view with a valid selection
    var isSplit = !$('#mainDetailColumn').hasClass('d-none');
    if (isSplit && hasSelection) {
        $row.removeClass('d-none');
    } else {
        $row.addClass('d-none');
    }

    $prev.prop('disabled', !hasPrev);
    $next.prop('disabled', !hasNext);

    // Counter: both the middle btn span AND the card-header badge (if loaded)
    $counter.text(label);
    if (hasSelection) $counter.removeClass('d-none');
    else              $counter.addClass('d-none');
};

/**
 * Load a specific employee into the split-view right panel.
 * Also highlights the matching row in the list and auto-scrolls to it.
 */
function loadEmployeeInSplitView(empId) {
    var $splitContainer = $('#commonDetailContainer');
    $splitContainer.html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');

    // Sync the LEFT list to this record: highlight it, and if it lives on another
    // page, jump the grid to that page. Page-jump is provided by the page (Index.cshtml
    // defines window.syncListToRecord); fall back to a plain on-page highlight when that
    // hook isn't present (e.g. a system that doesn't page-follow).
    if (typeof window.syncListToRecord === 'function') {
        window.syncListToRecord(empId);
    } else {
        $('.employee-active, tr.employee-item').removeClass('table-active active-row');
        var $matchRow = $('.employee-item[data-id="' + empId + '"]');
        if ($matchRow.length) {
            $matchRow.closest('.employee-active, tr').addClass('table-active active-row');
            var $listScroll = $('#splitListScroll, #employeeListContainer');
            $listScroll.each(function () {
                var $container = $(this);
                var rowTop     = $matchRow.offset().top;
                var listTop    = $container.offset().top;
                var scrollTop  = $container.scrollTop();
                $container.animate({ scrollTop: scrollTop + (rowTop - listTop) - 80 }, 200);
            });
        }
    }

    $.ajax({
        url: '/Employee/GetEmployeeBasicDetails',
        type: 'GET',
        data: { employeeId: empId },
        success: function (data) {
            $splitContainer.html(data);
            if (window.EmployeeCompare) window.EmployeeCompare.onDetailLoaded(empId, window.__cmpEmployeeYear);
            if (typeof window.calculateRiskAll        === 'function') window.calculateRiskAll($splitContainer);
            if (typeof window.fetchPenaltyDetails     === 'function') window.fetchPenaltyDetails($splitContainer);
            if (typeof window.refreshMonthCardDisplay === 'function') window.refreshMonthCardDisplay($splitContainer);
            if (typeof initModalUI                    === 'function') initModalUI($splitContainer);
            window.updateNavBar();
        }
    });
}

/**
 * Load a specific employee into the fullscreen modal.
 */
function loadEmployeeInModal(empId, isEditMode) {
    var $modal = $('#basicModal');
    $modal.find('.modal-title').text(isEditMode ? 'Edit Employee' : 'View Employee');
    $modal.find('.modal-body').html('<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>');
    bootstrap.Modal.getOrCreateInstance('#basicModal').show();

    $.ajax({
        url: '/Employee/GetEmployeeBasicDetails',
        type: 'GET',
        data: { employeeId: empId },
        success: function (html) {
            var $body = $modal.find('.modal-body');
            $body.html(html);
            if (typeof window.calculateRiskAll        === 'function') window.calculateRiskAll($body);
            if (typeof window.fetchPenaltyDetails     === 'function') window.fetchPenaltyDetails($body);
            if (typeof window.refreshMonthCardDisplay === 'function') window.refreshMonthCardDisplay($body);
            if (typeof initModalUI                    === 'function') initModalUI($body);
            loadEmployers_dropdwon();
            window.updateNavBar();
        }
    });
}

// ── Wire up EmployeeState nav-update callback ─────────────────────────────────
EmployeeState.onNavUpdate = function () {
    window.updateNavBar();
};

// ── Prev / Next button handlers ───────────────────────────────────────────────
$(document).on('click', '#btnEmpPrev', function () {
    var newId = EmployeeState.navigate('prev');
    if (!newId) return;
    var isSplit = !$('#mainDetailColumn').hasClass('d-none');
    if (isSplit) loadEmployeeInSplitView(newId);
    else         loadEmployeeInModal(newId, false);
});

$(document).on('click', '#btnEmpNext', function () {
    var newId = EmployeeState.navigate('next');
    if (!newId) return;
    var isSplit = !$('#mainDetailColumn').hasClass('d-none');
    if (isSplit) loadEmployeeInSplitView(newId);
    else         loadEmployeeInModal(newId, false);
});


// ==========================================
// 9. DOUBLE-CLICK TO EDIT + DIRTY TRACKING
// ==========================================

/**
 * Section config: maps each section wrapper ID to its save URL and list key.
 */
var _sectionConfig = {
    'section_basic'   : { url: '/Employee/EmployeedetailSave',  listKey: null },
    'section_hire'    : { url: '/Employee/EmployeeHireSave',    listKey: 'EmployeeHireDetail' },
    'section_status'  : { url: '/Employee/EmployeeStatusSave',  listKey: 'EmployeeEnrollment' },
    'section_payroll' : { url: '/Employee/EmployeePayrollSave', listKey: 'EmployeePayroll' },
    'section_medical' : { url: '/Employee/EmployeeMedicalSave', listKey: 'EmployeeMedical' },
    'section_cobra'   : { url: '/Employee/EmployeeCOBRASave',   listKey: 'EmployeeCOBRA' },
    'section_union'   : { url: '/Employee/EmployeeUnionSave',   listKey: 'EmployeeUnion' },
    'section_retiree' : { url: '/Employee/EmployeeRetireeSave', listKey: 'EmployeeRetiree' }
};

function _findSection($el) {
    var $section = $el.closest('[id^="section_"]');
    if ($section.length === 0) return null;
    var id = $section.attr('id');
    if (!_sectionConfig[id]) return null;
    return { $el: $section, id: id, cfg: _sectionConfig[id] };
}

function _markDirty(sectionId) { $('#' + sectionId).attr('data-dirty', 'true'); }
function _isDirty(sectionId)   { return $('#' + sectionId).attr('data-dirty') === 'true'; }
function _clearDirty(sectionId){ $('#' + sectionId).removeAttr('data-dirty'); }

// Snapshot the section's current values as the revert baseline. Called when
// editing STARTS so Cancel / auto-cancel restores exactly what was on screen.
// Without this, dynamically-populated selects (e.g. the Employer dropdown, whose
// options are added by AJAX after render) have no option with defaultSelected set,
// so a revert falls back to the placeholder ("-- Select Employer --").
function _snapshotSectionDefaults($section) {
    $section.find('input:not(:checkbox):not(:radio), textarea').each(function () { this.defaultValue = this.value; });
    $section.find('input[type="checkbox"], input[type="radio"]').each(function () { this.defaultChecked = this.checked; });
    $section.find('select').each(function () {
        var currentVal = $(this).val();
        $(this).find('option').each(function () { this.defaultSelected = (this.value === currentVal); });
    });
}

// Disabled form controls do NOT emit mouse events (no dblclick, nothing bubbles
// to document), so we cannot bind the handler to the <input>/<select> directly —
// that was the original bug. This rule makes the disabled fields transparent to
// the pointer so the dblclick lands on the section wrapper instead. It only
// applies while disabled (view mode); once a section enters edit mode the fields
// are enabled again and behave normally.
(function ensureSectionEditStyles() {
    if (document.getElementById('section-edit-styles')) return;
    var style = document.createElement('style');
    style.id = 'section-edit-styles';
    style.textContent =
        '[id^="section_"] input:disabled,' +
        '[id^="section_"] select:disabled,' +
        '[id^="section_"] textarea:disabled{pointer-events:none;}' +
        // Highlight fields that are currently being edited (double-click to edit).
        // Highlight ONLY the border in blue for the field being edited — no background fill.
        '[id^="section_"] .field-editing{' +
        'background-color:transparent !important;' +
        'border-color:#0d6efd !important;' +
        'box-shadow:0 0 0 .15rem rgba(13,110,253,.25) !important;}';
    document.head.appendChild(style);
})();

// ── Double-click a single field to make ONLY that field editable ─────────────
// We listen on the section wrapper (not the fields). Because disabled fields
// have pointer-events:none, the double-click's target is the field's column
// wrapper (or its label), so we resolve the field from there. Only the clicked
// field is enabled; the section's Save/Cancel buttons are revealed so the edit
// can be saved. saveGeneric() reads every field regardless of disabled state,
// so a single-field edit still persists the whole record.
$(document).on('dblclick', '[id^="section_"]', function (e) {
    // Ignore double-clicks on buttons / headers — those have their own actions.
    if ($(e.target).closest('button, a, .card-header-actions').length) return;

    var section = _findSection($(e.currentTarget));
    if (!section) return;

    // Resolve the field that was double-clicked from the event target.
    var $t = $(e.target);
    var $field = $t.closest('input, select, textarea');              // enabled field hit directly
    if ($field.length === 0) $field = $t.find('input, select, textarea').first();          // wrapper of a disabled field
    if ($field.length === 0) $field = $t.closest('.col, [class*="col-"], .form-check, .dynamic-row, td')
                                        .find('input, select, textarea').first();          // label / gap fallback

    if ($field.length === 0) return;
    if ($field.is('[readonly]') || $field.is('[type="hidden"]')) return;
    if (!$.contains(section.$el[0], $field[0])) return;

    // Reveal Save / Cancel for this section (without enabling the whole section).
    // Reset the dirty flag only when entering edit fresh, not when double-clicking
    // a second field in a section that is already being edited.
    if (section.$el.find('.action-buttons').hasClass('d-none')) {
        _clearDirty(section.id);
        // Snapshot current values so a later Cancel reverts correctly.
        _snapshotSectionDefaults(section.$el);
    }
    section.$el.find('.btn-edit').addClass('d-none');
    section.$el.find('.action-buttons').removeClass('d-none');

    // Enable ONLY the double-clicked field, and highlight it so multiple edited
    // fields are visually distinct.
    $field.prop('disabled', false).addClass('field-editing');
    if ($field.hasClass('select2-hidden-accessible')) {
        $field.trigger('change.select2');
    }

    setTimeout(function () { $field.trigger('focus'); }, 30);
});

// ── Dirty tracking: value changed while section is in edit mode ───────────────
$(document).on('input change', '[id^="section_"] input, [id^="section_"] select, [id^="section_"] textarea', function () {
    var section = _findSection($(this));
    if (!section) return;
    // Only track if currently in edit mode (action-buttons visible)
    if (section.$el.find('.action-buttons').hasClass('d-none')) return;
    _markDirty(section.id);
});

// ── Auto-cancel when focus leaves the section with no changes ─────────────────
$(document).on('focusout', '[id^="section_"] input, [id^="section_"] select, [id^="section_"] textarea', function () {
    var section = _findSection($(this));
    if (!section) return;

    setTimeout(function () {
        // Focus moved to another element still inside this section — keep editing
        if ($(document.activeElement).closest('#' + section.id).length > 0) return;

        // Section already left edit mode (Save/Cancel was clicked) — nothing to do
        if (section.$el.find('.action-buttons').hasClass('d-none')) return;

        // No changes made — auto-cancel and revert
        if (!_isDirty(section.id)) {
            _clearDirty(section.id);
            window.toggleSection(section.id, false, true);
        }
        // Dirty — leave Save/Cancel visible for user to act
    }, 200);
});

// ── Has a single field changed from its snapshotted (edit-start) value? ────────
function _fieldChanged(el) {
    if (!el) return false;
    if (el.type === 'checkbox' || el.type === 'radio') return el.checked !== el.defaultChecked;
    if (el.tagName === 'SELECT') {
        var defVal = null;
        $(el).find('option').each(function () { if (this.defaultSelected) defVal = this.value; });
        return String($(el).val()) !== String(defVal);
    }
    return el.value !== el.defaultValue;
}

// ── Per-field re-lock: a field opened via double-click but left UNCHANGED should ──
// re-lock itself on blur, even when OTHER fields in the same section were edited
// (the section-level auto-cancel above can't do this once the section is dirty).
// A field that WAS changed stays open as a pending edit.
$(document).on('focusout', '[id^="section_"] .field-editing', function () {
    var $field  = $(this);
    var section = _findSection($field);
    if (!section) return;

    setTimeout(function () {
        // Still (or again) focused on this field — leave it open.
        if (document.activeElement === $field[0]) return;
        // Section already locked (Save/Cancel handled) — nothing to do.
        if (section.$el.find('.action-buttons').hasClass('d-none')) return;
        // Field was actually edited — keep it open as a pending change.
        if (_fieldChanged($field[0])) return;

        // Unchanged → re-lock ONLY this field.
        $field.prop('disabled', true).removeClass('field-editing');
        if ($field.hasClass('select2-hidden-accessible')) $field.trigger('change.select2');

        // If nothing is left being edited AND the section has no real changes,
        // fully revert the section (hide Save/Cancel, restore view mode).
        if (section.$el.find('.field-editing').length === 0 && !_isDirty(section.id)) {
            _clearDirty(section.id);
            window.toggleSection(section.id, false, true);
        }
    }, 200);
});
