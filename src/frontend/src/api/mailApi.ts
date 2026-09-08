import { csrfFetch } from './csrfFetch'
import type { AiTone, AiWritingSuggestion, ComposeMessage, ContactAnalyticsSnapshot, ContactSuggestion, ControlCenterActivitySnapshot, ControlCenterPendingItem, ControlCenterSnapshot, DocumentIndexSnapshot, MailAccount, MailAttachment, MailMessage, MailMetadataSyncResult, MailSummary, MailThreadMessage, OutgoingAttachment, PagedResult } from '../types/mail'

const messageAttachmentCache = new Map<string, MailAttachment[]>()

function attachmentCacheKey(accountId: string, messageId: string) {
  return `${accountId}:${messageId}`
}

function attachmentPath(accountId: string, messageId: string, attachment: MailAttachment, download = false) {
  return `/api/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}?fileName=${encodeURIComponent(attachment.name)}${download ? '&download=true' : ''}`
}

function blobToBase64(blob: Blob) {
  return new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(reader.error ?? new Error('No fue posible leer el adjunto original.'))
    reader.onload = () => {
      const value = String(reader.result ?? '')
      const separator = value.indexOf(',')
      resolve(separator >= 0 ? value.slice(separator + 1) : value)
    }
    reader.readAsDataURL(blob)
  })
}

async function fetchSourceAttachment(accountId: string, messageId: string, attachment: MailAttachment): Promise<OutgoingAttachment> {
  const response = await csrfFetch(attachmentPath(accountId, messageId, attachment, true))
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? `No fue posible recuperar el adjunto ${attachment.name}.`)
  }
  const blob = await response.blob()
  return {
    name: attachment.name,
    contentType: attachment.contentType || blob.type || 'application/octet-stream',
    base64Content: await blobToBase64(blob),
  }
}

async function mergeSourceAttachments(accountId: string, messageId: string, message: ComposeMessage, sourceAttachments: MailAttachment[]) {
  const retained = await Promise.all(sourceAttachments.map(file => fetchSourceAttachment(accountId, messageId, file)))
  return { ...message, attachments: [...retained, ...(message.attachments ?? [])] }
}

function escapeHtml(value: string) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function forwardedMessageHtml(message: MailMessage) {
  const sender = message.from.name?.trim()
    ? `${escapeHtml(message.from.name)} &lt;${escapeHtml(message.from.address)}&gt;`
    : escapeHtml(message.from.address)
  const recipients = message.to.map(item => escapeHtml(item.address)).join(', ')
  const copies = message.cc.map(item => escapeHtml(item.address)).join(', ')
  const sentAt = new Date(message.receivedAt).toLocaleString('es-CL')

  return `<div style="margin-top:24px;padding-top:16px;border-top:1px solid #d9dfe1"><p><strong>---------- Mensaje reenviado ----------</strong></p><p><strong>De:</strong> ${sender}<br><strong>Fecha:</strong> ${escapeHtml(sentAt)}<br><strong>Asunto:</strong> ${escapeHtml(message.subject)}${recipients ? `<br><strong>Para:</strong> ${recipients}` : ''}${copies ? `<br><strong>CC:</strong> ${copies}` : ''}</p>${message.htmlBody}</div>`
}

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
  controlCenterContacts: (days: 30 | 90) => api<ContactAnalyticsSnapshot>(`/mail/control-center/contacts?days=${days}`),
  controlCenterDocuments: (search = '', type = 'all', take = 50, skip = 0) => api<DocumentIndexSnapshot>(`/mail/control-center/documents?take=${take}&skip=${skip}&type=${encodeURIComponent(type)}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`),
  syncMetadataIndex: (days = 90, limitPerAccount = 120) => api<MailMetadataSyncResult>(`/mail/control-center/index/sync?days=${days}&limitPerAccount=${limitPerAccount}`, { method: 'POST' }),
  controlCenterTrackedItems: (accountId?: string) => api<ControlCenterPendingItem[]>(`/mail/control-center/tracking${accountId ? `?accountId=${encodeURIComponent(accountId)}` : ''}`),
  controlCenterTrackingState: (accountId: string, messageId: string) => api<{ isTracked: boolean }>(`/mail/control-center/tracking/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}`),
  trackMessage: (accountId: string, messageId: string) => api<void>(`/mail/control-center/tracking/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}`, { method: 'POST' }),
  untrackMessage: (accountId: string, messageId: string) => api<void>(`/mail/control-center/tracking/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}`, { method: 'DELETE' }),
  updateControlCenterState: (accountId: string, conversationId: string, payload: { messageId: string; action: 'resolved' | 'snoozed'; snoozeHours?: number }) => api<void>(`/mail/control-center/${encodeURIComponent(accountId)}/${encodeURIComponent(conversationId)}/state`, { method: 'PATCH', body: JSON.stringify(payload) }),
  contacts: (accountId: string, search: string) => api<ContactSuggestion[]>(`/mail/contacts?accountId=${encodeURIComponent(accountId)}&search=${encodeURIComponent(search)}`),
  updateAccount: (accountId: string, settings: { displayName: string; color: string }) => api<MailAccount>(`/mail/accounts/${accountId}`, { method: 'PATCH', body: JSON.stringify(settings) }),
  removeAccount: (accountId: string) => api<void>(`/mail/accounts/${accountId}`, { method: 'DELETE' }),
  messages: (accountId?: string, folder = 'inbox', search = '', cursor?: string) => {
    const take = cursor ? 25 : accountId ? 20 : 12
    return api<PagedResult<MailSummary>>(`/mail/messages?folder=${encodeURIComponent(folder)}&take=${take}${accountId ? `&accountId=${encodeURIComponent(accountId)}` : ''}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`)
  },
  message: async (accountId: string, messageId: string) => {
    const value = await api<MailMessage>(`/mail/messages/${accountId}/${messageId}`)
    messageAttachmentCache.set(attachmentCacheKey(accountId, messageId), [...value.attachments])
    return value
  },
  thread: (accountId: string, messageId: string) => api<MailThreadMessage[]>(`/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/thread`),
  aiReply: (accountId: string, messageId: string, tone: AiTone, instruction = '') => api<AiWritingSuggestion>(`/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/ai-reply`, { method: 'POST', body: JSON.stringify({ tone, instruction }) }),
  aiDraft: (context: string, tone: AiTone, recipient = '') => api<AiWritingSuggestion>('/mail/ai/draft', { method: 'POST', body: JSON.stringify({ context, tone, recipient }) }),
  saveDraft: (message: ComposeMessage, replyToMessageId?: string) => api<void>('/mail/drafts', { method: 'POST', body: JSON.stringify({ message, replyToMessageId: replyToMessageId || null }) }),
  updateDraft: async (accountId: string, draftMessageId: string, message: ComposeMessage, sourceAttachments: MailAttachment[] = []) => {
    const payload = await mergeSourceAttachments(accountId, draftMessageId, message, sourceAttachments)
    return api<void>(`/mail/drafts/${encodeURIComponent(accountId)}/${encodeURIComponent(draftMessageId)}`, { method: 'PUT', body: JSON.stringify(payload) })
  },
  sendDraft: async (accountId: string, draftMessageId: string, message: ComposeMessage, sourceAttachments: MailAttachment[] = []) => {
    const payload = await mergeSourceAttachments(accountId, draftMessageId, message, sourceAttachments)
    return api<void>(`/mail/drafts/${encodeURIComponent(accountId)}/${encodeURIComponent(draftMessageId)}/send`, { method: 'POST', body: JSON.stringify(payload) })
  },
  attachmentUrl: (accountId: string, messageId: string, attachment: MailAttachment, download = false) => {
    const base = attachmentPath(accountId, messageId, attachment, download)
    const isPdf = attachment.contentType === 'application/pdf' || /\.pdf$/i.test(attachment.name)
    return !download && isPdf ? `${base}#page=1&view=Fit&zoom=page-fit&navpanes=0&pagemode=none` : base
  },
  read: (accountId: string, messageId: string, read: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/read`, { method: 'PATCH', body: JSON.stringify({ read }) }),
  trash: (accountId: string, messageId: string) => api<void>(`/mail/messages/${accountId}/${messageId}/trash`, { method: 'POST' }),
  move: (accountId: string, messageId: string, folderId: 'inbox' | 'archive' | 'spam' | 'trash') => api<void>(`/mail/messages/${accountId}/${messageId}/move`, { method: 'POST', body: JSON.stringify({ folderId }) }),
  ignoreSender: (accountId: string, senderAddress: string) => api<void>(`/mail/ignored-senders/${encodeURIComponent(accountId)}`, { method: 'POST', body: JSON.stringify({ senderAddress }) }),
  unignoreSender: (accountId: string, senderAddress: string) => api<void>(`/mail/ignored-senders/${encodeURIComponent(accountId)}?sender=${encodeURIComponent(senderAddress)}`, { method: 'DELETE' }),
  emptyFolder: (folderId: string, accountId?: string) => api<void>(`/mail/folders/${folderId}/empty${accountId ? `?accountId=${accountId}` : ''}`, { method: 'POST' }),
  send: (message: ComposeMessage) => api<void>('/mail/send', { method: 'POST', body: JSON.stringify(message) }),
  reply: (accountId: string, messageId: string, message: ComposeMessage, replyAll: boolean) => api<void>(`/mail/messages/${accountId}/${messageId}/reply`, { method: 'POST', body: JSON.stringify({ message, replyAll }) }),
  forward: async (accountId: string, messageId: string, message: ComposeMessage) => {
    const original = await api<MailMessage>(`/mail/messages/${accountId}/${messageId}`)
    const cacheKey = attachmentCacheKey(accountId, messageId)
    const sourceAttachments = [...original.attachments]
    messageAttachmentCache.set(cacheKey, sourceAttachments)
    const originalAttachments = await Promise.all(sourceAttachments.map(file => fetchSourceAttachment(accountId, messageId, file)))
    const attachments = [...originalAttachments, ...(message.attachments ?? [])]
    const htmlBody = `${message.htmlBody || '<p></p>'}${forwardedMessageHtml(original)}`
    return api<void>(`/mail/messages/${accountId}/${messageId}/forward`, { method: 'POST', body: JSON.stringify({ ...message, htmlBody, attachments }) })
  },
}
