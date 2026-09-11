import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Clock3, Search, ShieldCheck, Sparkles, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { commercialApi } from '../api/commercialApi'

type TrialType = 'premium' | 'nexi'

function formatDate(value: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return date.toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' })
}

function subscriptionLabel(status?: string) {
  switch (status) {
    case 'active': return 'Activa'
    case 'trialing': return 'Prueba'
    case 'legacy': return 'Heredada'
    case 'pending': return 'Pendiente'
    case 'past_due': return 'Pago pendiente'
    case 'canceled': return 'Cancelada'
    case 'expired': return 'Vencida'
    default: return status || 'Sin estado'
  }
}

function providerLabel(provider?: string | null) {
  if (provider === 'admin') return 'Asignación manual'
  if (provider === 'admin_trial') return 'Prueba administrada'
  if (provider === 'mercadopago') return 'Mercado Pago'
  return provider || 'NexoMail'
}

export function AdminUsersPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [draftPlans, setDraftPlans] = useState<Record<string, string>>({})
  const [trialTypes, setTrialTypes] = useState<Record<string, TrialType>>({})
  const [trialDays, setTrialDays] = useState<Record<string, number>>({})
  const status = useQuery({ queryKey: ['commercial-admin-status'], queryFn: commercialApi.adminStatus, staleTime: 5 * 60_000, retry: false })
  const users = useQuery({ queryKey: ['commercial-admin-users'], queryFn: commercialApi.adminUsers, enabled: status.data?.isAdministrator === true, staleTime: 0, retry: false })
  const plans = useQuery({ queryKey: ['commercial-admin-plans'], queryFn: commercialApi.adminPlans, enabled: status.data?.isAdministrator === true, staleTime: 30_000, retry: false })

  const assignment = useMutation({
    mutationFn: ({ userId, planCode }: { userId: string; planCode: string }) => commercialApi.assignUserPlan(userId, planCode),
    onSuccess: async updated => {
      setDraftPlans(current => ({ ...current, [updated.id]: updated.planCode }))
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['commercial-admin-users'] }),
        queryClient.invalidateQueries({ queryKey: ['commercial-admin-plans'] }),
        queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] }),
      ])
    },
  })

  const trial = useMutation({
    mutationFn: ({ userId, trialType, days }: { userId: string; trialType: TrialType; days: number }) => commercialApi.grantUserTrial(userId, trialType, days),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['commercial-admin-users'] }),
        queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] }),
      ])
    },
  })

  const filteredUsers = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('es')
    if (!term) return users.data ?? []
    return (users.data ?? []).filter(user => `${user.displayName} ${user.email} ${user.planName}`.toLocaleLowerCase('es').includes(term))
  }, [search, users.data])

  const activePlans = (plans.data ?? []).filter(plan => plan.isActive)

  if (status.isLoading) return <section className="settings-page commercial-admin-page"><p className="eyebrow">Administración</p><h1>Administración de usuarios</h1><div className="commercial-plan-loading">Comprobando permisos…</div></section>
  if (!status.data?.isAdministrator) return <section className="settings-page commercial-admin-page"><p className="eyebrow">Administración</p><h1>Administración de usuarios</h1><div className="notice">Esta sección está disponible sólo para administradores de NexoMail.</div><Link to="/settings/plan" className="secondary-button commercial-back-link"><ArrowLeft size={16} /> Volver a Plan y uso</Link></section>

  return <section className="settings-page commercial-admin-page commercial-users-page">
    <div className="commercial-admin-header">
      <div><p className="eyebrow">Administración</p><h1>Administración de usuarios</h1><p className="page-description">Revise los usuarios registrados, asigne tipos de cuenta y otorgue pruebas temporales de Premium o Nexi.</p></div>
      <div className="commercial-admin-header-actions"><Link to="/settings/plan" className="secondary-button"><ArrowLeft size={16} /> Plan y uso</Link><Link to="/admin/plans" className="secondary-button">Tipos de cuenta</Link></div>
    </div>

    <div className="commercial-admin-guidance"><ShieldCheck size={18} /><span>Los usuarios Freemium pueden recibir una prueba temporal de Premium completo o sólo de Nexi. Al vencer, recuperan automáticamente las funciones de Freemium. El Owner / Administrador general no depende de planes ni pruebas comerciales.</span></div>

    <div className="commercial-users-toolbar">
      <label className="commercial-users-search"><Search size={16} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar por nombre, correo o plan" aria-label="Buscar usuarios" /></label>
      <span><UsersRound size={16} /> {filteredUsers.length} de {users.data?.length ?? 0} usuarios</span>
    </div>

    {users.isError && <div className="notice">{users.error instanceof Error ? users.error.message : 'No fue posible cargar los usuarios.'}</div>}
    {plans.isError && <div className="notice">{plans.error instanceof Error ? plans.error.message : 'No fue posible cargar los tipos de cuenta.'}</div>}
    {assignment.isError && <div className="notice">{assignment.error instanceof Error ? assignment.error.message : 'No fue posible cambiar el plan del usuario.'}</div>}
    {trial.isError && <div className="notice">{trial.error instanceof Error ? trial.error.message : 'No fue posible otorgar la prueba temporal.'}</div>}

    <div className="commercial-admin-table-wrap">
      <table className="commercial-admin-table commercial-users-table">
        <thead><tr><th>Usuario</th><th>Plan asignado</th><th>Plan efectivo</th><th>Prueba temporal</th><th>Cuentas</th><th>Suscripción</th><th>Último acceso</th><th>Estado</th></tr></thead>
        <tbody>
          {filteredUsers.map(user => {
            const isOwner = user.effectivePlanCode === 'owner'
            const selectedPlan = draftPlans[user.id] ?? user.planCode
            const changed = selectedPlan !== user.planCode
            const pendingThisUser = assignment.isPending && assignment.variables?.userId === user.id
            const trialPendingThisUser = trial.isPending && trial.variables?.userId === user.id
            const selectedTrialType = trialTypes[user.id] ?? 'premium'
            const selectedTrialDays = trialDays[user.id] ?? 30
            const canTrial = !isOwner && user.isActive && user.planCode === 'freemium'
            const hasAdminTrial = user.subscription?.provider === 'admin_trial'
              && user.subscription.status === 'trialing'
              && Boolean(user.subscription.trialEndsAt)
            const trialName = hasAdminTrial
              ? user.effectivePlanCode === 'premium' ? 'Premium' : 'Nexi'
              : null

            return <tr key={user.id} className={user.isActive ? '' : 'inactive'}>
              <td><strong>{user.displayName || 'Sin nombre'}</strong><small>{user.email}{isOwner ? ' · Owner / Administrador general' : user.isAdministrator ? ' · Administrador' : ''}</small></td>
              <td>
                <div className="commercial-user-plan-control">
                  <select value={selectedPlan} disabled={isOwner || !user.isActive || pendingThisUser} onChange={event => setDraftPlans(current => ({ ...current, [user.id]: event.target.value }))} aria-label={`Plan de ${user.displayName || user.email}`} title={isOwner ? 'El Owner no depende de un plan comercial.' : undefined}>
                    {activePlans.map(plan => <option key={plan.code} value={plan.code}>{plan.name}</option>)}
                  </select>
                  <button type="button" className="primary-button" disabled={isOwner || !changed || !user.isActive || assignment.isPending} onClick={() => assignment.mutate({ userId: user.id, planCode: selectedPlan })}>{isOwner ? 'Protegido' : pendingThisUser ? 'Aplicando…' : 'Aplicar'}</button>
                </div>
              </td>
              <td><strong>{user.effectivePlanName}</strong>{!isOwner && user.effectivePlanCode !== user.planCode && <small>Acceso temporal por prueba o estado de suscripción</small>}</td>
              <td>
                {canTrial
                  ? <div className="commercial-user-trial-wrap">
                      <div className="commercial-user-trial-control">
                        <select value={selectedTrialType} disabled={trialPendingThisUser} onChange={event => setTrialTypes(current => ({ ...current, [user.id]: event.target.value as TrialType }))} aria-label={`Tipo de prueba de ${user.displayName || user.email}`}>
                          <option value="premium">Premium</option>
                          <option value="nexi">Sólo Nexi</option>
                        </select>
                        <label><Clock3 size={13} /><input type="number" min={1} max={90} value={selectedTrialDays} disabled={trialPendingThisUser} onChange={event => setTrialDays(current => ({ ...current, [user.id]: Math.min(90, Math.max(1, Number(event.target.value) || 1)) }))} aria-label={`Días de prueba de ${user.displayName || user.email}`} /><span>días</span></label>
                        <button type="button" className="secondary-button" disabled={trial.isPending} onClick={() => trial.mutate({ userId: user.id, trialType: selectedTrialType, days: selectedTrialDays })}><Sparkles size={14} /> {trialPendingThisUser ? 'Aplicando…' : hasAdminTrial ? 'Actualizar' : 'Dar prueba'}</button>
                      </div>
                      {hasAdminTrial && <small className="commercial-user-trial-active">{trialName} hasta {formatDate(user.subscription?.trialEndsAt ?? null)}</small>}
                    </div>
                  : <small>{isOwner ? 'No aplica al Owner' : user.planCode !== 'freemium' ? 'Disponible sólo para Freemium' : 'Usuario inactivo'}</small>}
              </td>
              <td><strong>{user.connectedAccounts}</strong><small>conectada{user.connectedAccounts === 1 ? '' : 's'}</small></td>
              <td><strong>{isOwner ? 'No aplica' : subscriptionLabel(user.subscription?.status)}</strong><small>{isOwner ? 'Acceso interno' : providerLabel(user.subscription?.provider)}</small></td>
              <td>{formatDate(user.lastLoginAt)}</td>
              <td><span className={`commercial-admin-status ${user.isActive ? 'active' : 'inactive'}`}>{user.isActive ? 'Activo' : 'Inactivo'}</span></td>
            </tr>
          })}
          {!users.isLoading && filteredUsers.length === 0 && <tr><td colSpan={8}><div className="commercial-users-empty">No hay usuarios que coincidan con la búsqueda.</div></td></tr>}
        </tbody>
      </table>
    </div>
  </section>
}
