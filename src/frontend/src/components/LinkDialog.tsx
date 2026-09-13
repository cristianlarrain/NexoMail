import { useEffect, useRef, useState } from 'react'
import { Link, X } from 'lucide-react'

type LinkDialogProps = {
  open: boolean
  onInsert: (url: string) => void
  onCancel: () => void
}

export function LinkDialog({ open, onInsert, onCancel }: LinkDialogProps) {
  const [url, setUrl] = useState('')
  const input = useRef<HTMLInputElement>(null)
  const valid = /^https:\/\/.+/i.test(url.trim())

  useEffect(() => {
    if (!open) return
    setUrl('')
    window.requestAnimationFrame(() => input.current?.focus())
  }, [open])

  if (!open) return null

  return <div className="confirm-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget) onCancel() }}>
    <section className="confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="link-dialog-title" aria-describedby="link-dialog-message">
      <header>
        <span className="confirm-icon" aria-hidden="true"><Link size={21} /></span>
        <div>
          <h2 id="link-dialog-title">Insertar enlace</h2>
          <p id="link-dialog-message">Ingrese una dirección segura que comience con https://</p>
        </div>
        <button type="button" className="icon-button confirm-close" onClick={onCancel} aria-label="Cerrar"><X size={18} /></button>
      </header>
      <form className="dialog-input-form" onSubmit={event => { event.preventDefault(); if (valid) onInsert(url.trim()) }}>
        <label htmlFor="compose-link-url">Dirección web</label>
        <input ref={input} id="compose-link-url" type="url" inputMode="url" value={url} onChange={event => setUrl(event.target.value)} placeholder="https://ejemplo.cl" aria-invalid={url.length > 0 && !valid} />
        {url.length > 0 && !valid && <small>La dirección debe comenzar con https://</small>}
        <footer>
          <button type="button" className="secondary-button" onClick={onCancel}>Cancelar</button>
          <button type="submit" className="primary-button" disabled={!valid}>Insertar enlace</button>
        </footer>
      </form>
    </section>
  </div>
}
