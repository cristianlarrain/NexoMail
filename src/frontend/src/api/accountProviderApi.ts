import { csrfFetch } from './csrfFetch'
import type { MailAccount } from '../types/mail'

export type ImapConnectionRequest = {
  emailAddress: string
  displayName: string
  username: string
  password: string
  imapHost: string
  imapPort: number
  imapSecurity: 'ssl' | 'starttls'
  smtpHost: string
  smtpPort: number
  smtpSecurity: 'ssl' | 'starttls'
}

export const accountProviderApi = {
  connectImap: async (request: ImapConnectionRequest) => {
    const response = await csrfFetch('/api/mail/accounts/imap/connect', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    })
    if (!response.ok) {
      const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
      throw new Error(problem?.error ?? problem?.detail ?? 'No fue posible conectar la cuenta IMAP/SMTP.')
    }
    return response.json() as Promise<MailAccount>
  },
}
