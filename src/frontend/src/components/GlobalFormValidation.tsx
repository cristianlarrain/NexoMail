import { useEffect, useState } from 'react'

type ValidatableField = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement

function isValidatableField(value: EventTarget | Element | null): value is ValidatableField {
  return value instanceof HTMLInputElement || value instanceof HTMLTextAreaElement || value instanceof HTMLSelectElement
}

function validationMessage(input: ValidatableField) {
  const { validity } = input
  if (validity.valueMissing) return 'Complete este campo para continuar.'
  if (validity.typeMismatch && input instanceof HTMLInputElement && input.type === 'email') return 'Ingrese un correo electrónico válido.'
  if (validity.typeMismatch) return 'Ingrese un valor con el formato correcto.'
  if (validity.patternMismatch) return 'Revise el formato de este campo.'
  if (validity.tooShort && (input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement)) return `Ingrese al menos ${input.minLength} caracteres.`
  if (validity.tooLong && (input instanceof HTMLInputElement || input instanceof HTMLTextAreaElement)) return `Ingrese como máximo ${input.maxLength} caracteres.`
  if (validity.rangeUnderflow && input instanceof HTMLInputElement) return `El valor mínimo permitido es ${input.min}.`
  if (validity.rangeOverflow && input instanceof HTMLInputElement) return `El valor máximo permitido es ${input.max}.`
  if (validity.stepMismatch) return 'Ingrese un valor válido para este campo.'
  if (validity.badInput) return 'Ingrese un valor válido.'
  return 'Revise este campo antes de continuar.'
}

export function GlobalFormValidation() {
  const [message, setMessage] = useState('')

  useEffect(() => {
    let timer: number | undefined

    const show = (text: string) => {
      setMessage(text)
      if (timer) window.clearTimeout(timer)
      timer = window.setTimeout(() => setMessage(''), 3600)
    }

    const markForms = (root: ParentNode = document) => {
      root.querySelectorAll('form').forEach(form => { form.noValidate = true })
    }

    const showFieldError = (target: ValidatableField) => {
      show(validationMessage(target))
      target.setAttribute('aria-invalid', 'true')
      target.focus({ preventScroll: false })
    }

    const onSubmit = (event: Event) => {
      const form = event.target
      if (!(form instanceof HTMLFormElement)) return

      let invalidField: ValidatableField | null = null
      for (const element of Array.from(form.elements)) {
        if (isValidatableField(element) && element.willValidate && !element.validity.valid) {
          invalidField = element
          break
        }
      }

      if (!invalidField) return
      event.preventDefault()
      event.stopPropagation()
      showFieldError(invalidField)
    }

    const onInvalid = (event: Event) => {
      event.preventDefault()
      if (isValidatableField(event.target)) showFieldError(event.target)
    }

    const onInput = (event: Event) => {
      if (!isValidatableField(event.target)) return
      if (event.target.validity.valid) event.target.removeAttribute('aria-invalid')
    }

    markForms()
    const observer = new MutationObserver(mutations => {
      mutations.forEach(mutation => mutation.addedNodes.forEach(node => {
        if (node instanceof Element) {
          if (node instanceof HTMLFormElement) node.noValidate = true
          markForms(node)
        }
      }))
    })
    observer.observe(document.body, { childList: true, subtree: true })

    document.addEventListener('submit', onSubmit, true)
    document.addEventListener('invalid', onInvalid, true)
    document.addEventListener('input', onInput, true)
    document.addEventListener('change', onInput, true)

    return () => {
      observer.disconnect()
      document.removeEventListener('submit', onSubmit, true)
      document.removeEventListener('invalid', onInvalid, true)
      document.removeEventListener('input', onInput, true)
      document.removeEventListener('change', onInput, true)
      if (timer) window.clearTimeout(timer)
    }
  }, [])

  if (!message) return null
  return <div className="global-form-validation" role="alert" aria-live="assertive">{message}</div>
}
