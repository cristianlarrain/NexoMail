import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, RefreshCw } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { ControlCenterAccountSummary } from '../types/mail'

type ActivityDays = 7 | 14 | 30

function dayLabel(value: string, days: ActivityDays) {
  const date = new Date(`${value}T12:00:00`)
  if (days === 7) return date.toLocaleDateString('es-CL', { weekday: 'short', day: 'numeric' }).replace('.', '')
  return date.toLocaleDateString('es-CL', { day: 'numeric', month: 'short' }).replace('.', '')
}

function rangeLabel(startDate?: string, endDate?: string) {
  if (!startDate || !endDate) return ''
  const start = new Date(`${startDate}T12:00:00`)
  const end = new Date(`${endDate}T12:00:00`)
  const format = (date: Date) => date.toLocaleDateString('es-CL', { day: 'numeric', month: 'short', year: date.getFullYear() !== new Date().getFullYear() ? 'numeric' : undefined }).replace('.', '')
  return `${format(start)} – ${format(end)}`
}

export function ControlCenterActivity({ accountId, accounts }: { accountId?: string; accounts: ControlCenterAccountSummary[] }) {
  const [days, setDays] = useState<ActivityDays>(7)
  const [offsetDays, setOffsetDays] = useState(0)

  useEffect(() => setOffsetDays(0), [accountId])

  const activityQuery = useQuery({
    queryKey: ['control-center-activity', accountId ?? 'all', days, offsetDays],
    queryFn: () => mailApi.controlCenterActivity(accountId, days, offsetDays),
    staleTime: 5 * 60_000,
    retry: 1,
  })

  const activity = activityQuery.data?.activity ?? []
  const maximumActivity = Math.max(1, ...activity.flatMap(day => [day.received, day.sent]))
  const period = rangeLabel(activityQuery.data?.startDate, activityQuery.data?.endDate)
  const chartMinWidth = days === 30 ? 1120 : days === 14 ? 650 : 0

  function changeDays(value: ActivityDays) {
    setDays(value)
    setOffsetDays(0)
  }

  return <article className="control-panel activity-panel">
    <header className="activity-panel-header">
      <div className="activity-title"><strong>Actividad</strong><span>{activityQuery.isFetching ? 'Actualizando…' : period || `Últimos ${days} días`}</span></div>
      <div className="activity-toolbar">
        <div className="activity-period-selector" aria-label="Período del gráfico">
          {([7, 14, 30] as ActivityDays[]).map(value => <button type="button" key={value} className={days === value ? 'active' : ''} onClick={() => changeDays(value)}>{value} días</button>)}
        </div>
        <div className="activity-navigation" aria-label="Navegar períodos">
          <button type="button" className="icon-button" onClick={() => setOffsetDays(current => Math.min(365, current + days))} disabled={offsetDays + days > 365} title="Período anterior" aria-label="Período anterior"><ChevronLeft size={16} /></button>
          <button type="button" className="icon-button" onClick={() => setOffsetDays(current => Math.max(0, current - days))} disabled={offsetDays === 0} title="Período siguiente" aria-label="Período siguiente"><ChevronRight size={16} /></button>
          {offsetDays > 0 && <button type="button" className="activity-today-button" onClick={() => setOffsetDays(0)}>Actual</button>}
          <button type="button" className="icon-button" onClick={() => activityQuery.refetch()} disabled={activityQuery.isFetching} title="Actualizar gráfico" aria-label="Actualizar gráfico"><RefreshCw size={15} className={activityQuery.isFetching ? 'spin' : ''} /></button>
        </div>
      </div>
    </header>

    {activityQuery.isError ? <div className="notice activity-error">No fue posible consultar la actividad de este período. <button onClick={() => activityQuery.refetch()}>Reintentar</button></div> : <div className="activity-chart-scroll">
      <div className="activity-chart activity-chart-dynamic" style={{ gridTemplateColumns: `repeat(${Math.max(1, activity.length)}, minmax(32px, 1fr))`, minWidth: chartMinWidth || undefined }} aria-label={`Actividad de correo: ${period || `${days} días`}`}>
        {activity.map(day => <div className="activity-day" key={day.date} title={`${day.received} recibidos · ${day.sent} enviados`}>
          <div className="activity-bars">
            <span className="activity-bar-column received"><b>{day.received}</b><i style={{ height: day.received === 0 ? '3px' : `${Math.max(10, Math.round(day.received / maximumActivity * 100))}%` }} /></span>
            <span className="activity-bar-column sent"><b>{day.sent}</b><i style={{ height: day.sent === 0 ? '3px' : `${Math.max(10, Math.round(day.sent / maximumActivity * 100))}%` }} /></span>
          </div>
          <span>{dayLabel(day.date, days)}</span>
        </div>)}
      </div>
    </div>}

    <div className="activity-legend activity-legend-footer"><span><i className="received" /> Recibidos</span><span><i className="sent" /> Enviados</span>{activityQuery.data && activityQuery.data.unavailableAccounts > 0 && <span className="activity-unavailable">{activityQuery.data.unavailableAccounts} cuenta{activityQuery.data.unavailableAccounts === 1 ? '' : 's'} no disponible{activityQuery.data.unavailableAccounts === 1 ? '' : 's'}</span>}</div>

    <div className="control-account-strip">
      {accounts.map(account => {
        const pending = account.receivedWithoutReply + account.sentWithoutResponse
        return <div className={`control-account-chip ${account.isAvailable ? '' : 'unavailable'}`} key={account.accountId} title={account.isAvailable ? `${pending} pendientes · ${account.unread} sin leer` : 'Cuenta no disponible para el cálculo'}><i style={{ background: account.accountColor }} /><span>{account.accountName}</span><strong>{account.isAvailable ? pending : '—'}</strong></div>
      })}
    </div>
  </article>
}
