import { useEffect, useState, type UIEvent } from 'react'
import { CheckCircle2, ShieldCheck, X } from 'lucide-react'
import { LEGAL_VERSION } from '../appMeta'
import { legalDocumentOrder, legalDocuments } from '../legal/legalDocuments'

type LegalConsentModalProps = {
  open: boolean
  onClose: () => void
  onAccept: () => void
}

export function LegalConsentModal({ open, onClose, onAccept }: LegalConsentModalProps) {
  const [hasReadToEnd, setHasReadToEnd] = useState(false)

  useEffect(() => {
    if (!open) return
    setHasReadToEnd(false)
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [open, onClose])

  if (!open) return null

  function handleScroll(event: UIEvent<HTMLDivElement>) {
    const { scrollTop, scrollHeight, clientHeight } = event.currentTarget
    if (scrollHeight - scrollTop - clientHeight <= 8) setHasReadToEnd(true)
  }

  function accept() {
    if (!hasReadToEnd) return
    onAccept()
    onClose()
  }

  return <div className="legal-consent-overlay" role="presentation">
    <section className="legal-consent-dialog" role="dialog" aria-modal="true" aria-labelledby="legal-consent-title" aria-describedby="legal-consent-summary">
      <header className="legal-consent-header">
        <div>
          <p className="eyebrow">Consentimiento · versión {LEGAL_VERSION}</p>
          <h2 id="legal-consent-title">Antes de crear tu cuenta</h2>
        </div>
        <button type="button" className="legal-consent-close" onClick={onClose} aria-label="Cerrar condiciones legales"><X size={20} /></button>
      </header>

      <div className="legal-consent-summary" id="legal-consent-summary">
        <ShieldCheck size={22} aria-hidden="true" />
        <p><strong>Privacidad por diseño.</strong> NexoMail no almacena de forma persistente el contenido completo de tus correos ni de sus archivos adjuntos. Cuando una función de Nexi necesita analizar contenido, se procesa únicamente la información necesaria para ejecutar la acción solicitada.</p>
      </div>

      <p className="legal-consent-instruction">Lee los documentos completos. El botón de aceptación se habilitará cuando llegues al final.</p>

      <div className="legal-consent-scroll" onScroll={handleScroll} tabIndex={0} aria-label="Términos, privacidad y seguridad">
        {legalDocumentOrder.map((key, documentIndex) => {
          const document = legalDocuments[key]
          return <article className="legal-consent-document" key={key}>
            <div className="legal-consent-document-heading">
              <span>{documentIndex + 1} de {legalDocumentOrder.length}</span>
              <h3>{document.title}</h3>
              <p>{document.intro}</p>
            </div>
            {document.sections.map(section => <section key={`${key}-${section.title}`}>
              <h4>{section.title}</h4>
              {section.paragraphs.map(paragraph => <p key={paragraph}>{paragraph}</p>)}
            </section>)}
          </article>
        })}
        <div className="legal-consent-end" aria-live="polite">
          <CheckCircle2 size={20} aria-hidden="true" />
          <span>Has llegado al final de las condiciones legales.</span>
        </div>
      </div>

      <footer className="legal-consent-footer">
        <button type="button" className="secondary-button" onClick={onClose}>Cancelar</button>
        <button type="button" className="primary-button legal-consent-accept" disabled={!hasReadToEnd} onClick={accept}>
          He leído y acepto los Términos, la Política de Privacidad y la Política de Seguridad
        </button>
      </footer>
    </section>
  </div>
}
