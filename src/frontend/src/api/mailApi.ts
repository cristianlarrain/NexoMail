import type { ComposeMessage, ContactSuggestion, MailAccount, MailAttachment, MailMessage, MailSummary, PagedResult } from '../types/mail'

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`/api${path}`, { headers: { 'Content-Type': 'application/json', ...init?.headers }, ...init })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible completar la operación.')
  }
  return response.status === 204 || response.status === 202 || response.headers.get('content-length') === '0' ? undefined as T : response.json() as Promise<T>
}

export const mailApi = {
  accounts: () => api<MailAccount[]>('/mail/accounts'),
  contacts: (accountId: string, search: string) => api<ContactSuggestion[]>(`/mail/contacts?accountId=${encodeURIComponent(accountId)}&search=${encodeURIComponent(search)}`),
  updateAccount: (accountId: string, settings: { displayName: string; color: string }) => api<MailAccount>(`/mail/accounts/${accountId}`, { method: 'PATCH', body: JSON.stringify(settings) }),
  messages: (accountId?: string, folder = 'inbox', search = '') => api<PagedResult<MailSummary>>(`/mail/messages?folder=${folder}${accountId ? `&accountId=${accountId}` : ''}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`),
  message: (accountId: string, messageId: string) => api<MailMessage>(`/mail/messages/${accountId}/${messageId}`),
  attachmentUrl: (accountId: string, messageId: string, attachment: MailAttachment, download = false) => `/api/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}?fileName=${encodeURIComponent(attachment.name)}${download ? '&download=true' : ''}`,
  read: (accountId: string, messageId: string, read: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/read`, { method: 'PATCH', body: JSON.stringify({ read }) }),
  trash: (accountId: string, messageId: string) => api<void>(`/mail/messages/${accountId}/${messageId}/trash`, { method: 'POST' }),
  emptyFolder: (folderId: string, accountId?: string) => api<void>(`/mail/folders/${folderId}/empty${accountId ? `?accountId=${accountId}` : ''}`, { method: 'POST' }),
  send: (message: ComposeMessage) => api<void>('/mail/send', { method: 'POST', body: JSON.stringify(message) }),
  reply: (accountId: string, messageId: string, message: ComposeMessage, replyAll: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/reply`, { method: 'POST', body: JSON.stringify({ message, replyAll }) }),
  forward: (accountId: string, messageId: string, message: ComposeMessage) => api<void>(`/mail/messages/${accountId}/${messageId}/forward`, { method: 'POST', body: JSON.stringify(message) }),
}
