import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Clock3, Inbox, Mail, Send, Sparkles, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../../api/mailApi'
import { NexiVisual } from './NexiVisual'

function messageKey(accountId: string, messageId: string) {
  return `${accountId}:${messageId}`
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

  const priorityCount = useMemo(() => {
    const keys = new Set<string>()
    snapshot.data?.pendingItems.forEach(item => keys.add(messageKey(item.accountId, item.messageId)))
    tracking.data?.forEach(item => keys.add(messageKey(item.accountId, item.messageId)))
    return keys.size
  }, [snapshot.data?.pendingItems, tracking.data])

  function go(path: string) {
    onClose()
    navigate(path)
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
          <section className="nexi-panel-summary" aria-label="Resumen de pendientes">
            <button type="button" onClick={() => go('/control-center')}><Inbox size={16} /><span><b>{snapshot.data.receivedWithoutReply}</b>Por responder</span></button>
            <button type="button" onClick={() => go('/control-center')}><Send size={16} /><span><b>{snapshot.data.sentWithoutResponse}</b>Sin respuesta</span></button>
            <button type="button" onClick={() => go(`/inbox?q=${encodeURIComponent('is:unread')}`)}><Mail size={16} /><span><b>{snapshot.data.unread}</b>Sin leer</span></button>
            <button type="button" onClick={() => go('/inbox?tracking=priority')}><Clock3 size={16} /><span><b>{priorityCount}</b>Seguimiento</span></button>
          </section>

          <section className="nexi-panel-priority">
            <div className="nexi-section-heading"><Sparkles size={15} /><div><strong>Atención sugerida</strong><span>Basado en los indicadores reales de NexoMail.</span></div></div>
            {snapshot.data.overdue > 0
              ? <button type="button" className="nexi-priority-callout" onClick={() => go('/control-center')}><b>{snapshot.data.overdue}</b><span>{snapshot.data.overdue === 1 ? 'conversación lleva' : 'conversaciones llevan'} más de 48 horas pendiente{snapshot.data.overdue === 1 ? '' : 's'}.</span></button>
              : <div className="nexi-priority-clear">No hay pendientes de más de 48 horas.</div>}
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
