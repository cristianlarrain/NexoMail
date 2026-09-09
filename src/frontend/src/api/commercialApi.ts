import { csrfFetch } from './csrfFetch'

export interface CommercialPlan {
  code: string
  name: string
  price: string
  cadence: string
  maxAccounts: number | null
  description: string
  features: string[]
  entitlements: string[]
  isFeatured: boolean
  isCorporate: boolean
  isWhiteLabel: boolean
  isActive: boolean
}

export interface CommercialSubscriptionState {
  status: 'active' | 'trialing' | 'legacy' | 'past_due' | 'canceled' | 'expired' | string
  provider: string | null
  providerCustomerId: string | null
  providerSubscriptionId: string | null
  currentPeriodStart: string | null
  currentPeriodEnd: string | null
  trialEndsAt: string | null
  cancelAtPeriodEnd: boolean
  canceledAt: string | null
  paymentDueAt: string | null
  updatedAt: string
}

export interface CommercialSubscription {
  currentPlan: CommercialPlan
  connectedAccounts: number
  remainingAccounts: number | null
  canAddAccount: boolean
  overLimit: boolean
  plans: CommercialPlan[]
  subscription: CommercialSubscriptionState
  entitlements: string[]
  paidAccessActive: boolean
  effectivePlanCode: string
}

export interface CommercialEntitlementDefinition {
  code: string
  name: string
  description: string
}

export interface CommercialAdminPlan extends CommercialPlan {
  sortOrder: number
  assignedUsers: number
  canDelete: boolean
}

export interface CommercialPlanWriteRequest {
  code?: string
  name: string
  price: string
  cadence: string
  maxAccounts: number | null
  description: string
  features: string[]
  isFeatured: boolean
  isCorporate: boolean
  isWhiteLabel: boolean
  isActive: boolean
  sortOrder: number
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api/commercial${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible obtener la información del plan.')
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const commercialApi = {
  subscription: () => api<CommercialSubscription>('/subscription'),
  entitlements: () => api<CommercialEntitlementDefinition[]>('/entitlements'),
  adminStatus: () => api<{ isAdministrator: boolean }>('/admin/status'),
  adminPlans: () => api<CommercialAdminPlan[]>('/admin/plans'),
  createPlan: (request: CommercialPlanWriteRequest) => api<CommercialAdminPlan>('/admin/plans', { method: 'POST', body: JSON.stringify(request) }),
  updatePlan: (code: string, request: CommercialPlanWriteRequest) => api<CommercialAdminPlan>(`/admin/plans/${encodeURIComponent(code)}`, { method: 'PATCH', body: JSON.stringify(request) }),
  deletePlan: (code: string) => api<void>(`/admin/plans/${encodeURIComponent(code)}`, { method: 'DELETE' }),
}
