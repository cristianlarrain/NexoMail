import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, ChevronRight, CircleHelp, Info, LoaderCircle, MessageSquareReply, Sparkles, TimerReset } from 'lucide-react'
import { nexiApi } from '../api/nexiApi'
import type { ControlCenterPendingItem } from '../types/mail'
import { NexiEmptyState } from './nexi/NexiEmptyState'
import {
  classifyByRules,
  classifyFromInsight,
  PRIORITY_ORDER,
  priorityKey,
  type NexiPriorityCategory,
  type NexiPriorityClassification,
  type PriorityDisplayItem,
} from './nexi/priorityEngine'

type PriorityFilter = 'all' | NexiPriorityCategory

const CATEGORY_META: Record<NexiPriorityCategory, { label: string; short: string; icon: typeof AlertTriangle }> = {
  urgent: { label: 'Urgente', short: 'Urgentes', icon: AlertTriangle },
  response: { label: 'Requiere respuesta', short: 'Responder', icon: MessageSquareReply },
  follow_up: { label: 'Seguimiento', short: 'Seguimiento', icon: TimerReset },
  informative: { label: 'Informativo', short: 'Informativos', icon: Info },
  probably_resolved: { label: 'Probablemente resuelto', short: 'Prob. resueltos', icon: CheckCircle2 },
}

function rowKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.conversationId}:${item.messageId}`
}

function ageLabel(value: string) {
  const elapsedMinutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60_000))
  if (elapsedMinutes < 60) return `hace ${Math.max(1, elapsedMinutes)} min`
  const hours = Math.floor(elapsedMinutes / 60)
  if (hours < 24) return `hace ${hours} h`
  const days = Math.floor(hours / 24)
  return `hace ${days} día${days === 1 ? '' : 's'}`
}

export function NexiPriorityQueue({
  items,
  openingTarget,
  onManage,
  onOpen,
}: {
  items: PriorityDisplayItem[]
  openingTarget: string | null
  onManage: (item: ControlCenterPendingItem) => void
  onOpen: (item: ControlCenterPendingItem, manual: boolean) => void
}) {
  const [filter, setFilter] = useState<PriorityFilter>('all')
  const [visible, setVisible] = useState(10)
  const [semantic, setSemantic] = useState<Record<string, NexiPriorityClassification>>({})
  const [semanticError, setSemanticError] = useState('')

  const classified = useMemo(() => items.map(entry => {
    const key = priorityKey(entry.item)
    const classification = semantic[key] ?? classifyByRules(entry)
    return { entry, classification }
  }).sort((left, right) => {
    const priority = PRIORITY_ORDER[left.classification.category] - PRIORITY_ORDER[right.classification.category]
    if (priority !== 0) return priority
    return new Date(left.entry.item.since).getTime() - new Date(right.entry.item.since).getTime()
  }), [items, semantic])

  const counts = useMemo(() => classified.reduce((result, value) => {
    result[value.classification.category] += 1
    return result
  }, { urgent: 0, response: 0, follow_up: 0, informative: 0, probably_resolved: 0 } as Record<NexiPriorityCategory, number>), [classified])

  const filtered = filter === 'all' ? classified : classified.filter(value => value.classification.category === filter)
  const shown = filtered.slice(0, visible)

  const candidates = useMemo(() => classified
    .filter(value => !semantic[priorityKey(value.entry.item)])
    .slice(0, 5), [classified, semantic])

  const refine = useMutation({
    mutationFn: async () => {
      const results: Array<{ key: string; classification: NexiPriorityClassification }> = []
      let failed = 0
      for (const candidate of candidates) {
        const { entry } = candidate
        try {
          const insight = await nexiApi.summarizeMessage(entry.item.accountId, entry.item.messageId, false)
          results.push({ key: priorityKey(entry.item), classification: classifyFromInsight(entry, insight) })
        } catch {
          failed += 1
        }
      }
      if (results.length === 0 && failed > 0) throw new Error('Nexi no pudo revisar el contenido de estos correos.')
      return { results, failed }
    },
    onMutate: () => setSemanticError(''),
    onSuccess: value => {
      setSemantic(current => {
        const next = { ...current }
        value.results.forEach(result => { next[result.key] = result.classification })
        return next
      })
      if (value.failed > 0) setSemanticError(`${value.failed} correo${value.failed === 1 ? '' : 's'} no pudo${value.failed === 1 ? '' : 'ieron'} analizarse; se mantiene su clasificación por reglas.`)
    },
    onError: error => setSemanticError(error instanceof Error ? error.message : 'Nexi no pudo afinar la priorización.'),
  })

  return <article className="control-panel nexi-priority-panel" aria-label="Priorización inteligente">
    <header className="nexi-priority-header">
      <div>
        <span className="nexi-priority-kicker"><Sparkles size={14} /> Priorización inteligente</span>
        <strong>Qué atender primero</strong>
        <p>Ordena las conversaciones por urgencia, necesidad de respuesta, seguimiento o carácter informativo. Nexi puede revisar el contenido bajo demanda.</p>
      </div>
      {items.length > 0 && <button type="button" className="secondary-button nexi-priority-refine" disabled={refine.isPending || candidates.length === 0} onClick={() => refine.mutate()}>
        {refine.isPending ? <LoaderCircle size={14} className="spin" /> : candidates.length === 0 ? <CheckCircle2 size={14} /> : <Sparkles size={14} />}
        {refine.isPending ? 'Analizando…' : candidates.length === 0 ? 'Revisado con Nexi' : 'Afinar con Nexi'}
      </button>}
    </header>

    <div className="nexi-priority-summary" aria-label="Filtros de priorización">
      <button type="button" className={filter === 'all' ? 'active all' : 'all'} onClick={() => { setFilter('all'); setVisible(10) }}><CircleHelp size={14} /><span>Todos</span><b>{classified.length}</b></button>
      {(Object.keys(CATEGORY_META) as NexiPriorityCategory[]).map(category => {
        const Icon = CATEGORY_META[category].icon
        return <button type="button" key={category} className={`${filter === category ? 'active ' : ''}${category}`} onClick={() => { setFilter(category); setVisible(10) }}>
          <Icon size={14} /><span>{CATEGORY_META[category].short}</span><b>{counts[category]}</b>
        </button>
      })}
    </div>

    {semanticError && <div className="notice nexi-priority-notice">{semanticError}</div>}

    <div className="nexi-priority-list">
      {shown.length === 0 ? <NexiEmptyState compact title={items.length === 0 ? 'Sin pendientes' : 'Sin correos en esta categoría'} description={items.length === 0 ? 'No hay conversaciones pendientes ni correos marcados para seguimiento.' : 'No hay conversaciones clasificadas en este grupo.'} /> : shown.map(({ entry, classification }) => {
        const { item, automatic, manual } = entry
        const meta = CATEGORY_META[classification.category]
        const Icon = meta.icon
        const canManage = classification.category === 'urgent' || classification.category === 'response' || classification.category === 'follow_up'
        const opening = openingTarget === rowKey(item)
        return <div className={`nexi-priority-row ${classification.category}`} key={priorityKey(item)}>
          <i className="account-dot" style={{ background: item.accountColor }} />
          <button type="button" className="nexi-priority-main" onClick={() => onOpen(item, manual)}>
            <span className={`nexi-priority-badge ${classification.category}`}><Icon size={12} />{meta.label}</span>
            <strong>{item.subject}</strong>
            <span>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</span>
            <small>{item.accountName} · {ageLabel(item.since)} · {manual && automatic ? 'Manual + automático' : manual ? 'Manual' : 'Automático'}</small>
            <em className={classification.source}>{classification.source === 'nexi' ? 'Nexi' : 'Regla'} · {classification.reason}</em>
          </button>
          <div className="nexi-priority-actions">
            {canManage && <button type="button" className="secondary-button compact-action" disabled={opening} onClick={() => onManage(item)}>{opening ? <LoaderCircle size={13} className="spin" /> : <MessageSquareReply size={13} />}{item.direction === 'received' ? 'Responder' : 'Seguimiento'}</button>}
            <button type="button" className="icon-button" title="Ver correo" aria-label="Ver correo" onClick={() => onOpen(item, manual)}><ChevronRight size={16} /></button>
          </div>
        </div>
      })}
    </div>

    <footer className="nexi-priority-footer">
      <span>Mostrando {Math.min(visible, filtered.length)} de {filtered.length}{Object.keys(semantic).length > 0 ? ` · ${Object.keys(semantic).length} revisados por Nexi` : ''}</span>
      {visible < filtered.length && <button type="button" className="secondary-button" onClick={() => setVisible(current => current + 10)}>Cargar más</button>}
      {candidates.length > 0 && !refine.isPending && <small>Nexi revisa hasta 5 correos por tanda para mantener rápida esta vista.</small>}
    </footer>
  </article>
}
