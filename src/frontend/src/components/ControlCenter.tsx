import { useState } from 'react'
import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ChevronRight, Clock3, Eye, Inbox, Mail, MessageSquareReply, Pause, RefreshCw, Send, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem, ControlCenterSnapshot } from '../types/mail'
import { ControlCenterActivity } from './ControlCenterActivity'

type ManagementView = 'received' | 'sent' | 'overdue' | null

function ageLabel(value: string) {
  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60_000))
  if (elapsedMinutes < 60) return `hace ${Math.max(1, elapsedMinutes)} min`
  const hours = Math.floor(elapsedMinutes / 60)
  if (hours < 24) return `hace ${hours} h`
  const days = Math.floor(hours / 24)
  return `hace ${days} día${days === 1 ? '' : 's'}`
}

function itemKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.conversationId}:${item.messageId}`
}

function isOverdue(item: ControlCenterPendingItem) {
  return Date.now() - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000
}

function MetricCard({ tone, icon, value, label, hint, active, onClick }: { tone: string; icon: ReactNode; value: number; label: string; hint: string; active?: boolean; onClick: () => void }) {
  return <button type="button" className={`control-metric ${tone} ${active ? 'active' : ''}`} onClick={onClick}>
    <div className="control-metric-icon">{icon}</div>
    <div><strong>{value}</strong><span>{label}</span><small>{hint}</small></div>
    <ChevronRight size={16} className="control-metric-chevron" />
  </button>
}

function managementCopy(view: Exclude<ManagementView, null>) {
  if (view === 'received') return { title: 'Recibidos sin responder', description: 'Conversaciones en que la otra persona escribió al final. Puede responder, posponer o indicar que no requieren respuesta.' }
  if (view === 'sent') return { title: 'Enviados sin respuesta', description: 'Conversaciones en que usted escribió al final. Puede enviar un seguimiento, posponer o indicar que no requieren seguimiento.' }
  return { title: 'Pendientes de más de 48 horas', description: 'Reúne pendientes recibidos y enviados cuya última actividad ocurrió hace 48 horas o más.' }
}

export function ControlCenter({ accountId, accountName }: { accountId?: string; accountName?: string }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [activeView, setActiveView] = useState<ManagementView>(null)
  const [snoozeTarget, setSnoozeTarget] = useState<string | null>(null)
  const [openingTarget, setOpeningTarget] = useState<string | null>(null)
  const [actionError, setActionError] = useState('')
  const queryKey = ['control-center', accountId ?? 'all'] as const
  const inboxPath = accountId ? `/account/${accountId}` : '/inbox'
  const snapshot = useQuery({
    queryKey,
    queryFn: () => mailApi.controlCenter(accountId),
    staleTime: 90_000,
    gcTime: 10 * 60_000,
    refetchInterval: 5 * 60_000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: false,
  })

  const manage = useMutation({
    mutationFn: ({ item, action, snoozeHours }: { item: ControlCenterPendingItem; action: 'resolved' | 'snoozed'; snoozeHours?: number }) => mailApi.updateControlCenterState(item.accountId, item.conversationId, { messageId: item.messageId, action, snoozeHours }),
    onMutate: () => setActionError(''),
    onSuccess: (_, variables) => {
      const item = variables.item
      queryClient.setQueryData<ControlCenterSnapshot>(queryKey, current => {
        if (!current) return current
        const overdueAdjustment = isOverdue(item) ? 1 : 0
        return {
          ...current,
          receivedWithoutReply: Math.max(0, current.receivedWithoutReply - (item.direction === 'received' ? 1 : 0)),
          sentWithoutResponse: Math.max(0, current.sentWithoutResponse - (item.direction === 'sent' ? 1 : 0)),
          overdue: Math.max(0, current.overdue - overdueAdjustment),
          priorityItems: current.priorityItems.filter(value => itemKey(value) !== itemKey(item)),
          pendingItems: current.pendingItems.filter(value => itemKey(value) !== itemKey(item)),
          accounts: current.accounts.map(account => account.accountId !== item.accountId ? account : {
            ...account,
            receivedWithoutReply: Math.max(0, account.receivedWithoutReply - (item.direction === 'received' ? 1 : 0)),
            sentWithoutResponse: Math.max(0, account.sentWithoutResponse - (item.direction === 'sent' ? 1 : 0)),
          }),
        }
      })
      setSnoozeTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
    },
    onError: error => setActionError(error instanceof Error ? error.message : 'No fue posible actualizar el seguimiento.'),
  })

  async function openComposer(item: ControlCenterPendingItem) {
    const key = itemKey(item)
    setOpeningTarget(key)
    setActionError('')
    try {
      const message = await queryClient.fetchQuery({ queryKey: ['message', item.accountId, item.messageId], queryFn: () => mailApi.message(item.accountId, item.messageId), staleTime: 30_000 })
      navigate('/compose', { state: { mode: item.direction === 'received' ? 'reply' : 'followUp', message } })
    } catch (error) {
      setActionError(error instanceof Error ? error.message : 'No fue posible abrir la conversación.')
    } finally {
      setOpeningTarget(null)
    }
  }

  if (snapshot.isLoading) return <section className="control-center control-center-loading" aria-label="Cargando centro de control"><div className="reading-skeleton" /><div className="control-loading-grid">{Array.from({ length: 4 }, (_, index) => <div className="reading-skeleton" key={index} />)}</div></section>

  if (snapshot.isError || !snapshot.data) return <section className="control-center"><div className="control-center-header"><div><h2>Centro de control</h2><p>No fue posible cargar los indicadores.</p></div><button className="icon-button" onClick={() => snapshot.refetch()} aria-label="Reintentar centro de control"><RefreshCw size={17} /></button></div></section>

  const data = snapshot.data
  const updatedAt = new Date(data.generatedAt).toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })
  const managementItems = activeView === 'received'
    ? data.pendingItems.filter(item => item.direction === 'received')
    : activeView === 'sent'
      ? data.pendingItems.filter(item => item.direction === 'sent')
      : activeView === 'overdue'
        ? data.pendingItems.filter(isOverdue)
        : []
  const activeCopy = activeView ? managementCopy(activeView) : null
  const scopeLabel = accountId ? accountName ?? 'Esta cuenta' : 'Todas las cuentas'

  return <section className="control-center" aria-labelledby="control-center-title">
    <div className="control-center-header">
      <div><div className="control-center-title-line"><h2 id="control-center-title">Centro de control</h2><span>Seguimiento · 14 días</span></div><p>{scopeLabel} · Sólo conversaciones con posible acción pendiente.</p></div>
      <div className="control-center-refresh"><span>Actualizado {updatedAt}</span></div>
    </div>

    {data.unavailableAccounts > 0 && <div className="notice control-center-warning">No se pudo consultar {data.unavailableAccounts} cuenta{data.unavailableAccounts === 1 ? '' : 's'}. Los indicadores muestran las cuentas disponibles.</div>}

    <div className="control-metrics">
      <MetricCard tone="received" icon={<Inbox size={19} />} value={data.receivedWithoutReply} label="Recibidos sin responder" hint="Promociones y avisos informativos se excluyen" active={activeView === 'received'} onClick={() => setActiveView(current => current === 'received' ? null : 'received')} />
      <MetricCard tone="sent" icon={<Send size={19} />} value={data.sentWithoutResponse} label="Enviados sin respuesta" hint="Usted escribió al final" active={activeView === 'sent'} onClick={() => setActiveView(current => current === 'sent' ? null : 'sent')} />
      <MetricCard tone="unread" icon={<Mail size={19} />} value={data.unread} label="Correos sin leer" hint="Abrir y gestionar en forma masiva" onClick={() => navigate(`${inboxPath}?q=${encodeURIComponent('is:unread')}`)} />
      <MetricCard tone="overdue" icon={<Clock3 size={19} />} value={data.overdue} label="Más de 48 horas" hint="Pendientes que requieren atención" active={activeView === 'overdue'} onClick={() => setActiveView(current => current === 'overdue' ? null : 'overdue')} />
    </div>

    {activeView && activeCopy && <article className="control-management-panel">
      <header><div><p className="eyebrow">Gestión</p><strong>{activeCopy.title}</strong><span>{activeCopy.description}</span></div><button type="button" className="icon-button" onClick={() => { setActiveView(null); setSnoozeTarget(null) }} aria-label="Cerrar gestión"><X size={17} /></button></header>
      {actionError && <div className="notice control-management-error">{actionError}</div>}
      {managementItems.length === 0 ? <div className="control-empty management-empty"><Check size={24} /><strong>Sin pendientes en esta vista</strong><span>No hay conversaciones que requieran gestión en este momento.</span></div> : <div className="management-list">
        {managementItems.map(item => {
          const key = itemKey(item)
          const pendingAction = manage.isPending || openingTarget === key
          return <div className="management-row" key={key}>
            <i className="account-dot" style={{ background: item.accountColor }} />
            <div className="management-main"><div className="management-heading"><span className={`priority-direction ${item.direction}`}>{item.direction === 'received' ? 'Responder' : 'Esperando'}</span><strong>{item.subject}</strong></div><span>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</span><small>{item.accountName} · {ageLabel(item.since)}{isOverdue(item) ? ' · Más de 48 h' : ''}</small></div>
            <div className="management-actions">
              <button type="button" className="primary-button compact-action" disabled={pendingAction} onClick={() => void openComposer(item)}><MessageSquareReply size={14} /> {item.direction === 'received' ? 'Responder' : 'Seguimiento'}</button>
              <button type="button" className="secondary-button compact-action" disabled={pendingAction} onClick={() => setSnoozeTarget(current => current === key ? null : key)}><Pause size={14} /> Posponer</button>
              <button type="button" className="secondary-button compact-action" disabled={pendingAction} onClick={() => manage.mutate({ item, action: 'resolved' })}><Check size={14} /> No requiere {item.direction === 'received' ? 'respuesta' : 'seguimiento'}</button>
              <button type="button" className="icon-button" disabled={pendingAction} title="Ver correo" aria-label="Ver correo" onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`, { state: { returnTo: inboxPath } })}><Eye size={16} /></button>
            </div>
            {snoozeTarget === key && <div className="snooze-options"><span>Volver a mostrar en:</span><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 24 })}>1 día</button><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 72 })}>3 días</button><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 168 })}>7 días</button></div>}
          </div>
        })}
      </div>}
    </article>}

    <div className="control-center-grid">
      <ControlCenterActivity accountId={accountId} accounts={data.accounts} />

      <article className="control-panel priority-panel">
        <header><div><strong>Seguimiento prioritario</strong><span>Conversaciones pendientes más antiguas</span></div></header>
        {data.priorityItems.length === 0 ? <div className="control-empty"><strong>Sin pendientes recientes</strong><span>No hay conversaciones que requieran seguimiento en el período analizado.</span></div> : <div className="priority-list">
          {data.priorityItems.map(item => <button type="button" className="priority-row" key={itemKey(item)} onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`, { state: { returnTo: inboxPath } })}>
            <i className="account-dot" style={{ background: item.accountColor }} />
            <span className="priority-main"><span className={`priority-direction ${item.direction}`}>{item.direction === 'received' ? 'Responder' : 'Esperando'}</span><strong>{item.subject}</strong><small>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</small></span>
            <span className="priority-age">{ageLabel(item.since)}<ChevronRight size={15} /></span>
          </button>)}
        </div>}
      </article>
    </div>
  </section>
}
