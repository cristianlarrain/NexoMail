import type { ReactNode } from 'react'
import { NexiVisual } from './NexiVisual'

export function NexiEmptyState({ title, description, action }: { title: string; description: string; action?: ReactNode }) {
  return <div className="nexi-empty-state">
    <NexiVisual size="large" />
    <strong>{title}</strong>
    <span>{description}</span>
    {action && <div className="nexi-empty-action">{action}</div>}
  </div>
}
