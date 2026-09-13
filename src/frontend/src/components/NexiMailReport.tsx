import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, BarChart3, CalendarDays, CheckCircle2, FileText, Inbox, Info, Search, Sparkles } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import { nexiApi, type NexiReportPeriod } from '../api/nexiApi'
import { NexiVisual } from './nexi/NexiVisual'

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

function escapeHtml(value: string) {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;')
}

function downloadFile(content: string, type: string, fileName: string) {
  const url = URL.createObjectURL(new Blob([content], { type }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 0)
}

export function NexiMailReport() {
  const navigate = useNavigate()
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

  function openRelated(query: string) {
    const next = new URLSearchParams({ q: query, scope: 'mail' })
    if (accountId) next.set('account', accountId)
    navigate(`/search?${next.toString()}`)
  }

  function openNexiTask(instruction: string) {
    const next = new URLSearchParams({ q: instruction })
    if (accountId) next.set('account', accountId)
    navigate(`/search-action?${next.toString()}`)
  }

  function generateDocument() {
    if (!report.data) return
    const data = report.data
    const actions = data.actions.length > 0
      ? `<h2>Acciones detectadas</h2><ul>${data.actions.map(action => `<li>${escapeHtml(action)}</li>`).join('')}</ul>`
      : '<h2>Acciones detectadas</h2><p>Sin acciones adicionales detectadas.</p>'
    const details = `<h2>Correos analizados</h2><table><thead><tr><th>Remitente</th><th>Asunto</th><th>Importancia</th><th>Acción</th><th>Resumen</th></tr></thead><tbody>${data.items.map(item => `<tr><td>${escapeHtml(item.sender || 'Remitente')}</td><td>${escapeHtml(item.subject)}</td><td>${escapeHtml(item.importance)}</td><td>${escapeHtml(item.requestedAction ?? '')}</td><td>${escapeHtml(item.summary)}</td></tr>`).join('')}</tbody></table>`
    const html = `<!doctype html><html><head><meta charset="utf-8"><title>Informe de correo</title><style>body{font-family:Arial,sans-serif;color:#172126;line-height:1.45;margin:36px}h1{font-size:24px;margin:0 0 4px}h2{font-size:16px;margin-top:24px}p.meta{color:#66747b;margin-top:0}table{width:100%;border-collapse:collapse;font-size:11px}th,td{border:1px solid #d9e0e3;padding:7px;text-align:left;vertical-align:top}th{background:#f3f6f7}</style></head><body><h1>Nexi Control Center · Informe de correo</h1><p class="meta">${escapeHtml(rangeLabel)} · ${data.messageCount} correos analizados</p>${actions}${details}</body></html>`
    downloadFile(html, 'application/msword;charset=utf-8', `nexi-informe-${localDate}.doc`)
  }

  const attentionItems = report.data?.items.filter(item => Boolean(item.requestedAction)) ?? []
  const informationalItems = report.data?.items.filter(item => !item.requestedAction) ?? []
  const importance = report.data?.items.reduce((result, item) => {
    result[item.importance] += 1
    return result
  }, { alta: 0, media: 0, baja: 0 }) ?? { alta: 0, media: 0, baja: 0 }
  const importanceMaximum = Math.max(1, importance.alta, importance.media, importance.baja)

  const renderItem = (item: NonNullable<typeof report.data>['items'][number], index: number) => <article key={`${item.sender}-${item.subject}-${index}`} className="nexi-report-item">
    <div className="nexi-report-item-top">
      <div><strong>{item.sender || 'Remitente'}</strong><span>{item.subject}</span></div>
      <span className={`nexi-importance ${item.importance}`}>{item.importance === 'alta' && <AlertTriangle size={12} />}{item.importance}</span>
    </div>
    <p>{item.summary}</p>
    {item.requestedAction && <div className="nexi-report-request"><CheckCircle2 size={14} /><span><strong>Acción solicitada:</strong> {item.requestedAction}</span></div>}
    {item.requestedAction && <div className="nexi-report-row-actions">
      <button type="button" className="secondary-button compact-action" onClick={() => openRelated(`${item.sender} ${item.subject}`)}><Search size={13} /> Ver correos</button>
      <button type="button" className="primary-button compact-action" onClick={() => openNexiTask(`Prepara una respuesta para el correo de ${item.sender} con asunto ${item.subject}. Acción pendiente: ${item.requestedAction}`)}><Sparkles size={13} /> Preparar respuesta con IA</button>
    </div>}
  </article>

  return <div className="nexi-report-view">
    <div className="nexi-report-periods" aria-label="Controles del informe">
      {(Object.keys(labels) as NexiReportPeriod[]).map(value => <button key={value} type="button" className={period === value ? 'active' : ''} onClick={() => updateParam('period', value)}><CalendarDays size={14} />{labels[value]}</button>)}
      <span className="nexi-report-range">{rangeLabel}</span>
      <label className="nexi-report-account-select">
        <span>Cuenta</span>
        <select value={accountId} onChange={event => updateParam('account', event.target.value || null)} aria-label="Cuenta para el informe">
          <option value="">Todas las cuentas</option>
          {(accounts.data ?? []).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}
        </select>
      </label>
    </div>

    {report.isLoading && <section className="nexi-report-loading nexi-integrated-loading"><NexiVisual size="small" className="nexi-inline-processing" /><small>Analizando correos…</small></section>}
    {report.isError && <div className="notice">{report.error instanceof Error ? report.error.message : 'No fue posible generar el informe.'}</div>}

    {report.data && <>
      <section className="nexi-report-overview">
        <div className="nexi-report-count"><Inbox size={18} /><strong>{report.data.messageCount}</strong><span>correos analizados</span></div>
        <div className="nexi-report-summary"><span>Resumen ejecutivo</span><p>{report.data.summary}</p><small>Vista interna · no descargable</small></div>
      </section>

      <section className="nexi-report-visual-statistics" aria-label="Resumen estadístico">
        <header><div><BarChart3 size={16} /><strong>Resumen estadístico</strong></div><span>Vista interna · no descargable</span></header>
        <div className="nexi-report-stat-cards">
          <div><span>Analizados</span><strong>{report.data.messageCount}</strong></div>
          <div><span>Acciones</span><strong>{report.data.actions.length}</strong></div>
          <div><span>Prioridad alta</span><strong>{importance.alta}</strong></div>
          <div><span>Prioridad media</span><strong>{importance.media}</strong></div>
          <div><span>Prioridad baja</span><strong>{importance.baja}</strong></div>
        </div>
        <div className="nexi-report-stat-bars" aria-label="Distribución por importancia">
          {(['alta', 'media', 'baja'] as const).map(level => <div key={level} className={`nexi-report-stat-bar ${level}`}>
            <span>{level === 'alta' ? 'Alta' : level === 'media' ? 'Media' : 'Baja'}</span>
            <i><b style={{ width: `${importance[level] === 0 ? 0 : Math.max(8, importance[level] / importanceMaximum * 100)}%` }} /></i>
            <strong>{importance[level]}</strong>
          </div>)}
        </div>
      </section>

      <section className="nexi-report-export-actions" aria-label="Generar documento del informe">
        <button type="button" className="secondary-button" onClick={generateDocument}><FileText size={15} /> Generar documento</button>
      </section>

      {report.data.actions.length > 0 && <section className="nexi-report-actions">
        <header><CheckCircle2 size={16} /><strong>Acciones detectadas</strong></header>
        <div className="nexi-report-action-grid">
          {report.data.actions.map((action, index) => <article key={`${action}-${index}`} className="nexi-report-action-card">
            <p>{action}</p>
            <div>
              <button type="button" className="secondary-button compact-action" onClick={() => openRelated(action)}><Search size={13} /> Ver correos</button>
              <button type="button" className="primary-button compact-action" onClick={() => openNexiTask(`Revisa los correos relacionados y prepara la acción siguiente: ${action}`)}><Sparkles size={13} /> Preparar con IA</button>
            </div>
          </article>)}
        </div>
      </section>}

      {attentionItems.length > 0 && <section className="nexi-report-messages">
        <header><AlertTriangle size={16} /><strong>Con acción pendiente</strong><span>{attentionItems.length} · {rangeLabel}</span></header>
        {attentionItems.map(renderItem)}
      </section>}

      {informationalItems.length > 0 && <section className="nexi-report-messages nexi-report-informational">
        <header><Info size={16} /><strong>Correos analizados</strong><span>{informationalItems.length} · {rangeLabel}</span></header>
        {informationalItems.map(renderItem)}
      </section>}

      {report.data.items.length === 0 && <section className="nexi-report-messages">
        <header><Sparkles size={16} /><strong>Sin detalle adicional</strong><span>{rangeLabel}</span></header>
        <div className="nexi-report-empty">No hay elementos adicionales para mostrar.</div>
      </section>}
    </>}
  </div>
}
