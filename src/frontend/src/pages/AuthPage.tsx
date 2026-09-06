import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Mail } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { authApi } from '../api/authApi'

type AuthMode = 'login' | 'register' | 'forgot' | 'verify' | 'reset'

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
  const [recoveryMessage, setRecoveryMessage] = useState('')

  const submit = useMutation({
    mutationFn: () => mode === 'login'
      ? authApi.login({ email, password })
      : authApi.register({ displayName, email, password }),
    onSuccess: session => {
      queryClient.setQueryData(['session'], session)
      void queryClient.invalidateQueries({ queryKey: ['accounts'] })
      navigate('/inbox', { replace: true })
    },
  })

  const forgot = useMutation({
    mutationFn: () => authApi.forgotPassword({ email }),
    onSuccess: result => {
      setRecoveryMessage(result.message)
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
      setRecoveryMessage('Contraseña actualizada correctamente. Ya puede iniciar sesión.')
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
    forgot.reset()
    verify.reset()
    reset.reset()
    setPassword('')
    setConfirmPassword('')
    setVerificationCode('')
    if (next !== 'reset') setResetToken('')
    if (next !== 'forgot' && next !== 'verify') setRecoveryMessage('')
  }

  function submitReset() {
    if (password !== confirmPassword) return
    reset.mutate()
  }

  const heading = mode === 'login'
    ? 'Iniciar sesión'
    : mode === 'register'
      ? 'Crear cuenta'
      : mode === 'forgot'
        ? 'Recuperar contraseña'
        : mode === 'verify'
          ? 'Verificar código'
          : 'Nueva contraseña'

  const description = mode === 'login'
    ? 'Accede a tus cuentas de correo conectadas.'
    : mode === 'register'
      ? 'Crea tu usuario NexoMail y conecta tus propias cuentas.'
      : mode === 'forgot'
        ? 'Ingresa el correo asociado a tu cuenta NexoMail.'
        : mode === 'verify'
          ? 'Ingresa el código de 6 dígitos enviado al correo de tu cuenta.'
          : 'Define una nueva contraseña para tu cuenta.'

  return <main className="auth-page">
    <section className="auth-card">
      <div className="auth-brand"><span className="brand-mark"><Mail size={22} /></span><strong>NexoMail</strong></div>
      <div className="auth-heading"><p className="eyebrow">Correo unificado</p><h1>{heading}</h1><p>{description}</p></div>

      {mode === 'forgot' ? <form onSubmit={event => { event.preventDefault(); forgot.mutate() }}>
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        {forgot.isError && <div className="notice auth-error">{forgot.error instanceof Error ? forgot.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={forgot.isPending}>{forgot.isPending ? 'Procesando…' : 'Enviar código'}</button>
      </form> : mode === 'verify' ? <form onSubmit={event => { event.preventDefault(); verify.mutate() }}>
        <label>Correo<input type="email" value={email} readOnly autoComplete="email" /></label>
        <label>Código de verificación<input value={verificationCode} onChange={event => setVerificationCode(event.target.value.replace(/\D/g, '').slice(0, 6))} inputMode="numeric" autoComplete="one-time-code" pattern="\d{6}" minLength={6} maxLength={6} required autoFocus /></label>
        {recoveryMessage && <div className="success-notice auth-error">{recoveryMessage}</div>}
        {verify.isError && <div className="notice auth-error">{verify.error instanceof Error ? verify.error.message : 'El código no es válido o ya expiró.'}</div>}
        <button className="primary-button auth-submit" disabled={verify.isPending || verificationCode.length !== 6}>{verify.isPending ? 'Verificando…' : 'Verificar código'}</button>
      </form> : mode === 'reset' ? <form onSubmit={event => { event.preventDefault(); submitReset() }}>
        <label>Correo<input type="email" value={email} readOnly autoComplete="email" /></label>
        <label>Nueva contraseña<input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete="new-password" minLength={10} required autoFocus /></label>
        <label>Repetir contraseña<input type="password" value={confirmPassword} onChange={event => setConfirmPassword(event.target.value)} autoComplete="new-password" minLength={10} required /></label>
        <p className="auth-hint">Mínimo 10 caracteres, con mayúsculas, minúsculas y números.</p>
        {password && confirmPassword && password !== confirmPassword && <div className="notice auth-error">Las contraseñas no coinciden.</div>}
        {reset.isError && <div className="notice auth-error">{reset.error instanceof Error ? reset.error.message : 'No fue posible actualizar la contraseña.'}</div>}
        <button className="primary-button auth-submit" disabled={reset.isPending || password !== confirmPassword}>{reset.isPending ? 'Guardando…' : 'Guardar nueva contraseña'}</button>
      </form> : <form onSubmit={event => { event.preventDefault(); submit.mutate() }}>
        {mode === 'register' && <label>Nombre<input value={displayName} onChange={event => setDisplayName(event.target.value)} minLength={2} maxLength={120} autoComplete="name" required /></label>}
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        <label>Contraseña<input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'register' ? 10 : undefined} required /></label>
        {mode === 'register' && <p className="auth-hint">Mínimo 10 caracteres, con mayúsculas, minúsculas y números.</p>}
        {recoveryMessage && mode === 'login' && <div className="success-notice auth-error">{recoveryMessage}</div>}
        {submit.isError && <div className="notice auth-error">{submit.error instanceof Error ? submit.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={submit.isPending}>{submit.isPending ? 'Procesando…' : mode === 'login' ? 'Entrar' : 'Crear cuenta'}</button>
        {mode === 'login' && <button type="button" className="auth-link" onClick={() => changeMode('forgot')}>Olvidé mi contraseña</button>}
      </form>}

      <div className="auth-switch">
        {mode === 'login' ? <>¿Primera vez en NexoMail? <button type="button" onClick={() => changeMode('register')}>Crear cuenta</button></>
          : mode === 'register' ? <>¿Ya tienes una cuenta? <button type="button" onClick={() => changeMode('login')}>Iniciar sesión</button></>
            : <>Volver a <button type="button" onClick={() => changeMode('login')}>Iniciar sesión</button></>}
      </div>
    </section>
  </main>
}
