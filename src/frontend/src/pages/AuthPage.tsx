import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Mail } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { authApi } from '../api/authApi'

type AuthMode = 'login' | 'register' | 'forgot' | 'reset'

export function AuthPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const queryClient = useQueryClient()
  const initialToken = searchParams.get('token') ?? ''
  const initialEmail = searchParams.get('email') ?? ''
  const [mode, setMode] = useState<AuthMode>(initialToken && initialEmail ? 'reset' : 'login')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState(initialEmail)
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [resetToken, setResetToken] = useState(initialToken)
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
      if (result.developmentResetToken) {
        setResetToken(result.developmentResetToken)
        setMode('reset')
      }
    },
  })

  const reset = useMutation({
    mutationFn: () => authApi.resetPassword({ email, token: resetToken, newPassword: password }),
    onSuccess: () => {
      setRecoveryMessage('Contraseña actualizada correctamente. Ya puede iniciar sesión.')
      setPassword('')
      setConfirmPassword('')
      setResetToken('')
      setMode('login')
    },
  })

  function changeMode(next: AuthMode) {
    setMode(next)
    submit.reset()
    forgot.reset()
    reset.reset()
    setPassword('')
    setConfirmPassword('')
    if (next !== 'reset') setResetToken('')
    if (next !== 'forgot') setRecoveryMessage('')
  }

  function submitReset() {
    if (password !== confirmPassword) return
    reset.mutate()
  }

  const heading = mode === 'login' ? 'Iniciar sesión' : mode === 'register' ? 'Crear cuenta' : mode === 'forgot' ? 'Recuperar contraseña' : 'Nueva contraseña'
  const description = mode === 'login'
    ? 'Accede a tus cuentas de correo conectadas.'
    : mode === 'register'
      ? 'Crea tu usuario NexoMail y conecta tus propias cuentas.'
      : mode === 'forgot'
        ? 'Ingresa el correo asociado a tu cuenta NexoMail.'
        : 'Define una nueva contraseña para tu cuenta.'

  return <main className="auth-page">
    <section className="auth-card">
      <div className="auth-brand"><span className="brand-mark"><Mail size={22} /></span><strong>NexoMail</strong></div>
      <div className="auth-heading"><p className="eyebrow">Correo unificado</p><h1>{heading}</h1><p>{description}</p></div>

      {mode === 'forgot' ? <form onSubmit={event => { event.preventDefault(); forgot.mutate() }}>
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        {recoveryMessage && <div className="success-notice auth-error">{recoveryMessage}</div>}
        {forgot.isError && <div className="notice auth-error">{forgot.error instanceof Error ? forgot.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={forgot.isPending}>{forgot.isPending ? 'Procesando…' : 'Continuar'}</button>
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
