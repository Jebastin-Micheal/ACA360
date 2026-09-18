// Hardcoded for demonstration. In production, pull this dynamically from a meta tag, JWT, or layout context.

const EmailAPI = {
    getInbox: async () => (await fetch(`/api/email/inbox?userId=${CURRENT_USER_ID}`)).json(),
    getTrash: async () => (await fetch(`/api/email/trash?userId=${CURRENT_USER_ID}`)).json(),
    getLabels: async () => (await fetch(`/api/email/labels?userId=${CURRENT_USER_ID}`)).json(),

    compose: async (data) => fetch(`/api/email/compose?userId=${CURRENT_USER_ID}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data)
    }),

    moveToTrash: async (id) => fetch(`/api/email/${id}/trash?userId=${CURRENT_USER_ID}`, { method: 'PUT' }),

    toggleStar: async (id, isStarred) => fetch(`/api/email/${id}/star?isStarred=${isStarred}&userId=${CURRENT_USER_ID}`, { method: 'PUT' }),
    toggleRead: async (id, isRead) => fetch(`/api/email/${id}/read?isRead=${isRead}&userId=${CURRENT_USER_ID}`, { method: 'PUT' }),
    assignLabel: async (emailId, labelId) => fetch(`/api/email/${emailId}/label?labelId=${labelId}&userId=${CURRENT_USER_ID}`, { method: 'PUT' }),

    addLabel: async (data) => fetch(`/api/email/labels?userId=${CURRENT_USER_ID}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data)
    }),

    updateLabel: async (id, data) => fetch(`/api/email/labels/${id}?userId=${CURRENT_USER_ID}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(data)
    }),

    deleteLabel: async (id) => fetch(`/api/email/labels/${id}?userId=${CURRENT_USER_ID}`, { method: 'DELETE' })
};