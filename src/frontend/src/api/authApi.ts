export interface RateLimitInfo {
  limit: number
  remaining: number
  resetSeconds: number
}

export interface AuthSession {
  id: string
  displayName: string
  email: string
  avatarDataUrl: string | null
}

export interface RegisterResponse {
  message: string
  rateLimit?: RateLimitInfo
}

export interface MessageResponse {
  message: string
  rateLimit?: RateLimitInfo
}

export interface ForgotPasswordResponse {
  message: string
  rateLimit?: RateLimitInfo
}

export interface VerifyResetCodeResponse {
  resetToken: string
  rateLimit?: RateLimitInfo
}

function readRateLimit(response: Response): RateLimitInfo | undefined {
  const limit = Number(response.headers.get('X-RateLimit-Limit'))
  const remaining = Number(response.headers.get('X-RateLimit-Remaining'))
  const resetSeconds = Number(response.headers.get('X-RateLimit-Reset-Seconds'))
  if (!Number.isFinite(limit) || !Number.isFinite(remaining) || !Number.isFinite(resetSeconds)) return undefined
  return { limit, remaining, resetSeconds }
}

function formatDuration(totalSeconds: number) {
  const seconds = Math.max(1, Math.ceil(totalSeconds))
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  if (minutes === 0) return `${rest} segundo${rest === 1 ? '' : 's'}`
  if (rest === 0) return `${minutes} minuto${minutes === 1 ? '' : 's'}`
  return `${minutes} min ${rest} s`
}

async function authRequest<T>(path: string, init?: RequestInit, allowUnauthorized = false): Promise<T | null> {
  const response = await fetch(`/api/auth${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  if (allowUnauthorized && response.status === 401) return null

  const rateLimit = readRateLimit(response)
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string; retryAfterSeconds?: number } | null
    let message = problem?.detail ?? problem?.error ?? (response.status === 401 ? 'Correo o contraseña incorrectos.' : 'No fue posible completar la operación.')

    if (response.status === 429) {
      const retryAfter = Number(response.headers.get('Retry-After')) || problem?.retryAfterSeconds || rateLimit?.resetSeconds
      message = retryAfter
        ? `Demasiados intentos. Podrá intentarlo nuevamente en ${formatDuration(retryAfter)}.`
        : 'Demasiados intentos. Inténtelo nuevamente en unos minutos.'
    } else if (response.status === 401 && rateLimit) {
      message = `Correo o contraseña incorrectos. Quedan ${rateLimit.remaining} de ${rateLimit.limit} intentos en este período.`
    }

    throw new Error(message)
  }

  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  const data = await response.json() as T
  if (rateLimit && typeof data === 'object' && data !== null)
    return { ...data, rateLimit } as T
  return data
}

export const authApi = {
  me: () => authRequest<AuthSession>('/me', undefined, true),
  register: (request: { displayName: string; email: string; password: string }) => authRequest<RegisterResponse>('/register', { method: 'POST', body: JSON.stringify(request) }) as Promise<RegisterResponse>,
  verifyEmail: (request: { email: string; code: string }) => authRequest<AuthSession>('/verify-email', { method: 'POST', body: JSON.stringify(request) }) as Promise<AuthSession>,
  resendVerification: (request: { email: string }) => authRequest<MessageResponse>('/resend-verification', { method: 'POST', body: JSON.stringify(request) }) as Promise<MessageResponse>,
  login: (request: { email: string; password: string }) => authRequest<AuthSession>('/login', { method: 'POST', body: JSON.stringify(request) }) as Promise<AuthSession>,
  updateProfile: (request: { displayName: string; avatarDataUrl: string | null }) => authRequest<AuthSession>('/me', { method: 'PATCH', body: JSON.stringify(request) }) as Promise<AuthSession>,
  forgotPassword: (request: { email: string }) => authRequest<ForgotPasswordResponse>('/forgot-password', { method: 'POST', body: JSON.stringify(request) }) as Promise<ForgotPasswordResponse>,
  verifyResetCode: (request: { email: string; code: string }) => authRequest<VerifyResetCodeResponse>('/verify-reset-code', { method: 'POST', body: JSON.stringify(request) }) as Promise<VerifyResetCodeResponse>,
  resetPassword: (request: { email: string; token: string; newPassword: string }) => authRequest<void>('/reset-password', { method: 'POST', body: JSON.stringify(request) }) as Promise<void>,
  logout: () => authRequest<void>('/logout', { method: 'POST' }) as Promise<void>,
}
