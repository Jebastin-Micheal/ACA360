// ============================================================
// SHORTCUTS DROPDOWN - handles ONLY the 4 DYNAMIC slots
// (Dashboard, Calendar, FAQs, Settings are hardcoded in Default.cshtml,
//  they don't need JS/DB at all)
// Requires jQuery + Bootstrap (already used in the project)
// ============================================================

$(function () {
    loadMyShortcuts();

    $('#btnAddShortcut').on('click', function (e) {
        e.preventDefault();
        e.stopPropagation();

        // ignore click when all dynamic slots are already full
        if ($(this).hasClass('disabled')) {
            return;
        }

        loadAvailableShortcuts();
        hidePopupMessage();

        var pos = $(this).offset();
        $('#availableShortcutsPopup')
            .css({ top: pos.top + 26, left: pos.left - 190 })
            .toggle();
    });

    // click outside -> close the add-popup
    $(document).on('click', function (e) {
        if (!$(e.target).closest('#availableShortcutsPopup, #btnAddShortcut').length) {
            $('#availableShortcutsPopup').hide();
        }
    });
});

// ------------------------------------------------------------
// Helpers
// ------------------------------------------------------------
function getAntiForgeryToken() {
    return $('#__afTokenForm input[name="__RequestVerificationToken"]').val();
}

// Inline message shown INSIDE the Add Shortcut popup instead of alert()
function showPopupMessage(message, isError) {
    var $msg = $('#addShortcutMsg');
    $msg.text(message)
        .removeClass('text-danger text-success')
        .addClass(isError ? 'text-danger' : 'text-success')
        .stop(true, true).show();

    clearTimeout(window.__shortcutMsgTimer);
    window.__shortcutMsgTimer = setTimeout(function () {
        $msg.fadeOut(200);
    }, 3000);
}

function hidePopupMessage() {
    $('#addShortcutMsg').hide();
}

// ------------------------------------------------------------
// Load the user's DYNAMIC shortcuts only (max 4)
// GET /Shortcut/GetMyShortcuts
// ------------------------------------------------------------
var MAX_DYNAMIC_SLOTS = 4;

function loadMyShortcuts() {
    $.get('/Shortcut/GetMyShortcuts')
        .done(function (res) {
            if (res && res.success) {
                var items = res.data || [];
                renderShortcuts(items);
                updateAddButtonState(items.length);
            } else {
                $('#shortcutsContainer').empty();
            }
        })
        .fail(function () {
            $('#shortcutsContainer').empty();
        });
}

// Disable the "+" button when all 4 dynamic slots are used,
// so the user can't even open the popup / hit the "slots full" error.
// Re-enables automatically once a shortcut is removed.
function updateAddButtonState(count) {
    var $btn = $('#btnAddShortcut');

    if (count >= MAX_DYNAMIC_SLOTS) {
        $btn.addClass('disabled')
            .css({ opacity: 0.4, cursor: 'not-allowed', pointerEvents: 'none' })
            .attr('title', 'Remove a shortcut to add a new one');
    } else {
        $btn.removeClass('disabled')
            .css({ opacity: 1, cursor: 'pointer', pointerEvents: 'auto' })
            .attr('title', 'Add shortcuts');
    }
}

// ------------------------------------------------------------
// Render dynamic shortcuts: 2 items per row (max 2 rows = 4 items)
// If there are none, the container is simply left empty
// (no placeholder text/message).
// ------------------------------------------------------------
function renderShortcuts(items) {
    var $container = $('#shortcutsContainer');
    $container.empty();

    if (!items.length) {
        return; // nothing to show, keep it clean
    }

    for (var i = 0; i < items.length; i += 2) {
        var $row = $('<div class="row row-bordered overflow-visible g-0"></div>');
        $row.append(buildShortcutItem(items[i]));
        if (items[i + 1]) {
            $row.append(buildShortcutItem(items[i + 1]));
        }
        $container.append($row);
    }
}

// ------------------------------------------------------------
// Build one dynamic shortcut tile (always removable, since
// fixed items are hardcoded separately in the .cshtml)
// href = "/" + item.controller, built fresh from DB every load
// ------------------------------------------------------------
function buildShortcutItem(item) {
    var $col = $('<div class="dropdown-shortcuts-item col position-relative"></div>');

    var $remove = $('<a href="javascript:void(0)" class="position-absolute" ' +
        'style="top:6px;right:10px;z-index:2;color:#dc3545;" title="Remove">' +
        '<i class="bx bx-x" style="font-size:18px;color:#dc3545;"></i></a>');

    $remove.on('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        removeShortcut(item.menuId);
    });
    $col.append($remove);

    var $icon = $('<span class="dropdown-shortcuts-icon rounded-circle mb-3"></span>')
        .append('<i class="icon-base ' + item.icon + ' icon-26px text-heading"></i>');

    var $link = $('<a class="stretched-link"></a>')
        .attr('href', '/' + item.controller)
        .text(item.menuName);

    $col.append($icon).append($link);
    return $col;
}

// ------------------------------------------------------------
// Load menus available to add (Dashboard/Calendar/FAQs/Settings are
// excluded server-side since they're already fixed/hardcoded)
// GET /Shortcut/GetAvailable
// ------------------------------------------------------------
function loadAvailableShortcuts() {
    var $list = $('#availableShortcutsList');
    $list.html('<li class="dropdown-item p-2 text-muted small">Loading...</li>');

    $.get('/Shortcut/GetAvailable')
        .done(function (res) {
            $list.empty();

            if (res && res.success && res.data && res.data.length) {
                res.data.forEach(function (m) {
                    var $li = $('<li class="dropdown-item p-2 border-bottom d-flex align-items-center" style="cursor:pointer;"></li>')
                        .html('<i class="icon-base ' + m.icon + ' me-2"></i> ' + m.menuName);

                    $li.on('click', function () {
                        addShortcut(m.menuId);
                    });

                    $list.append($li);
                });
            } else {
                $list.append('<li class="dropdown-item p-2 text-muted small">No menus available to add.</li>');
            }
        })
        .fail(function () {
            $list.html('<li class="dropdown-item p-2 text-muted small">Error loading menus.</li>');
        });
}

// ------------------------------------------------------------
// Add a shortcut (fills the next free dynamic slot, max 4)
// POST /Shortcut/Add
// ------------------------------------------------------------
function addShortcut(menuId) {
    $.ajax({
        url: '/Shortcut/Add',
        type: 'POST',
        data: {
            menuId: menuId,
            __RequestVerificationToken: getAntiForgeryToken()
        },
        success: function (res) {
            if (res && res.success) {
                loadMyShortcuts();
                // close the popup right after a successful add.
                // If the user wants to add another one, they click "+" again.
                setTimeout(function () {
                    $('#availableShortcutsPopup').hide();
                }, 500); // tiny delay so the "Shortcut added!" message is visible
                showPopupMessage('Shortcut added!', false);
            } else {
                showPopupMessage((res && res.message) || 'All slots are full. Please remove one first.', true);
            }
        },
        error: function () {
            showPopupMessage('Error adding shortcut.', true);
        }
    });
}

// ------------------------------------------------------------
// Remove a dynamic shortcut
// POST /Shortcut/Remove
// ------------------------------------------------------------
function removeShortcut(menuId) {
    $.ajax({
        url: '/Shortcut/Remove',
        type: 'POST',
        data: {
            menuId: menuId,
            __RequestVerificationToken: getAntiForgeryToken()
        },
        success: function (res) {
            if (res && res.success) {
                loadMyShortcuts();
            }
        }
    });
}