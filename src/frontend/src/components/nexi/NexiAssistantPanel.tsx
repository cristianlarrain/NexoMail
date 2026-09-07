import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Clock3, Inbox, Mail, Send, Sparkles, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
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

export function NexiAssistantPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
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

  const insights = snapshot.data ? buildNexiInsights(snapshot.data, tracking.data ?? [], 2) : []

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
        {snapshot.isLoading && <div className="nexi-panel-loading"><span className="nexi-loading-line" /><span className="nexi-loading-line short" /><small>Revisando sus cuentas…</small></div>}
        {snapshot.isError && <div className="notice">No fue posible recuperar los indicadores de Nexi.</div>}
        {snapshot.data && <>
          <section className="nexi-panel-summary" aria-label="Información complementaria de Nexi">
            <button type="button" onClick={() => go('/inbox?tracking=priority')}><Inbox size={16} /><span><b>{complementary.priorityCount}</b>Seguimiento total</span></button>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}><Send size={16} /><span><b>{complementary.manualCount}</b>Marcados manual</span></button>
            <button type="button" onClick={() => go('/control-center')}><Mail size={16} /><span><b>{complementary.accountsWithPending}</b>Cuentas con pendientes</span></button>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}><Clock3 size={16} /><span><b>{complementary.oldestAge}</b>Más antiguo</span></button>
          </section>

          <section className="nexi-panel-priority">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Hallazgos complementarios</strong><span>Nexi evita repetir las tarjetas principales y destaca relaciones entre sus datos.</span></div></div>
            {insights.map(insight => insight.action
              ? <button type="button" key={insight.id} className="nexi-priority-callout" onClick={() => goInsight(insight.action)}><span><strong>{insight.title}</strong><br />{insight.description}</span></button>
              : <div key={insight.id} className="nexi-priority-clear"><strong>{insight.title}</strong><br />{insight.description}</div>)}
          </section>

          <section className="nexi-panel-actions">
            <strong>Accesos rápidos</strong>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}>Revisar seguimientos</button>
            <button type="button" onClick={() => go('/control-center')}>Revisar pendientes</button>
            <button type="button" disabled title="Disponible en una etapa posterior">Resumir pendientes <small>Próximamente</small></button>
            <button type="button" disabled title="Disponible en una etapa posterior">Priorizar correos <small>Próximamente</small></button>
            <button type="button" disabled title="Disponible en una etapa posterior">Buscar correos importantes <small>Próximamente</small></button>
          </section>
        </>}
      </div>
    </aside>
  </>
}
