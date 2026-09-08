import { withActionIndicator } from '../utils/actionIndicator'

let csrfToken: string | null = null
let csrfTokenRequest: Promise<string> | null = null

function isUnsafeMethod(method?: string) {
  const normalized = (method ?? 'GET').toUpperCase()
  return normalized !== 'GET' && normalized !== 'HEAD' && normalized !== 'OPTIONS' && normalized !== 'TRACE'
}

function requestPath(input: RequestInfo | URL) {
  const value = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
  try {
    return new URL(value, window.location.origin).pathname
  } catch {
    return value
  }
}

function actionLabel(input: RequestInfo | URL, init?: RequestInit) {
  const method = (init?.method ?? 'GET').toUpperCase()
  if (!isUnsafeMethod(method)) return null

  const path = requestPath(input)

  if (path.includes('/api/mail/ai/report')) return 'Generando reporte con Nexi…'
  if (path.includes('/api/mail/ai/context') || path.includes('/api/mail/ai/search')) return 'Analizando con Nexi…'
  if (path.includes('/ai-summary')) return 'Resumiendo correo con Nexi…'
  if (path.includes('/ai-reply')) return 'Preparando respuesta con Nexi…'
  if (path.includes('/api/mail/ai/draft')) return 'Generando texto con Nexi…'

  if (path.includes('/drafts/') && path.endsWith('/send')) return 'Enviando borrador…'
  if (path.includes('/reply')) return 'Enviando respuesta…'
  if (path.includes('/forward')) return 'Reenviando correo…'
  if (path === '/api/mail/send') return 'Enviando correo…'
  if (path.includes('/drafts')) return 'Guardando borrador…'

  if (path.includes('/trash')) return 'Moviendo correo a Papelera…'
  if (path.includes('/move')) return 'Moviendo correo…'
  if (path.includes('/read')) return 'Actualizando estado del correo…'
  if (path.includes('/folders/') && path.endsWith('/empty')) return 'Vaciando carpeta…'
  if (path.includes('/ignored-senders')) return method === 'DELETE' ? 'Quitando remitente de ignorados…' : 'Ignorando remitente…'

  if (path.includes('/control-center')) {
    if (method === 'DELETE') return 'Eliminando acción del Centro de Control…'
    if (path.includes('/index/sync')) return 'Actualizando índice del Centro de Control…'
    return 'Actualizando Centro de Control…'
  }

  if (path.includes('/accounts')) return method === 'DELETE' ? 'Eliminando cuenta…' : 'Guardando configuración de cuenta…'
  if (path.includes('/mail/refresh')) return 'Actualizando correo…'

  if (path === '/api/auth/login') return 'Iniciando sesión…'
  if (path === '/api/auth/signout') return 'Cerrando sesión…'
  if (path.includes('/reset-password')) return 'Actualizando contraseña…'

  return 'Procesando acción…'
}

function changesAuthenticationState(input: RequestInfo | URL) {
  const path = requestPath(input)
  return path === '/api/auth/login' ||
    path === '/api/auth/verify-email' ||
    path === '/api/auth/signout' ||
    path === '/api/auth/reset-password'
}

function requiresActiveSession(input: RequestInfo | URL) {
  const path = requestPath(input)
  return path.startsWith('/api/mail') ||
    path === '/api/auth/me' ||
    path.startsWith('/api/auth/sessions')
}

function clearCsrfToken() {
  csrfToken = null
  csrfTokenRequest = null
}

function redirectToLogin() {
  clearCsrfToken()
  if (window.location.pathname !== '/login')
    window.location.replace('/login')
}

async function loadCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken
  if (csrfTokenRequest) return csrfTokenRequest

  csrfTokenRequest = fetch('/api/auth/csrf', {
    method: 'GET',
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  }).then(async response => {
    if (!response.ok) throw new Error('No fue posible iniciar la protección de seguridad de NexoMail.')
    const data = await response.json() as { token?: string }
    if (!data.token) throw new Error('NexoMail no entregó el token de seguridad requerido.')
    csrfToken = data.token
    return data.token
  }).finally(() => {
    csrfTokenRequest = null
  })

  return csrfTokenRequest
}

async function buildRequest(init?: RequestInit) {
  const next: RequestInit = { ...init, credentials: 'same-origin' }
  const headers = new Headers(init?.headers)

  if (isUnsafeMethod(init?.method))
    headers.set('X-CSRF-TOKEN', await loadCsrfToken())

  next.headers = headers
  return next
}

export async function csrfFetch(input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  const execute = async () => {
    const unsafe = isUnsafeMethod(init?.method)
    let response = await fetch(input, await buildRequest(init))

    if (response.status === 403 && response.headers.get('X-NexoMail-CSRF') === 'invalid' && unsafe) {
      clearCsrfToken()
      response = await fetch(input, await buildRequest(init))
    }

    if (response.ok && changesAuthenticationState(input))
      clearCsrfToken()

    if (response.status === 401 && requiresActiveSession(input))
      redirectToLogin()

    return response
  }

  const label = actionLabel(input, init)
  return label ? withActionIndicator(label, execute) : execute()
}
