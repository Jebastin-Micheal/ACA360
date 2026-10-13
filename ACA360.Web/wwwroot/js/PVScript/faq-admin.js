// ==========================================
// GLOBALS & MODAL INITIALIZATION HELPERS
// ==========================================
let faqModalInstance = null;
let contactModalInstance = null;

// Safely gets or creates the FAQ Modal
function getFaqModal() {
    if (!faqModalInstance) {
        const el = document.getElementById('faqModal');
        if (el) faqModalInstance = new bootstrap.Modal(el);
    }
    return faqModalInstance;
}

// Safely gets or creates the Contact Modal
function getContactModal() {
    if (!contactModalInstance) {
        const el = document.getElementById('contactModal');
        if (el) contactModalInstance = new bootstrap.Modal(el);
    }
    return contactModalInstance;
}


// --- 1. SEARCH LOGIC ---
const searchInput = document.getElementById('faqSearchInput');
const layoutContainer = document.getElementById('faqLayoutContainer');
const noResultsMsg = document.getElementById('noSearchResults');

// Helper: Escape Regex characters so special characters don't break the search
function escapeRegExp(string) {
    return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

if (searchInput) {
    // Step A: Save the original HTML of titles, questions, and answers on page load
    // This allows us to safely restore the text after injecting <mark> tags for highlighting
    document.querySelectorAll('.category-header h5, .accordion-button, .accordion-body').forEach(el => {
        el.originalHTML = el.innerHTML;
    });

    searchInput.addEventListener('input', function () {
        const searchTerm = this.value.trim();
        const lowerTerm = searchTerm.toLowerCase();
        const categoryPanes = document.querySelectorAll('.faq-category-pane');
        let totalMatches = 0;

        if (searchTerm.length > 0) {
            // This Regex safely finds text outside of HTML tags to prevent breaking your layout
            const regex = new RegExp(`(?![^<]*>)(${escapeRegExp(searchTerm)})`, "gi");

            // Activate Search Mode UI
            if (layoutContainer) {
                layoutContainer.classList.add('search-mode', 'justify-content-center');
            }
            const sidebar = document.getElementById('faqSidebar');
            if (sidebar) sidebar.style.display = 'none';

            categoryPanes.forEach(pane => {
                let paneHasMatches = false;

                // 1. Check Category Name
                const headerH5 = pane.querySelector('.category-header h5');
                const categoryName = headerH5 ? headerH5.originalHTML.replace(/<[^>]*>?/gm, '').toLowerCase() : '';
                const categoryMatches = categoryName.includes(lowerTerm);

                if (headerH5) {
                    headerH5.innerHTML = categoryMatches ? headerH5.originalHTML.replace(regex, "<mark class='bg-warning text-dark'>$1</mark>") : headerH5.originalHTML;
                }

                // 2. Check FAQ Items inside the category
                const faqItems = pane.querySelectorAll('.faq-item');
                faqItems.forEach(item => {
                    const qBtn = item.querySelector('.accordion-button');
                    const aBody = item.querySelector('.accordion-body');

                    // Extract pure text for safe searching
                    const qText = qBtn ? qBtn.originalHTML.replace(/<[^>]*>?/gm, '').toLowerCase() : '';
                    const aText = aBody ? aBody.originalHTML.replace(/<[^>]*>?/gm, '').toLowerCase() : '';

                    const itemMatches = qText.includes(lowerTerm) || aText.includes(lowerTerm);

                    // If the category matches, show all its items. Otherwise, only show matching items.
                    if (categoryMatches || itemMatches) {
                        item.style.display = '';
                        paneHasMatches = true;
                        totalMatches++;

                        // Apply Highlighting to Question and Answer
                        if (qBtn) qBtn.innerHTML = itemMatches ? qBtn.originalHTML.replace(regex, "<mark class='bg-warning text-dark px-1 rounded'>$1</mark>") : qBtn.originalHTML;
                        if (aBody) aBody.innerHTML = itemMatches ? aBody.originalHTML.replace(regex, "<mark class='bg-warning text-dark px-1 rounded'>$1</mark>") : aBody.originalHTML;

                        // UX Boost: Auto-expand the accordion so the user can see the highlighted answer!
                        const collapse = item.querySelector('.accordion-collapse');
                        if (collapse && qBtn && itemMatches) {
                            collapse.classList.add('show');
                            qBtn.classList.remove('collapsed');
                        }

                    } else {
                        item.style.display = 'none';
                    }
                });

                // Toggle Category Pane visibility
                pane.style.display = paneHasMatches ? 'block' : 'none';
                const header = pane.querySelector('.category-header');
                if (header) header.style.display = paneHasMatches ? '' : 'none';
            });

            if (noResultsMsg) noResultsMsg.style.display = totalMatches === 0 ? 'block' : 'none';

        } else {
            // Step B: Reset everything when the search box is cleared
            if (layoutContainer) {
                layoutContainer.classList.remove('search-mode', 'justify-content-center');
            }
            const sidebar = document.getElementById('faqSidebar');
            if (sidebar) sidebar.style.display = 'block';
            if (noResultsMsg) noResultsMsg.style.display = 'none';

            categoryPanes.forEach(pane => {
                pane.style.display = '';
                const header = pane.querySelector('.category-header');
                if (header) header.style.display = '';

                const headerH5 = pane.querySelector('.category-header h5');
                if (headerH5) headerH5.innerHTML = headerH5.originalHTML; // Remove highlights

                const faqItems = pane.querySelectorAll('.faq-item');
                faqItems.forEach(item => {
                    item.style.display = '';

                    const qBtn = item.querySelector('.accordion-button');
                    const aBody = item.querySelector('.accordion-body');

                    if (qBtn) qBtn.innerHTML = qBtn.originalHTML; // Remove highlights
                    if (aBody) aBody.innerHTML = aBody.originalHTML; // Remove highlights

                    // Collapse all accordions back to default state
                    const collapse = item.querySelector('.accordion-collapse');
                    if (collapse && qBtn) {
                        collapse.classList.remove('show');
                        qBtn.classList.add('collapsed');
                    }
                });
            });
        }
    });
}


// ==========================================
// PUBLIC FUNCTIONS (Called by inline onclick)
// ==========================================

function openFaqModal(id = '') {
    const url = id ? `/Faq/GetFaqForm/${id}` : `/Faq/GetFaqForm`;
    const titleEl = document.getElementById('faqModalTitle');
    if (titleEl) titleEl.innerText = id ? "Edit FAQ" : "Add New FAQ";

    fetch(url)
        .then(response => response.text())
        .then(html => {
            document.getElementById('faqModalBody').innerHTML = html;
            getFaqModal().show();
        })
        .catch(error => Swal.fire('Error', 'Unable to load form data.', 'error'));
}

function deleteFaq(id) {
    Swal.fire({
        title: 'Are you sure?',
        text: "You won't be able to revert this!",
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Yes, delete it!',
        cancelButtonText: 'Cancel',
        customClass: {
            confirmButton: 'btn btn-danger me-2',
            cancelButton: 'btn btn-label-secondary'
        },
        buttonsStyling: false
    }).then(function (result) {
        if (result.isConfirmed) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('id', id);
            formData.append('__RequestVerificationToken', token);

            fetch('/Faq/DeleteFaq', { method: 'POST', body: formData })
                .then(response => response.json())
                .then(data => {
                    if (data.success) {
                        Swal.fire({
                            title: 'Deleted!',
                            text: data.message,
                            icon: 'success',
                            customClass: { confirmButton: 'btn btn-primary' },
                            buttonsStyling: false
                        }).then(() => location.reload());
                    } else {
                        Swal.fire('Error', data.message, 'error');
                    }
                });
        }
    });
}


function openContactModal() {
    fetch('/Faq/GetContactSettingsForm')
        .then(response => response.text())
        .then(html => {
            document.getElementById('contactModalBody').innerHTML = html;
            getContactModal().show();
        })
        .catch(error => Swal.fire('Error', 'Unable to load contact data.', 'error'));
}

// ==========================================
// CATEGORY CRUD MODAL LOGIC
// ==========================================


let categoryModalInstance = null;

function getCategoryModal() {
    if (!categoryModalInstance) {
        const el = document.getElementById('categoryModal');
        if (el) categoryModalInstance = new bootstrap.Modal(el);
    }
    return categoryModalInstance;
}

// 1. OPEN IN ADD MODE
function openAddCategoryModal() {
    document.getElementById('categoryModalTitle').innerText = 'Add New Category';
    document.getElementById('categoryForm').action = '/Faq/AddNewCategory';
    document.getElementById('categoryId').value = '';
    document.getElementById('newCategoryName').value = '';

    // Reset Select2 to the placeholder
    $('#newCategoryIcon').val('').trigger('change');

    getCategoryModal().show();
    initSelect2ForCategory();
}

// 2. OPEN IN EDIT MODE
function openEditCategoryModal(id) {
    document.getElementById('categoryModalTitle').innerText = 'Edit Category';
    document.getElementById('categoryForm').action = '/Faq/EditCategory';
    document.getElementById('categoryId').value = id;

    // Fetch existing category data
    fetch(`/Faq/GetCategory/${id}`)
        .then(res => res.json())
        .then(data => {
            document.getElementById('newCategoryName').value = data.name;
            // Update Select2 with the saved icon
            $('#newCategoryIcon').val(data.icon).trigger('change');

            getCategoryModal().show();
            initSelect2ForCategory();
        })
        .catch(() => Swal.fire('Error', 'Could not load category data.', 'error'));
}

// Helper to initialize Select2
function initSelect2ForCategory() {
    setTimeout(() => {
        if (typeof $ !== 'undefined' && $.fn.select2) {
            $('#newCategoryIcon').select2({
                dropdownParent: $('#categoryModal'),
                templateResult: renderSelect2Icons,
                templateSelection: renderSelect2Icons,
                escapeMarkup: function (es) { return es; }
            });
        }
    }, 50);
}

// 3. DELETE CATEGORY
function deleteCategory(id) {
    Swal.fire({
        title: 'Delete Category?',
        text: "Are you sure? You cannot undo this action.",
        icon: 'warning',
        showCancelButton: true,
        confirmButtonText: 'Yes, delete it!',
        cancelButtonText: 'Cancel',
        customClass: {
            confirmButton: 'btn btn-danger me-2',
            cancelButton: 'btn btn-label-secondary'
        },
        buttonsStyling: false
    }).then((result) => {
        if (result.isConfirmed) {
            const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
            const formData = new FormData();
            formData.append('id', id);
            formData.append('__RequestVerificationToken', token);

            fetch('/Faq/DeleteCategory', {
                method: 'POST',
                body: formData
            })
                .then(response => response.json())
                .then(data => {
                    if (data.success) {
                        Swal.fire({
                            title: 'Deleted!',
                            text: data.message,
                            icon: 'success',
                            customClass: { confirmButton: 'btn btn-primary' },
                            buttonsStyling: false
                        }).then(() => location.reload());
                    } else {
                        // This handles our safety check message if FAQs still exist!
                        Swal.fire('Cannot Delete', data.message, 'warning');
                    }
                });
        }
    });
}

// 4. INTERCEPT FORM SUBMISSION (Handles both Add and Edit)
document.addEventListener("DOMContentLoaded", function () {
    const categoryForm = document.getElementById('categoryForm');

    if (categoryForm) {
        categoryForm.addEventListener('submit', function (e) {
            e.preventDefault();
            const formData = new FormData(this);

            fetch(this.action, {
                method: 'POST',
                body: formData
            })
                .then(response => response.json())
                .then(data => {
                    if (data.success) {
                        getCategoryModal().hide();

                        Swal.fire({
                            title: 'Success!',
                            text: data.message,
                            icon: 'success',
                            customClass: { confirmButton: 'btn btn-primary' },
                            buttonsStyling: false
                        }).then(() => location.reload()); // Reload to show updated category headers
                    } else {
                        Swal.fire('Error', data.message, 'error');
                    }
                });
        });
    }
});

// Function to render the HTML icons inside the Select2 dropdown
function renderSelect2Icons(option) {
    // If it's the default placeholder or lacks an icon, just return the text
    if (!option.id || !$(option.element).data("icon")) {
        return option.text;
    }

    // Wrapped the entire output in a <span> so jQuery doesn't delete the text node!
    var $icon = $("<span><i class='" + $(option.element).data("icon") + " me-2'></i>" + option.text + "</span>");

    return $icon;
}


document.addEventListener("DOMContentLoaded", function () {
    // Intercept the Add Category form submission
    const addCategoryForm = document.getElementById('addCategoryForm');

    if (addCategoryForm) {
        addCategoryForm.addEventListener('submit', function (e) {
            e.preventDefault();

            const formData = new FormData(this);

            fetch(this.action, {
                method: 'POST',
                body: formData
            })
                .then(response => response.json())
                .then(data => {
                    if (data.success) {
                        // Hide the Add Category Modal
                        getAddCategoryModal().hide();

                        // Inject the new category into the open FAQ Form dropdown
                        const select = document.getElementById('CategoryId');
                        if (select) {
                            const newOption = new Option(data.name, data.id, true, true);
                            select.add(newOption);
                        }

                        // Success toast
                        Swal.fire({
                            toast: true,
                            position: 'top-end',
                            icon: 'success',
                            title: 'Category Added!',
                            showConfirmButton: false,
                            timer: 2000
                        });
                    } else {
                        Swal.fire('Error', data.message, 'error');
                    }
                })
                .catch(() => {
                    Swal.fire('Error', 'Something went wrong while saving the category.', 'error');
                });
        });
    }
});