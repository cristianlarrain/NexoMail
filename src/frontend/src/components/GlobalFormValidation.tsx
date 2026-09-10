import { useEffect, useState } from 'react'

function validationMessage(input: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement) {
  const { validity } = input
  if (validity.valueMissing) return 'Complete este campo para continuar.'
  if (validity.typeMismatch && input instanceof HTMLInputElement && input.type === 'email') return 'Ingrese un correo electrónico válido.'
  if (validity.typeMismatch) return 'Ingrese un valor con el formato correcto.'
  if (validity.patternMismatch) return 'Revise el formato de este campo.'
  if (validity.tooShort) return `Ingrese al menos ${input.minLength} caracteres.`
  if (validity.tooLong) return `Ingrese como máximo ${input.maxLength} caracteres.`
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
      timer = window.setTimeout(() => setMessage(''), 4200)
    }

    const onInvalid = (event: Event) => {
      event.preventDefault()
      const target = event.target
      if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement)) return
      show(validationMessage(target))
      target.focus({ preventScroll: false })
      target.setAttribute('aria-invalid', 'true')
    }

    const onInput = (event: Event) => {
      const target = event.target
      if (!(target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement)) return
      if (target.validity.valid) target.removeAttribute('aria-invalid')
    }

    document.addEventListener('invalid', onInvalid, true)
    document.addEventListener('input', onInput, true)
    document.addEventListener('change', onInput, true)

    return () => {
      document.removeEventListener('invalid', onInvalid, true)
      document.removeEventListener('input', onInput, true)
      document.removeEventListener('change', onInput, true)
      if (timer) window.clearTimeout(timer)
    }
  }, [])

  if (!message) return null
  return <div className="global-form-validation" role="alert" aria-live="assertive">{message}</div>
}
