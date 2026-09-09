import { useQuery } from '@tanstack/react-query'
import { Building2, Check, CreditCard, Crown, MailPlus, Palette, Settings2, Sparkles } from 'lucide-react'
import { Link } from 'react-router-dom'
import { commercialApi, type CommercialPlan } from '../api/commercialApi'

function planIcon(plan: CommercialPlan) {
  if (plan.isWhiteLabel) return <Palette size={20} />
  if (plan.isCorporate) return <Building2 size={20} />
  if (plan.isFeatured) return <Crown size={20} />
  return <MailPlus size={20} />
}

export function PlanPage() {
  const subscription = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const adminStatus = useQuery({ queryKey: ['commercial-admin-status'], queryFn: commercialApi.adminStatus, staleTime: 5 * 60_000, retry: false })

  if (subscription.isLoading) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="commercial-plan-loading"><Sparkles size={18} /> Cargando información del plan…</div></section>
  if (subscription.isError || !subscription.data) return <section className="settings-page commercial-plan-page"><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><div className="notice">{subscription.error instanceof Error ? subscription.error.message : 'No fue posible cargar el plan.'}</div></section>

  const data = subscription.data
  const current = data.currentPlan
  const usagePercent = current.maxAccounts
    ? Math.min(100, Math.round((data.connectedAccounts / current.maxAccounts) * 100))
    : 0

  return <section className="settings-page commercial-plan-page">
    <div className="commercial-plan-title-row">
      <div><p className="eyebrow">Configuración</p><h1>Plan y uso</h1><p className="page-description">Revise su plan actual, el uso de cuentas y las alternativas disponibles para NexoMail.</p></div>
      {adminStatus.data?.isAdministrator && <Link to="/admin/plans" className="secondary-button"><Settings2 size={16} /> Administrar tipos de cuenta</Link>}
    </div>

    <section className="commercial-current-plan">
      <div className="commercial-current-heading">
        <span className="commercial-plan-icon"><CreditCard size={20} /></span>
        <div><span>Plan actual</span><strong>{current.name}</strong><small>{current.price} · {current.cadence}</small></div>
      </div>
      <div className="commercial-account-usage">
        <div><span>Cuentas conectadas</span><strong>{data.connectedAccounts}{current.maxAccounts ? ` / ${current.maxAccounts}` : ''}</strong></div>
        {current.maxAccounts && <div className="commercial-usage-track" aria-label={`${usagePercent}% del límite de cuentas utilizado`}><i style={{ width: `${usagePercent}%` }} /></div>}
        <small>{current.maxAccounts ? `${data.remainingAccounts ?? 0} cuenta${data.remainingAccounts === 1 ? '' : 's'} disponible${data.remainingAccounts === 1 ? '' : 's'}` : 'Sin límite fijo de cuentas'}</small>
      </div>
      {!data.canAddAccount && <div className="commercial-limit-notice">Ha alcanzado el límite de cuentas de su plan. Puede seguir usando las cuentas ya conectadas, pero necesitará un plan superior para agregar otra.</div>}
      {data.overLimit && <div className="commercial-limit-notice warning">Su cantidad actual de cuentas supera el límite nominal del plan. Las cuentas existentes se mantienen activas, pero no podrá agregar nuevas hasta cambiar de plan.</div>}
    </section>

    <div className="commercial-section-heading"><div><span>Planes disponibles</span><h2>Elija el nivel de servicio que necesita</h2></div><Link to="/settings/accounts" className="secondary-button">Administrar cuentas</Link></div>

    <section className="commercial-plan-grid">
      {data.plans.map(plan => {
        const isCurrent = plan.code === current.code
        return <article className={`commercial-plan-card ${plan.isFeatured ? 'featured' : ''} ${isCurrent ? 'current' : ''}`} key={plan.code}>
          <header>
            <span className="commercial-plan-icon">{planIcon(plan)}</span>
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
                ? <button type="button" className="primary-button" disabled title="La contratación en línea se incorporará en la siguiente etapa.">Contratación próximamente</button>
                : <button type="button" className="secondary-button" disabled>{plan.isWhiteLabel ? 'Cotización próximamente' : 'Contratación próximamente'}</button>}
          </footer>
        </article>
      })}
    </section>

    <div className="commercial-next-step"><Sparkles size={17} /><div><strong>Base comercial preparada</strong><span>El sistema reconoce el plan de cada usuario y aplica el límite de cuentas. Los administradores también pueden gestionar los tipos de cuenta disponibles.</span></div></div>
  </section>
}
