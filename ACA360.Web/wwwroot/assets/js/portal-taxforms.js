(function () {
    'use strict';

    function init() {
        // These bind regardless of the preference radios being present.
        initChangeEmail();
        initDisclosureLog();

        var electronic = document.querySelector('input[name="DeliveryPreference"][value="Electronic"]');
        var paper = document.querySelector('input[name="DeliveryPreference"][value="Paper"]');
        var emailBlock = document.getElementById('emailBlock');
        var consentBlock = document.getElementById('consentBlock');
        var optElectronic = document.getElementById('optElectronic');
        var optPaper = document.getElementById('optPaper');

        if (!electronic || !paper) return;

        function apply() {
            var on = electronic.checked;
            if (emailBlock) emailBlock.classList.toggle('d-none', !on);
            if (consentBlock) consentBlock.classList.toggle('d-none', !on);
            if (optElectronic) optElectronic.classList.toggle('selected', on);
            if (optPaper) optPaper.classList.toggle('selected', !on);
        }

        electronic.addEventListener('change', apply);
        paper.addEventListener('change', apply);
        apply();
    }

    // "Change" link on the status card → jump to and focus the notification-email field.
    function initChangeEmail() {
        var link = document.querySelector('.portal-change-email');
        var input = document.getElementById('NotificationEmail');
        var block = document.getElementById('emailBlock');
        if (!link || !input) return;

        link.addEventListener('click', function (e) {
            e.preventDefault();
            if (block) block.classList.remove('d-none');
            (block || input).scrollIntoView({ behavior: 'smooth', block: 'center' });
            input.focus();
            input.select();
        });
    }

    function initDisclosureLog() {
        var modal = document.getElementById('disclosureModal');
        if (!modal) return;

        var logged = false;
        modal.addEventListener('shown.bs.modal', function () {
            if (logged) return;              // log once per page view
            logged = true;

            var url = modal.getAttribute('data-disclosure-url');
            var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
            if (!url || !tokenEl) return;

            fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: '__RequestVerificationToken=' + encodeURIComponent(tokenEl.value),
                credentials: 'same-origin'
            }).catch(function () { /* logging must never break the page */ });
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
