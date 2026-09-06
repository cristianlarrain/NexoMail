import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Eye, EyeOff, Mail } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { authApi, type RateLimitInfo } from '../api/authApi'

type AuthMode = 'login' | 'register' | 'emailVerify' | 'forgot' | 'verify' | 'reset'

type PasswordInputProps = {
  value: string
  onChange: (value: string) => void
  autoComplete: string
  minLength?: number
  autoFocus?: boolean
}

function PasswordInput({ value, onChange, autoComplete, minLength, autoFocus }: PasswordInputProps) {
  const [visible, setVisible] = useState(false)
  return <span className="password-control">
    <input
      type={visible ? 'text' : 'password'}
      value={value}
      onChange={event => onChange(event.target.value)}
      autoComplete={autoComplete}
      minLength={minLength}
      required
      autoFocus={autoFocus}
    />
    <button
      type="button"
      className="password-toggle"
      onClick={() => setVisible(current => !current)}
      aria-label={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
      title={visible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
    >{visible ? <EyeOff size={18} /> : <Eye size={18} />}</button>
  </span>
}

function withRequestAllowance(message: string, rateLimit?: RateLimitInfo) {
  if (!rateLimit) return message
  return `${message} Quedan ${rateLimit.remaining} de ${rateLimit.limit} solicitudes disponibles en este período.`
}

export function AuthPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<AuthMode>('login')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [verificationCode, setVerificationCode] = useState('')
  const [resetToken, setResetToken] = useState('')
  const [statusMessage, setStatusMessage] = useState('')

  const submit = useMutation({
    mutationFn: async () => {
      if (mode === 'login') {
        const session = await authApi.login({ email, password })
        return { kind: 'login' as const, session }
      }
      const result = await authApi.register({ displayName, email, password })
      return { kind: 'register' as const, result }
    },
    onSuccess: result => {
      if (result.kind === 'login') {
        queryClient.setQueryData(['session'], result.session)
        void queryClient.invalidateQueries({ queryKey: ['accounts'] })
        navigate('/inbox', { replace: true })
        return
      }

      setStatusMessage(result.result.message)
      setVerificationCode('')
      setPassword('')
      setMode('emailVerify')
    },
  })

  const emailVerification = useMutation({
    mutationFn: () => authApi.verifyEmail({ email, code: verificationCode }),
    onSuccess: session => {
      queryClient.setQueryData(['session'], session)
      void queryClient.invalidateQueries({ queryKey: ['accounts'] })
      navigate('/inbox', { replace: true })
    },
  })

  const resendVerification = useMutation({
    mutationFn: () => authApi.resendVerification({ email }),
    onSuccess: result => {
      setStatusMessage(withRequestAllowance(result.message, result.rateLimit))
      setVerificationCode('')
    },
  })

  const forgot = useMutation({
    mutationFn: () => authApi.forgotPassword({ email }),
    onSuccess: result => {
      setStatusMessage(withRequestAllowance(result.message, result.rateLimit))
      setVerificationCode('')
      setMode('verify')
    },
  })

  const verify = useMutation({
    mutationFn: () => authApi.verifyResetCode({ email, code: verificationCode }),
    onSuccess: result => {
      setResetToken(result.resetToken)
      setPassword('')
      setConfirmPassword('')
      setMode('reset')
    },
  })

  const reset = useMutation({
    mutationFn: () => authApi.resetPassword({ email, token: resetToken, newPassword: password }),
    onSuccess: () => {
      setStatusMessage('Contraseña actualizada correctamente. Ya puede iniciar sesión.')
      setPassword('')
      setConfirmPassword('')
      setVerificationCode('')
      setResetToken('')
      setMode('login')
    },
  })

  function changeMode(next: AuthMode) {
    setMode(next)
    submit.reset()
    emailVerification.reset()
    resendVerification.reset()
    forgot.reset()
    verify.reset()
    reset.reset()
    setPassword('')
    setConfirmPassword('')
    setVerificationCode('')
    if (next !== 'reset') setResetToken('')
    if (next !== 'login') setStatusMessage('')
  }

  function submitReset() {
    if (password !== confirmPassword) return
    reset.mutate()
  }

  const heading = mode === 'login'
    ? 'Iniciar sesión'
    : mode === 'register'
      ? 'Crear cuenta'
      : mode === 'emailVerify'
        ? 'Verificar correo'
        : mode === 'forgot'
          ? 'Recuperar contraseña'
          : mode === 'verify'
            ? 'Verificar código'
            : 'Nueva contraseña'

  const description = mode === 'login'
    ? 'Accede a tus cuentas de correo conectadas.'
    : mode === 'register'
      ? 'Crea tu usuario NexoMail y conecta tus propias cuentas.'
      : mode === 'emailVerify'
        ? 'Ingresa el código de 6 dígitos enviado a tu correo para activar la cuenta.'
        : mode === 'forgot'
          ? 'Ingresa el correo asociado a tu cuenta NexoMail.'
          : mode === 'verify'
            ? 'Ingresa el código de 6 dígitos enviado al correo de tu cuenta.'
            : 'Define una nueva contraseña para tu cuenta.'

  return <main className="auth-page">
    <section className="auth-card">
      <div className="auth-brand"><span className="brand-mark"><Mail size={22} /></span><strong>NexoMail</strong></div>
      <div className="auth-heading"><p className="eyebrow">Correo unificado</p><h1>{heading}</h1><p>{description}</p></div>

      {mode === 'emailVerify' ? <form onSubmit={event => { event.preventDefault(); emailVerification.mutate() }}>
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required /></label>
        <label>Código de verificación<input value={verificationCode} onChange={event => setVerificationCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" minLength={6} maxLength={6} required autoFocus /></label>
        {statusMessage && <div className="success-notice auth-error">{statusMessage}</div>}
        {emailVerification.isError && <div className="notice auth-error">{emailVerification.error instanceof Error ? emailVerification.error.message : 'El código no es válido o ya expiró.'}</div>}
        {resendVerification.isError && <div className="notice auth-error">{resendVerification.error instanceof Error ? resendVerification.error.message : 'No fue posible reenviar el código.'}</div>}
        <button className="primary-button auth-submit" disabled={emailVerification.isPending || verificationCode.length !== 6}>{emailVerification.isPending ? 'Verificando…' : 'Verificar y continuar'}</button>
        <button type="button" className="auth-link" disabled={resendVerification.isPending || !email.trim()} onClick={() => resendVerification.mutate()}>{resendVerification.isPending ? 'Reenviando…' : 'Reenviar código'}</button>
      </form> : mode === 'forgot' ? <form onSubmit={event => { event.preventDefault(); forgot.mutate() }}>
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        {forgot.isError && <div className="notice auth-error">{forgot.error instanceof Error ? forgot.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={forgot.isPending}>{forgot.isPending ? 'Procesando…' : 'Enviar código'}</button>
      </form> : mode === 'verify' ? <form onSubmit={event => { event.preventDefault(); verify.mutate() }}>
        <label>Correo<input type="email" value={email} readOnly autoComplete="email" /></label>
        <label>Código de verificación<input value={verificationCode} onChange={event => setVerificationCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" minLength={6} maxLength={6} required autoFocus /></label>
        {statusMessage && <div className="success-notice auth-error">{statusMessage}</div>}
        {verify.isError && <div className="notice auth-error">{verify.error instanceof Error ? verify.error.message : 'El código no es válido o ya expiró.'}</div>}
        <button className="primary-button auth-submit" disabled={verify.isPending || verificationCode.length !== 6}>{verify.isPending ? 'Verificando…' : 'Verificar código'}</button>
      </form> : mode === 'reset' ? <form onSubmit={event => { event.preventDefault(); submitReset() }}>
        <label>Correo<input type="email" value={email} readOnly autoComplete="email" /></label>
        <label>Nueva contraseña<PasswordInput value={password} onChange={setPassword} autoComplete="new-password" minLength={10} autoFocus /></label>
        <label>Repetir contraseña<PasswordInput value={confirmPassword} onChange={setConfirmPassword} autoComplete="new-password" minLength={10} /></label>
        <p className="auth-hint">Mínimo 10 caracteres, con mayúsculas, minúsculas y números.</p>
        {password && confirmPassword && password !== confirmPassword && <div className="notice auth-error">Las contraseñas no coinciden.</div>}
        {reset.isError && <div className="notice auth-error">{reset.error instanceof Error ? reset.error.message : 'No fue posible actualizar la contraseña.'}</div>}
        <button className="primary-button auth-submit" disabled={reset.isPending || password !== confirmPassword}>{reset.isPending ? 'Guardando…' : 'Guardar nueva contraseña'}</button>
      </form> : <form onSubmit={event => { event.preventDefault(); submit.mutate() }}>
        {mode === 'register' && <label>Nombre<input value={displayName} onChange={event => setDisplayName(event.target.value)} minLength={2} maxLength={120} autoComplete="name" required /></label>}
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        <label>Contraseña<PasswordInput value={password} onChange={setPassword} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'register' ? 10 : undefined} /></label>
        {mode === 'register' && <p className="auth-hint">Mínimo 10 caracteres, con mayúsculas, minúsculas y números.</p>}
        {statusMessage && mode === 'login' && <div className="success-notice auth-error">{statusMessage}</div>}
        {submit.isError && <div className="notice auth-error">{submit.error instanceof Error ? submit.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={submit.isPending}>{submit.isPending ? 'Procesando…' : mode === 'login' ? 'Entrar' : 'Crear cuenta'}</button>
        {mode === 'login' && <><button type="button" className="auth-link" onClick={() => changeMode('forgot')}>Olvidé mi contraseña</button><button type="button" className="auth-link" onClick={() => changeMode('emailVerify')}>Verificar mi correo</button></>}
      </form>}

      <div className="auth-switch">
        {mode === 'login' ? <>¿Primera vez en NexoMail? <button type="button" onClick={() => changeMode('register')}>Crear cuenta</button></>
          : mode === 'register' ? <>¿Ya tienes una cuenta? <button type="button" onClick={() => changeMode('login')}>Iniciar sesión</button></>
            : <>Volver a <button type="button" onClick={() => changeMode('login')}>Iniciar sesión</button></>}
      </div>
    </section>
  </main>
}
