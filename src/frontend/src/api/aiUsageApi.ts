import { csrfFetch } from './csrfFetch'

export type AiUsagePeriod = 'week' | 'month'
export type AiUsageSort = 'projected' | 'accumulated'

export interface AiUsageSummary {
  period: AiUsagePeriod
  currentStart: string
  currentEnd: string
  currentCostClp: number
  previousCostClp: number
  variationPercent: number | null
  operations: number
  activeUsers: number
  averageCostPerActiveUserClp: number
}

export interface AiUsageUserRow {
  userId: string
  displayName: string
  email: string
  planCode: string
  effectivePlanCode: string
  isTrialActive: boolean
  trialType: string | null
  trialStart: string | null
  trialEndsAt: string | null
  trialDaysRemaining: number | null
  weekOperations: number
  inputTokens: number
  outputTokens: number
  weekCostClp: number
  accumulatedCostClp: number
  averageDailyCostClp: number
  projectedCostClp: number
  isInitialProjection: boolean
  sevenDayProjectedCostClp: number | null
  sevenDayTrendPercent: number | null
  costStatus: 'verde' | 'amarillo' | 'rojo' | string
}

export interface AiUsageDaily {
  date: string
  operations: number
  inputTokens: number
  outputTokens: number
  costClp: number
}

export interface AiUsageOperation {
  operationType: string
  operations: number
  costClp: number
}

export interface AiUsageUserDetail {
  user: AiUsageUserRow
  daily: AiUsageDaily[]
  operations: AiUsageOperation[]
}

export interface AiUsageSettings {
  greenMaxClp: number
  yellowMaxClp: number
  referenceClpPerUsd: number
}

export interface AiUsageSettingsPatch {
  greenMaxClp: number
  yellowMaxClp: number
  referenceClpPerUsd: number
}

export class AiUsageApiError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message)
  }
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api/ai-usage/admin${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new AiUsageApiError(problem?.detail ?? problem?.error ?? 'No fue posible obtener el consumo de Nexi.', response.status)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const aiUsageApi = {
  summary: (period: AiUsagePeriod) => api<AiUsageSummary>(`/summary?period=${encodeURIComponent(period)}`),
  users: (sort: AiUsageSort = 'projected') => api<AiUsageUserRow[]>(`/users?sort=${encodeURIComponent(sort)}`),
  user: (userId: string) => api<AiUsageUserDetail>(`/users/${encodeURIComponent(userId)}`),
  settings: () => api<AiUsageSettings>('/settings'),
  updateSettings: (payload: AiUsageSettingsPatch) => api<AiUsageSettings>('/settings', { method: 'PATCH', body: JSON.stringify(payload) }),
}
