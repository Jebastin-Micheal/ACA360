(function ($) {
    'use strict';

    /* ── Core mask function (mirrors C# DataMasker.MaskEIN) ── */
    function maskEIN(ein) {
        if (!ein) return '';
        var clean = ein.replace(/\D/g, '');
        return clean.length === 9 ? '**-***' + clean.substr(5, 4) : ein;
    }

    /* ── Global helper for non-plugin use (table rows, badges etc) ── */
    window.maskEINDisplay = function (ein) {
        return maskEIN(ein) || '—';
    };

    /* ── $.fn.einMask ── */
    $.fn.einMask = function () {
        return this.each(function () {
            var $input = $(this);
            if ($input.data('ein-init')) return;
            $input.data('ein-init', true);

            $input
                .wrap('<div class="input-group"></div>')
                .after('<span class="input-group-text cursor-pointer ein-eye" title="Show / Hide EIN" style="display:none"><i class="bx bx-hide"></i></span>');

            var $eye = $input.next('.ein-eye');
            var $icon = $eye.find('i');
            var revealed = false;

            $input[0]._einShowEye = function () { $eye.show(); };
            $input[0]._einHideEye = function () { $eye.hide(); };

            $eye.on('mousedown', function (e) { e.preventDefault(); });

            $eye.on('click', function () {
                revealed = !revealed;
                var canReveal = $input.data('can-reveal') !== false && $input.data('can-reveal') !== 'false';
                if (revealed) {
                    var shown = canReveal ? $input.data('real') : maskEIN($input.data('real'));
                    $input.val(shown).removeAttr('readonly').focus();
                }
else {
                    $input.data('real', $input.val());
                    $input.val(maskEIN($input.val())).attr('readonly', true);
                }
                $icon.toggleClass('bx-hide', !revealed)
                    .toggleClass('bx-show', revealed);
            });

            $input.on('input', function () {
                if (revealed) $input.data('real', $input.val());
            });

            $input.on('blur', function () {
                setTimeout(function () {
                    if (revealed && !$eye.is(':focus')) {
                        revealed = false;
                        $input.data('real', $input.val());
                        $input.val(maskEIN($input.val())).attr('readonly', true);
                        $icon.removeClass('bx-show').addClass('bx-hide');
                    }
                }, 150);
            });

            $input[0]._einReset = function () {
                revealed = false;
                $input.val(maskEIN($input.data('real') || '')).attr('readonly', true);
                $icon.removeClass('bx-show').addClass('bx-hide');
            };
        });
    };

    $.fn.einMaskPrepare = function () {
        return this.find('.ein-mask-field').each(function () {
            $(this).val($(this).data('real') || '');
        });
    };

    $.fn.einMaskRestore = function () {
        return this.find('.ein-mask-field').each(function () {
            var $el = $(this);
            if ($el[0]._einReset) $el[0]._einReset();
            if ($el[0]._einHideEye) $el[0]._einHideEye();
        });
    };

    $.einMaskEditStart = function () {
        $('.ein-mask-field').each(function () {
            if (this._einShowEye) this._einShowEye();
        });
    };

    $.einMaskEditEnd = function () {
        $('.ein-mask-field').each(function () {
            if (this._einHideEye) this._einHideEye();
            if (this._einReset) this._einReset();
        });
    };

}(jQuery));