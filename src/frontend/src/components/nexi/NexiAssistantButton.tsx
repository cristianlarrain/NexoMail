import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { useLocation } from 'react-router-dom'
import { NexiVisual } from './NexiVisual'

const POSITION_KEY = 'nexomail-nexi-position'
const VIEWPORT_MARGIN = 8
const PROMPT_HEIGHT = 30
const PROMPT_GAP = 6

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

function promptForPath(pathname: string) {
  if (pathname.startsWith('/message/')) return '¿Qué quieres hacer con este correo?'
  if (pathname === '/compose') return '¿Te ayudo a redactar?'
  if (pathname === '/search') return '¿Qué quieres encontrar?'
  if (pathname === '/control-center') return '¿Qué quieres revisar ahora?'
  if (pathname === '/settings/profile') return '¿Qué quieres ajustar de tu perfil?'
  if (pathname === '/settings/accounts') return '¿Qué cuenta quieres configurar?'
  if (pathname === '/settings/appearance') return '¿Qué quieres cambiar de la apariencia?'
  if (pathname.startsWith('/settings/')) return '¿Qué quieres configurar aquí?'
  if (pathname === '/inbox' || pathname.startsWith('/account/')) return '¿Qué quieres hacer en esta bandeja?'
  if (['/archive', '/ignored', '/sent', '/drafts', '/spam', '/trash'].includes(pathname)) return '¿Qué quieres hacer aquí?'
  return '¿Qué puedo hacer por ti?'
}

export function NexiAssistantButton({ open, onClick }: { open: boolean; onClick: () => void }) {
  const location = useLocation()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const dragRef = useRef<DragState | null>(null)
  const draggedRef = useRef(false)
  const [position, setPosition] = useState<Position | null>(() => storedPosition())
  const [dragging, setDragging] = useState(false)
  const prompt = open ? 'Estoy listo. ¿Qué hacemos?' : promptForPath(location.pathname)

  function clampPosition(next: Position) {
    const rect = buttonRef.current?.getBoundingClientRect()
    const width = rect?.width ?? 54
    const height = rect?.height ?? 54
    const promptWidth = window.innerWidth <= 760 ? 156 : 184
    const horizontalPromptMargin = Math.max(0, (promptWidth - width) / 2)
    const minX = VIEWPORT_MARGIN + horizontalPromptMargin
    const maxX = Math.max(minX, window.innerWidth - width - VIEWPORT_MARGIN - horizontalPromptMargin)
    const maxY = Math.max(VIEWPORT_MARGIN, window.innerHeight - height - PROMPT_GAP - PROMPT_HEIGHT - VIEWPORT_MARGIN)
    return {
      x: Math.min(Math.max(minX, next.x), maxX),
      y: Math.min(Math.max(VIEWPORT_MARGIN, next.y), maxY),
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
    aria-label={open ? 'Cerrar Nexi. También puedes arrastrarlo para moverlo.' : `Abrir Nexi. ${prompt}`}
    aria-expanded={open}
  >
    <NexiVisual size="small" />
    <span className="nexi-assistant-button-label">Nexi</span>
    <span className="nexi-assistant-prompt" aria-hidden="true">{prompt}</span>
  </button>
}
