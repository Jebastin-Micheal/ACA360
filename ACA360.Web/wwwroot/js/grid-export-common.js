$(document).on('click', '.grid-export-action', function (e) {
    e.preventDefault();

    var format = $(this).data('format');
    var $wrapper = $(this).closest('.grid-export-wrapper');
    var exportUrl = $wrapper.data('export-url');

    if (!exportUrl) {
        console.error('No data-export-url found on .grid-export-wrapper');
        return;
    }

    var fullUrl = exportUrl + '?format=' + format;

    if (format === 'print') {
        fetch(fullUrl)
            .then(function (response) { return response.text(); })
            .then(function (html) {
                var iframe = document.createElement('iframe');
                iframe.style.display = 'none';
                document.body.appendChild(iframe);

                iframe.contentDocument.open();
                iframe.contentDocument.write(html);
                iframe.contentDocument.close();

                iframe.contentWindow.focus();
                iframe.contentWindow.print();

                setTimeout(function () {
                    document.body.removeChild(iframe);
                }, 1000);
            })
            .catch(function (err) {
                console.error('Print failed:', err);
            });
        return;
    }

    // ✅ CSV / Excel / PDF — same tab download
    fetch(fullUrl)
        .then(function (response) {
            var disposition = response.headers.get('Content-Disposition');
            var filename = 'Export';
            if (disposition && disposition.indexOf('filename=') !== -1) {
                filename = disposition.split('filename=')[1].replace(/"/g, '').trim();
            }
            return response.blob().then(function (blob) {
                return { blob: blob, filename: filename };
            });
        })
        .then(function (data) {
            var downloadUrl = window.URL.createObjectURL(data.blob);
            var a = document.createElement('a');
            a.href = downloadUrl;
            a.download = data.filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            window.URL.revokeObjectURL(downloadUrl);
        })
        .catch(function (err) {
            console.error('Export failed:', err);
        });
});
