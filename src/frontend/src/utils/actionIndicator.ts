export const ACTION_START_EVENT = 'nexomail:action-start'
export const ACTION_END_EVENT = 'nexomail:action-end'

export type ActionStartDetail = { id: string; label: string }
export type ActionEndDetail = { id: string }

export async function withActionIndicator<T>(label: string, operation: () => Promise<T>): Promise<T> {
  const id = typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`

  window.dispatchEvent(new CustomEvent<ActionStartDetail>(ACTION_START_EVENT, { detail: { id, label } }))
  try {
    return await operation()
  } finally {
    window.dispatchEvent(new CustomEvent<ActionEndDetail>(ACTION_END_EVENT, { detail: { id } }))
  }
}
