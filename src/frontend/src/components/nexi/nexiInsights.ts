import type { ControlCenterPendingItem, ControlCenterSnapshot } from '../../types/mail'

export type NexiInsightPriority = 'high' | 'medium' | 'info' | 'positive'
export type NexiInsightAction = 'received' | 'sent' | 'unread' | 'overdue' | 'tracking'

export interface NexiInsight {
  id: string
  title: string
  description: string
  priority: NexiInsightPriority
  action?: NexiInsightAction
  actionLabel?: string
}

function plural(value: number, singular: string, pluralForm: string) {
  return value === 1 ? singular : pluralForm
}

function messageKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.messageId}`
}

function ageLabel(value: string) {
  const elapsedHours = Math.max(1, Math.floor((Date.now() - new Date(value).getTime()) / 3_600_000))
  if (elapsedHours < 24) return `${elapsedHours} h`
  const days = Math.floor(elapsedHours / 24)
  return `${days} ${plural(days, 'día', 'días')}`
}

export function buildNexiInsights(data: ControlCenterSnapshot, manualTracking: ControlCenterPendingItem[] = [], limit = 3): NexiInsight[] {
  const insights: NexiInsight[] = []
  const combined = new Map<string, ControlCenterPendingItem>()
  data.pendingItems.forEach(item => combined.set(messageKey(item), item))
  manualTracking.forEach(item => combined.set(messageKey(item), item))

  const priorityItems = [...combined.values()].sort((left, right) => new Date(left.since).getTime() - new Date(right.since).getTime())
  const manualCount = new Set(manualTracking.map(messageKey)).size
  const accountsWithPending = data.accounts.filter(account => account.receivedWithoutReply + account.sentWithoutResponse > 0)
  const topPendingAccount = [...data.accounts]
    .map(account => ({ account, pending: account.receivedWithoutReply + account.sentWithoutResponse }))
    .sort((left, right) => right.pending - left.pending)[0]
  const oldest = priorityItems[0]

  if (manualCount > 0) {
    insights.push({
      id: 'manual-tracking',
      title: 'Marcados manualmente',
      description: `${manualCount} ${plural(manualCount, 'correo fue marcado', 'correos fueron marcados')} manualmente para seguimiento.`,
      priority: 'info',
      action: 'tracking',
      actionLabel: 'Revisar marcados',
    })
  }

  if (topPendingAccount && topPendingAccount.pending > 0 && data.accounts.length > 1) {
    insights.push({
      id: 'pending-concentration',
      title: 'Mayor concentración de pendientes',
      description: `${topPendingAccount.account.accountName} concentra ${topPendingAccount.pending} ${plural(topPendingAccount.pending, 'pendiente', 'pendientes')} entre respuestas y seguimientos.`,
      priority: topPendingAccount.pending >= 10 ? 'medium' : 'info',
      action: 'tracking',
      actionLabel: 'Revisar seguimiento',
    })
  } else if (accountsWithPending.length > 1) {
    insights.push({
      id: 'pending-accounts',
      title: 'Pendientes distribuidos',
      description: `${accountsWithPending.length} cuentas tienen conversaciones que requieren atención.`,
      priority: 'info',
      action: 'tracking',
      actionLabel: 'Ver seguimiento',
    })
  }

  if (oldest) {
    insights.push({
      id: 'oldest-pending',
      title: 'Pendiente más antiguo',
      description: `La conversación más antigua del seguimiento lleva ${ageLabel(oldest.since)} pendiente: “${oldest.subject}”.`,
      priority: Date.now() - new Date(oldest.since).getTime() >= 48 * 60 * 60 * 1000 ? 'high' : 'info',
      action: 'tracking',
      actionLabel: 'Ver seguimiento',
    })
  }

  if (insights.length === 0) {
    insights.push({
      id: 'clear',
      title: 'Sin hallazgos adicionales',
      description: 'Nexi no encontró información complementaria que requiera su atención en este momento.',
      priority: 'positive',
    })
  }

  return insights.slice(0, Math.max(1, limit))
}
