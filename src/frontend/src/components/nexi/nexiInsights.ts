import type { ControlCenterSnapshot } from '../../types/mail'

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

export function buildNexiInsights(data: ControlCenterSnapshot, limit = 3): NexiInsight[] {
  const insights: NexiInsight[] = []

  if (data.overdue > 0) {
    insights.push({
      id: 'overdue',
      title: 'Pendientes antiguos',
      description: `${data.overdue} ${plural(data.overdue, 'conversación lleva', 'conversaciones llevan')} más de 48 horas sin resolverse.`,
      priority: 'high',
      action: 'overdue',
      actionLabel: 'Revisar pendientes',
    })
  }

  if (data.receivedWithoutReply > 0) {
    insights.push({
      id: 'received',
      title: 'Correos por responder',
      description: `Tiene ${data.receivedWithoutReply} ${plural(data.receivedWithoutReply, 'correo recibido pendiente', 'correos recibidos pendientes')} de respuesta.`,
      priority: data.receivedWithoutReply >= 10 ? 'high' : 'medium',
      action: 'received',
      actionLabel: 'Ver por responder',
    })
  }

  if (data.sentWithoutResponse > 0) {
    insights.push({
      id: 'sent',
      title: 'Esperando respuesta',
      description: `${data.sentWithoutResponse} ${plural(data.sentWithoutResponse, 'correo enviado todavía no recibe', 'correos enviados todavía no reciben')} respuesta.`,
      priority: 'medium',
      action: 'sent',
      actionLabel: 'Revisar enviados',
    })
  }

  if (data.unread > 0) {
    insights.push({
      id: 'unread',
      title: 'Correos sin leer',
      description: `Tiene ${data.unread} ${plural(data.unread, 'correo sin leer', 'correos sin leer')} entre sus cuentas disponibles.`,
      priority: 'info',
      action: 'unread',
      actionLabel: 'Ver sin leer',
    })
  }

  if (insights.length === 0) {
    insights.push({
      id: 'clear',
      title: 'Todo al día',
      description: 'Nexi no encontró mensajes que requieran atención inmediata en este momento.',
      priority: 'positive',
    })
  }

  return insights.slice(0, Math.max(1, limit))
}
