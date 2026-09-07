import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { NexiVisual } from './NexiVisual'

const POSITION_KEY = 'nexomail-nexi-position'
const VIEWPORT_MARGIN = 8

type Position = { x: number; y: number }
type DragState = { pointerId: number; startX: number; startY: number; originX: number; originY: number }

function storedPosition(): Position | null {
  try {
    const raw = localStorage.getItem(POSITION_KEY)
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<Position>
    return Number.isFinite(value.x) && Number.isFinite(value.y) ? { x: value.x!, y: value.y! } : null
  } catch {
    return null
  }
}

export function NexiAssistantButton({ open, onClick }: { open: boolean; onClick: () => void }) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const dragRef = useRef<DragState | null>(null)
  const draggedRef = useRef(false)
  const [position, setPosition] = useState<Position | null>(() => storedPosition())
  const [dragging, setDragging] = useState(false)

  function clampPosition(next: Position) {
    const rect = buttonRef.current?.getBoundingClientRect()
    const width = rect?.width ?? 54
    const height = rect?.height ?? 54
    return {
      x: Math.min(Math.max(VIEWPORT_MARGIN, next.x), Math.max(VIEWPORT_MARGIN, window.innerWidth - width - VIEWPORT_MARGIN)),
      y: Math.min(Math.max(VIEWPORT_MARGIN, next.y), Math.max(VIEWPORT_MARGIN, window.innerHeight - height - VIEWPORT_MARGIN)),
    }
  }

  useEffect(() => {
    if (!position) return
    const keepVisible = () => setPosition(current => current ? clampPosition(current) : current)
    keepVisible()
    window.addEventListener('resize', keepVisible)
    return () => window.removeEventListener('resize', keepVisible)
  }, [])

  function handlePointerDown(event: ReactPointerEvent<HTMLButtonElement>) {
    if (event.button !== 0) return
    const rect = event.currentTarget.getBoundingClientRect()
    dragRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      originX: rect.left,
      originY: rect.top,
    }
    draggedRef.current = false
    event.currentTarget.setPointerCapture(event.pointerId)
  }

  function handlePointerMove(event: ReactPointerEvent<HTMLButtonElement>) {
    const drag = dragRef.current
    if (!drag || drag.pointerId !== event.pointerId) return
    const dx = event.clientX - drag.startX
    const dy = event.clientY - drag.startY
    if (!draggedRef.current && Math.hypot(dx, dy) < 4) return
    draggedRef.current = true
    setDragging(true)
    setPosition(clampPosition({ x: drag.originX + dx, y: drag.originY + dy }))
  }

  function finishDrag(event: ReactPointerEvent<HTMLButtonElement>) {
    const drag = dragRef.current
    if (!drag || drag.pointerId !== event.pointerId) return
    dragRef.current = null
    setDragging(false)
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId)
    if (draggedRef.current) {
      setPosition(current => {
        if (current) localStorage.setItem(POSITION_KEY, JSON.stringify(current))
        return current
      })
    }
  }

  function handleClick() {
    if (draggedRef.current) {
      draggedRef.current = false
      return
    }
    onClick()
  }

  return <button
    ref={buttonRef}
    type="button"
    className={`nexi-assistant-button ${open ? 'active' : ''} ${dragging ? 'dragging' : ''}`}
    style={position ? { left: position.x, top: position.y, right: 'auto', bottom: 'auto', touchAction: 'none', cursor: dragging ? 'grabbing' : 'grab' } : { touchAction: 'none', cursor: dragging ? 'grabbing' : 'grab' }}
    onPointerDown={handlePointerDown}
    onPointerMove={handlePointerMove}
    onPointerUp={finishDrag}
    onPointerCancel={finishDrag}
    onClick={handleClick}
    title={open ? 'Arrastrar para mover · clic para cerrar Nexi' : 'Arrastrar para mover · clic para abrir Nexi'}
    aria-label={open ? 'Cerrar Nexi. También puedes arrastrarlo para moverlo.' : 'Abrir Nexi. También puedes arrastrarlo para moverlo.'}
    aria-expanded={open}
  >
    <NexiVisual size="small" />
    <span className="nexi-assistant-button-label">Nexi</span>
  </button>
}
