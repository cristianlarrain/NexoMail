import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, TrendingUp } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { ControlCenterAccountActivity, ControlCenterAccountSummary, ControlCenterDay } from '../types/mail'

type ActivityDays = 7 | 14 | 30

function dayLabel(value: string) {
  const date = new Date(`${value}T12:00:00`)
  return date.toLocaleDateString('es-CL', { weekday: 'short', day: 'numeric' }).replace('.', '')
}

function fullDayLabel(value: string) {
  const date = new Date(`${value}T12:00:00`)
  return date.toLocaleDateString('es-CL', { weekday: 'long', day: 'numeric', month: 'short' }).replace('.', '')
}

function rangeLabel(startDate?: string, endDate?: string) {
  if (!startDate || !endDate) return ''
  const start = new Date(`${startDate}T12:00:00`)
  const end = new Date(`${endDate}T12:00:00`)
  const format = (date: Date) => date.toLocaleDateString('es-CL', { day: 'numeric', month: 'short', year: date.getFullYear() !== new Date().getFullYear() ? 'numeric' : undefined }).replace('.', '')
  return `${format(start)} – ${format(end)}`
}

function totals(activity: ControlCenterDay[]) {
  return activity.reduce((result, day) => ({ received: result.received + day.received, sent: result.sent + day.sent }), { received: 0, sent: 0 })
}

function linePoints(activity: ControlCenterDay[], field: 'received' | 'sent', maximum: number) {
  if (activity.length === 0) return ''
  return activity.map((day, index) => {
    const x = activity.length === 1 ? 500 : 20 + (index / (activity.length - 1)) * 960
    const value = day[field]
    const y = 110 - (value / maximum) * 100
    return `${x.toFixed(1)},${y.toFixed(1)}`
  }).join(' ')
}

export function ControlCenterActivity({ accountId, accounts }: { accountId?: string; accounts: ControlCenterAccountSummary[] }) {
  const [days, setDays] = useState<ActivityDays>(7)
  const [offsetDays, setOffsetDays] = useState(0)
  const [selectedSeries, setSelectedSeries] = useState(accountId ?? 'all')

  useEffect(() => {
    setOffsetDays(0)
    setSelectedSeries(accountId ?? 'all')
  }, [accountId])

  const activityQuery = useQuery({
    queryKey: ['control-center-activity', accountId ?? 'all', days, offsetDays],
    queryFn: () => mailApi.controlCenterActivity(accountId, days, offsetDays),
    staleTime: 5 * 60_000,
    gcTime: 15 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })

  const accountActivity = useMemo<ControlCenterAccountActivity[]>(() => {
    if (activityQuery.data?.accounts) return activityQuery.data.accounts
    return accounts.map(account => ({
      accountId: account.accountId,
      accountName: account.accountName,
      accountColor: account.accountColor,
      isAvailable: account.isAvailable,
      activity: [],
    }))
  }, [accounts, activityQuery.data?.accounts])

  const combinedActivity = activityQuery.data?.activity ?? []
  const selectedAccount = selectedSeries === 'all' ? undefined : accountActivity.find(account => account.accountId === selectedSeries)
  const activity = selectedAccount?.activity ?? combinedActivity
  const selectedLabel = selectedAccount?.accountName ?? 'Todas las cuentas'
  const maximumActivity = Math.max(1, ...activity.flatMap(day => [day.received, day.sent]))
  const period = rangeLabel(activityQuery.data?.startDate, activityQuery.data?.endDate)
  const chartMinWidth = days === 30 ? 1120 : days === 14 ? 650 : 0
  const peakDay = activity.reduce<ControlCenterDay | null>((peak, day) => !peak || day.received + day.sent > peak.received + peak.sent ? day : peak, null)
  const peakTotal = peakDay ? peakDay.received + peakDay.sent : 0
  const receivedLine = linePoints(activity, 'received', maximumActivity)
  const sentLine = linePoints(activity, 'sent', maximumActivity)
  const combinedTotals = totals(combinedActivity)

  function changeDays(value: ActivityDays) {
    setDays(value)
    setOffsetDays(0)
  }

  return <article className="control-panel activity-panel">
    <header className="activity-panel-header">
      <div className="activity-title"><strong>Actividad por cuenta</strong><span>{activityQuery.isFetching ? 'Actualizando…' : period || `Últimos ${days} días`}</span></div>
      <div className="activity-toolbar">
        <div className="activity-period-selector" aria-label="Período del gráfico">
          {([7, 14, 30] as ActivityDays[]).map(value => <button type="button" key={value} className={days === value ? 'active' : ''} onClick={() => changeDays(value)}>{value} días</button>)}
        </div>
        <div className="activity-navigation" aria-label="Navegar períodos">
          <button type="button" className="icon-button" onClick={() => setOffsetDays(current => Math.min(365, current + days))} disabled={offsetDays + days > 365} title="Período anterior" aria-label="Período anterior"><ChevronLeft size={16} /></button>
          <button type="button" className="icon-button" onClick={() => setOffsetDays(current => Math.max(0, current - days))} disabled={offsetDays === 0} title="Período siguiente" aria-label="Período siguiente"><ChevronRight size={16} /></button>
          {offsetDays > 0 && <button type="button" className="activity-today-button" onClick={() => setOffsetDays(0)}>Actual</button>}
        </div>
      </div>
    </header>

    <div className="activity-account-selector" aria-label="Actividad por cuenta">
      {!accountId && accountActivity.length > 1 && <button type="button" className={`activity-account-card ${selectedSeries === 'all' ? 'active' : ''}`} onClick={() => setSelectedSeries('all')}>
        <span className="activity-account-name"><i className="all-accounts-dot" />Todas</span>
        <span className="activity-account-counts"><span><b>{combinedTotals.received}</b><small>Recibidos</small></span><span><b>{combinedTotals.sent}</b><small>Enviados</small></span></span>
      </button>}
      {accountActivity.map(account => {
        const count = totals(account.activity)
        return <button type="button" disabled={!account.isAvailable} className={`activity-account-card ${selectedSeries === account.accountId ? 'active' : ''} ${account.isAvailable ? '' : 'unavailable'}`} key={account.accountId} onClick={() => setSelectedSeries(account.accountId)}>
          <span className="activity-account-name"><i style={{ background: account.accountColor }} />{account.accountName}</span>
          {account.isAvailable ? <span className="activity-account-counts"><span><b>{count.received}</b><small>Recibidos</small></span><span><b>{count.sent}</b><small>Enviados</small></span></span> : <span className="activity-account-unavailable">No disponible</span>}
        </button>
      })}
    </div>

    <div className="activity-insight-row">
      <div><span>Mostrando</span><strong>{selectedLabel}</strong></div>
      <div className="activity-peak"><TrendingUp size={15} /><span>Día más activo</span><strong>{peakDay && peakTotal > 0 ? `${fullDayLabel(peakDay.date)} · ${peakTotal}` : 'Sin actividad'}</strong></div>
    </div>

    {activityQuery.isError ? <div className="notice activity-error">No fue posible consultar la actividad de este período.</div> : <div className="activity-chart-scroll">
      <div className="activity-plot" style={{ minWidth: chartMinWidth || undefined }}>
        <svg className="activity-line-overlay" viewBox="0 0 1000 120" preserveAspectRatio="none" aria-hidden="true">
          {receivedLine && <polyline className="received" points={receivedLine} />}
          {sentLine && <polyline className="sent" points={sentLine} />}
        </svg>
        <div className="activity-chart activity-chart-dynamic" style={{ gridTemplateColumns: `repeat(${Math.max(1, activity.length)}, minmax(32px, 1fr))` }} aria-label={`Actividad de ${selectedLabel}: ${period || `${days} días`}`}>
          {activity.map(day => <div className="activity-day" key={day.date} title={`${fullDayLabel(day.date)} · ${day.received} recibidos · ${day.sent} enviados`}>
            <div className="activity-bars">
              <span className="activity-bar-column received"><b>{day.received}</b><i style={{ height: day.received === 0 ? '3px' : `${Math.max(10, Math.round(day.received / maximumActivity * 100))}%` }} /></span>
              <span className="activity-bar-column sent"><b>{day.sent}</b><i style={{ height: day.sent === 0 ? '3px' : `${Math.max(10, Math.round(day.sent / maximumActivity * 100))}%` }} /></span>
            </div>
            <span>{dayLabel(day.date)}</span>
          </div>)}
        </div>
      </div>
    </div>}

    <div className="activity-legend activity-legend-footer"><span><i className="received" /> Recibidos</span><span><i className="sent" /> Enviados</span><span className="activity-line-key">Barras + tendencia</span>{activityQuery.data && activityQuery.data.unavailableAccounts > 0 && <span className="activity-unavailable">{activityQuery.data.unavailableAccounts} cuenta{activityQuery.data.unavailableAccounts === 1 ? '' : 's'} no disponible{activityQuery.data.unavailableAccounts === 1 ? '' : 's'}</span>}</div>
  </article>
}
