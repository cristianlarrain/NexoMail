let csrfToken: string | null = null
let csrfTokenRequest: Promise<string> | null = null

function isUnsafeMethod(method?: string) {
  const normalized = (method ?? 'GET').toUpperCase()
  return normalized !== 'GET' && normalized !== 'HEAD' && normalized !== 'OPTIONS' && normalized !== 'TRACE'
}

function changesAuthenticationState(input: RequestInfo | URL) {
  const value = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url
  return value.endsWith('/api/auth/login') ||
    value.endsWith('/api/auth/verify-email') ||
    value.endsWith('/api/auth/logout') ||
    value.endsWith('/api/auth/reset-password')
}

function clearCsrfToken() {
  csrfToken = null
  csrfTokenRequest = null
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
  const unsafe = isUnsafeMethod(init?.method)
  let response = await fetch(input, await buildRequest(init))

  if (response.status === 403 && unsafe) {
    clearCsrfToken()
    response = await fetch(input, await buildRequest(init))
  }

  if (response.ok && changesAuthenticationState(input))
    clearCsrfToken()

  return response
}
