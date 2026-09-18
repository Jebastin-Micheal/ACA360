/*!
 * searchable-select.js
 * ---------------------------------------------------------------------------
 * Drop-in select2 wrapper for ACA360.
 *   - turns a <select> into a searchable dropdown
 *   - adds a search icon + clear (×) inside the search box
 *   - safe to re-run (destroy guard) — no frozen double-init on modal reopen
 *   - auto-detects modals for dropdownParent so it stays clickable inside them
 *
 * Requires: jQuery + select2 (already loaded by the Sneat theme).
 *
 * Usage:
 *   makeSearchable('#ddlService', { placeholder: '-- Select a Service --' });
 *   makeSearchable('#activeSvcSel', { placeholder: '— Select a service —' });
 *   makeSearchable('.select2');                       // all at once
 *   makeSearchable('#plainSelect', { allowClear: false });   // no clear ×
 *   makeSearchable('#noSearch', { select2: { minimumResultsForSearch: Infinity } });
 * ---------------------------------------------------------------------------
 */
(function (window, $) {
    'use strict';

    if (!$ || !$.fn || !$.fn.select2) {
        console.warn('[searchable-select] jQuery or select2 not found — skipping.');
        return;
    }

    // ── Inject CSS once ──────────────────────────────────────────────────
    var STYLE_ID = 'searchable-select-css';
    if (!document.getElementById(STYLE_ID)) {
        var css =
            '.select2-search--dropdown{position:relative}' +
            '.select2-search--dropdown .select2-search__field{' +
            'padding-left:32px;padding-right:28px;' +
            'background-image:url(\'data:image/svg+xml;utf8,' +
            '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="%238592a3" viewBox="0 0 16 16">' +
            '<path d="M11.74 10.34a6.5 6.5 0 1 0-1.4 1.4q.05.06.1.12l3.85 3.85a1 1 0 0 0 1.41-1.42l-3.85-3.85a1 1 0 0 0-.11-.1zM12 6.5a5.5 5.5 0 1 1-11 0 5.5 5.5 0 0 1 11 0"/></svg>\');' +
            'background-repeat:no-repeat;background-position:10px center;background-size:14px}' +
            '.select2-search--dropdown .s2-clear{' +
            'position:absolute;right:12px;top:50%;transform:translateY(-50%);' +
            'cursor:pointer;color:#adb5bd;font-size:16px;line-height:1;display:none}' +
            '.select2-search--dropdown .s2-clear:hover{color:#566a7f}' +
            'select.input-validation-error + .select2-container .select2-selection{border-color:#FF0000 !important}';

        var style = document.createElement('style');
        style.id = STYLE_ID;
        style.textContent = css;
        document.head.appendChild(style);
    }

    // ── Public API ───────────────────────────────────────────────────────
    function makeSearchable(selector, options) {
        options = options || {};
        var $set = (selector instanceof $) ? selector : $(selector);
        if (!$set.length) return $set;

        $set.each(function () {
            var $el = $(this);

            // avoid the frozen double-init
            if ($el.hasClass('select2-hidden-accessible')) {
                $el.select2('destroy');
            }

            var $modal = $el.closest('.modal');

            $el.select2($.extend({
                placeholder: options.placeholder || '— Select —',
                allowClear: options.allowClear !== false,   // default true; pass false to turn off
                width: '100%',
                dropdownParent: $modal.length ? $modal : $(document.body)
            }, options.select2 || {}));

            // clear-× inside the search box (select2 rebuilds the search DOM each open)
            $el.off('select2:open.s2clr').on('select2:open.s2clr', function () {
                var $field = $('.select2-dropdown .select2-search__field');
                var $wrap = $field.closest('.select2-search--dropdown');
                if (!$field.length || $wrap.find('.s2-clear').length) return;

                var $clear = $('<span class="s2-clear">&times;</span>').appendTo($wrap);
                var toggle = function () { $clear.toggle(!!$field.val()); };

                $field.on('input', toggle);
                $clear
                    .on('mousedown', function (e) { e.preventDefault(); })  // keep focus, don't close
                    .on('click', function (e) {
                        e.preventDefault();
                        e.stopPropagation();
                        $field.val('').trigger('input').trigger('keyup').focus();
                    });

                toggle();
            });
        });

        return $set;
    }

    window.makeSearchable = makeSearchable;

})(window, window.jQuery);