import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Mail } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { authApi } from '../api/authApi'

export function AuthPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

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

  function changeMode(next: 'login' | 'register') {
    setMode(next)
    submit.reset()
    setPassword('')
  }

  return <main className="auth-page">
    <section className="auth-card">
      <div className="auth-brand"><span className="brand-mark"><Mail size={22} /></span><strong>NexoMail</strong></div>
      <div className="auth-heading"><p className="eyebrow">Correo unificado</p><h1>{mode === 'login' ? 'Iniciar sesión' : 'Crear cuenta'}</h1><p>{mode === 'login' ? 'Accede a tus cuentas de correo conectadas.' : 'Crea tu usuario NexoMail y conecta tus propias cuentas.'}</p></div>
      <form onSubmit={event => { event.preventDefault(); submit.mutate() }}>
        {mode === 'register' && <label>Nombre<input value={displayName} onChange={event => setDisplayName(event.target.value)} minLength={2} maxLength={120} autoComplete="name" required /></label>}
        <label>Correo<input type="email" value={email} onChange={event => setEmail(event.target.value)} autoComplete="email" required autoFocus /></label>
        <label>Contraseña<input type="password" value={password} onChange={event => setPassword(event.target.value)} autoComplete={mode === 'login' ? 'current-password' : 'new-password'} minLength={mode === 'register' ? 10 : undefined} required /></label>
        {mode === 'register' && <p className="auth-hint">Mínimo 10 caracteres, con mayúsculas, minúsculas y números.</p>}
        {submit.isError && <div className="notice auth-error">{submit.error instanceof Error ? submit.error.message : 'No fue posible completar la operación.'}</div>}
        <button className="primary-button auth-submit" disabled={submit.isPending}>{submit.isPending ? 'Procesando…' : mode === 'login' ? 'Entrar' : 'Crear cuenta'}</button>
      </form>
      <div className="auth-switch">{mode === 'login' ? <>¿Primera vez en NexoMail? <button type="button" onClick={() => changeMode('register')}>Crear cuenta</button></> : <>¿Ya tienes una cuenta? <button type="button" onClick={() => changeMode('login')}>Iniciar sesión</button></>}</div>
    </section>
  </main>
}
