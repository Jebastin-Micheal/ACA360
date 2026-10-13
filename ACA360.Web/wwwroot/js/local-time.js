/* Rewrites <time class="js-local-time" datetime="...Z"> elements (rendered by
   Html.LocalTime) in the viewer's time zone. Runs on load and after every jQuery
   AJAX call, so partials loaded into grids and modals are converted too. */
(function () {
    var MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    function format(d, kind) {
        var mon = MONTHS[d.getMonth()], day = pad(d.getDate());
        var hm = pad(d.getHours()) + ':' + pad(d.getMinutes());
        var hms = hm + ':' + pad(d.getSeconds());
        switch (kind) {
            case 'long': return mon + ' ' + day + ', ' + d.getFullYear() + ' · ' + hm;
            case 'seconds': return mon + ' ' + day + ' · ' + hms;
            case 'time': return hms;
            default: return mon + ' ' + day + ', ' + hm;
        }
    }

    function renderLocalTimes(root) {
        (root || document).querySelectorAll('time.js-local-time:not([data-localized])').forEach(function (el) {
            var d = new Date(el.getAttribute('datetime'));
            if (isNaN(d.getTime())) return;
            el.textContent = format(d, el.getAttribute('data-format'));
            el.title = d.toString();
            el.setAttribute('data-localized', '1');
        });
    }

    window.renderLocalTimes = renderLocalTimes;
    document.addEventListener('DOMContentLoaded', function () { renderLocalTimes(); });
    if (window.jQuery) jQuery(document).ajaxComplete(function () { renderLocalTimes(); });
})();
