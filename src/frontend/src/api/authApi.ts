export interface AuthSession {
  id: string
  displayName: string
  email: string
}

export interface ForgotPasswordResponse {
  message: string
}

export interface VerifyResetCodeResponse {
  resetToken: string
}

async function authRequest<T>(path: string, init?: RequestInit, allowUnauthorized = false): Promise<T | null> {
  const response = await fetch(`/api/auth${path}`, {
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...init?.headers },
    ...init,
  })
  if (allowUnauthorized && response.status === 401) return null
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; error?: string } | null
    throw new Error(problem?.detail ?? problem?.error ?? (response.status === 401 ? 'Correo o contraseña incorrectos.' : 'No fue posible completar la operación.'))
  }
  if (response.status === 204 || response.headers.get('content-length') === '0') return undefined as T
  return response.json() as Promise<T>
}

export const authApi = {
  me: () => authRequest<AuthSession>('/me', undefined, true),
  register: (request: { displayName: string; email: string; password: string }) => authRequest<AuthSession>('/register', { method: 'POST', body: JSON.stringify(request) }) as Promise<AuthSession>,
  login: (request: { email: string; password: string }) => authRequest<AuthSession>('/login', { method: 'POST', body: JSON.stringify(request) }) as Promise<AuthSession>,
  forgotPassword: (request: { email: string }) => authRequest<ForgotPasswordResponse>('/forgot-password', { method: 'POST', body: JSON.stringify(request) }) as Promise<ForgotPasswordResponse>,
  verifyResetCode: (request: { email: string; code: string }) => authRequest<VerifyResetCodeResponse>('/verify-reset-code', { method: 'POST', body: JSON.stringify(request) }) as Promise<VerifyResetCodeResponse>,
  resetPassword: (request: { email: string; token: string; newPassword: string }) => authRequest<void>('/reset-password', { method: 'POST', body: JSON.stringify(request) }) as Promise<void>,
  logout: () => authRequest<void>('/logout', { method: 'POST' }) as Promise<void>,
}
