/* ==========================================================================
   BULK SELECTION (idea #20)
   A list marks rows with <input type="checkbox" class="bulk-row" value="id" data-...>
   and its header with <input type="checkbox" class="bulk-all">. A bar marked
   [data-bulk-bar] shows how many are picked ([data-bulk-count]) while any are.
   Pages run their actions with BulkSelect.runEach, one row after another, so each
   row goes through the same checks as its own button.
   ========================================================================== */
window.BulkSelect = (function () {
    var cfg = { root: 'body', bar: '[data-bulk-bar]' };

    function rows() { return $(cfg.root).find('input.bulk-row:not(:disabled)'); }
    function selected() { return rows().filter(':checked').toArray(); }

    function refresh() {
        var n = selected().length, total = rows().length;
        var $bar = $(cfg.bar);
        $bar.toggleClass('d-none', n === 0);
        // Only when it changes: a bar inside the list would otherwise wake the observer below forever.
        $bar.find('[data-bulk-count]').each(function () { if (this.textContent !== String(n)) this.textContent = n; });
        $(cfg.root).find('input.bulk-all').each(function () {
            var $scope = $(this).closest('table');
            var $mine = $scope.find('input.bulk-row:not(:disabled)');
            var picked = $mine.filter(':checked').length;
            this.checked = $mine.length > 0 && picked === $mine.length;
            this.indeterminate = picked > 0 && picked < $mine.length;
        });
        $bar.trigger('bulk:changed', [n, total]);
    }

    function clear() { rows().prop('checked', false); refresh(); }

    var bound = false;

    function init(options) {
        $.extend(cfg, options || {});
        // A list partial can call this on every reload: bind once, then just recount.
        if (bound) { refresh(); return; }
        bound = true;
        $(document).on('change', cfg.root + ' input.bulk-row', refresh);
        $(document).on('change', cfg.root + ' input.bulk-all', function () {
            $(this).closest('table').find('input.bulk-row:not(:disabled)').prop('checked', this.checked);
            refresh();
        });
        $(document).on('click', cfg.bar + ' [data-bulk-clear]', clear);
        // The list is replaced on every reload: start the count again from what is there.
        var root = document.querySelector(cfg.root);
        if (root && window.MutationObserver) {
            new MutationObserver(function () { refresh(); }).observe(root, { childList: true, subtree: true });
        }
        refresh();
    }

    /**
     * Runs worker(item) for each item in turn (worker returns a promise of {success, message}).
     * Resolves to { ok, failed, messages } so the page can say what happened to each.
     */
    async function runEach(items, worker, onProgress) {
        var result = { ok: 0, failed: 0, messages: [] };
        for (var i = 0; i < items.length; i++) {
            try {
                var r = await worker(items[i]);
                if (r && r.success === false) { result.failed++; if (r.message) result.messages.push(r.message); }
                else result.ok++;
            } catch (e) {
                result.failed++;
                var msg = e && e.responseJSON && e.responseJSON.message;
                if (msg) result.messages.push(msg);
            }
            if (typeof onProgress === 'function') onProgress(i + 1, items.length);
        }
        return result;
    }

    function summary(result, verb) {
        var text = result.ok + ' ' + verb;
        if (result.failed) {
            var reasons = Array.from(new Set(result.messages)).slice(0, 2).join(' ');
            text += ', ' + result.failed + ' could not be' + (reasons ? ': ' + reasons : '.');
        }
        return text;
    }

    return { init: init, refresh: refresh, selected: selected, clear: clear, runEach: runEach, summary: summary };
})();
