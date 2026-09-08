import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, CalendarDays, CheckCircle2, Inbox, Sparkles } from 'lucide-react'
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

export function NexiMailReport() {
  const [params, setParams] = useSearchParams()
  const period = normalizedPeriod(params.get('period'))
  const accountId = params.get('account') ?? ''
  const localDate = useMemo(localDateValue, [])
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

  return <div className="nexi-report-view">
    <section className="nexi-report-toolbar">
      <div className="nexi-report-toolbar-copy">
        <span className="nexi-report-icon"><Sparkles size={18} /></span>
        <div><strong>Reporte inteligente de Nexi</strong><span>Quién te escribió, qué necesita y qué conviene atender.</span></div>
      </div>
      <select value={accountId} onChange={event => updateParam('account', event.target.value || null)} aria-label="Cuenta para el reporte">
        <option value="">Todas las cuentas</option>
        {(accounts.data ?? []).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}
      </select>
    </section>

    <div className="nexi-report-periods" aria-label="Período del reporte">
      {(Object.keys(labels) as NexiReportPeriod[]).map(value => <button key={value} type="button" className={period === value ? 'active' : ''} onClick={() => updateParam('period', value)}><CalendarDays size={14} />{labels[value]}</button>)}
    </div>

    {report.isLoading && <section className="nexi-report-loading"><span className="reading-skeleton" /><span className="reading-skeleton" /><span className="reading-skeleton" /><small>Nexi está leyendo y organizando los correos del período…</small></section>}
    {report.isError && <div className="notice">{report.error instanceof Error ? report.error.message : 'No fue posible generar el reporte.'}</div>}

    {report.data && <>
      <section className="nexi-report-overview">
        <div className="nexi-report-count"><Inbox size={18} /><strong>{report.data.messageCount}</strong><span>correos analizados</span></div>
        <div className="nexi-report-summary"><span>Nexi resume</span><p>{report.data.summary}</p></div>
      </section>

      {report.data.actions.length > 0 && <section className="nexi-report-actions">
        <header><CheckCircle2 size={16} /><strong>Acciones que requieren atención</strong></header>
        <ul>{report.data.actions.map((action, index) => <li key={`${action}-${index}`}>{action}</li>)}</ul>
      </section>}

      <section className="nexi-report-messages">
        <header><Sparkles size={16} /><strong>Resumen del período</strong><span>{report.data.periodLabel}</span></header>
        {report.data.items.length === 0
          ? <div className="nexi-report-empty">No hay detalles adicionales para mostrar.</div>
          : report.data.items.map((item, index) => <article key={`${item.sender}-${item.subject}-${index}`} className="nexi-report-item">
            <div className="nexi-report-item-top">
              <div><strong>{item.sender || 'Remitente'}</strong><span>{item.subject}</span></div>
              <span className={`nexi-importance ${item.importance}`}>{item.importance === 'alta' && <AlertTriangle size={12} />}{item.importance}</span>
            </div>
            <p>{item.summary}</p>
            {item.requestedAction && <div className="nexi-report-request"><CheckCircle2 size={14} /><span><strong>Qué requiere de ti:</strong> {item.requestedAction}</span></div>}
          </article>)}
      </section>
    </>}
  </div>
}
