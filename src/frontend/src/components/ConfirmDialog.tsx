import { AlertTriangle, X } from 'lucide-react'

type ConfirmDialogProps = {
  open: boolean
  title: string
  message: string
  confirmLabel: string
  cancelLabel?: string
  tone?: 'default' | 'danger'
  pending?: boolean
  onConfirm: () => void
  onCancel: () => void
}

export function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel,
  cancelLabel = 'Cancelar',
  tone = 'danger',
  pending = false,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  if (!open) return null

  return <div className="confirm-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !pending) onCancel() }}>
    <section className={`confirm-dialog ${tone === 'danger' ? 'danger' : ''}`} role="dialog" aria-modal="true" aria-labelledby="confirm-dialog-title" aria-describedby="confirm-dialog-message">
      <header>
        <span className="confirm-icon" aria-hidden="true"><AlertTriangle size={21} /></span>
        <div>
          <h2 id="confirm-dialog-title">{title}</h2>
          <p id="confirm-dialog-message">{message}</p>
        </div>
        <button type="button" className="icon-button confirm-close" onClick={onCancel} disabled={pending} aria-label="Cerrar"><X size={18} /></button>
      </header>
      <footer>
        <button type="button" className="secondary-button" onClick={onCancel} disabled={pending} autoFocus>{cancelLabel}</button>
        <button type="button" className={tone === 'danger' ? 'confirm-danger-button' : 'primary-button'} onClick={onConfirm} disabled={pending}>{pending ? 'Procesando…' : confirmLabel}</button>
      </footer>
    </section>
  </div>
}
