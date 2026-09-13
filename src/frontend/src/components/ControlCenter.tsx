import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Clock3, Eye, Inbox, Mail, Pause, RefreshCw, Send, Sparkles, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem, ControlCenterSnapshot } from '../types/mail'
import { NexiPriorityQueue } from './NexiPriorityQueue'
import { NexiEmptyState } from './nexi/NexiEmptyState'
import { NexiVisual } from './nexi/NexiVisual'

type ManagementView = 'received' | 'sent' | 'overdue' | null
type PriorityDisplayItem = { item: ControlCenterPendingItem; automatic: boolean; manual: boolean }

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

function messageKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.messageId}`
}

function isOverdue(item: ControlCenterPendingItem) {
  return Date.now() - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000
}

function MetricCard({ tone, icon, value, label, active, onClick }: { tone: string; icon: ReactNode; value: number; label: string; active?: boolean; onClick: () => void }) {
  return <button type="button" className={`control-metric ${tone} ${active ? 'active' : ''}`} onClick={onClick} aria-label={`${label}: ${value}`}>
    <div className="control-metric-icon">{icon}</div>
    <div><strong>{value}</strong><span>{label}</span></div>
  </button>
}

function managementCopy(view: Exclude<ManagementView, null>) {
  if (view === 'received') return { title: 'Recibidos sin responder', description: 'Conversaciones en que la otra persona escribió al final. Puede preparar una respuesta, posponer o indicar que no requieren respuesta.' }
  if (view === 'sent') return { title: 'Enviados sin respuesta', description: 'Conversaciones en que usted escribió al final. Puede preparar un seguimiento, posponer o indicar que no requieren seguimiento.' }
  return { title: 'Pendientes de más de 48 horas', description: 'Reúne pendientes recibidos y enviados cuya última actividad ocurrió hace 48 horas o más.' }
}

export function ControlCenter({ accountId, onUpdatedAtChange }: { accountId?: string; accountName?: string; onUpdatedAtChange?: (value: string) => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [activeView, setActiveView] = useState<ManagementView>(null)
  const [snoozeTarget, setSnoozeTarget] = useState<string | null>(null)
  const [openingTarget, setOpeningTarget] = useState<string | null>(null)
  const [actionError, setActionError] = useState('')
  const queryKey = ['control-center', accountId ?? 'all'] as const
  const controlCenterPath = '/control-center'

  const snapshot = useQuery({
    queryKey,
    queryFn: () => mailApi.controlCenter(accountId),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchInterval: false,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
  })

  const manualTracking = useQuery({
    queryKey: ['control-center-tracking', accountId ?? 'all'],
    queryFn: () => mailApi.controlCenterTrackedItems(accountId),
    staleTime: 0,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
  })

  const generatedAt = snapshot.data?.generatedAt
  useEffect(() => {
    if (!generatedAt || !onUpdatedAtChange) return
    onUpdatedAtChange(new Date(generatedAt).toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' }))
  }, [generatedAt, onUpdatedAtChange])

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
    },
    onError: error => setActionError(error instanceof Error ? error.message : 'No fue posible actualizar el seguimiento.'),
  })

  async function openComposer(item: ControlCenterPendingItem) {
    const key = itemKey(item)
    setOpeningTarget(key)
    setActionError('')
    try {
      const message = await queryClient.fetchQuery({ queryKey: ['message', item.accountId, item.messageId], queryFn: () => mailApi.message(item.accountId, item.messageId), staleTime: 5 * 60_000 })
      navigate('/compose', { state: { mode: item.direction === 'received' ? 'reply' : 'followUp', message, returnTo: controlCenterPath } })
    } catch (error) {
      setActionError(error instanceof Error ? error.message : 'No fue posible abrir la conversación.')
    } finally {
      setOpeningTarget(null)
    }
  }

  if (snapshot.isLoading) return <section className="control-center control-center-cinematic control-center-loading" role="status" aria-live="polite" aria-label="Actualizando Nexi Control Center">
    <div className="control-loading-copy">
      <span className="control-loading-icon nexi-processing-icon" aria-hidden="true"><NexiVisual size="small" /></span>
      <div><strong>Actualizando</strong><span>Nexi está revisando indicadores y pendientes.</span></div>
    </div>
    <div className="control-loading-progress" aria-hidden="true"><span /></div>
    <div className="control-loading-steps" aria-hidden="true"><span>Cuentas</span><span>Indicadores</span><span>Prioridades</span></div>
  </section>

  if (snapshot.isError || !snapshot.data) return <section className="control-center control-center-cinematic"><div className="control-center-header"><div><h2>Estado operativo</h2><p>No fue posible cargar los indicadores.</p></div><button className="icon-button" onClick={() => snapshot.refetch()} aria-label="Reintentar indicadores"><RefreshCw size={17} /></button></div></section>

  const data = snapshot.data
  const managementItems = activeView === 'received'
    ? data.pendingItems.filter(item => item.direction === 'received')
    : activeView === 'sent'
      ? data.pendingItems.filter(item => item.direction === 'sent')
      : activeView === 'overdue'
        ? data.pendingItems.filter(isOverdue)
        : []
  const activeCopy = activeView ? managementCopy(activeView) : null
  const priorityMap = new Map<string, PriorityDisplayItem>()

  data.pendingItems.forEach(item => priorityMap.set(messageKey(item), { item, automatic: true, manual: false }))
  ;(manualTracking.data ?? []).forEach(item => {
    const key = messageKey(item)
    const current = priorityMap.get(key)
    if (current) priorityMap.set(key, { ...current, manual: true })
    else priorityMap.set(key, { item, automatic: false, manual: true })
  })

  const priorityItems = [...priorityMap.values()].sort((left, right) => new Date(left.item.since).getTime() - new Date(right.item.since).getTime())

  function openManagementView(view: Exclude<ManagementView, null>) {
    setActiveView(view)
    setSnoozeTarget(null)
    window.requestAnimationFrame(() => {
      document.querySelector('.control-management-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    })
  }

  function openUnread() {
    const params = new URLSearchParams({ q: 'correos sin leer', scope: 'mail', unread: '1' })
    if (accountId) params.set('account', accountId)
    navigate(`/search?${params.toString()}`)
  }

  return <section className="control-center control-center-cinematic" aria-label="Prioridades">
    {data.unavailableAccounts > 0 && <div className="notice control-center-warning">No se pudo consultar {data.unavailableAccounts} cuenta{data.unavailableAccounts === 1 ? '' : 's'}. Los indicadores consideran las cuentas disponibles.</div>}

    <div className="control-metrics nexi-control-metrics compact">
      <MetricCard tone="received" icon={<Inbox size={18} />} value={data.receivedWithoutReply} label="Recibidos sin responder" active={activeView === 'received'} onClick={() => openManagementView('received')} />
      <MetricCard tone="sent" icon={<Send size={18} />} value={data.sentWithoutResponse} label="Enviados sin respuesta" active={activeView === 'sent'} onClick={() => openManagementView('sent')} />
      <MetricCard tone="unread" icon={<Mail size={18} />} value={data.unread} label="Sin leer" onClick={openUnread} />
      <MetricCard tone="overdue" icon={<Clock3 size={18} />} value={data.overdue} label="Más de 48 h" active={activeView === 'overdue'} onClick={() => openManagementView('overdue')} />
    </div>

    <NexiPriorityQueue
      items={priorityItems}
      openingTarget={openingTarget}
      onManage={item => void openComposer(item)}
      onOpen={(item, manual) => navigate(`/message/${item.accountId}/${item.messageId}`, { state: { returnTo: controlCenterPath, controlCenterItem: item, manualTracking: manual } })}
    />

    {activeView && activeCopy && <article className="control-management-panel">
      <header><div><p className="eyebrow">Gestión</p><strong>{activeCopy.title}</strong><span>{activeCopy.description}</span></div><button type="button" className="icon-button" onClick={() => { setActiveView(null); setSnoozeTarget(null) }} aria-label="Cerrar gestión"><X size={17} /></button></header>
      {actionError && <div className="notice control-management-error">{actionError}</div>}
      {managementItems.length === 0 ? <NexiEmptyState compact title="Sin pendientes en esta vista" description="No hay conversaciones que requieran gestión en este momento." /> : <div className="management-list">
        {managementItems.map(item => {
          const key = itemKey(item)
          const pendingAction = manage.isPending || openingTarget === key
          return <div className="management-row" key={key}>
            <i className="account-dot" style={{ background: item.accountColor }} />
            <div className="management-main"><div className="management-heading"><span className={`priority-direction ${item.direction}`}>{item.direction === 'received' ? 'Responder' : 'Esperando'}</span><strong>{item.subject}</strong></div><span>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</span><small>{item.accountName} · {ageLabel(item.since)}{isOverdue(item) ? ' · Más de 48 h' : ''}</small></div>
            <div className="management-actions">
              <button type="button" className="primary-button compact-action" disabled={pendingAction} onClick={() => void openComposer(item)}><Sparkles size={14} /> {item.direction === 'received' ? 'Preparar respuesta con IA' : 'Preparar seguimiento'}</button>
              <button type="button" className="secondary-button compact-action" disabled={pendingAction} onClick={() => setSnoozeTarget(current => current === key ? null : key)}><Pause size={14} /> Posponer</button>
              <button type="button" className="secondary-button compact-action" disabled={pendingAction} onClick={() => manage.mutate({ item, action: 'resolved' })}><Check size={14} /> No requiere {item.direction === 'received' ? 'respuesta' : 'seguimiento'}</button>
              <button type="button" className="icon-button" disabled={pendingAction} title="Ver correo" aria-label="Ver correo" onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`, { state: { returnTo: controlCenterPath, controlCenterItem: item } })}><Eye size={16} /></button>
            </div>
            {snoozeTarget === key && <div className="snooze-options"><span>Volver a mostrar en:</span><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 24 })}>1 día</button><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 72 })}>3 días</button><button type="button" disabled={manage.isPending} onClick={() => manage.mutate({ item, action: 'snoozed', snoozeHours: 168 })}>7 días</button></div>}
          </div>
        })}
      </div>}
    </article>}
  </section>
}