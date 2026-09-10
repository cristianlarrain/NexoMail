import { csrfFetch } from './csrfFetch'

export type TrashRuleResult = {
  filterId: string
  query: string
  created: boolean
  accountId: string
  account: string
  action: 'trash'
}

export type TrashRule = {
  filterId: string
  query: string
  accountId: string
  account: string
  action: 'trash'
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible administrar las reglas.')
  }
  return response.json() as Promise<T>
}

export const ruleApi = {
  listTrash: (accountId: string) => api<TrashRule[]>(`/mail/rules/trash?accountId=${encodeURIComponent(accountId)}`),
  createTrash: (accountId: string, query: string) => api<TrashRuleResult>('/mail/rules/trash', {
    method: 'POST',
    body: JSON.stringify({ accountId, query }),
  }),
  removeTrash: (accountId: string, filterId: string) => api<{ removed: boolean; filterId: string }>(`/mail/rules/trash/${encodeURIComponent(accountId)}/${encodeURIComponent(filterId)}`, {
    method: 'DELETE',
  }),
}
