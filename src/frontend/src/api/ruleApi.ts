import { csrfFetch } from './csrfFetch'

export type TrashRuleResult = {
  filterId: string
  query: string
  created: boolean
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
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible crear la regla.')
  }
  return response.json() as Promise<T>
}

export const ruleApi = {
  createTrash: (accountId: string, query: string) => api<TrashRuleResult>('/mail/rules/trash', {
    method: 'POST',
    body: JSON.stringify({ accountId, query }),
  }),
}
