let currentView = 'inbox';
let emailsCache = [];
let fullInboxCache = [];
let allLabelsCache = [];
let currentOpenEmailId = null;

// Advanced Filter State
let filterState = {
    status: 'all',    // all | read | unread | starred
    dateRange: 'all'  // all | today | week | month
};

document.addEventListener('DOMContentLoaded', () => {
    loadLabels();
    loadEmails('inbox');

    document.getElementById('searchInput').addEventListener('keyup', () => {
        applyAdvancedFilters();
    });
});

async function refreshEmails() {
    if (currentView.startsWith('label-')) {
        const labelId = currentView.split('-')[1];
        await fetchBaseEmails();
        filterByLabel(labelId);
    } else {
        await loadEmails(currentView);
    }
}

function updateUnreadCount() {
    const unreadCount = fullInboxCache.filter(e => !e.isRead).length;
    const countEl = document.getElementById('inbox-count');
    if (countEl) {
        countEl.innerText = unreadCount;
        countEl.style.display = unreadCount > 0 ? 'inline-block' : 'none';
    }
}

async function fetchBaseEmails() {
    fullInboxCache = await EmailAPI.getInbox();
    updateUnreadCount();
}

async function loadEmails(view) {
    currentView = view;
    const selectAll = document.getElementById('email-select-all');
    if (selectAll) selectAll.checked = false;
    updateBulkActionsState();

    const inboxMenu = document.getElementById('menu-inbox');
    const trashMenu = document.getElementById('menu-trash');
    if (inboxMenu) inboxMenu.classList.toggle('active', view === 'inbox');
    if (trashMenu) trashMenu.classList.toggle('active', view === 'trash');

    const bulkTrashBtn = document.getElementById('bulk-trash-btn');
    if (bulkTrashBtn) {
        bulkTrashBtn.style.display = (view === 'trash') ? 'none' : 'inline-block';
    }

    if (view === 'inbox') {
        await fetchBaseEmails();
        emailsCache = [...fullInboxCache];
    } else {
        emailsCache = await EmailAPI.getTrash();
    }

    applyAdvancedFilters();
}

// --- ADVANCED FILTERING ENGINE ---

function applyAdvancedFilters() {
    const searchTerm = (document.getElementById('searchInput').value || '').toLowerCase();
    const statusVal = document.querySelector('input[name="filterStatus"]:checked')?.value || 'all';
    const dateRangeVal = document.getElementById('filterDateRange').value || 'all';

    filterState.status = statusVal;
    filterState.dateRange = dateRangeVal;

    // Check if any non-default filter is active to show the indicator dot
    const isFilterActive = statusVal !== 'all' || dateRangeVal !== 'all';
    const activeDot = document.getElementById('filter-active-badge');
    if (activeDot) {
        activeDot.classList.toggle('d-none', !isFilterActive);
    }

    const filtered = emailsCache.filter(email => {
        // 1. Text Search Filter
        const matchesSearch = (email.subject || '').toLowerCase().includes(searchTerm) ||
            (email.senderName || '').toLowerCase().includes(searchTerm);

        // 2. Read / Unread / Starred Filter
        let matchesStatus = true;
        if (statusVal === 'read') matchesStatus = email.isRead === true;
        else if (statusVal === 'unread') matchesStatus = email.isRead === false;
        else if (statusVal === 'starred') matchesStatus = email.isStarred === true;

        // 3. Date Range Filter
        let matchesDate = isWithinDateRange(email.receivedDate, dateRangeVal);

        return matchesSearch && matchesStatus && matchesDate;
    });

    const resultsCountEl = document.getElementById('filterResultsCount');
    if (resultsCountEl) {
        resultsCountEl.innerText = `${filtered.length} found`;
    }

    renderEmailList(filtered);
}

function isWithinDateRange(dateString, range) {
    if (range === 'all') return true;
    if (!dateString) return false; // If date is empty/null, exclude when date filter is explicitly requested

    const itemDate = new Date(dateString);
    const now = new Date();

    // Normalize to date-only boundary comparison
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const emailDay = new Date(itemDate.getFullYear(), itemDate.getMonth(), itemDate.getDate());

    if (range === 'today') {
        return emailDay.getTime() === today.getTime();
    } else if (range === 'week') {
        const sevenDaysAgo = new Date(today);
        sevenDaysAgo.setDate(today.getDate() - 7);
        return emailDay >= sevenDaysAgo;
    } else if (range === 'month') {
        const thirtyDaysAgo = new Date(today);
        thirtyDaysAgo.setDate(today.getDate() - 30);
        return emailDay >= thirtyDaysAgo;
    }
    return true;
}

function resetAdvancedFilters() {
    document.getElementById('statusAll').checked = true;
    document.getElementById('filterDateRange').value = 'all';
    document.getElementById('searchInput').value = '';
    applyAdvancedFilters();
}

// --- EMAIL DETAIL VIEW LOGIC ---

async function openEmail(event, id) {
    if (event.target && event.target.closest && event.target.closest('.email-list-item-input, .email-list-item-bookmark, .email-delete, .email-unread, .email-read')) {
        return;
    }

    const email = emailsCache.find(e => e.id === id);
    if (!email) return;

    currentOpenEmailId = id;

    document.getElementById('detail-subject').innerText = email.subject || '(No Subject)';
    document.getElementById('detail-label').innerHTML = email.labelName ? `<span class="badge bg-label-${email.colorClass} fs-6 me-2">${email.labelName}</span>` : '';
    document.getElementById('detail-avatar').innerText = getInitials(email.senderName);
    document.getElementById('detail-sender-name').innerText = email.senderName;
    document.getElementById('detail-sender-email').innerText = `<${email.senderEmail}>`;
    document.getElementById('detail-date').innerText = formatDate(email.receivedDate);
    document.getElementById('detail-body').innerText = email.body || '';

    const trashBtn = document.getElementById('detail-trash');
    const unreadBtn = document.getElementById('detail-mark-unread');

    if (trashBtn) {
        trashBtn.onclick = () => { deleteSingleEmail(id); closeEmail(); };
        trashBtn.style.display = (currentView === 'trash') ? 'none' : 'inline-block';
    }
    if (unreadBtn) {
        unreadBtn.onclick = async () => { await toggleRead(id, false); closeEmail(); };
    }

    if (!email.isRead) {
        email.isRead = true;

        const inboxEmail = fullInboxCache.find(e => e.id === id);
        if (inboxEmail) {
            inboxEmail.isRead = true;
            updateUnreadCount();
        }

        EmailAPI.toggleRead(id, true);

        const rowEl = document.getElementById(`row-${id}`);
        if (rowEl) {
            rowEl.classList.add('email-marked-read');
            const actionBtn = rowEl.querySelector('.email-unread');
            if (actionBtn) {
                actionBtn.classList.replace('email-unread', 'email-read');
                const icon = actionBtn.querySelector('.bx-envelope');
                if (icon) icon.classList.replace('bx-envelope', 'bx-envelope-open');
            }
        }
    }

    const emailView = document.getElementById('app-email-view');
    if (emailView) emailView.classList.add('show');
}

function closeEmail() {
    currentOpenEmailId = null;
    const emailView = document.getElementById('app-email-view');
    if (emailView) emailView.classList.remove('show');
}

// --- LABELS & BULK DROPDOWNS ---

async function changeSingleEmailLabel(labelId) {
    if (!currentOpenEmailId) return;
    await EmailAPI.assignLabel(currentOpenEmailId, labelId);

    const email = emailsCache.find(e => e.id === currentOpenEmailId);
    if (email) {
        if (labelId === '') {
            email.labelId = null;
            email.labelName = null;
            email.colorClass = null;
        } else {
            const label = allLabelsCache.find(l => l.id === labelId);
            if (label) {
                email.labelId = label.id;
                email.labelName = label.name;
                email.colorClass = label.colorClass;
            }
        }
        document.getElementById('detail-label').innerHTML = email.labelName ? `<span class="badge bg-label-${email.colorClass} fs-6 me-2">${email.labelName}</span>` : '';
    }
    refreshEmails();
}

async function loadLabels() {
    allLabelsCache = await EmailAPI.getLabels();
    const container = document.getElementById('label-list-container');
    const bulkDropdown = document.getElementById('bulkLabelDropdown');
    const detailDropdown = document.getElementById('detailLabelDropdown');

    if (container) {
        container.innerHTML = allLabelsCache.map(l => `
            <li class="d-flex justify-content-between align-items-center mb-1 cursor-pointer px-3 py-1 rounded border-bottom" onclick="filterByLabel('${l.id}')">
                <a href="javascript:void(0);" class="d-flex align-items-center">
                    <i class="badge badge-dot bg-${l.colorClass} me-2"></i>
                    <span class="align-middle text-body">${l.name}</span>
                </a>
                <div class="label-actions opacity-75" onclick="event.stopPropagation()">
                    <i class="icon-base bx bx-edit text-body-secondary me-1" onclick="openEditLabelModal('${l.id}', '${l.name}', '${l.colorClass}')"></i>
                    <i class="icon-base bx bx-trash text-body-secondary" onclick="deleteLabel('${l.id}')"></i>
                </div>
            </li>
        `).join('');
    }

    const dropdownOptionsHtml = allLabelsCache.map(l => `
        <li><a class="dropdown-item d-flex align-items-center" href="javascript:void(0);" onclick="bulkAssignLabel('${l.id}')">
            <i class="badge badge-dot bg-${l.colorClass} me-2"></i> ${l.name}
        </a></li>
    `).join('') + `<li><hr class="dropdown-divider"></li><li><a class="dropdown-item text-danger" href="javascript:void(0);" onclick="bulkAssignLabel('')">Remove Label</a></li>`;

    const detailDropdownHtml = allLabelsCache.map(l => `
        <li><a class="dropdown-item d-flex align-items-center" href="javascript:void(0);" onclick="changeSingleEmailLabel('${l.id}')">
            <i class="badge badge-dot bg-${l.colorClass} me-2"></i> ${l.name}
        </a></li>
    `).join('') + `<li><hr class="dropdown-divider"></li><li><a class="dropdown-item text-danger" href="javascript:void(0);" onclick="changeSingleEmailLabel('')">Remove Label</a></li>`;

    if (bulkDropdown) bulkDropdown.innerHTML = dropdownOptionsHtml;
    if (detailDropdown) detailDropdown.innerHTML = detailDropdownHtml;
}

function filterByLabel(labelId) {
    currentView = `label-${labelId}`;

    const inboxMenu = document.getElementById('menu-inbox');
    const trashMenu = document.getElementById('menu-trash');
    if (inboxMenu) inboxMenu.classList.remove('active');
    if (trashMenu) trashMenu.classList.remove('active');

    emailsCache = fullInboxCache.filter(e => e.labelId === labelId);
    applyAdvancedFilters();
}

// --- ROW LIST RENDERING ---

function getInitials(name) {
    if (!name) return 'U';
    const parts = name.trim().split(' ');
    if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
    return name.substring(0, 2).toUpperCase();
}

function renderEmailList(emails) {
    const container = document.getElementById('email-list-container');
    if (!container) return;

    if (emails.length === 0) {
        container.innerHTML = `<li class="p-5 text-center text-body-secondary">No items match the active filters.</li>`;
        return;
    }

    container.innerHTML = emails.map(email => `
        <li class="email-list-item d-flex align-items-center ${email.isRead ? 'email-marked-read' : ''}" 
            id="row-${email.id}" 
            ${email.isStarred ? 'data-starred="true"' : ''}
            onclick="openEmail(event, '${email.id}')">
            
            <div class="d-flex align-items-center w-100">
                <div class="form-check mb-0 ms-2">
                    <input class="email-list-item-input form-check-input email-checkbox" 
                           type="checkbox" 
                           value="${email.id}" 
                           id="email-check-${email.id}" 
                           onchange="toggleSingleSelection('${email.id}')">
                    <label class="form-check-label" for="email-check-${email.id}"></label>
                </div>
                
                <span class="ms-sm-3 me-3 d-sm-inline-block d-none">
                    <i class="email-list-item-bookmark icon-base bx bx-star icon-md cursor-pointer ms-1" onclick="toggleStar('${email.id}', ${!email.isStarred})"></i>
                </span>
                
                <div class="avatar avatar-sm flex-shrink-0 me-sm-3 me-2">
                    <span class="avatar-initial rounded-circle bg-label-primary text-primary fw-semibold" style="width:32px;height:32px;display:flex;align-items:center;justify-content:center;background:#e7e7ff;">${getInitials(email.senderName)}</span>
                </div>

                <div class="email-list-item-content ms-2 ms-sm-0 me-2 text-truncate" style="max-width: 55%;">
                    <span class="email-list-item-username me-2 text-heading fw-semibold">${email.senderName}</span>
                    <small class="email-list-item-subject text-body-secondary">${email.subject || '(No Subject)'}</small>
                </div>
                
                <div class="email-list-item-meta ms-auto d-flex align-items-center">
                    ${email.labelName ? `<span class="email-list-item-label badge badge-dot bg-${email.colorClass} d-none d-md-inline-block me-2" title="${email.labelName}"></span>` : ''}
                    
                    <small class="email-list-item-time text-body-secondary">${formatDate(email.receivedDate)}</small>
                    
                    <ul class="list-inline email-list-item-actions ms-2 mb-0">
                        ${currentView === 'trash' ? '' : `<li class="list-inline-item email-delete btn btn-icon p-0" onclick="deleteSingleEmail('${email.id}')" title="Trash"><i class="icon-base bx bx-trash icon-md"></i></li>`}
                        <li class="list-inline-item ${email.isRead ? 'email-read' : 'email-unread'} btn btn-icon p-0" onclick="toggleRead('${email.id}', ${!email.isRead})" title="Toggle Read Status">
                            <i class="icon-base bx ${email.isRead ? 'bx-envelope-open' : 'bx-envelope'} icon-md"></i>
                        </li>
                    </ul>
                </div>
            </div>
        </li>
    `).join('');
}

// --- DYNAMIC BULK SELECTION & ICON TOGGLING ---

function toggleSelectAll(master) {
    const isChecked = master.checked;
    document.querySelectorAll('.email-checkbox').forEach(cb => {
        cb.checked = isChecked;
    });
    updateBulkActionsState();
}

function toggleSingleSelection(id) {
    const total = document.querySelectorAll('.email-checkbox').length;
    const checked = document.querySelectorAll('.email-checkbox:checked').length;
    const selectAll = document.getElementById('email-select-all');

    if (selectAll) {
        selectAll.checked = (total > 0 && total === checked);
        selectAll.indeterminate = (checked > 0 && checked < total);
    }
    updateBulkActionsState();
}

function updateBulkActionsState() {
    const selectedIds = getSelectedIds();
    const checkedCount = selectedIds.length;
    const bar = document.getElementById('bulkActions');

    if (bar) {
        bar.style.opacity = checkedCount > 0 ? '1' : '0.3';
        bar.style.pointerEvents = checkedCount > 0 ? 'auto' : 'none';
    }

    // DYNAMIC BULK READ ICON TOGGLING
    const bulkIcon = document.getElementById('bulkToggleReadIcon');
    const bulkBtn = document.getElementById('bulkToggleReadBtn');

    if (bulkIcon && checkedCount > 0) {
        const selectedEmails = emailsCache.filter(e => selectedIds.includes(e.id));
        const hasUnread = selectedEmails.some(e => !e.isRead);

        // If any selected item is unread, clicking will Mark as Read (Show Open Envelope)
        // If all selected items are read, clicking will Mark as Unread (Show Closed Envelope)
        if (hasUnread) {
            bulkIcon.className = 'icon-base bx bx-envelope-open icon-md text-body';
            if (bulkBtn) bulkBtn.setAttribute('title', 'Mark Selected as Read');
        } else {
            bulkIcon.className = 'icon-base bx bx-envelope icon-md text-body';
            if (bulkBtn) bulkBtn.setAttribute('title', 'Mark Selected as Unread');
        }
    }
}

async function bulkToggleRead() {
    const selectedIds = getSelectedIds();
    if (selectedIds.length === 0) return;

    const selectedEmails = emailsCache.filter(e => selectedIds.includes(e.id));
    const targetReadState = selectedEmails.some(e => !e.isRead); // true = mark read, false = mark unread

    for (const id of selectedIds) {
        await EmailAPI.toggleRead(id, targetReadState);
    }
    refreshEmails();
}

// --- ACTIONS ---

async function toggleStar(id, isStarred) {
    await EmailAPI.toggleStar(id, isStarred);
    refreshEmails();
}

async function toggleRead(id, isRead) {
    await EmailAPI.toggleRead(id, isRead);
    refreshEmails();
}

async function deleteSingleEmail(id) {
    await EmailAPI.moveToTrash(id);
    refreshEmails();
}

function getSelectedIds() {
    return Array.from(document.querySelectorAll('.email-checkbox:checked')).map(cb => cb.value);
}

async function bulkDelete() {
    for (const id of getSelectedIds()) { await EmailAPI.moveToTrash(id); }
    refreshEmails();
}

async function bulkAssignLabel(labelId) {
    for (const id of getSelectedIds()) { await EmailAPI.assignLabel(id, labelId); }
    refreshEmails();
}

// --- MODALS SUBMISSION ---

async function submitEmail() {
    const data = {
        id: "",
        senderName: document.getElementById('composeSender').value,
        senderEmail: document.getElementById('composeEmail').value,
        subject: document.getElementById('composeSubject').value,
        body: document.getElementById('composeBody').value,
        receivedDate: null
    };
    await EmailAPI.compose(data);

    bootstrap.Modal.getOrCreateInstance(document.getElementById('emailComposeSidebar')).hide();

    document.getElementById('composeSender').value = '';
    document.getElementById('composeEmail').value = '';
    document.getElementById('composeSubject').value = '';
    document.getElementById('composeBody').value = '';

    refreshEmails();
}

async function submitLabel() {
    const data = {
        id: "",
        name: document.getElementById('labelName').value,
        colorClass: document.getElementById('labelColor').value
    };
    await EmailAPI.addLabel(data);
    bootstrap.Modal.getOrCreateInstance(document.getElementById('addLabelModal')).hide();
    document.getElementById('labelName').value = '';
    loadLabels();
}

function openEditLabelModal(id, name, color) {
    document.getElementById('editLabelId').value = id;
    document.getElementById('editLabelName').value = name;
    document.getElementById('editLabelColor').value = color;
    bootstrap.Modal.getOrCreateInstance(document.getElementById('editLabelModal')).show();
}

async function submitEditLabel() {
    const id = document.getElementById('editLabelId').value;
    const data = {
        id: id,
        name: document.getElementById('editLabelName').value,
        colorClass: document.getElementById('editLabelColor').value
    };
    await EmailAPI.updateLabel(id, data);
    bootstrap.Modal.getOrCreateInstance(document.getElementById('editLabelModal')).hide();

    loadLabels();
    refreshEmails();
}

async function deleteLabel(id) {
    if (confirm("Are you sure you want to delete this label?")) {
        await EmailAPI.deleteLabel(id);
        loadLabels();
        refreshEmails();
    }
}

// --- UTILITIES ---

function formatDate(dateString) {
    if (!dateString) return '';
    const date = new Date(dateString);

    // Safety check for invalid dates
    if (isNaN(date)) return '';

    const now = new Date();

    // Check if the date is exactly today
    const isToday = date.getDate() === now.getDate() &&
        date.getMonth() === now.getMonth() &&
        date.getFullYear() === now.getFullYear();

    // Check if the date is within the current year
    const isThisYear = date.getFullYear() === now.getFullYear();

    if (isToday) {
        // Today: Show only time (e.g., "6:55 AM")
        return date.toLocaleTimeString('en-US', {
            hour: 'numeric',
            minute: '2-digit',
            hour12: true
        });
    } else if (isThisYear) {
        // This Year: Show Month and Day (e.g., "Jul 23")
        return date.toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric'
        });
    } else {
        // Older: Show MM/DD/YYYY (e.g., "07/24/2025")
        return date.toLocaleDateString('en-US', {
            month: '2-digit',
            day: '2-digit',
            year: 'numeric'
        });
    }
}