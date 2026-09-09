import { csrfFetch } from './csrfFetch'

export interface CommercialPlan {
  code: string
  name: string
  price: string
  cadence: string
  maxAccounts: number | null
  description: string
  features: string[]
  isFeatured: boolean
  isCorporate: boolean
  isWhiteLabel: boolean
}

export interface CommercialSubscription {
  currentPlan: CommercialPlan
  connectedAccounts: number
  remainingAccounts: number | null
  canAddAccount: boolean
  overLimit: boolean
  plans: CommercialPlan[]
}

async function api<T>(path: string): Promise<T> {
  const response = await csrfFetch(`/api/commercial${path}`, { credentials: 'same-origin' })
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? 'No fue posible obtener la información del plan.')
  }
  return response.json() as Promise<T>
}

export const commercialApi = {
  subscription: () => api<CommercialSubscription>('/subscription'),
}
