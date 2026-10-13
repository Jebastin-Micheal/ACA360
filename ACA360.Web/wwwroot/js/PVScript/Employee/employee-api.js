// Location: wwwroot/assets/js/employee-api.js
const EmployeeApi = (function () {

    function sanitizePayload(data) {
        let cleanData = JSON.parse(JSON.stringify(data));

        function cleanObject(obj) {
            for (let key in obj) {
                if (typeof obj[key] === 'object' && obj[key] !== null) {
                    cleanObject(obj[key]);
                } else if (obj[key] === '0001-01-01' || obj[key] === '0001-01-01T00:00:00') {
                    obj[key] = null; // Send true nulls instead of empty strings for dates
                }
            }
        }

        cleanObject(cleanData);
        return cleanData;
    }

    return {
        saveSection: async function (actionUrl, payloadData) {
            const safeData = sanitizePayload(payloadData);

            // CRITICAL FIX: Grab the anti-forgery token from the DOM
            const token = $('input[name="__RequestVerificationToken"]').first().val();

            return $.ajax({
                url: actionUrl,
                type: 'POST',
                contentType: 'application/json; charset=utf-8',
                // CRITICAL FIX: Send the token in the headers so ASP.NET doesn't reject it with a 400
                headers: {
                    "RequestVerificationToken": token
                },
                data: JSON.stringify(safeData)
            });
        },

        loadPartialView: async function (viewUrl) {
            return $.get(viewUrl);
        },
        /**
         * Read the ordered list of employee IDs that are currently rendered
         * in the grid container (both split-view sidebar and table view).
         *
         * Strategy:
         *   - Split view  → .employee-item[data-id]    (in _EmployeeListPartial_am)
         *   - Table view  → tr.employee-item[data-id]  (in _EmployeeListPartial)
         *
         * Returns a plain array of string IDs in DOM order.
         * This is always in sync with the last GridManager reload because
         * the grid replaces the entire #employeeListContainer on each reload.
         */
        getFilteredIdsFromDom: function () {
            var ids = [];

            // Try split-view items first (div.employee-item with data-id)
            $('#employeeListContainer .employee-item[data-id]').each(function () {
                var id = String($(this).data('id'));
                if (id && ids.indexOf(id) === -1) ids.push(id);
            });

            // Table view rows (tr.employee-item with data-id)
            if (ids.length === 0) {
                $('#employeeListContainer tr.employee-item[data-id]').each(function () {
                    var id = String($(this).data('id'));
                    if (id && ids.indexOf(id) === -1) ids.push(id);
                });
            }

            return ids;
        }
    };
})();