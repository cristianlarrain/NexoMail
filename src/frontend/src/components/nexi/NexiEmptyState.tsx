import type { ReactNode } from 'react'
import { NexiVisual } from './NexiVisual'

export function NexiEmptyState({ title, description, action, compact = false }: { title: string; description: string; action?: ReactNode; compact?: boolean }) {
  return <div className={`nexi-empty-state ${compact ? 'compact' : ''}`}>
    <NexiVisual size={compact ? 'medium' : 'large'} />
    <strong>{title}</strong>
    <span>{description}</span>
    {action && <div className="nexi-empty-action">{action}</div>}
  </div>
}
