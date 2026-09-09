import type { AiMessageInsight, ControlCenterPendingItem } from '../../types/mail'

export type NexiPriorityCategory = 'urgent' | 'response' | 'follow_up' | 'informative' | 'probably_resolved'
export type NexiPrioritySource = 'rules' | 'nexi'

export interface NexiPriorityClassification {
  category: NexiPriorityCategory
  reason: string
  source: NexiPrioritySource
}

export interface PriorityDisplayItem {
  item: ControlCenterPendingItem
  automatic: boolean
  manual: boolean
}

const URGENT = /\b(urgente|urgencia|hoy|inmediato|inmediata|prioridad|plazo|vence|vencimiento|antes de|a más tardar|cuanto antes|asap|deadline)\b/i
const INFORMATIVE = /\b(informativo|información|aviso|comunicado|newsletter|boletín|notificación|recordatorio|para su conocimiento|fyi)\b/i
const RESOLVED = /\b(resuelto|resuelta|solucionado|solucionada|cerrado|cerrada|completado|completada|finalizado|finalizada|subsanado|subsanada)\b/i

export function priorityKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.messageId}`
}

export function isPriorityOverdue(item: ControlCenterPendingItem) {
  return Date.now() - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000
}

export function classifyByRules(entry: PriorityDisplayItem): NexiPriorityClassification {
  const { item, manual } = entry
  const subject = item.subject || ''

  if (URGENT.test(subject) || (item.direction === 'received' && isPriorityOverdue(item))) {
    return {
      category: 'urgent',
      reason: URGENT.test(subject) ? 'El asunto contiene una señal explícita de urgencia o plazo.' : 'Es una respuesta pendiente que supera las 48 horas.',
      source: 'rules',
    }
  }

  if (RESOLVED.test(subject)) {
    return { category: 'probably_resolved', reason: 'El asunto sugiere que la gestión podría estar cerrada o resuelta.', source: 'rules' }
  }

  if (INFORMATIVE.test(subject) && !manual) {
    return { category: 'informative', reason: 'El asunto parece principalmente informativo y no muestra una solicitud explícita.', source: 'rules' }
  }

  if (manual) {
    return { category: 'follow_up', reason: 'Fue marcado manualmente para seguimiento.', source: 'rules' }
  }

  if (item.direction === 'received') {
    return { category: 'response', reason: item.isRead ? 'La otra persona escribió al final y corresponde revisar una respuesta.' : 'Está sin leer y la otra persona escribió al final.', source: 'rules' }
  }

  return { category: 'follow_up', reason: 'Tú escribiste al final y la conversación sigue sin respuesta.', source: 'rules' }
}

export function classifyFromInsight(entry: PriorityDisplayItem, insight: AiMessageInsight): NexiPriorityClassification {
  const base = classifyByRules(entry)
  if (base.category === 'urgent') return base

  const text = `${insight.summary} ${insight.meaning} ${insight.requestedAction ?? ''}`
  if (URGENT.test(text)) return { category: 'urgent', reason: 'Nexi detectó urgencia, plazo o necesidad de atención inmediata en el contenido.', source: 'nexi' }
  if (RESOLVED.test(text) && !insight.requestedAction) return { category: 'probably_resolved', reason: 'Nexi detectó señales de que el asunto probablemente ya quedó resuelto.', source: 'nexi' }
  if (!insight.requestedAction) return { category: 'informative', reason: 'Nexi no detectó una acción concreta solicitada en el contenido.', source: 'nexi' }
  if (entry.item.direction === 'sent') return { category: 'follow_up', reason: 'Nexi detectó una gestión que conviene mantener en seguimiento.', source: 'nexi' }
  return { category: 'response', reason: 'Nexi detectó una solicitud concreta que requiere respuesta o gestión.', source: 'nexi' }
}

export const PRIORITY_ORDER: Record<NexiPriorityCategory, number> = {
  urgent: 0,
  response: 1,
  follow_up: 2,
  informative: 3,
  probably_resolved: 4,
}
