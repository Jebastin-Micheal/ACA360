//Script 1 
document.addEventListener("DOMContentLoaded", function () {
    let currentPage = 1;
    let currentSearchTerm = "";
    let currentPageSize = 25;
    let totalFilesCount = 0;

    const searchTermInput = document.getElementById('searchTerm');
    const pageSizeSelect = document.getElementById('pageSize');
    const filesTableBody = document.getElementById('filesTableBody');
    const paginationControls = document.getElementById('paginationControls');

    fetchUploadedFiles(); // Initial fetch when the page loads

    searchTermInput.addEventListener('keyup', debounce(function () {
        currentSearchTerm = searchTermInput.value;
        currentPage = 1;
        fetchUploadedFiles();
    }, 300));

    pageSizeSelect.addEventListener('change', function () {
        currentPageSize = parseInt(pageSizeSelect.value);
        currentPage = 1;
        fetchUploadedFiles();
    });

    function debounce(func, delay) {
        let timeout;
        return function (...args) {
            const context = this;
            clearTimeout(timeout);
            timeout = setTimeout(() => func.apply(context, args), delay);
        };
    }

    async function fetchUploadedFiles() {
        const filesTableContainer = document.getElementById("filesTableContainer");

        filesTableContainer.innerHTML = `
            <div class="text-center py-4">
                <span class="spinner-border text-primary" role="status"></span>
                <div class="mt-2">Loading files...</div>
            </div>`;

        try {
            const response = await fetch(`/EINUpload/GetUploadedFiles?searchTerm=${encodeURIComponent(currentSearchTerm)}&pageNumber=${currentPage}&pageSize=${currentPageSize}`);

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(errorText);
            }

            const data = await response.json();

            totalFilesCount = data.totalCount;

            // Now fetch and show partial view with current page data
            const partialView = await fetch(`/EINUpload/_FilesTablePartial?searchTerm=${encodeURIComponent(currentSearchTerm)}&pageNumber=${currentPage}&pageSize=${currentPageSize}`);
            filesTableContainer.innerHTML = await partialView.text();

            renderPagination(totalFilesCount); // ✅ This will render the << < 1 2 3 ... >> controls

        } catch (error) {
            console.error("Fetch error:", error);
            filesTableContainer.innerHTML = `<div class="text-danger text-center py-3">Error loading files. Please try again later.</div>`;
        }
    }


    function renderTable(files) {
        filesTableBody.innerHTML = '';
        if (files.length === 0) {
            filesTableBody.innerHTML = '<tr><td colspan="9" class="text-center">No files found.</td></tr>';
            return;
        }

        files.forEach(file => {
            const uploadDate = new Date(file.uploadDate).toLocaleDateString('en-GB');
            const row = `
                    <tr>
                        <td>
                            <img src="https://img.icons8.com/color/48/000000/ms-excel.png" width="30" height="30" alt="Excel" />
                            ${file.fileName}
                        </td>
                        <td>${uploadDate}</td>
                        <td>1</td>
                        <td>2</td>
                        <td>100%</td>
                        <td>Developing...</td>
                        <td class="${file.uploadStatus === "Success" ? "text-success" : "text-danger"}">
                            ${file.uploadStatus}
                        </td>
                        <td>
                            <button type="button" class="btn btn-link p-0" onclick="downloadFile(${file.id})">
                                <i class='bx bx-download fs-3'></i>
                            </button>
                        </td>
                        <td>
                            <button type="button" class="btn btn-sm" onclick="deleteFile(${file.id})">
                                <i class='bx bx-trash text-danger fs-3'></i>
                            </button>
                        </td>
                    </tr>
                `;
            filesTableBody.insertAdjacentHTML('beforeend', row);
        });
    }

    function renderPagination(totalCount) {
        paginationControls.innerHTML = '';
        const totalPages = Math.ceil(totalCount / currentPageSize);

        if (totalPages <= 1) return;

        // First Page Button (<<)
        const firstLi = document.createElement('li');
        firstLi.className = `page-item ${currentPage === 1 ? 'disabled' : ''}`;
        firstLi.innerHTML = `<a class="page-link" href="#" data-page="1" aria-label="First">&laquo;</a>`;
        paginationControls.appendChild(firstLi);

        // Previous Page Button (<)
        const prevLi = document.createElement('li');
        prevLi.className = `page-item ${currentPage === 1 ? 'disabled' : ''}`;
        prevLi.innerHTML = `<a class="page-link" href="#" data-page="${currentPage - 1}" aria-label="Previous">&lsaquo;</a>`;
        paginationControls.appendChild(prevLi);

        // Page Numbers with Ellipsis
        const maxVisiblePages = 5; // Number of pages to show around current page
        let startPage = Math.max(1, currentPage - Math.floor(maxVisiblePages / 2));
        let endPage = Math.min(totalPages, startPage + maxVisiblePages - 1);

        // Adjust if we're at the end
        if (endPage - startPage + 1 < maxVisiblePages) {
            startPage = Math.max(1, endPage - maxVisiblePages + 1);
        }

        // Show first page + ellipsis if needed
        if (startPage > 1) {
            const li = document.createElement('li');
            li.className = 'page-item';
            li.innerHTML = `<a class="page-link" href="#" data-page="1">1</a>`;
            paginationControls.appendChild(li);

            if (startPage > 2) {
                const ellipsisLi = document.createElement('li');
                ellipsisLi.className = 'page-item disabled';
                ellipsisLi.innerHTML = `<span class="page-link">...</span>`;
                paginationControls.appendChild(ellipsisLi);
            }
        }

        // Visible page numbers
        for (let i = startPage; i <= endPage; i++) {
            const li = document.createElement('li');
            li.className = `page-item ${currentPage === i ? 'active' : ''}`;
            li.innerHTML = `<a class="page-link" href="#" data-page="${i}">${i}</a>`;
            paginationControls.appendChild(li);
        }

        // Show last page + ellipsis if needed
        if (endPage < totalPages) {
            if (endPage < totalPages - 1) {
                const ellipsisLi = document.createElement('li');
                ellipsisLi.className = 'page-item disabled';
                ellipsisLi.innerHTML = `<span class="page-link">...</span>`;
                paginationControls.appendChild(ellipsisLi);
            }

            const li = document.createElement('li');
            li.className = 'page-item';
            li.innerHTML = `<a class="page-link" href="#" data-page="${totalPages}">${totalPages}</a>`;
            paginationControls.appendChild(li);
        }

        // Next Page Button (>)
        const nextLi = document.createElement('li');
        nextLi.className = `page-item ${currentPage === totalPages ? 'disabled' : ''}`;
        nextLi.innerHTML = `<a class="page-link" href="#" data-page="${currentPage + 1}" aria-label="Next">&rsaquo;</a>`;
        paginationControls.appendChild(nextLi);

        // Last Page Button (>>)
        const lastLi = document.createElement('li');
        lastLi.className = `page-item ${currentPage === totalPages ? 'disabled' : ''}`;
        lastLi.innerHTML = `<a class="page-link" href="#" data-page="${totalPages}" aria-label="Last">&raquo;</a>`;
        paginationControls.appendChild(lastLi);

        // Add click event listeners
        paginationControls.querySelectorAll('.page-link').forEach(link => {
            link.addEventListener('click', function (e) {
                e.preventDefault();
                if (this.parentElement.classList.contains('disabled')) return;

                const newPage = parseInt(this.dataset.page);
                if (newPage > 0 && newPage <= totalPages && newPage !== currentPage) {
                    currentPage = newPage;
                    fetchUploadedFiles();
                }
            });
        });
    }

    // Global functions for table actions
    window.downloadFile = function (id) {
        const url = `/EINUpload/Download?id=${id}`;
        window.open(url, '_blank');
    }

    window.deleteFile = async function (id) {
        if (!confirm("Are you sure you want to delete this file? This action cannot be undone.")) {
            return;
        }

        try {
            const response = await fetch('/EINUpload/Delete', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded'
                },
                body: `id=${id}`
            });

            if (!response.ok) {
                const errorResult = await response.json();
                alert(`Deletion failed: ${errorResult.message || "Unknown server error"}`);
                return;
            }

            const result = await response.json();
            if (result.success) {
                alert("File deleted successfully!");
                fetchUploadedFiles();
            } else {
                alert(`Deletion failed: ${result.message}`);
            }
        } catch (error) {
            console.error("Delete error:", error);
            alert("Deletion failed due to a network error or server issue.");
        }
    }
});

//script 2 
document.addEventListener("DOMContentLoaded", function () {
    const dropArea = document.getElementById('modalDropClickArea');
    const dropzone = document.getElementById('modalDropzone');
    const fileInput = document.getElementById('modalFileInput');
    const previewDiv = document.getElementById('modalPreview');
    const tabList = document.getElementById('modalTabList');
    const tabContent = document.getElementById('modalTabContent');

    let selectedFile = null; // 🔴 Hold the selected file for later upload

    dropArea.addEventListener('click', () => fileInput.click());

    function resetUI() {
        previewDiv.innerHTML = '';
        tabList.innerHTML = '';
        tabContent.innerHTML = '';
        dropzone.style.display = 'block';
        selectedFile = null; // 🔴 Reset
    }

    function handleFiles(files) {
        if (!files.length) return;
        const file = files[0];
        const fileName = file.name;
        const fileExt = fileName.split('.').pop().toLowerCase();
        const isExcel = fileExt === 'xlsx';

        resetUI(); // 🧨 This was clearing selectedFile

        selectedFile = file; // ✅ Move this AFTER resetUI

        if (!isExcel) {
            previewDiv.innerHTML = `
        <div class="alert alert-danger">
          <strong>${file.name}</strong> is not supported.<br/>
          Only Excel (.xlsx) files can be previewed.
        </div>`;
            return;
        }

        dropzone.style.display = 'none';

        const fileInfo = document.createElement('div');
        fileInfo.className = 'd-flex align-items-center justify-content-end gap-2 mb-2';
        fileInfo.innerHTML = `
          <span class="fw-bold">${file.name}</span>
          <img src="https://img.icons8.com/color/48/000000/ms-excel.png" width="40" />`;
        previewDiv.appendChild(fileInfo);

        const reader = new FileReader();
        reader.onload = function (e) {
            const data = new Uint8Array(e.target.result);
            const workbook = XLSX.read(data, { type: 'array' });

            workbook.SheetNames.forEach((sheetName, index) => {
                const sheet = workbook.Sheets[sheetName];
                const jsonData = XLSX.utils.sheet_to_json(sheet, { header: 1 });
                if (jsonData.length === 0) return;

                const tabId = `modal-tab-${index}`;
                const li = document.createElement('li');
                li.className = `nav-item${index !== 0 ? ' ms-1' : ''}`;
                li.innerHTML = `
              <a class="nav-link ${index === 0 ? 'active' : ''}" id="${tabId}-tab"
                 data-bs-toggle="tab" href="#${tabId}" role="tab"
                 aria-controls="${tabId}" aria-selected="${index === 0}">
                 ${sheetName}
              </a>`;
                tabList.appendChild(li);

                const tabPane = document.createElement('div');
                tabPane.className = `tab-pane fade ${index === 0 ? 'show active' : ''}`;
                tabPane.id = tabId;
                tabPane.setAttribute('role', 'tabpanel');

                const scrollWrapper = document.createElement('div');
                scrollWrapper.className = 'ps';
                scrollWrapper.style.maxHeight = '400px';
                scrollWrapper.style.overflow = 'auto';

                const table = document.createElement('table');
                table.className = 'table table-bordered table-hover';

                const thead = document.createElement('thead');
                thead.className = 'table-light';
                const headRow = document.createElement('tr');
                jsonData[0].forEach(header => {
                    const th = document.createElement('th');
                    th.textContent = header;
                    headRow.appendChild(th);
                });
                thead.appendChild(headRow);
                table.appendChild(thead);

                const tbody = document.createElement('tbody');
                jsonData.slice(1).forEach(row => {
                    const tr = document.createElement('tr');
                    row.forEach(cell => {
                        const td = document.createElement('td');
                        td.textContent = cell ?? '';
                        td.style.wordBreak = 'break-word';
                        tr.appendChild(td);
                    });
                    tbody.appendChild(tr);
                });
                table.appendChild(tbody);

                scrollWrapper.appendChild(table);
                tabPane.appendChild(scrollWrapper);
                tabContent.appendChild(tabPane);

                new PerfectScrollbar(scrollWrapper);
            });
        };

        reader.readAsArrayBuffer(file);
    }

    fileInput.addEventListener('change', function (e) {
        handleFiles(e.target.files);
    });

    ['dragenter', 'dragover'].forEach(eventName => {
        dropArea.addEventListener(eventName, e => {
            e.preventDefault();
            dropArea.classList.add('border-primary');
            dropArea.style.background = "#f0f8ff";
        });
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropArea.addEventListener(eventName, e => {
            e.preventDefault();
            dropArea.classList.remove('border-primary');
            dropArea.style.background = "transparent";
        });
    });

    dropArea.addEventListener('drop', e => {
        handleFiles(e.dataTransfer.files);
    });

    // 🔘 Upload Button Click Handler (inside modal)
    document.getElementById("uploadExcelBtn").addEventListener("click", function () {
        if (!selectedFile) {
            alert("No file selected. Please choose an Excel file first.");
            return;
        }

        const formData = new FormData();
        formData.append("file", selectedFile);

        // Disable button and show spinner
        const btn = this;
        btn.disabled = true;
        btn.innerHTML = `<span class="spinner-border spinner-border-sm me-1"></span> Uploading...`;

        fetch("/EINUpload/UploadViaAjax", {
            method: "POST",
            body: formData
        })
            .then(response => response.json())
            .then(result => {
                alert("File uploaded successfully!");
                location.reload(); // 🔄 Reload to refresh uploaded table
            })
            .catch(error => {
                console.error("Upload Error:", error);
                alert("Upload failed!");
                btn.disabled = false;
                btn.innerHTML = `<i class='bx bx-upload me-1'></i> Upload to Server`;
            });
    });
});
//Script 3
const dropzone = document.getElementById("modalDropzone");
const fileInput = document.getElementById("modalFileInput");

// 🖱️ Click anywhere in dropzone to trigger file input
dropzone.addEventListener("click", () => {
    fileInput.click();
});

// 🧲 Drag and drop support
dropzone.addEventListener("dragover", (e) => {
    e.preventDefault();
    dropzone.classList.add("border-primary"); // optional style on hover
});

dropzone.addEventListener("dragleave", () => {
    dropzone.classList.remove("border-primary"); // reset style
});

dropzone.addEventListener("drop", (e) => {
    e.preventDefault();
    dropzone.classList.remove("border-primary");

    const files = e.dataTransfer.files;
    if (files.length > 0) {
        fileInput.files = files;

        // 👉 Optional: trigger change event manually
        const event = new Event("change", { bubbles: true });
        fileInput.dispatchEvent(event);
    }
});

// 👀 Handle file selection (from click or drop)
fileInput.addEventListener("change", () => {
    const fileName = fileInput.files[0]?.name || "No file selected";
   // console.log("Selected file:", fileName);
});

//Download
function downloadFile(id) {
    const url = `/EINUpload/Download?id=${id}`;
    window.open(url, '_blank');
}
