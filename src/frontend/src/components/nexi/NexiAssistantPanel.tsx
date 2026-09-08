import { useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Clock3, Inbox, Mail, Palette, Reply, Send, Settings, Sparkles, UserRound, X } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router-dom'
import { mailApi } from '../../api/mailApi'
import type { ControlCenterPendingItem } from '../../types/mail'
import { NexiVisual } from './NexiVisual'
import { buildNexiInsights, type NexiInsightAction } from './nexiInsights'

type PriorityDisplayItem = { item: ControlCenterPendingItem; automatic: boolean; manual: boolean }

type SettingsContext = {
  title: string
  description: string
}

function messageKey(accountId: string, messageId: string) {
  return `${accountId}:${messageId}`
}

function ageLabel(value?: string) {
  if (!value) return '0 h'
  const hours = Math.max(1, Math.floor((Date.now() - new Date(value).getTime()) / 3_600_000))
  if (hours < 24) return `${hours} h`
  return `${Math.floor(hours / 24)} d`
}

function messageRoute(pathname: string) {
  const match = pathname.match(/^\/message\/([^/]+)\/([^/]+)$/)
  if (!match) return null
  return { accountId: decodeURIComponent(match[1]), messageId: decodeURIComponent(match[2]) }
}

function viewContext(pathname: string) {
  if (messageRoute(pathname)) return 'message'
  if (pathname === '/control-center') return 'control-center'
  if (pathname.startsWith('/settings/')) return 'settings'
  if (pathname === '/inbox' || pathname.startsWith('/account/')) return 'inbox'
  return 'general'
}

function settingsContext(pathname: string): SettingsContext {
  if (pathname === '/settings/profile') return {
    title: 'Mi perfil',
    description: 'Aquí puedes revisar y ajustar la información de tu perfil de NexoMail.',
  }
  if (pathname === '/settings/accounts') return {
    title: 'Cuentas conectadas',
    description: 'Aquí puedes administrar las cuentas de correo conectadas y su configuración.',
  }
  if (pathname === '/settings/appearance') return {
    title: 'Apariencia',
    description: 'Aquí puedes personalizar cómo se ve NexoMail.',
  }
  return {
    title: 'Configuración',
    description: 'Nexi está usando esta sección de configuración como contexto.',
  }
}

export function NexiAssistantPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const context = viewContext(location.pathname)
  const routeMessage = messageRoute(location.pathname)
  const currentSettings = settingsContext(location.pathname)
  const mailContextEnabled = open && context !== 'settings'

  const snapshot = useQuery({
    queryKey: ['control-center', 'all'],
    queryFn: () => mailApi.controlCenter(),
    enabled: mailContextEnabled,
    staleTime: 10 * 60_000,
    refetchOnWindowFocus: false,
  })
  const tracking = useQuery({
    queryKey: ['control-center-tracking', 'all'],
    queryFn: () => mailApi.controlCenterTrackedItems(),
    enabled: mailContextEnabled,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  })
  const currentMessage = useQuery({
    queryKey: ['message', routeMessage?.accountId, routeMessage?.messageId],
    queryFn: () => mailApi.message(routeMessage!.accountId, routeMessage!.messageId),
    enabled: open && context === 'message' && !!routeMessage,
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  })

  const priorityItems = useMemo(() => {
    const merged = new Map<string, PriorityDisplayItem>()
    snapshot.data?.pendingItems.forEach(item => merged.set(messageKey(item.accountId, item.messageId), { item, automatic: true, manual: false }))
    tracking.data?.forEach(item => {
      const key = messageKey(item.accountId, item.messageId)
      const current = merged.get(key)
      if (current) merged.set(key, { ...current, manual: true })
      else merged.set(key, { item, automatic: false, manual: true })
    })
    return [...merged.values()].sort((left, right) => new Date(left.item.since).getTime() - new Date(right.item.since).getTime())
  }, [snapshot.data?.pendingItems, tracking.data])

  const complementary = useMemo(() => {
    const manualCount = new Set((tracking.data ?? []).map(item => messageKey(item.accountId, item.messageId))).size
    const accountsWithPending = snapshot.data?.accounts.filter(account => account.receivedWithoutReply + account.sentWithoutResponse > 0).length ?? 0
    return {
      priorityCount: priorityItems.length,
      manualCount,
      accountsWithPending,
      oldestAge: ageLabel(priorityItems[0]?.item.since),
    }
  }, [priorityItems, snapshot.data?.accounts, tracking.data])

  const currentPending = routeMessage
    ? snapshot.data?.pendingItems.find(item => item.accountId === routeMessage.accountId && item.messageId === routeMessage.messageId)
    : undefined
  const currentPriority = routeMessage
    ? priorityItems.find(value => value.item.accountId === routeMessage.accountId && value.item.messageId === routeMessage.messageId)
    : undefined
  const currentTracked = currentPriority?.manual ?? false
  const currentAccount = routeMessage
    ? snapshot.data?.accounts.find(account => account.accountId === routeMessage.accountId)
    : undefined
  const canManualTrackCurrent = Boolean(currentMessage.data && !['drafts', 'trash', 'spam'].includes(currentMessage.data.folderId))
  const oldestPriority = priorityItems[0]
  const insights = snapshot.data ? buildNexiInsights(snapshot.data, tracking.data ?? [], context === 'control-center' ? 3 : 2) : []

  const trackingMutation = useMutation({
    mutationFn: async () => {
      if (!routeMessage) throw new Error('No hay un correo activo para actualizar el seguimiento.')
      if (currentTracked) await mailApi.untrackMessage(routeMessage.accountId, routeMessage.messageId)
      else await mailApi.trackMessage(routeMessage.accountId, routeMessage.messageId)
    },
    onSuccess: async () => {
      if (routeMessage) queryClient.setQueryData(['control-center-tracking-state', routeMessage.accountId, routeMessage.messageId], { isTracked: !currentTracked })
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['control-center-tracking'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' }),
      ])
    },
  })

  function go(path: string) {
    onClose()
    navigate(path)
  }

  function goInsight(action?: NexiInsightAction) {
    if (action === 'unread') go(`/inbox?q=${encodeURIComponent('is:unread')}`)
    else if (action === 'tracking') go('/inbox?priority=1')
    else go('/control-center')
  }

  function openPriority(value?: PriorityDisplayItem) {
    if (!value) return
    const returnTo = `${location.pathname}${location.search}`
    onClose()
    navigate(`/message/${encodeURIComponent(value.item.accountId)}/${encodeURIComponent(value.item.messageId)}`, {
      state: {
        returnTo,
        controlCenterItem: value.item,
        manualTracking: value.manual,
      },
    })
  }

  function openCurrentComposer(mode: 'reply' | 'followUp') {
    if (!currentMessage.data) return
    onClose()
    navigate('/compose', {
      state: {
        mode,
        message: currentMessage.data,
        returnTo: location.pathname,
        returnState: location.state,
      },
    })
  }

  return <>
    {open && <button type="button" className="nexi-panel-backdrop" aria-label="Cerrar Nexi" onClick={onClose} />}
    <aside className={`nexi-assistant-panel ${open ? 'open' : ''}`} aria-hidden={!open} aria-label="Nexi, asistente inteligente de NexoMail">
      <header className="nexi-panel-header">
        <div className="nexi-panel-identity"><NexiVisual size="medium" /><div><strong>Nexi</strong><span>Asistente inteligente de NexoMail</span></div></div>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Cerrar Nexi"><X size={18} /></button>
      </header>

      <div className="nexi-panel-content">
        {context !== 'settings' && snapshot.isLoading && <div className="nexi-panel-loading"><span className="nexi-loading-line" /><span className="nexi-loading-line short" /><small>Revisando el contexto actual…</small></div>}
        {context !== 'settings' && snapshot.isError && <div className="notice">No fue posible recuperar los indicadores de Nexi.</div>}

        {context === 'settings' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Settings size={15} /><div><strong>{currentSettings.title}</strong><span>{currentSettings.description}</span></div></div>
          </section>
          <section className="nexi-panel-actions">
            <strong>Acciones de configuración</strong>
            {location.pathname !== '/settings/profile' && <button type="button" onClick={() => go('/settings/profile')}><span><UserRound size={14} /> Mi perfil</span></button>}
            {location.pathname !== '/settings/accounts' && <button type="button" onClick={() => go('/settings/accounts')}><span><Settings size={14} /> Configurar cuentas</span></button>}
            {location.pathname !== '/settings/appearance' && <button type="button" onClick={() => go('/settings/appearance')}><span><Palette size={14} /> Apariencia</span></button>}
          </section>
        </>}

        {snapshot.data && context === 'message' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Mail size={15} /><div><strong>Correo actual</strong><span>Nexi está usando el mensaje que tiene abierto como contexto.</span></div></div>
            {currentMessage.isLoading && <div className="nexi-context-card"><span>Cargando correo actual…</span></div>}
            {currentMessage.isError && <div className="nexi-context-card"><span>No fue posible recuperar el contexto de este correo.</span></div>}
            {currentMessage.data && <div className="nexi-context-card">
              <strong>{currentMessage.data.subject || '(Sin asunto)'}</strong>
              <span>De: {currentMessage.data.from.name || currentMessage.data.from.address}</span>
              {currentAccount && <small>{currentAccount.accountName}</small>}
              <div className="nexi-context-status">
                {currentPending
                  ? <span className="attention">{currentPending.direction === 'received' ? 'Pendiente de respuesta' : 'Esperando respuesta'} · {ageLabel(currentPending.since)}</span>
                  : <span>Sin pendiente automático detectado</span>}
                {currentTracked && <span className="tracked">Seguimiento manual activo</span>}
              </div>
            </div>}
          </section>

          <section className="nexi-panel-actions">
            <strong>Acciones sobre este correo</strong>
            {currentPending?.direction === 'received' && <button type="button" className="nexi-action-primary" disabled={!currentMessage.data} onClick={() => openCurrentComposer('reply')}><span><Reply size={14} /> Responder</span></button>}
            {currentPending?.direction === 'sent' && <button type="button" className="nexi-action-primary" disabled={!currentMessage.data} onClick={() => openCurrentComposer('followUp')}><span><Send size={14} /> Hacer seguimiento</span></button>}
            {canManualTrackCurrent && <button type="button" disabled={trackingMutation.isPending} onClick={() => trackingMutation.mutate()}><span>{currentTracked ? <Check size={14} /> : <Clock3 size={14} />} {currentTracked ? 'Quitar seguimiento manual' : 'Marcar para seguimiento'}</span></button>}
            {(currentPending || currentTracked) && <button type="button" onClick={() => go('/inbox?priority=1')}>Ver en seguimiento prioritario</button>}
            <button type="button" onClick={() => go('/control-center')}>Abrir Centro de control</button>
            {trackingMutation.isError && <div className="notice">{trackingMutation.error instanceof Error ? trackingMutation.error.message : 'No fue posible actualizar el seguimiento.'}</div>}
            <button type="button" disabled title="Disponible en una etapa posterior">Resumir este correo <small>Próximamente</small></button>
            <button type="button" disabled title="Disponible en una etapa posterior">Sugerir respuesta <small>Próximamente</small></button>
          </section>
        </>}

        {snapshot.data && context === 'control-center' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Hallazgos del Centro de control</strong><span>Aquí Nexi muestra sólo información adicional a las métricas que ya ve en pantalla.</span></div></div>
            <div className="nexi-context-insights">
              {insights.map(insight => insight.action
                ? <button type="button" key={insight.id} className="nexi-priority-callout" onClick={() => goInsight(insight.action)}><span><strong>{insight.title}</strong><br />{insight.description}</span></button>
                : <div key={insight.id} className="nexi-priority-clear"><strong>{insight.title}</strong><br />{insight.description}</div>)}
            </div>
          </section>
          <section className="nexi-panel-actions">
            <strong>Acciones desde esta vista</strong>
            {oldestPriority && <button type="button" className="nexi-action-primary" onClick={() => openPriority(oldestPriority)}>Abrir pendiente más antiguo <small>{ageLabel(oldestPriority.item.since)}</small></button>}
            <button type="button" onClick={() => go('/inbox?priority=1')}>Abrir seguimiento prioritario <small>{priorityItems.length}</small></button>
            <button type="button" onClick={() => go('/inbox')}>Volver a Bandeja de entrada</button>
          </section>
        </>}

        {snapshot.data && context === 'inbox' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Inbox size={15} /><div><strong>Contexto de la bandeja</strong><span>Resumen operativo del seguimiento entre sus cuentas.</span></div></div>
            <section className="nexi-panel-summary" aria-label="Información complementaria de la bandeja">
              <button type="button" onClick={() => go('/inbox?priority=1')}><Inbox size={16} /><span><b>{complementary.priorityCount}</b>Seguimiento total</span></button>
              <button type="button" onClick={() => go('/inbox?priority=1')}><Send size={16} /><span><b>{complementary.manualCount}</b>Marcados manual</span></button>
              <button type="button" onClick={() => go('/control-center')}><Mail size={16} /><span><b>{complementary.accountsWithPending}</b>Cuentas con pendientes</span></button>
              <button type="button" onClick={() => openPriority(oldestPriority)} disabled={!oldestPriority}><Clock3 size={16} /><span><b>{complementary.oldestAge}</b>Más antiguo</span></button>
            </section>
          </section>

          <section className="nexi-panel-priority">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Qué conviene revisar</strong><span>Hallazgos derivados del seguimiento actual.</span></div></div>
            {insights.map(insight => insight.action
              ? <button type="button" key={insight.id} className="nexi-priority-callout" onClick={() => goInsight(insight.action)}><span><strong>{insight.title}</strong><br />{insight.description}</span></button>
              : <div key={insight.id} className="nexi-priority-clear"><strong>{insight.title}</strong><br />{insight.description}</div>)}
          </section>

          <section className="nexi-panel-actions">
            <strong>Acciones rápidas</strong>
            {oldestPriority && <button type="button" className="nexi-action-primary" onClick={() => openPriority(oldestPriority)}>Abrir pendiente más antiguo <small>{ageLabel(oldestPriority.item.since)}</small></button>}
            <button type="button" onClick={() => go('/inbox?priority=1')}>Revisar seguimiento prioritario <small>{priorityItems.length}</small></button>
            <button type="button" onClick={() => go(`/inbox?q=${encodeURIComponent('is:unread')}`)}>Revisar correos sin leer <small>{snapshot.data.unread}</small></button>
          </section>
        </>}

        {snapshot.data && context === 'general' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Nexi en esta sección</strong><span>No hay un correo específico abierto. Puede revisar seguimiento o Centro de control.</span></div></div>
          </section>
          <section className="nexi-panel-actions">
            <strong>Accesos rápidos</strong>
            {oldestPriority && <button type="button" className="nexi-action-primary" onClick={() => openPriority(oldestPriority)}>Abrir pendiente más antiguo <small>{ageLabel(oldestPriority.item.since)}</small></button>}
            <button type="button" onClick={() => go('/inbox?priority=1')}>Revisar seguimientos <small>{priorityItems.length}</small></button>
            <button type="button" onClick={() => go('/control-center')}>Abrir Centro de control</button>
          </section>
        </>}
      </div>
    </aside>
  </>
}
