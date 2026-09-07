import type { ReactNode } from 'react'
import { ChevronRight, Clock3, Mail, Send } from 'lucide-react'
import type { ControlCenterAccountActivity, ControlCenterAccountSummary, ControlCenterDay, ControlCenterPendingItem } from '../../types/mail'
import { NexiInsightCard } from '../nexi/NexiInsightCard'
import type { NexiInsightPriority } from '../nexi/nexiInsights'

export type DashboardPriorityItem = { item: ControlCenterPendingItem; automatic: boolean; manual: boolean }

function ageLabel(value: string) {
  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60_000))
  if (elapsedMinutes < 60) return `${Math.max(1, elapsedMinutes)} min`
  const hours = Math.floor(elapsedMinutes / 60)
  if (hours < 24) return `${hours} h`
  const days = Math.floor(hours / 24)
  return `${days} día${days === 1 ? '' : 's'}`
}

function dayLabel(value: string, compact = false) {
  const date = new Date(`${value}T12:00:00`)
  return date.toLocaleDateString('es-CL', compact ? { weekday: 'short' } : { weekday: 'short', day: 'numeric' }).replace('.', '')
}

function totals(activity: ControlCenterDay[]) {
  return activity.reduce((result, day) => ({ received: result.received + day.received, sent: result.sent + day.sent }), { received: 0, sent: 0 })
}

function linePoints(activity: ControlCenterDay[], field: 'received' | 'sent', maximum: number) {
  if (activity.length === 0) return ''
  return activity.map((day, index) => {
    const x = activity.length === 1 ? 500 : 18 + (index / (activity.length - 1)) * 964
    const value = day[field]
    const y = 118 - (value / maximum) * 98
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
}

function sparklinePoints(values: number[]) {
  if (values.length === 0) return ''
  const maximum = Math.max(1, ...values)
  return values.map((value, index) => {
    const x = values.length === 1 ? 50 : (index / (values.length - 1)) * 100
    const y = 24 - (value / maximum) * 20
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
}

export function MetricCard({ tone, icon, value, label, hint, comparison, sparkline, active, onClick }: {
  tone: 'sent' | 'received' | 'overdue' | 'accounts'
  icon: ReactNode
  value: number
  label: string
  hint?: string
  comparison?: string
  sparkline?: number[]
  active?: boolean
  onClick?: () => void
}) {
  const content = <>
    <div className="cc-metric-top"><span className="cc-metric-icon">{icon}</span>{comparison && <small className="cc-metric-comparison">{comparison}</small>}</div>
    <strong className="cc-metric-value">{value}</strong>
    <span className="cc-metric-label">{label}</span>
    {hint && <small className="cc-metric-hint">{hint}</small>}
    {sparkline && sparkline.length > 1 && <svg className="cc-metric-sparkline" viewBox="0 0 100 28" preserveAspectRatio="none" aria-hidden="true"><polyline points={sparklinePoints(sparkline)} /></svg>}
    {onClick && <ChevronRight className="cc-metric-chevron" size={16} />}
  </>
  return onClick
    ? <button type="button" className={`cc-metric-card ${tone} ${active ? 'active' : ''}`} onClick={onClick}>{content}</button>
    : <article className={`cc-metric-card ${tone}`}>{content}</article>
}

export function ActivityChart({ activity, days, loading, comparison, onDaysChange }: {
  activity: ControlCenterDay[]
  days: 7 | 30
  loading?: boolean
  comparison?: string
  onDaysChange: (days: 7 | 30) => void
}) {
  const maximum = Math.max(1, ...activity.flatMap(day => [day.received, day.sent]))
  const receivedLine = linePoints(activity, 'received', maximum)
  const sentLine = linePoints(activity, 'sent', maximum)
  const count = totals(activity)
  return <article className="cc-panel cc-activity-panel">
    <header className="cc-panel-header">
      <div><strong>Actividad de correos</strong><span>{loading ? 'Actualizando…' : `${count.received} recibidos · ${count.sent} enviados${comparison ? ` · ${comparison}` : ''}`}</span></div>
      <div className="cc-period-selector" aria-label="Período de actividad">{([7, 30] as const).map(value => <button type="button" key={value} className={days === value ? 'active' : ''} onClick={() => onDaysChange(value)}>{value} días</button>)}</div>
    </header>
    <div className="cc-chart-wrap">
      {activity.length === 0 ? <div className="cc-chart-empty">Sin actividad disponible para este período.</div> : <>
        <div className="cc-chart-grid" aria-hidden="true"><span /><span /><span /><span /></div>
        <svg className="cc-line-chart" viewBox="0 0 1000 130" preserveAspectRatio="none" role="img" aria-label={`Actividad de correos de los últimos ${days} días`}>
          <polyline className="received" points={receivedLine} />
          <polyline className="sent" points={sentLine} />
        </svg>
        <div className={`cc-chart-labels days-${days}`}>{activity.map((day, index) => <span key={day.date} title={`${day.received} recibidos · ${day.sent} enviados`}>{days === 7 || index % 5 === 0 || index === activity.length - 1 ? dayLabel(day.date, days === 30) : ''}</span>)}</div>
      </>}
    </div>
    <footer className="cc-chart-legend"><span><i className="received" />Recibidos</span><span><i className="sent" />Enviados</span></footer>
  </article>
}

export function PendingEmails({ items, onOpen, onCompose, onViewAll, limit = 7 }: {
  items: DashboardPriorityItem[]
  onOpen: (item: DashboardPriorityItem) => void
  onCompose: (item: ControlCenterPendingItem) => void
  onViewAll: () => void
  limit?: number
}) {
  const visible = items.slice(0, limit)
  return <article className="cc-panel cc-pending-panel">
    <header className="cc-panel-header"><div><strong>Correos que requieren atención</strong><span>{items.length} conversación{items.length === 1 ? '' : 'es'} en seguimiento</span></div>{items.length > limit && <button type="button" className="cc-text-button" onClick={onViewAll}>Ver todos</button>}</header>
    {visible.length === 0 ? <div className="cc-panel-empty">No hay correos pendientes en este momento.</div> : <div className="cc-pending-table">
      <div className="cc-pending-head"><span>Contacto</span><span>Asunto</span><span>Cuenta</span><span>Antigüedad</span><span>Estado</span><span>Prioridad</span><span /></div>
      {visible.map(value => {
        const overdue = Date.now() - new Date(value.item.since).getTime() >= 48 * 60 * 60 * 1000
        return <div className="cc-pending-row" key={`${value.item.accountId}:${value.item.messageId}`}>
          <span className="cc-pending-contact"><i style={{ background: value.item.accountColor }} />{value.item.counterpart}</span>
          <button type="button" className="cc-pending-subject" onClick={() => onOpen(value)}>{value.item.subject || '(Sin asunto)'}</button>
          <span>{value.item.accountName}</span>
          <span>{ageLabel(value.item.since)}</span>
          <span><b className={`cc-status-pill ${value.item.direction}`}>{value.item.direction === 'received' ? 'Responder' : 'Esperando'}</b></span>
          <span><b className={`cc-priority-pill ${overdue ? 'high' : 'normal'}`}>{overdue ? 'Alta' : value.manual ? 'Manual' : 'Normal'}</b></span>
          <button type="button" className="cc-row-action" onClick={() => onCompose(value.item)}>{value.item.direction === 'received' ? 'Responder' : 'Seguimiento'}</button>
        </div>
      })}
    </div>}
  </article>
}

export function RecentActivity({ items, limit = 5 }: { items: DashboardPriorityItem[]; limit?: number }) {
  const recent = [...items].sort((left, right) => new Date(right.item.since).getTime() - new Date(left.item.since).getTime()).slice(0, limit)
  return <article className="cc-panel cc-recent-panel">
    <header className="cc-panel-header"><div><strong>Actividad reciente</strong><span>Últimos movimientos que forman parte del seguimiento actual</span></div></header>
    {recent.length === 0 ? <div className="cc-panel-empty">Sin actividad pendiente reciente.</div> : <div className="cc-event-list">{recent.map(value => <div className="cc-event" key={`${value.item.accountId}:${value.item.messageId}`}><span className={`cc-event-icon ${value.item.direction}`}>{value.item.direction === 'received' ? <Mail size={14} /> : <Send size={14} />}</span><div><strong>{value.item.subject || '(Sin asunto)'}</strong><span>{value.item.direction === 'received' ? 'Recibido de' : 'Enviado a'} {value.item.counterpart}</span></div><time>{ageLabel(value.item.since)}</time></div>)}</div>}
  </article>
}

export function AccountSummary({ accounts, activity, manualTracking }: {
  accounts: ControlCenterAccountSummary[]
  activity: ControlCenterAccountActivity[]
  manualTracking: ControlCenterPendingItem[]
}) {
  const volumes = new Map(activity.map(account => [account.accountId, totals(account.activity)]))
  const maxVolume = Math.max(1, ...accounts.map(account => {
    const volume = volumes.get(account.accountId)
    return (volume?.received ?? 0) + (volume?.sent ?? 0)
  }))
  return <article className="cc-panel cc-account-panel">
    <header className="cc-panel-header"><div><strong>Resumen por cuenta</strong><span>Volumen del período y pendientes actuales</span></div></header>
    <div className="cc-account-list">{accounts.map(account => {
      const volume = volumes.get(account.accountId)
      const total = (volume?.received ?? 0) + (volume?.sent ?? 0)
      const manual = manualTracking.filter(item => item.accountId === account.accountId).length
      return <div className={`cc-account-row ${account.isAvailable ? '' : 'unavailable'}`} key={account.accountId}>
        <div className="cc-account-name"><i style={{ background: account.accountColor }} /><div><strong>{account.accountName}</strong><span>{account.isAvailable ? `${total} correos en el período` : 'Cuenta no disponible'}</span></div></div>
        <div className="cc-account-bar"><i style={{ width: `${Math.max(3, total / maxVolume * 100)}%`, background: account.accountColor }} /></div>
        <div className="cc-account-stats"><span><b>{account.receivedWithoutReply}</b>por responder</span><span><b>{account.sentWithoutResponse}</b>sin respuesta</span><span><b>{manual}</b>seguimientos</span></div>
      </div>
    })}</div>
  </article>
}

export function FollowUpTimeline({ items }: { items: DashboardPriorityItem[] }) {
  const now = Date.now()
  const overdue = items.filter(value => now - new Date(value.item.since).getTime() >= 48 * 60 * 60 * 1000).length
  const today = items.filter(value => now - new Date(value.item.since).getTime() < 24 * 60 * 60 * 1000).length
  const risk = items.filter(value => {
    const age = now - new Date(value.item.since).getTime()
    return age >= 24 * 60 * 60 * 1000 && age < 48 * 60 * 60 * 1000
  }).length
  return <article className="cc-panel cc-followup-panel">
    <header className="cc-panel-header"><div><strong>Próximos seguimientos</strong><span>Fechas futuras aún no están modeladas en backend</span></div></header>
    <div className="cc-timeline">
      <div className="cc-timeline-row overdue"><span /><strong>Vencidos</strong><b>{overdue}</b><small>Más de 48 horas</small></div>
      <div className="cc-timeline-row today"><span /><strong>Hoy</strong><b>{today}</b><small>Actividad de menos de 24 h</small></div>
      <div className="cc-timeline-row risk"><span /><strong>En riesgo</strong><b>{risk}</b><small>Entre 24 y 48 horas</small></div>
      <div className="cc-timeline-row unavailable"><span /><strong>Mañana</strong><b>—</b><small>Sin fecha programada disponible</small></div>
      <div className="cc-timeline-row unavailable"><span /><strong>Próximos días</strong><b>—</b><small>Sin calendario de seguimiento disponible</small></div>
    </div>
  </article>
}

export function InsightCard({ icon, title, description, priority, actionLabel, onAction }: {
  icon: ReactNode
  title: string
  description: string
  priority: NexiInsightPriority
  actionLabel?: string
  onAction?: () => void
}) {
  return <div className="cc-insight-card"><NexiInsightCard icon={icon} title={title} description={description} priority={priority} actionLabel={actionLabel} onAction={onAction} /></div>
}

export { ageLabel, totals }
