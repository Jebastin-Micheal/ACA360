// Define a global function so it can be called from anywhere

window.applyGlobalMasks = function ($context) {

    $context = $context || $(document);

    $context.find('.mask-us-phone').mask('(000) 000-0000');

    //$context.find('.mask-ssn').mask('000-00-0000');

    $context.find('.mask-ssn').each(function () {

        var value = $(this).val();

        // Already masked value -> don't apply jquery.mask
        if (value && value.indexOf('*') !== -1)
            return;

        $(this).mask('000-00-0000');
    });

    $context.find('.mask-ein').mask('00-0000000');

};

// EIN formatter

function formatEin(value) {

    // Numbers only
    value = value.replace(/\D/g, '');

    if (value.length > 2) {
        value = value.substring(0, 2) + '-' + value.substring(2);
    }

    return value.substring(0, 10);
}

$(document).on('input', '.ein-input', function () {
    this.value = formatEin(this.value);
});

// Initial page load

$(document).ready(function () {

    window.applyGlobalMasks();

});

// AJAX loaded content

$(document).ajaxComplete(function () {

    if (window.applyGlobalMasks) {

        window.applyGlobalMasks();

    }

});