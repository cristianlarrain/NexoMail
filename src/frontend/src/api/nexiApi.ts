import { csrfFetch } from './csrfFetch'
import type { AiMailReport, AiMessageInsight } from '../types/mail'
import type { NexiContextResponse } from '../types/nexi'

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api${path}`, { headers: { 'Content-Type': 'application/json', ...init?.headers }, ...init })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'Nexi no pudo completar la operación.')
  }
  return response.json() as Promise<T>
}

export type NexiReportPeriod = 'today' | 'this_week' | 'last_week'
export type NexiPerspectiveExpansion = { text: string }

export const nexiApi = {
  summarizeMessage: (accountId: string, messageId: string, includeThread = false) =>
    api<AiMessageInsight>(`/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/ai-summary`, {
      method: 'POST',
      body: JSON.stringify({ includeThread }),
    }),
  report: (period: NexiReportPeriod, localDate: string, accountId?: string) =>
    api<AiMailReport>('/mail/ai/report', {
      method: 'POST',
      body: JSON.stringify({ period, localDate, accountId: accountId || null }),
    }),
  context: (query: string, instruction: string, accountId?: string) =>
    api<NexiContextResponse>('/mail/ai/context', {
      method: 'POST',
      body: JSON.stringify({ query, instruction, accountId: accountId || null }),
    }),
  expandPerspective: (text: string, source: string, area: string) =>
    api<NexiPerspectiveExpansion>('/mail/ai/perspective-expansion', {
      method: 'POST',
      body: JSON.stringify({ text, source, area }),
    }),
}