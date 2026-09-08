import { useEffect, useState } from 'react'
import { useIsFetching, useIsMutating } from '@tanstack/react-query'

export function GlobalActionIndicator() {
  const fetching = useIsFetching()
  const mutating = useIsMutating()
  const busy = fetching > 0 || mutating > 0
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    if (busy) {
      const timer = window.setTimeout(() => setVisible(true), 140)
      return () => window.clearTimeout(timer)
    }

    const timer = window.setTimeout(() => setVisible(false), 180)
    return () => window.clearTimeout(timer)
  }, [busy])

  if (!visible) return null

  const label = mutating > 0 ? 'Procesando acción…' : 'Cargando…'

  return <div className="global-action-indicator" role="status" aria-live="polite" aria-label={label}>
    <span>{label}</span>
    <i aria-hidden="true"><b /></i>
  </div>
}
