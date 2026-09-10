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

type ApiProblem = {
  detail?: string
  error?: string
  title?: string
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await csrfFetch(`/api${path}`, {
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })

  if (!response.ok) {
    const raw = await response.text().catch(() => '')
    let problem: ApiProblem | null = null
    if (raw) {
      try { problem = JSON.parse(raw) as ApiProblem } catch { problem = null }
    }

    const providerMessage = problem?.detail ?? problem?.error
    if (providerMessage) throw new Error(providerMessage)

    if (response.status === 404)
      throw new Error('El servicio de reglas no está disponible en la API que está ejecutándose. Reinicia NexoMail.Api y vuelve a intentar.')
    if (response.status === 401)
      throw new Error('Tu sesión expiró. Vuelve a iniciar sesión para administrar las reglas.')
    if (response.status === 403)
      throw new Error('La cuenta necesita autorización adicional para administrar reglas de Gmail. Vuelve a conectar la cuenta desde Configuración.')

    const suffix = problem?.title ? `: ${problem.title}` : raw && raw.length < 180 ? `: ${raw}` : ''
    throw new Error(`No fue posible administrar las reglas (HTTP ${response.status})${suffix}`)
  }

  if (response.status === 204) return undefined as T
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
