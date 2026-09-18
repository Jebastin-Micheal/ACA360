$(document).ready(function () {

    // Helper: Determine Modal or Page execution Context to prevent DOM overlaps
    function getActiveContext(element) {
        let $modal = $(element).closest('.modal, .modal-content, .offcanvas');
        return $modal.length > 0 ? $modal : $(element).closest('.card').parent();
    }

    const isNewRecord = !$("#plan_ide").val() || $("#plan_ide").val() === "0";

    // Initialize State 
    if (isNewRecord) {
        if ($("#plan_banding_details tbody tr.dynamic-band-row").length === 0) {
            appendNewBandRow($("#plan_banding_details table tbody"));
        }
    } else {
        checkEmptyBandingGrid($("#plan_banding_details table tbody"));
    }

    // --- DOM EVENT LISTENERS ---

    $(document).off("click", "#btn_plan_edit").on("click", "#btn_plan_edit", function () {
        let $context = getActiveContext(this);
        enterEditMode($context);
    });

    $(document).off("click", "#btn_plan_cancel").on("click", "#btn_plan_cancel", function () {
        let $context = getActiveContext(this);

        if (isNewRecord) {
            // Cancel on a new record simply aborts the process (close modal or redirect)
            if ($context.closest('.modal').length) {
                $context.closest('.modal').modal('hide');
            } else {
                window.location = "/Plan/index";
            }
        } else {
            exitEditMode($context);
        }
    });

    $(document).off("click", "#add_band").on("click", "#add_band", function () {
        let $tbody = getActiveContext(this).find("#plan_banding_details table tbody");
        appendNewBandRow($tbody);
    });

    $(document).off("click", ".btn-delete-row").on("click", ".btn-delete-row", function () {
        let $tbody = $(this).closest("tbody");
        $(this).closest("tr").remove();
        checkEmptyBandingGrid($tbody);
    });

    $(document).off("click", "#btn_plan_save").on("click", "#btn_plan_save", function (e) {
        e.preventDefault();
        let $context = getActiveContext(this);

        if (!validatePlanForm($context)) {
            return; // Halt if validation fails
        }

        let planData = collectPlanFormData($context);
        let token = $context.find('input[name="__RequestVerificationToken"]').val();
        let actionText = isNewRecord ? "create" : "update";

        if (typeof Swal !== 'undefined') {
            Swal.fire({
                title: 'Are you sure?',
                text: `Do you want to ${actionText} this plan?`,
                icon: 'question',
                showCancelButton: true,
                confirmButtonText: `Yes, ${actionText} it!`,
                cancelButtonText: 'Cancel',
                reverseButtons: true
            }).then((result) => {
                if (result.isConfirmed) submitPlanData(planData, token);
            });
        } else {
            if (confirm(`Do you want to ${actionText} this plan?`)) {
                submitPlanData(planData, token);
            }
        }
    });

    // --- CORE FUNCTIONS ---

    function enterEditMode($context) {
        $context.find("#plan_info input, #plan_info select").prop("disabled", false);
        $context.find("#plan_banding_details input").removeClass("d-none");
        $context.find("#plan_banding_details span.view-text").addClass("d-none");

        $context.find("#btn_plan_edit").addClass("d-none");
        $context.find("#add_band, .btn-delete-row, .td_delete_row, #btn_plan_save, #btn_plan_cancel").removeClass("d-none");
    }

    function exitEditMode($context) {
        $context.find("#plan_info input, #plan_info select").prop("disabled", true);
        $context.find("#plan_banding_details input").addClass("d-none");
        $context.find("#plan_banding_details span.view-text").removeClass("d-none");

        $context.find("#btn_plan_edit").removeClass("d-none");
        $context.find("#add_band, .btn-delete-row, .td_delete_row, #btn_plan_save, #btn_plan_cancel").addClass("d-none");

        // Discard any unsaved rows added during the edit session
        $context.find("#plan_banding_details tr.dynamic-band-row:not([data-saved='true'])").remove();
        checkEmptyBandingGrid($context.find("#plan_banding_details table tbody"));
    }

    function appendNewBandRow($tbody) {
        $tbody.find("#no_data_found").remove();
        let nextIndex = Date.now(); // Unique ID to prevent DOM conflicts

        let rowHtml = `
            <tr class="dynamic-band-row" data-index="${nextIndex}">
              <td>
                <input name="Plan_start_value" class="form-control text-uppercase plan_start_value" type="text" value="" />
                <span class="view-text view_plan_start d-none"></span>
              </td>
              <td>
                <input name="Plan_end_value" class="form-control text-uppercase plan_end_value" type="text" value="" />
                <span class="view-text view_plan_end d-none"></span>
              </td>
              <td>
                <input name="Premium_start" class="form-control text-uppercase premium_start" type="date" value="" />
                <span class="view-text view_premium_start d-none"></span>
              </td>
              <td>
                <input name="Premium_end" class="form-control text-uppercase premium_end" type="date" value="" />
                <span class="view-text view_premium_end d-none"></span>
              </td>
              <td>
                <input name="Amount" class="form-control amount" type="number" value="" />
                <span class="view-text view_amount d-none"></span>
              </td>
              <td class="td_delete_row"> 
                 <button type="button" class="btn btn-danger btn-delete-row"> <i class="bx bx-x"></i></button>
              </td>
            </tr>
        `;
        $tbody.append(rowHtml);
    }

    function checkEmptyBandingGrid($tbody) {
        if ($tbody.find("tr.dynamic-band-row").length === 0) {
            $tbody.html('<tr id="no_data_found"><td colspan="6" class="text-center text-danger">No data found</td></tr>');
        }
    }

    function validatePlanForm($context) {
        let isValid = true;
        $context.find(".text-danger").text(""); // Clear previous errors

        if ($context.find("#plan_name").val().trim() === "") {
            $context.find("[data-valmsg-for='Name']").text("Enter Plan Name");
            isValid = false;
        }
        if ($context.find("#plan_fund_type").val() === "") {
            $context.find("[data-valmsg-for='FundingType']").text("Select Funding Type");
            isValid = false;
        }

        return isValid;
    }

    function collectPlanFormData($context) {
        var benefits = [];

        $context.find("#plan_banding_details tbody tr.dynamic-band-row").each(function () {
            const $row = $(this);

            let benefit = {
                Plan_start_value: $row.find(".plan_start_value").val() || "",
                Plan_end_value: $row.find(".plan_end_value").val() || "",
                Premium_start: $row.find(".premium_start").val() || null,
                Premium_end: $row.find(".premium_end").val() || null,
                Amount: parseFloat($row.find(".amount").val()) || 0
            };

            // Fix: Checks length/existence instead of strict "" equality to account for "0"
            if (benefit.Plan_start_value.toString().trim() !== "" || benefit.Plan_end_value.toString().trim() !== "" || benefit.Amount > 0) {
                benefits.push(benefit);
            }
        });

        return {
            ide: $context.find("#plan_ide").val() || "0",
            EmployerId: $context.find("#plan_EmployerId").val() || null,
            Name: $context.find("#plan_name").val() || "",
            FundingType: $context.find("#plan_fund_type").val() || null,
            WaitingDays: $context.find("#plan_waitingdays").val() ? parseInt($context.find("#plan_waitingdays").val()) : null,
            PremiumCap: $context.find("#plan_premiumcap").val().trim() !== "" ? parseFloat($context.find("#plan_premiumcap").val()) : null,
            BandingType: $context.find("#plan_banding_type").val() || null,
            EligibleFirstOfMonth: $context.find("#plan_eligiable").val() || null,
            MedicalPlan: $context.find("#plan_medical").val() || null,

            OfferedSpouse: $context.find("#plan_offeredspouse").is(":checked"),
            ConditionallyOffSpouse: $context.find("#plan_conditionally").is(":checked"),
            OfferedDependents: $context.find("#plan_offereddependents").is(":checked"),
            PlanTermTermination: $context.find("#plan_planterm").is(":checked"),
            PlanRenewal: $context.find("#plan_renewal").val() ? parseInt($context.find("#plan_renewal").val()) : null,
            MinimumValue: $context.find("#plan_minimumvalue").is(":checked"),

            CodeOneA: $context.find("#plan_codeoneA").is(":checked"),
            CodeTwoF: $context.find("#plan_codetwoF").is(":checked"),
            CodeTwoG: $context.find("#plan_codetwoG").is(":checked"),
            CodeTwoH: $context.find("#plan_codetwoH").is(":checked"),

            IsIchra: $context.find("#plan_isichra").is(":checked"),
            IchraLocationBasis: $context.find("#plan_ichrabasis").val() || null,
            IchraSelfOnlyAllow: $context.find("#plan_ichraallow").val().trim() !== "" ? parseFloat($context.find("#plan_ichraallow").val()) : null,

            Benefits: benefits
        };
    }

    function submitPlanData(planData, token) {
        $.ajax({
            type: "POST",
            url: "/Plan/PlanSave",
            data: JSON.stringify(planData),
            contentType: "application/json",
            headers: {
                "RequestVerificationToken": token
            },
            success: function () {
                window.location = "/Plan/index"; // Complete refresh or context closure
            },
            error: function (xhr, status, error) {
                console.error("Save Error:", xhr.responseText);
                alert("Save failed. Check console for details.");
            }
        });
    }
});