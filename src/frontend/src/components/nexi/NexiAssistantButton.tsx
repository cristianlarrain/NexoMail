import { NexiVisual } from './NexiVisual'

export function NexiAssistantButton({ open, onClick }: { open: boolean; onClick: () => void }) {
  return <button
    type="button"
    className={`nexi-assistant-button ${open ? 'active' : ''}`}
    onClick={onClick}
    title={open ? 'Cerrar Nexi' : 'Abrir Nexi'}
    aria-label={open ? 'Cerrar Nexi' : 'Abrir Nexi'}
    aria-expanded={open}
  >
    <NexiVisual size="small" />
    <span className="nexi-assistant-button-label">Nexi</span>
  </button>
}
