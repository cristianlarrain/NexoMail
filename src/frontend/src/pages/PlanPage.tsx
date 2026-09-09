import { useQuery } from '@tanstack/react-query'
import { Building2, Check, Crown, Mail, Paintbrush, Settings2, Sparkles, Tag, Users } from 'lucide-react'
import { Link } from 'react-router-dom'
import { commercialApi, type CommercialPlan } from '../api/commercialApi'

type PlanTone = 'freemium' | 'premium' | 'corporate' | 'white-label'

function planTone(plan: CommercialPlan): PlanTone {
  if (plan.code === 'freemium') return 'freemium'
  if (plan.code === 'premium') return 'premium'
  if (plan.code === 'corporate' || plan.isCorporate && !plan.isWhiteLabel) return 'corporate'
  if (plan.code === 'white_label' || plan.isWhiteLabel) return 'white-label'
  if (plan.isFeatured) return 'premium'
  return 'freemium'
}

function PlanBrandmark({ plan, compact = false }: { plan: CommercialPlan; compact?: boolean }) {
  const tone = planTone(plan)
  const size = compact ? 23 : 30
  return <span className={`commercial-plan-brandmark tone-${tone} ${compact ? 'compact' : ''}`} aria-hidden="true">
    {tone === 'corporate' && <Building2 className="plan-mark-building" size={compact ? 21 : 27} />}
    <Mail className="plan-mark-mail" size={size} />
    {tone === 'freemium' && <Sparkles className="plan-mark-spark" size={compact ? 12 : 16} />}
    {tone === 'premium' && <Crown className="plan-mark-crown" size={compact ? 18 : 23} />}
    {tone === 'corporate' && <Users className="plan-mark-users" size={compact ? 14 : 18} />}
    {tone === 'white-label' && <><Tag className="plan-mark-tag" size={compact ? 17 : 21} /><Paintbrush className="plan-mark-brush" size={compact ? 9 : 12} /></>}
  </span>
}

function subscriptionLabel(status: string) {
  switch (status) {
    case 'active': return 'Suscripción activa'
    case 'trialing': return 'Período de prueba'
    case 'legacy': return 'Acceso heredado'
    case 'past_due': return 'Pago pendiente'
    case 'canceled': return 'Suscripción cancelada'
    case 'expired': return 'Suscripción vencida'
    default: return status
  }
}

function formatDate(value: string | null) {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' })
}

export function PlanPage() {
  const subscription = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const adminStatus = useQuery({ queryKey: ['commercial-admin-status'], queryFn: commercialApi.adminStatus, staleTime: 5 * 60_000, retry: false })

  if (subscription.isLoading) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="commercial-plan-loading"><Sparkles size={18} /> Cargando información del plan…</div></section>
  if (subscription.isError || !subscription.data) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="notice">{subscription.error instanceof Error ? subscription.error.message : 'No fue posible cargar el plan.'}</div></section>

  const data = subscription.data
  const current = data.currentPlan
  const currentTone = planTone(current)
  const usageLimit = data.plans.find(plan => plan.code === data.effectivePlanCode)?.maxAccounts ?? current.maxAccounts
  const usagePercent = usageLimit
    ? Math.min(100, Math.round((data.connectedAccounts / usageLimit) * 100))
    : 0
  const renewalDate = formatDate(data.subscription.currentPeriodEnd)
  const trialEnd = formatDate(data.subscription.trialEndsAt)
  const statusNeedsAttention = !data.paidAccessActive

  return <section className="settings-page commercial-plan-page">
    <div className="commercial-plan-title-row">
      <div><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><p className="page-description">Revise su plan actual, el uso de cuentas y las funciones habilitadas para NexoMail.</p></div>
      {adminStatus.data?.isAdministrator && <Link to="/admin/plans" className="secondary-button"><Settings2 size={16} /> Administrar tipos de cuenta</Link>}
    </div>

    <section className={`commercial-current-plan tone-${currentTone}`}>
      <div className="commercial-current-heading">
        <PlanBrandmark plan={current} compact />
        <div><span>Plan actual</span><strong>{current.name}</strong><small>{current.price} · {current.cadence}</small></div>
      </div>
      <div className="commercial-account-usage">
        <div><span>Cuentas conectadas</span><strong>{data.connectedAccounts}{usageLimit ? ` / ${usageLimit}` : ''}</strong></div>
        {usageLimit && <div className="commercial-usage-track" aria-label={`${usagePercent}% del límite de cuentas utilizado`}><i style={{ width: `${usagePercent}%` }} /></div>}
        <small>{usageLimit ? `${data.remainingAccounts ?? 0} cuenta${data.remainingAccounts === 1 ? '' : 's'} disponible${data.remainingAccounts === 1 ? '' : 's'}` : 'Sin límite fijo de cuentas'}</small>
      </div>
      <div className="commercial-subscription-meta">
        <span className={`commercial-subscription-status ${statusNeedsAttention ? 'attention' : ''}`}>{subscriptionLabel(data.subscription.status)}</span>
        {renewalDate && <small>{data.subscription.cancelAtPeriodEnd ? `Finaliza el ${renewalDate}` : `Próxima renovación: ${renewalDate}`}</small>}
        {trialEnd && <small>Prueba hasta: {trialEnd}</small>}
        {!data.subscription.provider && data.subscription.status === 'legacy' && <small>Acceso de desarrollo previo a la integración de pagos.</small>}
      </div>
      {!data.paidAccessActive && <div className="commercial-limit-notice warning">El estado de la suscripción no habilita actualmente las funciones pagadas. Mientras se regulariza, NexoMail aplica las capacidades del plan Freemium.</div>}
      {!data.canAddAccount && <div className="commercial-limit-notice">Ha alcanzado el límite de cuentas efectivo de su plan. Puede seguir usando las cuentas ya conectadas, pero necesitará un plan superior o regularizar la suscripción para agregar otra.</div>}
      {data.overLimit && <div className="commercial-limit-notice warning">Su cantidad actual de cuentas supera el límite efectivo. Las cuentas existentes se mantienen activas, pero no podrá agregar nuevas hasta cambiar de plan o regularizar la suscripción.</div>}
    </section>

    <div className="commercial-section-heading"><div><span>Planes disponibles</span><h2>Elija el nivel de servicio que necesita</h2></div><Link to="/settings/accounts" className="secondary-button">Administrar cuentas</Link></div>

    <section className="commercial-plan-grid">
      {data.plans.map(plan => {
        const isCurrent = plan.code === current.code
        const tone = planTone(plan)
        return <article className={`commercial-plan-card tone-${tone} ${plan.isFeatured ? 'featured' : ''} ${isCurrent ? 'current' : ''}`} key={plan.code}>
          <header>
            <PlanBrandmark plan={plan} />
            <div><strong>{plan.name}</strong><span>{plan.description}</span></div>
            {isCurrent && <b className="commercial-current-badge">Plan actual</b>}
            {!isCurrent && plan.isFeatured && <b className="commercial-featured-badge">Más elegido</b>}
          </header>
          <div className="commercial-price"><strong>{plan.price}</strong><span>{plan.cadence}</span></div>
          <ul>{plan.features.map(feature => <li key={feature}><Check size={15} /><span>{feature}</span></li>)}</ul>
          <footer>
            {isCurrent
              ? <button type="button" className="secondary-button" disabled>Plan activo</button>
              : plan.code === 'premium'
                ? <button type="button" className="primary-button" disabled title="El checkout se habilitará al conectar la pasarela de pago.">Contratación próximamente</button>
                : <button type="button" className="secondary-button" disabled>{plan.isWhiteLabel ? 'Cotización próximamente' : 'Contratación próximamente'}</button>}
          </footer>
        </article>
      })}
    </section>

    <div className="commercial-next-step"><Sparkles size={17} /><div><strong>Suscripciones y permisos preparados</strong><span>NexoMail ya separa las capacidades por plan y registra el estado de suscripción. El siguiente paso es conectar el proveedor de pagos para activar, renovar, cancelar y regularizar planes automáticamente.</span></div></div>
  </section>
}
