import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, Bot, CircleAlert, Clock3, Inbox, Mail, Radio, Send, Users } from 'lucide-react'
import { mailApi } from '../../api/mailApi'
import type { ControlCenterAccountActivity, ControlCenterDay, ControlCenterPendingItem, ControlCenterSnapshot } from '../../types/mail'
import { NexiVisual } from '../nexi/NexiVisual'
import { buildNexiInsights, type NexiInsightAction } from '../nexi/nexiInsights'
import { AccountSummary, ActivityChart, FollowUpTimeline, InsightCard, MetricCard, PendingEmails, RecentActivity, totals, type DashboardPriorityItem } from './DashboardParts'

export type ControlCenterVariant = 'cinematic' | 'enterprise' | 'minimal'
export type ControlCenterManagementView = 'received' | 'sent' | 'overdue' | null

const STORAGE_KEY = 'nexomail.controlCenterView'
const variantCopy: Record<ControlCenterVariant, { label: string; subtitle: string }> = {
  cinematic: { label: 'Cinematográfica', subtitle: 'Visión global de su comunicación. Todo en movimiento.' },
  enterprise: { label: 'Corporativa', subtitle: 'Vista general de su operación de correo. Todo lo importante, en un solo lugar.' },
  minimal: { label: 'Minimalista', subtitle: 'Su comunicación, en un solo lugar.' },
}

function initialVariant(): ControlCenterVariant {
  if (typeof window === 'undefined') return 'enterprise'
  const saved = window.localStorage.getItem(STORAGE_KEY)
  return saved === 'cinematic' || saved === 'enterprise' || saved === 'minimal' ? saved : 'enterprise'
}

function messageKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.messageId}`
}

function percentChange(current: number, previous: number) {
  if (previous <= 0) return undefined
  const value = Math.round((current - previous) / previous * 100)
  return `${value > 0 ? '+' : ''}${value}% vs período anterior`
}

function insightIcon(action?: NexiInsightAction) {
  if (action === 'received') return <Inbox size={15} />
  if (action === 'sent') return <Send size={15} />
  if (action === 'unread') return <Mail size={15} />
  if (action === 'overdue' || action === 'tracking') return <Clock3 size={15} />
  return <Bot size={15} />
}

function AccountDistribution({ activity }: { activity: ControlCenterAccountActivity[] }) {
  const rows = activity.map(account => ({ account, count: totals(account.activity).received + totals(account.activity).sent })).filter(row => row.account.isAvailable)
  const total = rows.reduce((sum, row) => sum + row.count, 0)
  let cursor = 0
  const gradient = total > 0 ? rows.map(row => {
    const start = cursor
    cursor += row.count / total * 100
    return `${row.account.accountColor} ${start.toFixed(2)}% ${cursor.toFixed(2)}%`
  }).join(', ') : 'var(--cc-border) 0% 100%'
  return <article className="cc-panel cc-distribution-panel">
    <header className="cc-panel-header"><div><strong>Distribución por cuenta</strong><span>Volumen real del período seleccionado</span></div></header>
    <div className="cc-donut-layout">
      <div className="cc-donut" style={{ background: `conic-gradient(${gradient})` }}><span><b>{total}</b><small>correos</small></span></div>
      <div className="cc-donut-legend">{rows.map(row => <div key={row.account.accountId}><i style={{ background: row.account.accountColor }} /><span>{row.account.accountName}</span><b>{row.count}</b></div>)}</div>
    </div>
  </article>
}

function ResponseTimePanel({ pending }: { pending: ControlCenterPendingItem[] }) {
  const now = Date.now()
  const within = pending.filter(item => now - new Date(item.since).getTime() < 24 * 60 * 60 * 1000).length
  const risk = pending.filter(item => {
    const age = now - new Date(item.since).getTime()
    return age >= 24 * 60 * 60 * 1000 && age < 48 * 60 * 60 * 1000
  }).length
  const outside = pending.filter(item => now - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000).length
  const total = Math.max(1, within + risk + outside)
  const first = within / total * 100
  const second = first + risk / total * 100
  const gradient = `conic-gradient(var(--cc-success) 0% ${first}%, var(--cc-warning) ${first}% ${second}%, var(--cc-danger) ${second}% 100%)`
  return <article className="cc-panel cc-response-panel">
    <header className="cc-panel-header"><div><strong>Tiempo de respuesta</strong><span>Antigüedad actual de conversaciones pendientes; no corresponde a un SLA</span></div></header>
    <div className="cc-response-layout">
      <div className="cc-donut cc-response-donut" style={{ background: gradient }}><span><b>{pending.length}</b><small>pendientes</small></span></div>
      <div className="cc-response-legend"><span><i className="good" /><b>{within}</b> Dentro de 24 h</span><span><i className="risk" /><b>{risk}</b> En riesgo 24–48 h</span><span><i className="late" /><b>{outside}</b> Fuera de 48 h</span></div>
    </div>
  </article>
}

function TrafficPanel({ activity, accounts, latest, generatedAt }: {
  activity: ControlCenterDay[]
  accounts: ControlCenterAccountActivity[]
  latest?: DashboardPriorityItem
  generatedAt: string
}) {
  const volume = totals(activity)
  const activeAccounts = accounts.filter(account => {
    const count = totals(account.activity)
    return account.isAvailable && count.received + count.sent > 0
  }).length
  const lastDay = activity[activity.length - 1]
  const processed = (lastDay?.received ?? 0) + (lastDay?.sent ?? 0)
  const updated = new Date(generatedAt).toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })
  return <article className="cc-panel cc-traffic-panel">
    <header className="cc-panel-header"><div><strong>Actividad en tiempo real</strong><span>Últimos datos sincronizados disponibles; sin geolocalización ficticia</span></div><Radio size={16} /></header>
    <div className="cc-traffic-grid">
      <div><Activity size={16} /><span>Correos del último día</span><b>{processed}</b></div>
      <div><Users size={16} /><span>Cuentas con actividad</span><b>{activeAccounts}</b></div>
      <div className="wide"><Clock3 size={16} /><span>Último movimiento en seguimiento</span><b>{latest ? latest.item.subject : 'Sin movimientos pendientes'}</b></div>
      <div className="wide"><Radio size={16} /><span>Último análisis</span><b>{updated} · {volume.received + volume.sent} correos en el período</b></div>
    </div>
  </article>
}

export function ControlCenterVariantDashboard({ data, manualTracking, accountId, accountName, activeView, onMetricSelect, onUnread, onOpenPending, onCompose, onViewAllPriority, onInsightAction }: {
  data: ControlCenterSnapshot
  manualTracking: ControlCenterPendingItem[]
  accountId?: string
  accountName?: string
  activeView: ControlCenterManagementView
  onMetricSelect: (view: Exclude<ControlCenterManagementView, null>) => void
  onUnread: () => void
  onOpenPending: (value: DashboardPriorityItem) => void
  onCompose: (item: ControlCenterPendingItem) => void
  onViewAllPriority: () => void
  onInsightAction: (action?: NexiInsightAction) => void
}) {
  const [variant, setVariant] = useState<ControlCenterVariant>(initialVariant)
  const [days, setDays] = useState<7 | 30>(7)

  const activityQuery = useQuery({
    queryKey: ['control-center-activity', accountId ?? 'all', days, 0],
    queryFn: () => mailApi.controlCenterActivity(accountId, days, 0),
    staleTime: 5 * 60_000,
    gcTime: 15 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })
  const previousActivityQuery = useQuery({
    queryKey: ['control-center-activity', accountId ?? 'all', days, days],
    queryFn: () => mailApi.controlCenterActivity(accountId, days, days),
    staleTime: 5 * 60_000,
    gcTime: 15 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })

  const priorityItems = useMemo<DashboardPriorityItem[]>(() => {
    const merged = new Map<string, DashboardPriorityItem>()
    data.pendingItems.forEach(item => merged.set(messageKey(item), { item, automatic: true, manual: false }))
    manualTracking.forEach(item => {
      const key = messageKey(item)
      const current = merged.get(key)
      if (current) merged.set(key, { ...current, manual: true })
      else merged.set(key, { item, automatic: false, manual: true })
    })
    return [...merged.values()].sort((left, right) => new Date(left.item.since).getTime() - new Date(right.item.since).getTime())
  }, [data.pendingItems, manualTracking])

  const activity = activityQuery.data?.activity ?? data.activity
  const activityAccounts = activityQuery.data?.accounts ?? data.accounts.map(account => ({
    accountId: account.accountId,
    accountName: account.accountName,
    accountColor: account.accountColor,
    isAvailable: account.isAvailable,
    activity: [],
  }))
  const currentTotals = totals(activity)
  const previousTotals = totals(previousActivityQuery.data?.activity ?? [])
  const activityComparison = percentChange(currentTotals.received + currentTotals.sent, previousTotals.received + previousTotals.sent)
  const activeAccounts = data.accounts.filter(account => account.isAvailable).length
  const activeAccountsSparkline = activity.map(day => activityAccounts.filter(account => {
    const accountDay = account.activity.find(value => value.date === day.date)
    return account.isAvailable && Boolean(accountDay && accountDay.received + accountDay.sent > 0)
  }).length)
  const latestPriority = [...priorityItems].sort((left, right) => new Date(right.item.since).getTime() - new Date(left.item.since).getTime())[0]
  const insights = buildNexiInsights(data, manualTracking, 3)
  const scope = accountId ? accountName ?? 'Esta cuenta' : 'Todas las cuentas'
  const subtitle = variantCopy[variant].subtitle

  function selectVariant(value: ControlCenterVariant) {
    setVariant(value)
    window.localStorage.setItem(STORAGE_KEY, value)
  }

  return <div className={`cc-variant cc-${variant}`} data-control-view={variant}>
    <header className="cc-hero">
      <div className="cc-hero-copy"><span className="cc-kicker">Centro de Control · {scope}</span><h2 id="control-center-title">Centro de Control</h2><p>{subtitle}</p></div>
      <div className="cc-view-switcher"><label htmlFor="control-center-view">Vista</label><select id="control-center-view" value={variant} onChange={event => selectVariant(event.target.value as ControlCenterVariant)}>{(Object.entries(variantCopy) as [ControlCenterVariant, { label: string; subtitle: string }][]).map(([value, copy]) => <option value={value} key={value}>{copy.label}</option>)}</select><small>Selector temporal de prueba</small></div>
    </header>

    <section className="cc-metrics" aria-label="Indicadores principales">
      <MetricCard tone="sent" icon={<Send size={18} />} value={data.sentWithoutResponse} label="Correos enviados sin respuesta" hint="Usted escribió al final" active={activeView === 'sent'} onClick={() => onMetricSelect('sent')} />
      <MetricCard tone="received" icon={<Inbox size={18} />} value={data.receivedWithoutReply} label="Correos recibidos sin responder" hint="La otra persona escribió al final" active={activeView === 'received'} onClick={() => onMetricSelect('received')} />
      <MetricCard tone="overdue" icon={<CircleAlert size={18} />} value={data.overdue} label="Seguimientos vencidos" hint="Pendientes de más de 48 horas" active={activeView === 'overdue'} onClick={() => onMetricSelect('overdue')} />
      <MetricCard tone="accounts" icon={<Users size={18} />} value={activeAccounts} label="Cuentas activas" hint={`${data.accounts.length} cuenta${data.accounts.length === 1 ? '' : 's'} conectada${data.accounts.length === 1 ? '' : 's'}`} sparkline={activeAccountsSparkline} />
    </section>

    <section className="cc-dashboard-grid">
      <ActivityChart activity={activity} days={days} loading={activityQuery.isFetching} comparison={activityComparison} onDaysChange={setDays} />
      <AccountDistribution activity={activityAccounts} />
      <TrafficPanel activity={activity} accounts={activityAccounts} latest={latestPriority} generatedAt={data.generatedAt} />
      <ResponseTimePanel pending={data.pendingItems} />
      <AccountSummary accounts={data.accounts} activity={activityAccounts} manualTracking={manualTracking} />
      <PendingEmails items={priorityItems} onOpen={onOpenPending} onCompose={onCompose} onViewAll={onViewAllPriority} />
      <RecentActivity items={priorityItems} />
      <FollowUpTimeline items={priorityItems} />
      <article className="cc-panel cc-insights-panel">
        <header className="cc-panel-header"><div className="cc-insights-title"><NexiVisual size="small" /><div><strong>Insights IA</strong><span>Arquitectura preparada para IA; recomendaciones actuales basadas en reglas y datos reales</span></div></div></header>
        <div className="cc-insight-list">{insights.map(insight => <InsightCard key={insight.id} icon={insightIcon(insight.action)} title={insight.title} description={insight.description} priority={insight.priority} actionLabel={insight.actionLabel} onAction={insight.action ? () => onInsightAction(insight.action) : undefined} />)}</div>
      </article>
    </section>

    <div className="cc-data-note"><Bot size={14} /><span>Todos los indicadores visibles usan datos reales de NexoMail. Las fechas futuras de seguimiento todavía no existen en el backend y se muestran como no disponibles, sin datos ficticios.</span>{data.unread > 0 && <button type="button" onClick={onUnread}>Ver {data.unread} sin leer</button>}</div>
  </div>
}
