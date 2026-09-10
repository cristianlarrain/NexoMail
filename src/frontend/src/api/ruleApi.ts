import { csrfFetch } from './csrfFetch'

export type RuleAction = 'trash' | 'archive' | 'markRead' | 'moveToFolder'

export type MailRule = {
  ruleId: string
  query: string
  accountId: string
  account: string
  action: RuleAction
  destinationId?: string | null
  destinationName?: string | null
}

export type RuleDestination = {
  id: string
  displayName: string
}

export type RuleCreateResult = {
  created: boolean
  rule: MailRule
}

type ApiProblem = {
  detail?: string
  error?: string
  title?: string
}

function cleanRawError(value: string) {
  return value
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<[^>]+>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 500)
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
      throw new Error('La cuenta necesita autorización adicional para administrar reglas. Vuelve a conectar la cuenta desde Configuración.')

    const rawDetail = cleanRawError(raw)
    const titleDetail = problem?.title?.trim()
    const detail = rawDetail || titleDetail ? `: ${rawDetail || titleDetail}` : ''
    throw new Error(`No fue posible administrar las reglas (HTTP ${response.status})${detail}`)
  }

  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const ruleApi = {
  list: (accountId: string) => api<MailRule[]>(`/mail/rules?accountId=${encodeURIComponent(accountId)}`),
  destinations: (accountId: string) => api<RuleDestination[]>(`/mail/rules/destinations?accountId=${encodeURIComponent(accountId)}`),
  create: (accountId: string, query: string, action: RuleAction, destinationId?: string) => api<RuleCreateResult>('/mail/rules', {
    method: 'POST',
    body: JSON.stringify({ accountId, query, action, destinationId: destinationId || null }),
  }),
  remove: (accountId: string, ruleId: string) => api<{ removed: boolean; ruleId: string }>(`/mail/rules/${encodeURIComponent(accountId)}/${encodeURIComponent(ruleId)}`, {
    method: 'DELETE',
  }),
}
