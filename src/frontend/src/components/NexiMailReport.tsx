import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, CalendarDays, CheckCircle2, Inbox, Info, Sparkles } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import { nexiApi, type NexiReportPeriod } from '../api/nexiApi'

function localDateValue() {
  const now = new Date()
  const year = now.getFullYear()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function normalizedPeriod(value: string | null): NexiReportPeriod {
  return value === 'this_week' || value === 'last_week' ? value : 'today'
}

const labels: Record<NexiReportPeriod, string> = {
  today: 'Hoy',
  this_week: 'Esta semana',
  last_week: 'Semana pasada',
}

function reportRangeLabel(period: NexiReportPeriod, localDate: string) {
  const [year, month, day] = localDate.split('-').map(Number)
  const today = new Date(year, month - 1, day)
  const daySinceMonday = (today.getDay() + 6) % 7
  const weekStart = new Date(today)
  weekStart.setDate(today.getDate() - daySinceMonday)

  let start = new Date(today)
  let end = new Date(today)
  if (period === 'this_week') start = weekStart
  if (period === 'last_week') {
    start = new Date(weekStart)
    start.setDate(weekStart.getDate() - 7)
    end = new Date(weekStart)
    end.setDate(weekStart.getDate() - 1)
  }

  const formatter = new Intl.DateTimeFormat('es-CL', { day: 'numeric', month: 'short' })
  const endFormatter = new Intl.DateTimeFormat('es-CL', { day: 'numeric', month: 'short', year: 'numeric' })
  if (start.getTime() === end.getTime()) return endFormatter.format(end).replace(/\./g, '')
  return `${formatter.format(start).replace(/\./g, '')} – ${endFormatter.format(end).replace(/\./g, '')}`
}

export function NexiMailReport() {
  const [params, setParams] = useSearchParams()
  const period = normalizedPeriod(params.get('period'))
  const accountId = params.get('account') ?? ''
  const localDate = useMemo(localDateValue, [])
  const rangeLabel = useMemo(() => reportRangeLabel(period, localDate), [localDate, period])
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const report = useQuery({
    queryKey: ['nexi-mail-report', period, accountId, localDate],
    queryFn: () => nexiApi.report(period, localDate, accountId || undefined),
    staleTime: 10 * 60_000,
    retry: false,
  })

  function updateParam(name: string, value: string | null) {
    const next = new URLSearchParams(params)
    next.set('tab', 'report')
    if (value) next.set(name, value)
    else next.delete(name)
    setParams(next, { replace: true })
  }

  const attentionItems = report.data?.items.filter(item => Boolean(item.requestedAction)) ?? []
  const informationalItems = report.data?.items.filter(item => !item.requestedAction) ?? []

  const renderItem = (item: NonNullable<typeof report.data>['items'][number], index: number) => <article key={`${item.sender}-${item.subject}-${index}`} className="nexi-report-item">
    <div className="nexi-report-item-top">
      <div><strong>{item.sender || 'Remitente'}</strong><span>{item.subject}</span></div>
      <span className={`nexi-importance ${item.importance}`}>{item.importance === 'alta' && <AlertTriangle size={12} />}{item.importance}</span>
    </div>
    <p>{item.summary}</p>
    {item.requestedAction && <div className="nexi-report-request"><CheckCircle2 size={14} /><span><strong>Acción solicitada:</strong> {item.requestedAction}</span></div>}
  </article>

  return <div className="nexi-report-view">
    <section className="nexi-report-toolbar">
      <div className="nexi-report-toolbar-copy">
        <span className="nexi-report-icon"><Sparkles size={18} /></span>
        <div><strong>Síntesis del período</strong><span>Quién escribió, qué necesita y qué conviene atender.</span></div>
      </div>
      <select value={accountId} onChange={event => updateParam('account', event.target.value || null)} aria-label="Cuenta para el informe">
        <option value="">Todas las cuentas</option>
        {(accounts.data ?? []).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}
      </select>
    </section>

    <div className="nexi-report-periods" aria-label="Período del informe">
      {(Object.keys(labels) as NexiReportPeriod[]).map(value => <button key={value} type="button" className={period === value ? 'active' : ''} onClick={() => updateParam('period', value)}><CalendarDays size={14} />{labels[value]}</button>)}
      <span className="nexi-report-range">{rangeLabel}</span>
    </div>

    {report.isLoading && <section className="nexi-report-loading"><span className="reading-skeleton" /><span className="reading-skeleton" /><span className="reading-skeleton" /><small>Nexi está leyendo y organizando los correos del período…</small></section>}
    {report.isError && <div className="notice">{report.error instanceof Error ? report.error.message : 'No fue posible generar el informe.'}</div>}

    {report.data && <>
      <section className="nexi-report-overview">
        <div className="nexi-report-count"><Inbox size={18} /><strong>{report.data.messageCount}</strong><span>correos analizados</span></div>
        <div className="nexi-report-summary"><span>Resumen ejecutivo</span><p>{report.data.summary}</p></div>
      </section>

      {report.data.actions.length > 0 && <section className="nexi-report-actions">
        <header><CheckCircle2 size={16} /><strong>Acciones detectadas</strong></header>
        <ul>{report.data.actions.map((action, index) => <li key={`${action}-${index}`}>{action}</li>)}</ul>
      </section>}

      {attentionItems.length > 0 && <section className="nexi-report-messages">
        <header><AlertTriangle size={16} /><strong>Con acción pendiente</strong><span>{attentionItems.length} · {rangeLabel}</span></header>
        {attentionItems.map(renderItem)}
      </section>}

      {informationalItems.length > 0 && <section className="nexi-report-messages nexi-report-informational">
        <header><Info size={16} /><strong>Sólo informativos</strong><span>{informationalItems.length} · {rangeLabel}</span></header>
        {informationalItems.map(renderItem)}
      </section>}

      {report.data.items.length === 0 && <section className="nexi-report-messages">
        <header><Sparkles size={16} /><strong>Sin detalle adicional</strong><span>{rangeLabel}</span></header>
        <div className="nexi-report-empty">No hay elementos adicionales para mostrar.</div>
      </section>}
    </>}
  </div>
}
