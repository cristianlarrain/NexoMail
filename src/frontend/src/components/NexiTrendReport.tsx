import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Activity, ArrowDownRight, ArrowUpRight, Inbox, Minus, Send, Sparkles } from 'lucide-react'
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

function executiveCopy(period: TrendPeriod, current: Totals, previous: Totals) {
  const activity = comparison(current.total, previous.total)
  const periodName = period === 'today' ? 'hoy' : period === 'week' ? 'esta semana' : 'en los últimos 30 días'
  const comparisonName = period === 'today' ? 'ayer' : period === 'week' ? 'la semana pasada' : 'los 30 días anteriores'

  let opening = `El volumen ${periodName} se mantiene igual que ${comparisonName}.`
  if (activity.direction === 'up') opening = activity.percentage === null
    ? `Hay movimiento ${periodName}, mientras que en ${comparisonName} no hubo registros.`
    : `El volumen ${periodName} aumentó ${activity.percentage}% frente a ${comparisonName}.`
  if (activity.direction === 'down') opening = `El volumen ${periodName} disminuyó ${activity.percentage}% frente a ${comparisonName}.`

  const balance = current.received > current.sent
    ? `Predominan los recibidos (${current.received}) sobre los enviados (${current.sent}).`
    : current.sent > current.received
      ? `Predominan los enviados (${current.sent}) sobre los recibidos (${current.received}).`
      : `Recibidos y enviados están equilibrados (${current.received} cada uno).`

  return `${opening} ${balance}`
}

export function NexiTrendReport() {
  const [period, setPeriod] = useState<TrendPeriod>('week')
  const primaryDays: 7 | 14 | 30 = period === 'week' ? 14 : period === 'month' ? 30 : 7

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
    if (!primary.data) return null
    const periods = periodData(period, primary.data.activity, previousMonth.data?.activity ?? [])
    const current = totals(periods.current)
    const previous = totals(periods.previous)
    return {
      ...periods,
      currentTotals: current,
      previousTotals: previous,
      executive: executiveCopy(period, current, previous),
    }
  }, [period, primary.data, previousMonth.data?.activity])

  const loading = primary.isLoading || (period === 'month' && previousMonth.isLoading)
  const error = primary.isError || (period === 'month' && previousMonth.isError)

  return <section className="nexi-trend-report" aria-label="Evolución del correo">
    <header className="nexi-trend-header">
      <div>
        <span className="nexi-trend-kicker"><Sparkles size={14} /> Evolución</span>
        <strong>Cambio entre períodos</strong>
        <p>Compara el volumen actual con el período anterior; el detalle por día y por cuenta se consulta en Volumen por cuenta.</p>
      </div>
      <div className="nexi-trend-periods" aria-label="Período de comparación">
        {(Object.keys(PERIOD_LABELS) as TrendPeriod[]).map(value => <button type="button" key={value} className={period === value ? 'active' : ''} onClick={() => setPeriod(value)}>{PERIOD_LABELS[value]}</button>)}
      </div>
    </header>

    {loading && <div className="nexi-trend-loading" role="status"><span /><span /><span /><small>Comparando períodos…</small></div>}
    {error && <div className="notice">No fue posible construir la comparación.</div>}

    {report && !loading && <>
      <div className="nexi-trend-metrics">
        <article><span className="nexi-trend-icon"><Activity size={17} /></span><div><small>Movimiento total</small><strong>{report.currentTotals.total}</strong><span>{report.previousLabel}: {report.previousTotals.total}</span><TrendArrow current={report.currentTotals.total} previous={report.previousTotals.total} /></div></article>
        <article><span className="nexi-trend-icon"><Inbox size={17} /></span><div><small>Recibidos</small><strong>{report.currentTotals.received}</strong><span>{report.previousLabel}: {report.previousTotals.received}</span><TrendArrow current={report.currentTotals.received} previous={report.previousTotals.received} /></div></article>
        <article><span className="nexi-trend-icon"><Send size={17} /></span><div><small>Enviados</small><strong>{report.currentTotals.sent}</strong><span>{report.previousLabel}: {report.previousTotals.sent}</span><TrendArrow current={report.currentTotals.sent} previous={report.previousTotals.sent} /></div></article>
      </div>

      <article className="nexi-trend-executive">
        <span><Sparkles size={16} /></span>
        <div><small>Lectura de tendencia</small><p>{report.executive}</p></div>
      </article>
    </>}
  </section>
}
