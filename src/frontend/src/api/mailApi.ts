import { csrfFetch } from './csrfFetch'
import type { AiTone, AiWritingSuggestion, ComposeMessage, ContactSuggestion, ControlCenterActivitySnapshot, ControlCenterSnapshot, MailAccount, MailAttachment, MailMessage, MailSummary, PagedResult } from '../types/mail'

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api${path}`, { headers: { 'Content-Type': 'application/json', ...init?.headers }, ...init })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible completar la operación.')
  }
  return response.status === 204 || response.status === 202 || response.headers.get('content-length') === '0' ? undefined as T : response.json() as Promise<T>
}

export const mailApi = {
  accounts: () => api<MailAccount[]>('/mail/accounts'),
  refreshMail: () => api<void>('/mail/refresh', { method: 'POST' }),
  controlCenter: (accountId?: string) => api<ControlCenterSnapshot>(`/mail/control-center${accountId ? `?accountId=${encodeURIComponent(accountId)}` : ''}`),
  controlCenterActivity: (accountId: string | undefined, days: 7 | 14 | 30, offsetDays: number) => api<ControlCenterActivitySnapshot>(`/mail/control-center/activity?days=${days}&offsetDays=${offsetDays}${accountId ? `&accountId=${encodeURIComponent(accountId)}` : ''}`),
  updateControlCenterState: (accountId: string, conversationId: string, payload: { messageId: string; action: 'resolved' | 'snoozed'; snoozeHours?: number }) => api<void>(`/mail/control-center/${encodeURIComponent(accountId)}/${encodeURIComponent(conversationId)}/state`, { method: 'PATCH', body: JSON.stringify(payload) }),
  contacts: (accountId: string, search: string) => api<ContactSuggestion[]>(`/mail/contacts?accountId=${encodeURIComponent(accountId)}&search=${encodeURIComponent(search)}`),
  updateAccount: (accountId: string, settings: { displayName: string; color: string }) => api<MailAccount>(`/mail/accounts/${accountId}`, { method: 'PATCH', body: JSON.stringify(settings) }),
  removeAccount: (accountId: string) => api<void>(`/mail/accounts/${accountId}`, { method: 'DELETE' }),
  messages: (accountId?: string, folder = 'inbox', search = '', cursor?: string) => api<PagedResult<MailSummary>>(`/mail/messages?folder=${encodeURIComponent(folder)}&take=25${accountId ? `&accountId=${encodeURIComponent(accountId)}` : ''}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`),
  message: (accountId: string, messageId: string) => api<MailMessage>(`/mail/messages/${accountId}/${messageId}`),
  aiReply: (accountId: string, messageId: string, tone: AiTone, instruction = '') => api<AiWritingSuggestion>(`/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/ai-reply`, { method: 'POST', body: JSON.stringify({ tone, instruction }) }),
  aiDraft: (context: string, tone: AiTone, recipient = '') => api<AiWritingSuggestion>('/mail/ai/draft', { method: 'POST', body: JSON.stringify({ context, tone, recipient }) }),
  attachmentUrl: (accountId: string, messageId: string, attachment: MailAttachment, download = false) => `/api/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}?fileName=${encodeURIComponent(attachment.name)}${download ? '&download=true' : ''}`,
  read: (accountId: string, messageId: string, read: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/read`, { method: 'PATCH', body: JSON.stringify({ read }) }),
  trash: (accountId: string, messageId: string) => api<void>(`/mail/messages/${accountId}/${messageId}/trash`, { method: 'POST' }),
  move: (accountId: string, messageId: string, folderId: 'inbox' | 'trash') => api<void>(`/mail/messages/${accountId}/${messageId}/move`, { method: 'POST', body: JSON.stringify({ folderId }) }),
  emptyFolder: (folderId: string, accountId?: string) => api<void>(`/mail/folders/${folderId}/empty${accountId ? `?accountId=${accountId}` : ''}`, { method: 'POST' }),
  send: (message: ComposeMessage) => api<void>('/mail/send', { method: 'POST', body: JSON.stringify(message) }),
  reply: (accountId: string, messageId: string, message: ComposeMessage, replyAll: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/reply`, { method: 'POST', body: JSON.stringify({ message, replyAll }) }),
  forward: (accountId: string, messageId: string, message: ComposeMessage) => api<void>(`/mail/messages/${accountId}/${messageId}/forward`, { method: 'POST', body: JSON.stringify(message) }),
}
