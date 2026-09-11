import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Search, ShieldCheck, UsersRound } from 'lucide-react'
import { Link } from 'react-router-dom'
import { commercialApi } from '../api/commercialApi'

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
  if (provider === 'mercadopago') return 'Mercado Pago'
  return provider || 'NexoMail'
}

export function AdminUsersPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [draftPlans, setDraftPlans] = useState<Record<string, string>>({})
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
      <div><p className="eyebrow">Administración</p><h1>Administración de usuarios</h1><p className="page-description">Revise los usuarios registrados y asigne manualmente el tipo de cuenta que corresponde a cada uno.</p></div>
      <div className="commercial-admin-header-actions"><Link to="/settings/plan" className="secondary-button"><ArrowLeft size={16} /> Plan y uso</Link><Link to="/admin/plans" className="secondary-button">Tipos de cuenta</Link></div>
    </div>

    <div className="commercial-admin-guidance"><ShieldCheck size={18} /><span>Un cambio manual actualiza el acceso de NexoMail de inmediato. Si el usuario tiene una suscripción externa activa, esta acción no cancela ni modifica cobros en Mercado Pago.</span></div>

    <div className="commercial-users-toolbar">
      <label className="commercial-users-search"><Search size={16} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar por nombre, correo o plan" aria-label="Buscar usuarios" /></label>
      <span><UsersRound size={16} /> {filteredUsers.length} de {users.data?.length ?? 0} usuarios</span>
    </div>

    {users.isError && <div className="notice">{users.error instanceof Error ? users.error.message : 'No fue posible cargar los usuarios.'}</div>}
    {plans.isError && <div className="notice">{plans.error instanceof Error ? plans.error.message : 'No fue posible cargar los tipos de cuenta.'}</div>}
    {assignment.isError && <div className="notice">{assignment.error instanceof Error ? assignment.error.message : 'No fue posible cambiar el plan del usuario.'}</div>}

    <div className="commercial-admin-table-wrap">
      <table className="commercial-admin-table commercial-users-table">
        <thead><tr><th>Usuario</th><th>Plan asignado</th><th>Plan efectivo</th><th>Cuentas</th><th>Suscripción</th><th>Último acceso</th><th>Estado</th></tr></thead>
        <tbody>
          {filteredUsers.map(user => {
            const selectedPlan = draftPlans[user.id] ?? user.planCode
            const changed = selectedPlan !== user.planCode
            const pendingThisUser = assignment.isPending && assignment.variables?.userId === user.id
            return <tr key={user.id} className={user.isActive ? '' : 'inactive'}>
              <td><strong>{user.displayName || 'Sin nombre'}</strong><small>{user.email}{user.isAdministrator ? ' · Administrador' : ''}</small></td>
              <td>
                <div className="commercial-user-plan-control">
                  <select value={selectedPlan} disabled={!user.isActive || pendingThisUser} onChange={event => setDraftPlans(current => ({ ...current, [user.id]: event.target.value }))} aria-label={`Plan de ${user.displayName || user.email}`}>
                    {activePlans.map(plan => <option key={plan.code} value={plan.code}>{plan.name}</option>)}
                  </select>
                  <button type="button" className="primary-button" disabled={!changed || !user.isActive || assignment.isPending} onClick={() => assignment.mutate({ userId: user.id, planCode: selectedPlan })}>{pendingThisUser ? 'Aplicando…' : 'Aplicar'}</button>
                </div>
              </td>
              <td><strong>{user.effectivePlanName}</strong>{user.effectivePlanCode !== user.planCode && <small>Limitado temporalmente por estado de suscripción</small>}</td>
              <td><strong>{user.connectedAccounts}</strong><small>conectada{user.connectedAccounts === 1 ? '' : 's'}</small></td>
              <td><strong>{subscriptionLabel(user.subscription?.status)}</strong><small>{providerLabel(user.subscription?.provider)}</small></td>
              <td>{formatDate(user.lastLoginAt)}</td>
              <td><span className={`commercial-admin-status ${user.isActive ? 'active' : 'inactive'}`}>{user.isActive ? 'Activo' : 'Inactivo'}</span></td>
            </tr>
          })}
          {!users.isLoading && filteredUsers.length === 0 && <tr><td colSpan={7}><div className="commercial-users-empty">No hay usuarios que coincidan con la búsqueda.</div></td></tr>}
        </tbody>
      </table>
    </div>
  </section>
}
