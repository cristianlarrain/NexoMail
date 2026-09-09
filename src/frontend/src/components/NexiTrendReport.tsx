import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, ArrowDownRight, ArrowUpRight, Clock3, Inbox, Minus, Send, Sparkles } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { ControlCenterDay } from '../types/mail'

type TrendPeriod = 'today' | 'week' | 'month'

type Totals = { received: number; sent: number; total: number }

const PERIOD_LABELS: Record<TrendPeriod, string> = {
  today: 'Hoy',
  week: 'Esta semana',
  month: '30 días',
}

function dateKey(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function parseDay(value: string) {
  return new Date(`${value.slice(0, 10)}T12:00:00`)
}

function startOfWeek(value: Date) {
  const result = new Date(value)
  const distance = (result.getDay() + 6) % 7
  result.setDate(result.getDate() - distance)
  result.setHours(0, 0, 0, 0)
  return result
}

function endOfDay(value: Date) {
  const result = new Date(value)
  result.setHours(23, 59, 59, 999)
  return result
}

function filterRange(activity: ControlCenterDay[], start: Date, end: Date) {
  return activity.filter(item => {
    const value = parseDay(item.date)
    return value >= start && value <= end
  })
}

function totals(activity: ControlCenterDay[]): Totals {
  const value = activity.reduce((result, item) => ({
    received: result.received + item.received,
    sent: result.sent + item.sent,
  }), { received: 0, sent: 0 })
  return { ...value, total: value.received + value.sent }
}

function comparison(current: number, previous: number) {
  if (current === previous) return { direction: 'flat' as const, percentage: 0, text: 'Sin variación' }
  if (previous === 0) return { direction: current > 0 ? 'up' as const : 'flat' as const, percentage: current > 0 ? null : 0, text: current > 0 ? 'Sin base anterior' : 'Sin variación' }
  const percentage = Math.round(Math.abs((current - previous) / previous) * 100)
  return {
    direction: current > previous ? 'up' as const : 'down' as const,
    percentage,
    text: `${percentage}% ${current > previous ? 'más' : 'menos'}`,
  }
}

function TrendArrow({ current, previous }: { current: number; previous: number }) {
  const value = comparison(current, previous)
  if (value.direction === 'up') return <span className="nexi-trend-change up"><ArrowUpRight size={14} />{value.text}</span>
  if (value.direction === 'down') return <span className="nexi-trend-change down"><ArrowDownRight size={14} />{value.text}</span>
  return <span className="nexi-trend-change flat"><Minus size={14} />{value.text}</span>
}

function periodData(period: TrendPeriod, primary: ControlCenterDay[], secondary: ControlCenterDay[]) {
  const now = new Date()
  if (period === 'today') {
    const yesterday = new Date(now)
    yesterday.setDate(now.getDate() - 1)
    const current = primary.filter(item => item.date.slice(0, 10) === dateKey(now))
    const previous = primary.filter(item => item.date.slice(0, 10) === dateKey(yesterday))
    return { current, previous, currentLabel: 'Hoy', previousLabel: 'Ayer' }
  }

  if (period === 'week') {
    const currentStart = startOfWeek(now)
    const previousStart = new Date(currentStart)
    previousStart.setDate(currentStart.getDate() - 7)
    const previousEnd = new Date(currentStart)
    previousEnd.setMilliseconds(-1)
    return {
      current: filterRange(primary, currentStart, endOfDay(now)),
      previous: filterRange(primary, previousStart, previousEnd),
      currentLabel: 'Esta semana',
      previousLabel: 'Semana pasada',
    }
  }

  return {
    current: primary,
    previous: secondary,
    currentLabel: 'Últimos 30 días',
    previousLabel: '30 días anteriores',
  }
}

function executiveCopy(period: TrendPeriod, current: Totals, previous: Totals, pending: number, overdue: number) {
  const activity = comparison(current.total, previous.total)
  const periodName = period === 'today' ? 'hoy' : period === 'week' ? 'esta semana' : 'en los últimos 30 días'
  const comparisonName = period === 'today' ? 'ayer' : period === 'week' ? 'la semana pasada' : 'los 30 días anteriores'

  let opening = `La actividad ${periodName} se mantiene igual que ${comparisonName}.`
  if (activity.direction === 'up') opening = activity.percentage === null
    ? `Hay actividad ${periodName}, mientras que en ${comparisonName} no hubo registros.`
    : `La actividad ${periodName} aumentó ${activity.percentage}% frente a ${comparisonName}.`
  if (activity.direction === 'down') opening = `La actividad ${periodName} disminuyó ${activity.percentage}% frente a ${comparisonName}.`

  const balance = current.received > current.sent
    ? `Predominan los recibidos (${current.received}) sobre los enviados (${current.sent}).`
    : current.sent > current.received
      ? `Predominan los enviados (${current.sent}) sobre los recibidos (${current.received}).`
      : `Recibidos y enviados están equilibrados (${current.received} cada uno).`

  const backlog = overdue > 0
    ? `Actualmente hay ${pending} pendientes operativos y ${overdue} superan las 48 horas.`
    : `Actualmente hay ${pending} pendientes operativos y ninguno supera las 48 horas.`

  return `${opening} ${balance} ${backlog}`
}

function dayShortLabel(value: string) {
  return parseDay(value).toLocaleDateString('es-CL', { weekday: 'short', day: 'numeric' }).replace(/\./g, '')
}

export function NexiTrendReport() {
  const [period, setPeriod] = useState<TrendPeriod>('week')
  const primaryDays: 7 | 14 | 30 = period === 'week' ? 14 : period === 'month' ? 30 : 7

  const snapshot = useQuery({
    queryKey: ['control-center', 'all'],
    queryFn: () => mailApi.controlCenter(),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchOnWindowFocus: false,
  })

  const primary = useQuery({
    queryKey: ['control-center-activity', 'all', primaryDays, 0],
    queryFn: () => mailApi.controlCenterActivity(undefined, primaryDays, 0),
    staleTime: 5 * 60_000,
    gcTime: 15 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })

  const previousMonth = useQuery({
    queryKey: ['control-center-activity', 'all', 30, 30],
    queryFn: () => mailApi.controlCenterActivity(undefined, 30, 30),
    enabled: period === 'month',
    staleTime: 5 * 60_000,
    gcTime: 15 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })

  const report = useMemo(() => {
    if (!primary.data || !snapshot.data) return null
    const periods = periodData(period, primary.data.activity, previousMonth.data?.activity ?? [])
    const current = totals(periods.current)
    const previous = totals(periods.previous)
    const pending = snapshot.data.receivedWithoutReply + snapshot.data.sentWithoutResponse
    const chartMax = Math.max(1, ...periods.current.flatMap(item => [item.received, item.sent]))
    return {
      ...periods,
      currentTotals: current,
      previousTotals: previous,
      pending,
      overdue: snapshot.data.overdue,
      unread: snapshot.data.unread,
      chartMax,
      executive: executiveCopy(period, current, previous, pending, snapshot.data.overdue),
    }
  }, [period, primary.data, previousMonth.data?.activity, snapshot.data])

  const loading = primary.isLoading || snapshot.isLoading || (period === 'month' && previousMonth.isLoading)
  const error = primary.isError || snapshot.isError || (period === 'month' && previousMonth.isError)

  return <section className="nexi-trend-report" aria-label="Reportes comparativos y tendencias">
    <header className="nexi-trend-header">
      <div>
        <span className="nexi-trend-kicker"><Sparkles size={14} /> Nexi · Tendencias</span>
        <strong>Comparación de actividad</strong>
        <p>Compara actividad real entre períodos. Los pendientes se muestran como estado actual, porque NexoMail no reconstruye artificialmente un saldo histórico.</p>
      </div>
      <div className="nexi-trend-periods" aria-label="Período de comparación">
        {(Object.keys(PERIOD_LABELS) as TrendPeriod[]).map(value => <button type="button" key={value} className={period === value ? 'active' : ''} onClick={() => setPeriod(value)}>{PERIOD_LABELS[value]}</button>)}
      </div>
    </header>

    {loading && <div className="nexi-trend-loading" role="status"><span /><span /><span /><small>Comparando períodos…</small></div>}
    {error && <div className="notice">No fue posible construir la comparación de actividad.</div>}

    {report && !loading && <>
      <div className="nexi-trend-metrics">
        <article><span className="nexi-trend-icon"><Activity size={17} /></span><div><small>Actividad total</small><strong>{report.currentTotals.total}</strong><span>{report.previousLabel}: {report.previousTotals.total}</span><TrendArrow current={report.currentTotals.total} previous={report.previousTotals.total} /></div></article>
        <article><span className="nexi-trend-icon"><Inbox size={17} /></span><div><small>Recibidos</small><strong>{report.currentTotals.received}</strong><span>{report.previousLabel}: {report.previousTotals.received}</span><TrendArrow current={report.currentTotals.received} previous={report.previousTotals.received} /></div></article>
        <article><span className="nexi-trend-icon"><Send size={17} /></span><div><small>Enviados</small><strong>{report.currentTotals.sent}</strong><span>{report.previousLabel}: {report.previousTotals.sent}</span><TrendArrow current={report.currentTotals.sent} previous={report.previousTotals.sent} /></div></article>
        <article><span className="nexi-trend-icon"><Clock3 size={17} /></span><div><small>Pendientes actuales</small><strong>{report.pending}</strong><span>{report.overdue} con más de 48 h · {report.unread} sin leer</span><em>Estado actual</em></div></article>
      </div>

      <article className="nexi-trend-executive">
        <span><Sparkles size={16} /></span>
        <div><small>Lectura ejecutiva de Nexi</small><p>{report.executive}</p></div>
      </article>

      <div className="nexi-trend-comparison">
        <div className="nexi-trend-comparison-copy">
          <strong>{report.currentLabel}</strong>
          <span>vs. {report.previousLabel}</span>
        </div>
        {(['received', 'sent', 'total'] as const).map(field => {
          const label = field === 'received' ? 'Recibidos' : field === 'sent' ? 'Enviados' : 'Total'
          const current = report.currentTotals[field]
          const previous = report.previousTotals[field]
          const maximum = Math.max(1, current, previous)
          return <div className="nexi-trend-compare-row" key={field}>
            <span>{label}</span>
            <div className="nexi-trend-compare-bars">
              <i className="current"><b style={{ width: `${current / maximum * 100}%` }} /></i>
              <i className="previous"><b style={{ width: `${previous / maximum * 100}%` }} /></i>
            </div>
            <div><strong>{current}</strong><small>{previous}</small></div>
          </div>
        })}
        <div className="nexi-trend-legend"><span><i className="current" />Período actual</span><span><i className="previous" />Período anterior</span></div>
      </div>

      <div className={`nexi-trend-daily-chart ${period}`}>
        <header><strong>Actividad diaria · {report.currentLabel}</strong><span>Recibidos / Enviados</span></header>
        {report.current.length === 0 ? <div className="nexi-trend-no-data">Sin actividad registrada para este período.</div> : <div className="nexi-trend-days" style={{ gridTemplateColumns: `repeat(${report.current.length}, minmax(${period === 'month' ? 8 : 34}px, 1fr))` }}>
          {report.current.map((day, index) => {
            const showLabel = period !== 'month' || index % 5 === 0 || index === report.current.length - 1
            return <div className="nexi-trend-day" key={day.date} title={`${dayShortLabel(day.date)} · ${day.received} recibidos · ${day.sent} enviados`}>
              <div><i className="received" style={{ height: day.received === 0 ? '2px' : `${Math.max(7, day.received / report.chartMax * 100)}%` }} /><i className="sent" style={{ height: day.sent === 0 ? '2px' : `${Math.max(7, day.sent / report.chartMax * 100)}%` }} /></div>
              <span>{showLabel ? dayShortLabel(day.date) : ''}</span>
            </div>
          })}
        </div>}
      </div>
    </>}
  </section>
}
