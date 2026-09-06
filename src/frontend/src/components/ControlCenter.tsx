import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ChevronRight, Clock3, Inbox, Mail, RefreshCw, Send } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'

function ageLabel(value: string) {
  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60_000))
  if (elapsedMinutes < 60) return `hace ${Math.max(1, elapsedMinutes)} min`
  const hours = Math.floor(elapsedMinutes / 60)
  if (hours < 24) return `hace ${hours} h`
  const days = Math.floor(hours / 24)
  return `hace ${days} día${days === 1 ? '' : 's'}`
}

function dayLabel(value: string) {
  const date = new Date(`${value}T12:00:00`)
  return date.toLocaleDateString('es-CL', { weekday: 'short' }).replace('.', '')
}

function MetricCard({ tone, icon, value, label, hint }: { tone: string; icon: ReactNode; value: number; label: string; hint: string }) {
  return <article className={`control-metric ${tone}`}>
    <div className="control-metric-icon">{icon}</div>
    <div><strong>{value}</strong><span>{label}</span><small>{hint}</small></div>
  </article>
}

export function ControlCenter() {
  const navigate = useNavigate()
  const snapshot = useQuery({
    queryKey: ['control-center'],
    queryFn: mailApi.controlCenter,
    staleTime: 60_000,
    refetchInterval: 120_000,
    refetchIntervalInBackground: false,
  })

  if (snapshot.isLoading) return <section className="control-center control-center-loading" aria-label="Cargando centro de control"><div className="reading-skeleton" /><div className="control-loading-grid">{Array.from({ length: 4 }, (_, index) => <div className="reading-skeleton" key={index} />)}</div></section>

  if (snapshot.isError || !snapshot.data) return <section className="control-center"><div className="control-center-header"><div><p className="eyebrow">Resumen operativo</p><h2>Centro de control</h2></div><button className="icon-button" onClick={() => snapshot.refetch()} aria-label="Reintentar centro de control"><RefreshCw size={17} /></button></div><div className="notice control-center-error">No fue posible actualizar el Centro de Control. <button onClick={() => snapshot.refetch()}>Reintentar</button></div></section>

  const data = snapshot.data
  const maximumActivity = Math.max(1, ...data.activity.flatMap(day => [day.received, day.sent]))
  const updatedAt = new Date(data.generatedAt).toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })

  return <section className="control-center" aria-labelledby="control-center-title">
    <div className="control-center-header">
      <div><p className="eyebrow">Resumen operativo · últimos 14 días</p><h2 id="control-center-title">Centro de control</h2><p>Seguimiento de conversaciones y carga de correo de todas sus cuentas.</p></div>
      <div className="control-center-refresh"><span>Actualizado {updatedAt}</span><button className="icon-button" onClick={() => snapshot.refetch()} disabled={snapshot.isFetching} aria-label="Actualizar centro de control"><RefreshCw size={17} className={snapshot.isFetching ? 'spin' : ''} /></button></div>
    </div>

    {data.unavailableAccounts > 0 && <div className="notice control-center-warning">No se pudo consultar {data.unavailableAccounts} cuenta{data.unavailableAccounts === 1 ? '' : 's'}. Los indicadores muestran las cuentas disponibles.</div>}

    <div className="control-metrics">
      <MetricCard tone="received" icon={<Inbox size={19} />} value={data.receivedWithoutReply} label="Recibidos sin responder" hint="La otra persona escribió al final" />
      <MetricCard tone="sent" icon={<Send size={19} />} value={data.sentWithoutResponse} label="Enviados sin respuesta" hint="Usted escribió al final" />
      <MetricCard tone="unread" icon={<Mail size={19} />} value={data.unread} label="Correos sin leer" hint="Total actual en Gmail" />
      <MetricCard tone="overdue" icon={<Clock3 size={19} />} value={data.overdue} label="Más de 48 horas" hint="Pendientes que requieren atención" />
    </div>

    <div className="control-center-grid">
      <article className="control-panel activity-panel">
        <header><div><strong>Actividad de 7 días</strong><span>Recibidos y enviados por día</span></div><div className="activity-legend"><span><i className="received" /> Recibidos</span><span><i className="sent" /> Enviados</span></div></header>
        <div className="activity-chart" aria-label="Actividad de correo de los últimos siete días">
          {data.activity.map(day => <div className="activity-day" key={day.date}>
            <div className="activity-bars" title={`${day.received} recibidos · ${day.sent} enviados`}>
              <i className="received" style={{ height: day.received === 0 ? '3px' : `${Math.max(10, Math.round(day.received / maximumActivity * 100))}%` }} />
              <i className="sent" style={{ height: day.sent === 0 ? '3px' : `${Math.max(10, Math.round(day.sent / maximumActivity * 100))}%` }} />
            </div>
            <span>{dayLabel(day.date)}</span>
          </div>)}
        </div>
        <div className="control-account-strip">
          {data.accounts.map(account => {
            const pending = account.receivedWithoutReply + account.sentWithoutResponse
            return <div className={`control-account-chip ${account.isAvailable ? '' : 'unavailable'}`} key={account.accountId} title={account.isAvailable ? `${pending} pendientes · ${account.unread} sin leer` : 'Cuenta no disponible para el cálculo'}><i style={{ background: account.accountColor }} /><span>{account.accountName}</span><strong>{account.isAvailable ? pending : '—'}</strong></div>
          })}
        </div>
      </article>

      <article className="control-panel priority-panel">
        <header><div><strong>Seguimiento prioritario</strong><span>Conversaciones pendientes más antiguas</span></div></header>
        {data.priorityItems.length === 0 ? <div className="control-empty"><strong>Sin pendientes recientes</strong><span>No hay conversaciones que requieran seguimiento en el período analizado.</span></div> : <div className="priority-list">
          {data.priorityItems.map(item => <button type="button" className="priority-row" key={`${item.accountId}:${item.messageId}`} onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`, { state: { returnTo: '/inbox' } })}>
            <i className="account-dot" style={{ background: item.accountColor }} />
            <span className="priority-main"><span className={`priority-direction ${item.direction}`}>{item.direction === 'received' ? 'Responder' : 'Esperando'}</span><strong>{item.subject}</strong><small>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</small></span>
            <span className="priority-age">{ageLabel(item.since)}<ChevronRight size={15} /></span>
          </button>)}
        </div>}
      </article>
    </div>
  </section>
}
