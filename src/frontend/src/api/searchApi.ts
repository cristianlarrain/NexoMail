import { csrfFetch } from './csrfFetch'
import type { AiSearchInterpretation, ContactAnalyticsSnapshot, ControlCenterSnapshot, DocumentIndexSnapshot, MailSummary, PagedResult } from '../types/mail'

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api${path}`, { headers: { 'Content-Type': 'application/json', ...init?.headers }, ...init })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible completar la búsqueda.')
  }
  return response.status === 204 || response.status === 202 || response.headers.get('content-length') === '0'
    ? undefined as T
    : response.json() as Promise<T>
}

export const searchApi = {
  interpret: (query: string) => api<AiSearchInterpretation>('/mail/ai/search', {
    method: 'POST',
    body: JSON.stringify({ query }),
  }),
  messages: (accountId: string | undefined, folder: 'inbox' | 'sent' | 'archive', search: string) =>
    api<PagedResult<MailSummary>>(`/mail/messages?folder=${folder}&take=50${accountId ? `&accountId=${encodeURIComponent(accountId)}` : ''}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`),
  contacts: () => api<ContactAnalyticsSnapshot>('/mail/control-center/contacts?days=90'),
  documents: (search: string, type: string) => api<DocumentIndexSnapshot>(`/mail/control-center/documents?take=100&skip=0&type=${encodeURIComponent(type)}${search.trim() ? `&search=${encodeURIComponent(search.trim())}` : ''}`),
  controlCenter: (accountId?: string) => api<ControlCenterSnapshot>(`/mail/control-center${accountId ? `?accountId=${encodeURIComponent(accountId)}` : ''}`),
}
