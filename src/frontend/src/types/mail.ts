export type MailProviderType = 'Demo' | 'MicrosoftGraph' | 'Gmail' | 'Imap'
export type AiTone = 'profesional' | 'formal' | 'informal' | 'breve' | 'explicito'

export interface MailAccount { id: string; provider: MailProviderType; emailAddress: string; displayName: string; color: string; isActive: boolean }
export interface MailAttachment { id: string; name: string; contentType: string; size: number }
export interface OutgoingAttachment { name: string; contentType: string; base64Content: string }
export interface MailAddress { name: string; address: string }
export interface ContactSuggestion { name: string; emailAddress: string }
export interface MailThreadMessage { providerMessageId: string; from: MailAddress; htmlBody: string; receivedAt: string; isCurrent: boolean }
export interface MailSummary { providerMessageId: string; accountId: string; senderName: string; senderAddress: string; subject: string; preview: string; receivedAt: string; isRead: boolean; hasAttachments: boolean; folderId: string }
export interface MailMessage extends MailSummary { from: MailAddress; to: MailAddress[]; cc: MailAddress[]; htmlBody: string; attachments: MailAttachment[]; thread?: MailThreadMessage[] }
export interface PagedResult<T> { items: T[]; nextCursor?: string }
export interface ComposeMessage { fromAccountId: string; to: string[]; cc: string[]; bcc: string[]; subject: string; htmlBody: string; attachments: OutgoingAttachment[] }
export interface AiWritingSuggestion { text: string }

export interface ControlCenterDay { date: string; received: number; sent: number }
export interface ControlCenterPendingItem {
  accountId: string
  accountName: string
  accountColor: string
  messageId: string
  conversationId: string
  direction: 'received' | 'sent'
  counterpart: string
  subject: string
  since: string
  isRead: boolean
}
export interface ControlCenterAccountSummary {
  accountId: string
  accountName: string
  accountColor: string
  receivedWithoutReply: number
  sentWithoutResponse: number
  unread: number
  isAvailable: boolean
}
export interface ControlCenterSnapshot {
  receivedWithoutReply: number
  sentWithoutResponse: number
  unread: number
  overdue: number
  activity: ControlCenterDay[]
  priorityItems: ControlCenterPendingItem[]
  pendingItems: ControlCenterPendingItem[]
  accounts: ControlCenterAccountSummary[]
  unavailableAccounts: number
  generatedAt: string
}
export interface ControlCenterActivitySnapshot {
  days: 7 | 14 | 30
  offsetDays: number
  startDate: string
  endDate: string
  activity: ControlCenterDay[]
  unavailableAccounts: number
  generatedAt: string
}
