var FilterSections = (function () {
    var sections = {};

    function init(key, label, options) {
        sections[key] = { key: key, label: label, options: [], selected: {} };
        var $body = $('#fsBody_' + key);

        $body.find('.fs-search-input').on('keyup', function () { render(key); });

        $body.find('.fs-selectall').on('click', function () {
            var s = sections[key];
            var visible = visibleOptions(key);
            var allChecked = visible.length > 0 && visible.every(function (o) { return s.selected[o.value]; });
            visible.forEach(function (o) {
                if (allChecked) delete s.selected[o.value];
                else s.selected[o.value] = true;
            });
            render(key);
        });

        $body.find('.fs-options').on('click', '.fs-row', function () {
            var s = sections[key];
            var v = String($(this).data('value'));
            if (s.selected[v]) delete s.selected[v];
            else s.selected[v] = true;
            render(key);
        });

        setOptions(key, options, false);
    }

    function searchTerm(key) { return ($('#fsBody_' + key).find('.fs-search-input').val() || '').toLowerCase(); }

    function visibleOptions(key) {
        var term = searchTerm(key);
        return sections[key].options.filter(function (o) { return !term || o.text.toLowerCase().indexOf(term) !== -1; });
    }

    function isDefault(key) {
        var s = sections[key];
        if (!s) return true;
        if (s.options.length === 0) return true;
        return Object.keys(s.selected).length >= s.options.length;
    }

    function selectAllMap(options) {
        var m = {}; options.forEach(function (o) { m[o.value] = true; }); return m;
    }

    function setOptions(key, options, preserve) {
        var s = sections[key];
        options = (options || []).map(function (o) { return { value: String(o.value), text: String(o.text), dot: o.dot }; });
        var keepSubset = preserve && s.options.length > 0 && !isDefault(key);
        s.options = options;
        if (keepSubset) {
            var kept = {};
            options.forEach(function (o) { if (s.selected[o.value]) kept[o.value] = true; });
            s.selected = Object.keys(kept).length ? kept : selectAllMap(options);
        } else {
            s.selected = selectAllMap(options);
        }
        render(key);
    }

    function render(key) {
        var s = sections[key];
        var $body = $('#fsBody_' + key);
        var $list = $body.find('.fs-options').empty();
        var visible = visibleOptions(key);

        if (visible.length === 0) {
            $list.append($('<div class="fs-empty"></div>').text(s.options.length ? 'No matches' : 'No options'));
        } else {
            visible.forEach(function (o) {
                var $row = $('<div class="fs-row"></div>').attr('data-value', o.value);
                $row.append($('<span class="fs-cb"></span>').addClass(s.selected[o.value] ? 'checked' : ''));
                if (o.dot) $row.append($('<span class="fs-dot"></span>').addClass(o.dot));
                $row.append($('<span class="flex-grow-1"></span>').text(o.text));
                $list.append($row);
            });
        }

        var checkedVisible = visible.filter(function (o) { return s.selected[o.value]; }).length;
        $body.find('.fs-selectall .fs-cb')
            .toggleClass('checked', visible.length > 0 && checkedVisible === visible.length)
            .toggleClass('indet', checkedVisible > 0 && checkedVisible < visible.length);
        $body.find('.fs-matches').text(searchTerm(key) ? visible.length + ' matches' : '');

        var count = Object.keys(s.selected).length;
        var $badge = $('#fsBadge_' + key);
        if (count === 0 && s.options.length > 0) { $badge.addClass('on').text('None'); }
        else if (isDefault(key)) { $badge.removeClass('on').text('All' + (s.options.length ? ' · ' + s.options.length : '')); }
        else { $badge.addClass('on').text(summary(key)); }

        updateActiveChips();
    }

    function summary(key) {
        var s = sections[key];
        if (!s) return '';
        var count = Object.keys(s.selected).length;
        if (count === 0) return 'None';
        if (count === 1) {
            var only = s.options.find(function (o) { return s.selected[o.value]; });
            if (only) return only.text;
        }
        return count + ' of ' + s.options.length;
    }

    function getValues(key) {
        if (isDefault(key)) return [];
        var vals = Object.keys(sections[key].selected);
        return vals.length === 0 ? ['-1'] : vals;
    }

    function reset(key) {
        var s = sections[key]; s.selected = selectAllMap(s.options);
        $('#fsBody_' + key).find('.fs-search-input').val(''); render(key);
    }
    function clear(key) {
        var s = sections[key]; s.selected = {};
        $('#fsBody_' + key).find('.fs-search-input').val(''); render(key);
    }

    function resetAll() { Object.keys(sections).forEach(reset); }
    function clearAll() { Object.keys(sections).forEach(clear); }

    return {
        init: init, setOptions: setOptions, getValues: getValues, isDefault: isDefault,
        summary: summary, reset: reset, resetAll: resetAll, clearAll: clearAll,
        labelOf: function (key) { return sections[key] ? sections[key].label : key; }
    };
})();

function updateActiveChips() {
    var $c = $('#activeFilterChips');
    if ($c.length === 0) return;
    $c.empty();
    ['plan', 'status', 'enrollment', 'flag', 'other'].forEach(function (key) {
        if (!FilterSections.isDefault(key)) {
            var $chip = $('<span class="emp-chip"></span>').text(FilterSections.labelOf(key) + ': ' + FilterSections.summary(key));
            $('<span class="emp-chip-remove" title="Clear">&times;</span>').on('click', function () { FilterSections.reset(key); }).appendTo($chip);
            $c.append($chip);
        }
    });
}

function loadGlobalFlags() {
    $.ajax({
        url: '/Flags/Options', type: 'GET',
        success: function (data) {
            var opts = (Array.isArray(data) ? data : []).map(function (item) {
                var sev = (item.severity || '').toLowerCase();
                var dot = (sev === 'critical' || sev === 'error') ? 'fs-dot-danger' : sev === 'warning' ? 'fs-dot-warning' : 'fs-dot-info';
                return { value: item.value, text: item.text, dot: dot };
            });
            opts.unshift({ value: '-2', text: 'No flags' });
            FilterSections.setOptions('flag', opts, false);
        }
    });
}

function loadGlobalPlans(employerIds) {
    if (!employerIds || employerIds == 0) {
        FilterSections.setOptions('plan', [], false);
        return;
    }
    $.ajax({
        url: '/Employee/drp_plan', type: 'GET', data: { Emp_ID: employerIds },
        success: function (data) {
            var opts = (Array.isArray(data) ? data : []).map(function (item) {
                return { value: item.value ?? item.Value, text: item.text ?? item.Text };
            }).filter(function (o) { return o.value != null; });
            opts.unshift({ value: '-2', text: 'No plan' });
            FilterSections.setOptions('plan', opts, true);
        }
    });
}