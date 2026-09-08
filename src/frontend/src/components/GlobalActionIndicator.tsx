import { useEffect, useMemo, useRef, useState } from 'react'
import { ACTION_END_EVENT, ACTION_START_EVENT, type ActionEndDetail, type ActionStartDetail } from '../utils/actionIndicator'

type ActiveAction = ActionStartDetail & { order: number }

export function GlobalActionIndicator() {
  const [actions, setActions] = useState<ActiveAction[]>([])
  const [visible, setVisible] = useState(false)
  const order = useRef(0)

  useEffect(() => {
    function onStart(event: Event) {
      const detail = (event as CustomEvent<ActionStartDetail>).detail
      if (!detail?.id || !detail.label) return
      order.current += 1
      setActions(current => [...current.filter(item => item.id !== detail.id), { ...detail, order: order.current }])
    }

    function onEnd(event: Event) {
      const detail = (event as CustomEvent<ActionEndDetail>).detail
      if (!detail?.id) return
      setActions(current => current.filter(item => item.id !== detail.id))
    }

    window.addEventListener(ACTION_START_EVENT, onStart)
    window.addEventListener(ACTION_END_EVENT, onEnd)
    return () => {
      window.removeEventListener(ACTION_START_EVENT, onStart)
      window.removeEventListener(ACTION_END_EVENT, onEnd)
    }
  }, [])

  const current = useMemo(() => [...actions].sort((left, right) => right.order - left.order)[0], [actions])

  useEffect(() => {
    if (current) {
      const timer = window.setTimeout(() => setVisible(true), 160)
      return () => window.clearTimeout(timer)
    }
    const timer = window.setTimeout(() => setVisible(false), 180)
    return () => window.clearTimeout(timer)
  }, [current])

  if (!visible || !current) return null

  return <div className="global-action-indicator" role="status" aria-live="polite" aria-label={current.label}>
    <span>{current.label}</span>
    <i aria-hidden="true"><b /></i>
  </div>
}
