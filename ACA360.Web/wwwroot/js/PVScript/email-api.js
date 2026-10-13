// Calls the /api/email endpoints. The server works on the signed-in user's own mailbox, so no
// user id is sent; changes carry the antiforgery token that _Layout renders on every page.

const emailToken = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
const emailSend = (url, method, data) => fetch(url, {
    method,
    headers: data === undefined
        ? { 'RequestVerificationToken': emailToken() }
        : { 'Content-Type': 'application/json', 'RequestVerificationToken': emailToken() },
    body: data === undefined ? undefined : JSON.stringify(data)
});

const EmailAPI = {
    getInbox: async () => (await fetch('/api/email/inbox')).json(),
    getTrash: async () => (await fetch('/api/email/trash')).json(),
    getLabels: async () => (await fetch('/api/email/labels')).json(),

    compose: async (data) => emailSend('/api/email/compose', 'POST', data),

    moveToTrash: async (id) => emailSend(`/api/email/${encodeURIComponent(id)}/trash`, 'PUT'),

    toggleStar: async (id, isStarred) => emailSend(`/api/email/${encodeURIComponent(id)}/star?isStarred=${isStarred}`, 'PUT'),
    toggleRead: async (id, isRead) => emailSend(`/api/email/${encodeURIComponent(id)}/read?isRead=${isRead}`, 'PUT'),
    assignLabel: async (emailId, labelId) => emailSend(`/api/email/${encodeURIComponent(emailId)}/label?labelId=${encodeURIComponent(labelId ?? '')}`, 'PUT'),

    addLabel: async (data) => emailSend('/api/email/labels', 'POST', data),

    updateLabel: async (id, data) => emailSend(`/api/email/labels/${encodeURIComponent(id)}`, 'PUT', data),

    deleteLabel: async (id) => emailSend(`/api/email/labels/${encodeURIComponent(id)}`, 'DELETE')
};
