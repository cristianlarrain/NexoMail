import type { ReactNode } from 'react'
import { ChevronRight } from 'lucide-react'
import type { NexiInsightPriority } from './nexiInsights'

export function NexiInsightCard({ icon, title, description, priority, actionLabel, onAction }: {
  icon: ReactNode
  title: string
  description: string
  priority: NexiInsightPriority
  actionLabel?: string
  onAction?: () => void
}) {
  return <article className={`nexi-insight-card ${priority}`}>
    <span className="nexi-insight-icon" aria-hidden="true">{icon}</span>
    <div className="nexi-insight-copy">
      <strong>{title}</strong>
      <span>{description}</span>
    </div>
    {actionLabel && onAction && <button type="button" className="nexi-insight-action" onClick={onAction}>{actionLabel}<ChevronRight size={14} /></button>}
  </article>
}
