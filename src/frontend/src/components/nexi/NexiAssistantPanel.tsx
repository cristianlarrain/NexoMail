import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Clock3, Inbox, Mail, Send, Sparkles, X } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router-dom'
import { mailApi } from '../../api/mailApi'
import { NexiVisual } from './NexiVisual'
import { buildNexiInsights, type NexiInsightAction } from './nexiInsights'

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
  if (pathname === '/inbox' || pathname.startsWith('/account/')) return 'inbox'
  return 'general'
}

export function NexiAssistantPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const location = useLocation()
  const context = viewContext(location.pathname)
  const routeMessage = messageRoute(location.pathname)

  const snapshot = useQuery({
    queryKey: ['control-center', 'all'],
    queryFn: () => mailApi.controlCenter(),
    enabled: open,
    staleTime: 10 * 60_000,
    refetchOnWindowFocus: false,
  })
  const tracking = useQuery({
    queryKey: ['control-center-tracking', 'all'],
    queryFn: () => mailApi.controlCenterTrackedItems(),
    enabled: open,
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

  const complementary = useMemo(() => {
    const keys = new Set<string>()
    const items = new Map<string, { since: string }>()
    snapshot.data?.pendingItems.forEach(item => {
      const key = messageKey(item.accountId, item.messageId)
      keys.add(key)
      items.set(key, item)
    })
    tracking.data?.forEach(item => {
      const key = messageKey(item.accountId, item.messageId)
      keys.add(key)
      items.set(key, item)
    })

    const manualCount = new Set((tracking.data ?? []).map(item => messageKey(item.accountId, item.messageId))).size
    const accountsWithPending = snapshot.data?.accounts.filter(account => account.receivedWithoutReply + account.sentWithoutResponse > 0).length ?? 0
    const oldest = [...items.values()].sort((left, right) => new Date(left.since).getTime() - new Date(right.since).getTime())[0]

    return {
      priorityCount: keys.size,
      manualCount,
      accountsWithPending,
      oldestAge: ageLabel(oldest?.since),
    }
  }, [snapshot.data?.accounts, snapshot.data?.pendingItems, tracking.data])

  const currentPending = routeMessage
    ? snapshot.data?.pendingItems.find(item => item.accountId === routeMessage.accountId && item.messageId === routeMessage.messageId)
    : undefined
  const currentTracked = routeMessage
    ? (tracking.data ?? []).some(item => item.accountId === routeMessage.accountId && item.messageId === routeMessage.messageId)
    : false
  const currentAccount = routeMessage
    ? snapshot.data?.accounts.find(account => account.accountId === routeMessage.accountId)
    : undefined

  const insights = snapshot.data ? buildNexiInsights(snapshot.data, tracking.data ?? [], context === 'control-center' ? 3 : 2) : []

  function go(path: string) {
    onClose()
    navigate(path)
  }

  function goInsight(action?: NexiInsightAction) {
    if (action === 'unread') go(`/inbox?q=${encodeURIComponent('is:unread')}`)
    else go('/inbox?tracking=priority')
  }

  return <>
    {open && <button type="button" className="nexi-panel-backdrop" aria-label="Cerrar Nexi" onClick={onClose} />}
    <aside className={`nexi-assistant-panel ${open ? 'open' : ''}`} aria-hidden={!open} aria-label="Nexi, asistente inteligente de NexoMail">
      <header className="nexi-panel-header">
        <div className="nexi-panel-identity"><NexiVisual size="medium" /><div><strong>Nexi</strong><span>Asistente inteligente de NexoMail</span></div></div>
        <button type="button" className="icon-button" onClick={onClose} aria-label="Cerrar Nexi"><X size={18} /></button>
      </header>

      <div className="nexi-panel-content">
        {snapshot.isLoading && <div className="nexi-panel-loading"><span className="nexi-loading-line" /><span className="nexi-loading-line short" /><small>Revisando el contexto actual…</small></div>}
        {snapshot.isError && <div className="notice">No fue posible recuperar los indicadores de Nexi.</div>}

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
            <strong>Acciones según este correo</strong>
            {(currentPending || currentTracked) && <button type="button" onClick={() => go('/inbox?tracking=priority')}>Ver en seguimiento prioritario</button>}
            <button type="button" onClick={() => go('/control-center')}>Abrir Centro de control</button>
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
            <strong>Accesos desde esta vista</strong>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}>Abrir seguimiento prioritario</button>
            <button type="button" onClick={() => go('/inbox')}>Volver a Bandeja de entrada</button>
          </section>
        </>}

        {snapshot.data && context === 'inbox' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Inbox size={15} /><div><strong>Contexto de la bandeja</strong><span>Resumen operativo del seguimiento entre sus cuentas.</span></div></div>
            <section className="nexi-panel-summary" aria-label="Información complementaria de la bandeja">
              <button type="button" onClick={() => go('/inbox?tracking=priority')}><Inbox size={16} /><span><b>{complementary.priorityCount}</b>Seguimiento total</span></button>
              <button type="button" onClick={() => go('/inbox?tracking=priority')}><Send size={16} /><span><b>{complementary.manualCount}</b>Marcados manual</span></button>
              <button type="button" onClick={() => go('/control-center')}><Mail size={16} /><span><b>{complementary.accountsWithPending}</b>Cuentas con pendientes</span></button>
              <button type="button" onClick={() => go('/inbox?tracking=priority')}><Clock3 size={16} /><span><b>{complementary.oldestAge}</b>Más antiguo</span></button>
            </section>
          </section>

          <section className="nexi-panel-priority">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Qué conviene revisar</strong><span>Hallazgos derivados del seguimiento actual.</span></div></div>
            {insights.map(insight => insight.action
              ? <button type="button" key={insight.id} className="nexi-priority-callout" onClick={() => goInsight(insight.action)}><span><strong>{insight.title}</strong><br />{insight.description}</span></button>
              : <div key={insight.id} className="nexi-priority-clear"><strong>{insight.title}</strong><br />{insight.description}</div>)}
          </section>
        </>}

        {snapshot.data && context === 'general' && <>
          <section className="nexi-context-block">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Nexi en esta sección</strong><span>No hay un correo específico abierto. Puede revisar seguimiento o Centro de control.</span></div></div>
          </section>
          <section className="nexi-panel-actions">
            <strong>Accesos rápidos</strong>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}>Revisar seguimientos</button>
            <button type="button" onClick={() => go('/control-center')}>Abrir Centro de control</button>
          </section>
        </>}
      </div>
    </aside>
  </>
}
