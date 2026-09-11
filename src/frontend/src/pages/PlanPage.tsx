import { useEffect } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Building2, Check, Crown, ExternalLink, Mail, Paintbrush, Settings2, Sparkles, Tag, Users } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
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
    case 'pending': return 'Activación pendiente'
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

function isSelfServicePlan(plan: CommercialPlan) {
  return plan.code !== 'freemium' && !plan.isCorporate && !plan.isWhiteLabel
}

export function PlanPage() {
  const [searchParams] = useSearchParams()
  const returnedFromBilling = searchParams.get('billing') === 'return'
  const subscription = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const billing = useQuery({ queryKey: ['commercial-billing-status'], queryFn: commercialApi.billingStatus, staleTime: 60_000, retry: false })
  const adminStatus = useQuery({ queryKey: ['commercial-admin-status'], queryFn: commercialApi.adminStatus, staleTime: 5 * 60_000, retry: false })
  const checkout = useMutation({
    mutationFn: (planCode: string) => commercialApi.checkout(planCode),
    onSuccess: result => window.location.assign(result.checkoutUrl),
  })

  useEffect(() => {
    if (!returnedFromBilling) return
    void subscription.refetch()
    const first = window.setTimeout(() => void subscription.refetch(), 2_500)
    const second = window.setTimeout(() => void subscription.refetch(), 7_500)
    return () => { window.clearTimeout(first); window.clearTimeout(second) }
  }, [returnedFromBilling]) // eslint-disable-line react-hooks/exhaustive-deps

  if (subscription.isLoading) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="commercial-plan-loading"><Sparkles size={18} /> Cargando información del plan…</div></section>
  if (subscription.isError || !subscription.data) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="notice">{subscription.error instanceof Error ? subscription.error.message : 'No fue posible cargar el plan.'}</div></section>

  const data = subscription.data
  const isOwner = data.effectivePlanCode === 'owner'
  const current = data.currentPlan
  const currentTone: PlanTone = isOwner ? 'white-label' : planTone(current)
  const usageLimit = isOwner ? null : data.plans.find(plan => plan.code === data.effectivePlanCode)?.maxAccounts ?? current.maxAccounts
  const usagePercent = usageLimit
    ? Math.min(100, Math.round((data.connectedAccounts / usageLimit) * 100))
    : 0
  const renewalDate = formatDate(data.subscription.currentPeriodEnd)
  const trialEnd = formatDate(data.subscription.trialEndsAt)
  const statusNeedsAttention = !isOwner && !data.paidAccessActive
  const paymentReady = billing.data?.configured === true && billing.data?.webhookConfigured === true

  return <section className="settings-page commercial-plan-page">
    <div className="commercial-plan-title-row">
      <div><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><p className="page-description">Revise su plan actual, el uso de cuentas y las funciones habilitadas para NexoMail.</p></div>
      {adminStatus.data?.isAdministrator && <div className="commercial-admin-header-actions"><Link to="/admin/users" className="secondary-button"><Users size={16} /> Administrar usuarios</Link><Link to="/admin/plans" className="secondary-button"><Settings2 size={16} /> Administrar tipos de cuenta</Link></div>}
    </div>

    {!isOwner && returnedFromBilling && <div className="commercial-billing-return"><Sparkles size={17} /><div><strong>Estamos verificando su suscripción</strong><span>La activación se refleja automáticamente cuando Mercado Pago confirma el estado del cobro.</span></div></div>}
    {!isOwner && checkout.isError && <div className="notice">{checkout.error instanceof Error ? checkout.error.message : 'No fue posible iniciar la contratación.'}</div>}

    <section className={`commercial-current-plan tone-${currentTone}`}>
      <div className="commercial-current-heading">
        {isOwner
          ? <span className="commercial-plan-brandmark tone-white-label compact" aria-hidden="true"><Crown className="plan-mark-crown" size={20} /></span>
          : <PlanBrandmark plan={current} compact />}
        {isOwner
          ? <div><span>Acceso interno</span><strong>Owner / Administrador general</strong><small>Sin vencimiento · independiente de planes comerciales</small></div>
          : <div><span>Plan actual</span><strong>{current.name}</strong><small>{current.price} · {current.cadence}</small></div>}
      </div>
      <div className="commercial-account-usage">
        <div><span>Cuentas conectadas</span><strong>{data.connectedAccounts}{usageLimit ? ` / ${usageLimit}` : ''}</strong></div>
        {usageLimit && <div className="commercial-usage-track" aria-label={`${usagePercent}% del límite de cuentas utilizado`}><i style={{ width: `${usagePercent}%` }} /></div>}
        <small>{usageLimit ? `${data.remainingAccounts ?? 0} cuenta${data.remainingAccounts === 1 ? '' : 's'} disponible${data.remainingAccounts === 1 ? '' : 's'}` : 'Sin límite fijo de cuentas'}</small>
      </div>
      <div className="commercial-subscription-meta">
        {isOwner
          ? <><span className="commercial-subscription-status">Acceso interno protegido</span><small>No depende de Mercado Pago, vencimientos ni estado de suscripción.</small></>
          : <>
              <span className={`commercial-subscription-status ${statusNeedsAttention ? 'attention' : ''}`}>{subscriptionLabel(data.subscription.status)}</span>
              {renewalDate && <small>{data.subscription.cancelAtPeriodEnd ? `Finaliza el ${renewalDate}` : `Próxima renovación: ${renewalDate}`}</small>}
              {trialEnd && <small>Prueba hasta: {trialEnd}</small>}
              {!data.subscription.provider && data.subscription.status === 'legacy' && <small>Acceso previo a la integración de pagos.</small>}
              {data.subscription.provider === 'mercadopago' && <small>Pago recurrente mediante Mercado Pago.</small>}
              {data.subscription.provider === 'admin' && <small>Plan asignado manualmente por administración.</small>}
            </>}
      </div>
      {!isOwner && !data.paidAccessActive && <div className="commercial-limit-notice warning">El estado de la suscripción no habilita actualmente las funciones pagadas. Mientras se regulariza, NexoMail aplica las capacidades del plan Freemium.</div>}
      {!isOwner && !data.canAddAccount && <div className="commercial-limit-notice">Ha alcanzado el límite de cuentas efectivo de su plan. Puede seguir usando las cuentas ya conectadas, pero necesitará un plan superior o regularizar la suscripción para agregar otra.</div>}
      {!isOwner && data.overLimit && <div className="commercial-limit-notice warning">Su cantidad actual de cuentas supera el límite efectivo. Las cuentas existentes se mantienen activas, pero no podrá agregar nuevas hasta cambiar de plan o regularizar la suscripción.</div>}
    </section>

    <div className="commercial-section-heading"><div><span>Planes disponibles</span><h2>{isOwner ? 'Planes comerciales administrados por NexoMail' : 'Elija el nivel de servicio que necesita'}</h2></div><Link to="/settings/accounts" className="secondary-button">Administrar cuentas</Link></div>

    <section className="commercial-plan-grid">
      {data.plans.map(plan => {
        const isCurrent = !isOwner && plan.code === current.code
        const tone = planTone(plan)
        const selfService = isSelfServicePlan(plan)
        const regularize = !isOwner && isCurrent && !data.paidAccessActive && selfService
        const pendingSamePlan = !isOwner && isCurrent && data.subscription.status === 'pending' && data.subscription.provider === 'mercadopago'
        const canCheckout = !isOwner && selfService && paymentReady && (!isCurrent || regularize)
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
            {isOwner
              ? <button type="button" className="secondary-button" disabled>No aplica al Owner</button>
              : pendingSamePlan
                ? <button type="button" className="secondary-button" disabled>Confirmación de pago pendiente</button>
                : isCurrent && !regularize
                  ? <button type="button" className="secondary-button" disabled>Plan activo</button>
                  : plan.code === 'freemium'
                    ? <button type="button" className="secondary-button" disabled>Plan gratuito</button>
                    : plan.isCorporate || plan.isWhiteLabel
                      ? <button type="button" className="secondary-button" disabled>{plan.isWhiteLabel ? 'Cotización personalizada' : 'Contratación administrada'}</button>
                      : <button type="button" className="primary-button" disabled={!canCheckout || checkout.isPending} onClick={() => checkout.mutate(plan.code)} title={!paymentReady ? 'Configure Mercado Pago para habilitar la contratación.' : undefined}>{checkout.isPending ? 'Abriendo pago…' : regularize ? 'Regularizar suscripción' : <><ExternalLink size={15} /> Contratar con Mercado Pago</>}</button>}
          </footer>
        </article>
      })}
    </section>

    <div className="commercial-next-step"><Sparkles size={17} /><div><strong>{isOwner ? 'Acceso interno protegido' : paymentReady ? 'Pagos recurrentes disponibles' : 'Integración de pagos preparada'}</strong><span>{isOwner ? 'El Owner conserva todas las capacidades, sin límite comercial de cuentas y sin depender del estado de una suscripción.' : paymentReady ? 'Premium puede contratarse mediante Mercado Pago. Las confirmaciones actualizan automáticamente la suscripción y las funciones habilitadas.' : 'La lógica de suscripción, checkout y webhooks está incorporada. Falta configurar las credenciales privadas de Mercado Pago en el entorno para habilitar cobros reales.'}</span></div></div>
  </section>
}
