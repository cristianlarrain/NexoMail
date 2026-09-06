let csrfToken: string | null = null
let csrfTokenRequest: Promise<string> | null = null

function isUnsafeMethod(method?: string) {
  const normalized = (method ?? 'GET').toUpperCase()
  return normalized !== 'GET' && normalized !== 'HEAD' && normalized !== 'OPTIONS' && normalized !== 'TRACE'
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
  let response = await fetch(input, await buildRequest(init))

  if (response.status === 403 && response.headers.get('X-NexoMail-CSRF') === 'invalid' && isUnsafeMethod(init?.method)) {
    csrfToken = null
    response = await fetch(input, await buildRequest(init))
  }

  return response
}
