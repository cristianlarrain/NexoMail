import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { AlertCircle, Sparkles } from 'lucide-react'

type ValidationState = {
  host: HTMLElement | null
  missingRecipient: boolean
  missingSubject: boolean
}

const emptyState: ValidationState = { host: null, missingRecipient: false, missingSubject: false }

function validationStateFor(form: HTMLFormElement): ValidationState {
  const host = form.querySelector<HTMLElement>('.ai-compose-fields')
  const recipient = form.querySelector<HTMLInputElement>('.recipient-field input')
  const subject = form.querySelector<HTMLInputElement>('.subject-field input')
  return {
    host,
    missingRecipient: !recipient?.value.trim(),
    missingSubject: !subject?.value.trim(),
  }
}

function isComposeForm(target: EventTarget | null): target is HTMLInputElement {
  return target instanceof HTMLInputElement && Boolean(target.closest('.ai-compose-card form'))
}

export function ComposeValidationNotice() {
  const [validation, setValidation] = useState<ValidationState>(emptyState)
  const [active, setActive] = useState(false)

  useEffect(() => {
    function onInvalid(event: Event) {
      if (!isComposeForm(event.target)) return
      const form = event.target.closest('form') as HTMLFormElement | null
      if (!form) return
      event.preventDefault()
      const next = validationStateFor(form)
      setValidation(next)
      setActive(next.missingRecipient || next.missingSubject)
      window.setTimeout(() => {
        if (next.missingRecipient) form.querySelector<HTMLInputElement>('.recipient-field input')?.focus()
        else if (next.missingSubject) form.querySelector<HTMLInputElement>('.subject-field input')?.focus()
      }, 0)
    }

    function onInput(event: Event) {
      if (!active || !isComposeForm(event.target)) return
      const form = event.target.closest('form') as HTMLFormElement | null
      if (!form) return
      const next = validationStateFor(form)
      setValidation(next)
      if (!next.missingRecipient && !next.missingSubject) setActive(false)
    }

    function onSubmit(event: Event) {
      if (!(event.target instanceof HTMLFormElement) || !event.target.closest('.ai-compose-card')) return
      setActive(false)
    }

    document.addEventListener('invalid', onInvalid, true)
    document.addEventListener('input', onInput, true)
    document.addEventListener('submit', onSubmit, true)
    return () => {
      document.removeEventListener('invalid', onInvalid, true)
      document.removeEventListener('input', onInput, true)
      document.removeEventListener('submit', onSubmit, true)
    }
  }, [active])

  if (!active || !validation.host) return null

  const missing = [
    validation.missingRecipient ? 'destinatario' : null,
    validation.missingSubject ? 'asunto' : null,
  ].filter(Boolean)
  const detail = missing.length === 2
    ? 'Completa el destinatario y el asunto antes de enviar.'
    : validation.missingRecipient
      ? 'Agrega al menos un destinatario antes de enviar.'
      : 'Escribe un asunto antes de enviar.'

  return createPortal(
    <div className="compose-validation-notice" role="alert" aria-live="assertive">
      <span className="compose-validation-icon"><Sparkles size={16} /></span>
      <div>
        <strong>Nexi necesita un dato más</strong>
        <span>{detail}</span>
      </div>
      <AlertCircle size={16} className="compose-validation-alert" />
    </div>,
    validation.host,
  )
}
