import { useEffect, useRef, useState } from 'react'
import { Mic, MicOff } from 'lucide-react'

type VoiceTarget = HTMLInputElement | HTMLTextAreaElement
type SpeechResult = { 0?: { transcript?: string } }
type SpeechRecognitionEventLike = { results: { length: number; [index: number]: SpeechResult } }
type SpeechRecognitionLike = {
  lang: string
  continuous: boolean
  interimResults: boolean
  start: () => void
  stop: () => void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onend: (() => void) | null
  onerror: (() => void) | null
}
type SpeechWindow = Window & {
  SpeechRecognition?: new () => SpeechRecognitionLike
  webkitSpeechRecognition?: new () => SpeechRecognitionLike
}

type Position = { top: number; left: number }

function eligibleTarget(value: EventTarget | Element | null): VoiceTarget | null {
  if (value instanceof HTMLTextAreaElement) {
    return value.disabled || value.readOnly ? null : value
  }
  if (!(value instanceof HTMLInputElement) || value.disabled || value.readOnly) return null
  return ['text', 'email', 'search', 'url', 'tel'].includes(value.type) ? value : null
}

function setNativeValue(target: VoiceTarget, value: string) {
  const prototype = target instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype
  const setter = Object.getOwnPropertyDescriptor(prototype, 'value')?.set
  setter?.call(target, value)
  target.dispatchEvent(new Event('input', { bubbles: true }))
}

export function GlobalVoiceDictation() {
  const [target, setTarget] = useState<VoiceTarget | null>(null)
  const [position, setPosition] = useState<Position | null>(null)
  const [listening, setListening] = useState(false)
  const recognition = useRef<SpeechRecognitionLike | null>(null)
  const baseValue = useRef('')

  const speechWindow = window as SpeechWindow
  const Recognition = speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition
  const supported = Boolean(Recognition)

  useEffect(() => {
    const updatePosition = () => {
      if (!target || !document.contains(target)) {
        setPosition(null)
        return
      }
      const rect = target.getBoundingClientRect()
      if (rect.width < 72 || rect.height < 28 || rect.bottom < 0 || rect.top > window.innerHeight) {
        setPosition(null)
        return
      }
      setPosition({ top: rect.top + rect.height / 2, left: rect.right - 20 })
    }

    const onFocus = (event: FocusEvent) => {
      const next = eligibleTarget(event.target)
      setTarget(next)
      if (next) window.requestAnimationFrame(updatePosition)
    }

    document.addEventListener('focusin', onFocus, true)
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)
    updatePosition()

    return () => {
      document.removeEventListener('focusin', onFocus, true)
      window.removeEventListener('resize', updatePosition)
      window.removeEventListener('scroll', updatePosition, true)
    }
  }, [target])

  useEffect(() => () => recognition.current?.stop(), [])

  function stop() {
    recognition.current?.stop()
    recognition.current = null
    setListening(false)
  }

  function toggleDictation() {
    if (!target || !Recognition) return
    if (listening) {
      stop()
      return
    }

    const instance = new Recognition()
    instance.lang = 'es-CL'
    instance.continuous = true
    instance.interimResults = true
    baseValue.current = target.value.trimEnd()
    instance.onresult = event => {
      let spoken = ''
      for (let index = 0; index < event.results.length; index += 1) {
        spoken += event.results[index]?.[0]?.transcript ?? ''
      }
      const transcript = spoken.trim()
      const separator = baseValue.current && transcript ? ' ' : ''
      setNativeValue(target, `${baseValue.current}${separator}${transcript}`)
    }
    instance.onend = () => {
      recognition.current = null
      setListening(false)
    }
    instance.onerror = () => {
      recognition.current = null
      setListening(false)
    }

    recognition.current = instance
    setListening(true)
    instance.start()
  }

  if (!supported || !target || !position) return null

  return <button
    type="button"
    className={`global-voice-dictation ${listening ? 'listening' : ''}`}
    style={{ top: position.top, left: position.left }}
    title={listening ? 'Detener dictado' : 'Dictar texto'}
    aria-label={listening ? 'Detener dictado' : 'Dictar texto'}
    onPointerDown={event => event.preventDefault()}
    onClick={toggleDictation}
  >
    {listening ? <MicOff size={15} /> : <Mic size={15} />}
  </button>
}
