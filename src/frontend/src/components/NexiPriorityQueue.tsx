import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, ChevronRight, CircleHelp, Info, MessageSquareReply, Sparkles, TimerReset, X } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { nexiApi } from '../api/nexiApi'
import type { AiMessageInsight, ControlCenterPendingItem } from '../types/mail'
import { NexiEmptyState } from './nexi/NexiEmptyState'
import { NexiVisual } from './nexi/NexiVisual'
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
type ContextFocus = 'overdue' | 'received' | 'sent' | 'person' | 'topic'

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

function isOverdue(item: ControlCenterPendingItem) {
  return Date.now() - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000
}

function normalizeSubject(value: string) {
  return value
    .replace(/^\s*(?:(?:re|rv|fwd|fw)\s*:\s*)+/i, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLocaleLowerCase('es')
}

function validFocus(value: string | null): ContextFocus | null {
  return value === 'overdue' || value === 'received' || value === 'sent' || value === 'person' || value === 'topic' ? value : null
}

function focusLabel(focus: ContextFocus | null, value: string) {
  if (focus === 'overdue') return 'Más de 48 horas'
  if (focus === 'received') return 'Recibidos sin responder'
  if (focus === 'sent') return 'Enviados sin respuesta'
  if (focus === 'person') return value ? `Persona · ${value}` : 'Persona'
  if (focus === 'topic') return value ? `Tema · ${value}` : 'Tema'
  return ''
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
  const [params, setParams] = useSearchParams()
  const panelRef = useRef<HTMLElement>(null)
  const [filter, setFilter] = useState<PriorityFilter>('all')
  const [visible, setVisible] = useState(10)
  const [semantic, setSemantic] = useState<Record<string, NexiPriorityClassification>>({})
  const [rowInsights, setRowInsights] = useState<Record<string, AiMessageInsight>>({})
  const [summarizingTarget, setSummarizingTarget] = useState<string | null>(null)
  const [semanticError, setSemanticError] = useState('')
  const focus = validFocus(params.get('focus'))
  const focusValue = params.get('value')?.trim() ?? ''

  const classified = useMemo(() => items.map(entry => {
    const key = priorityKey(entry.item)
    const classification = semantic[key] ?? classifyByRules(entry)
    return { entry, classification }
  }).sort((left, right) => {
    const priority = PRIORITY_ORDER[left.classification.category] - PRIORITY_ORDER[right.classification.category]
    if (priority !== 0) return priority
    return new Date(left.entry.item.since).getTime() - new Date(right.entry.item.since).getTime()
  }), [items, semantic])

  const focused = useMemo(() => {
    if (!focus) return classified
    const normalizedValue = focusValue.toLocaleLowerCase('es')
    const normalizedTopic = normalizeSubject(focusValue)
    return classified.filter(({ entry }) => {
      const item = entry.item
      if (focus === 'overdue') return isOverdue(item)
      if (focus === 'received' || focus === 'sent') return item.direction === focus
      if (focus === 'person') return normalizedValue.length > 0 && item.counterpart.trim().toLocaleLowerCase('es') === normalizedValue
      if (focus === 'topic') return normalizedTopic.length > 0 && normalizeSubject(item.subject) === normalizedTopic
      return true
    })
  }, [classified, focus, focusValue])

  useEffect(() => {
    if (!focus) return
    setFilter('all')
    setVisible(10)
    window.requestAnimationFrame(() => panelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }))
  }, [focus, focusValue])

  const counts = useMemo(() => focused.reduce((result, value) => {
    result[value.classification.category] += 1
    return result
  }, { urgent: 0, response: 0, follow_up: 0, informative: 0, probably_resolved: 0 } as Record<NexiPriorityCategory, number>), [focused])

  const filtered = filter === 'all' ? focused : focused.filter(value => value.classification.category === filter)
  const shown = filtered.slice(0, visible)

  const candidates = useMemo(() => focused
    .filter(value => !semantic[priorityKey(value.entry.item)])
    .slice(0, 5), [focused, semantic])

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

  async function summarizeRow(item: ControlCenterPendingItem) {
    const key = priorityKey(item)
    if (rowInsights[key]) return
    setSummarizingTarget(key)
    setSemanticError('')
    try {
      const insight = await nexiApi.summarizeMessage(item.accountId, item.messageId, false)
      setRowInsights(current => ({ ...current, [key]: insight }))
    } catch (error) {
      setSemanticError(error instanceof Error ? error.message : 'No fue posible resumir este correo.')
    } finally {
      setSummarizingTarget(null)
    }
  }

  function clearContextFocus() {
    const next = new URLSearchParams(params)
    next.delete('focus')
    next.delete('value')
    setParams(next, { replace: true })
    setFilter('all')
    setVisible(10)
  }

  return <article ref={panelRef} className="control-panel nexi-priority-panel nexi-priority-featured" aria-label="Priorización inteligente">
    <header className="nexi-priority-header">
      <div>
        <span className="nexi-priority-kicker"><Sparkles size={14} /> Priorización inteligente</span>
        <strong>Qué atender primero</strong>
        <p>Nexi ordena las conversaciones y permite resumir, responder o dar seguimiento desde la misma grilla.</p>
      </div>
      {items.length > 0 && <button type="button" className="secondary-button nexi-priority-refine" disabled={refine.isPending || candidates.length === 0} onClick={() => refine.mutate()}>
        {refine.isPending ? <NexiVisual size="small" className="nexi-inline-processing" /> : candidates.length === 0 ? <CheckCircle2 size={14} /> : <Sparkles size={14} />}
        {refine.isPending ? 'Analizando…' : candidates.length === 0 ? 'Revisado con Nexi' : 'Revisar con Nexi'}
      </button>}
    </header>

    {focus && <div className="nexi-priority-context-filter">
      <span>Mostrando pendientes relacionados con <strong>{focusLabel(focus, focusValue)}</strong></span>
      <button type="button" onClick={clearContextFocus}><X size={13} /> Quitar filtro</button>
    </div>}

    <div className="nexi-priority-summary" aria-label="Filtros de priorización">
      <button type="button" className={filter === 'all' ? 'active all' : 'all'} onClick={() => { setFilter('all'); setVisible(10) }}><CircleHelp size={14} /><span>Todos</span><b>{focused.length}</b></button>
      {(Object.keys(CATEGORY_META) as NexiPriorityCategory[]).map(category => {
        const Icon = CATEGORY_META[category].icon
        return <button type="button" key={category} className={`${filter === category ? 'active ' : ''}${category}`} onClick={() => { setFilter(category); setVisible(10) }}>
          <Icon size={14} /><span>{CATEGORY_META[category].short}</span><b>{counts[category]}</b>
        </button>
      })}
    </div>

    {semanticError && <div className="notice nexi-priority-notice">{semanticError}</div>}

    <div className="nexi-priority-list">
      {shown.length === 0 ? <NexiEmptyState compact title={items.length === 0 ? 'Sin pendientes' : focus ? 'Sin pendientes relacionados' : 'Sin correos en esta categoría'} description={items.length === 0 ? 'No hay conversaciones pendientes ni correos marcados para seguimiento.' : focus ? 'No quedan conversaciones pendientes que coincidan con este foco.' : 'No hay conversaciones clasificadas en este grupo.'} /> : shown.map(({ entry, classification }) => {
        const { item, automatic, manual } = entry
        const key = priorityKey(item)
        const insight = rowInsights[key]
        const meta = CATEGORY_META[classification.category]
        const Icon = meta.icon
        const canManage = classification.category === 'urgent' || classification.category === 'response' || classification.category === 'follow_up'
        const opening = openingTarget === rowKey(item)
        const summarizing = summarizingTarget === key
        return <div className={`nexi-priority-row ${classification.category} ${insight ? 'has-insight' : ''}`} key={key}>
          <i className="account-dot" style={{ background: item.accountColor }} />
          <button type="button" className="nexi-priority-main" onClick={() => onOpen(item, manual)}>
            <span className={`nexi-priority-badge ${classification.category}`}><Icon size={12} />{meta.label}</span>
            <strong>{item.subject}</strong>
            <span>{item.direction === 'received' ? 'De' : 'Para'}: {item.counterpart}</span>
            <small>{item.accountName} · {ageLabel(item.since)} · {manual && automatic ? 'Manual + automático' : manual ? 'Manual' : 'Automático'}</small>
            <em className={classification.source}>{classification.source === 'nexi' ? 'Nexi' : 'Regla'} · {classification.reason}</em>
          </button>
          <div className="nexi-priority-actions">
            <button type="button" className="secondary-button compact-action" disabled={summarizing} onClick={() => void summarizeRow(item)}>{summarizing ? <NexiVisual size="small" className="nexi-inline-processing" /> : insight ? <CheckCircle2 size={13} /> : <Sparkles size={13} />}{insight ? 'Resumen listo' : 'Resumir'}</button>
            {canManage && <button type="button" className="secondary-button compact-action" disabled={opening} onClick={() => onManage(item)}>{opening ? <NexiVisual size="small" className="nexi-inline-processing" /> : <Sparkles size={13} />}{item.direction === 'received' ? 'Preparar respuesta' : 'Preparar seguimiento'}</button>}
            <button type="button" className="icon-button" title="Ver correo" aria-label="Ver correo" onClick={() => onOpen(item, manual)}><ChevronRight size={16} /></button>
          </div>
          {insight && <div className="nexi-priority-insight">
            <div><span>Resumen</span><p>{insight.summary}</p></div>
            {insight.requestedAction && <div><span>Acción</span><p>{insight.requestedAction}</p></div>}
            {insight.keyPoints.length > 0 && <div><span>Puntos clave</span><ul>{insight.keyPoints.slice(0, 3).map((point, index) => <li key={`${point}-${index}`}>{point}</li>)}</ul></div>}
          </div>}
        </div>
      })}
    </div>

    <footer className="nexi-priority-footer">
      <span>Mostrando {Math.min(visible, filtered.length)} de {filtered.length}{Object.keys(semantic).length > 0 ? ` · ${Object.keys(semantic).length} revisados por Nexi` : ''}</span>
      {visible < filtered.length && <button type="button" className="secondary-button" onClick={() => setVisible(current => current + 10)}>Cargar más</button>}
      {candidates.length > 0 && !refine.isPending && <small>Nexi revisa hasta 5 correos por tanda.</small>}
    </footer>
  </article>
}
