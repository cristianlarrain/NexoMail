import { useEffect, useRef, useState } from 'react'
import { Check, Mic, Search, SendHorizontal, X } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router-dom'
import { requestsTrashAction } from '../utils/nexiSearchIntent'

type SpeechResult = { 0?: { transcript?: string } }
type SpeechRecognitionEventLike = { resultIndex?: number; results: { length: number; [index: number]: SpeechResult } }
type SpeechRecognitionLike = {
  lang: string
  continuous: boolean
  interimResults: boolean
  start: () => void
  stop: () => void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onend: (() => void) | null
  onerror: ((event: { error?: string }) => void) | null
}
type SpeechWindow = Window & {
  SpeechRecognition?: new () => SpeechRecognitionLike
  webkitSpeechRecognition?: new () => SpeechRecognitionLike
}

type TopSearchBoxProps = {
  value: string
  onChange: (value: string) => void
  onSubmit: () => void
}

function accountIdFromPath(pathname: string) {
  const accountMatch = pathname.match(/^\/account\/([^/]+)/)
  if (accountMatch) return decodeURIComponent(accountMatch[1])
  const messageMatch = pathname.match(/^\/message\/([^/]+)\/[^/]+$/)
  return messageMatch ? decodeURIComponent(messageMatch[1]) : undefined
}

export function TopSearchBox({ value, onChange, onSubmit }: TopSearchBoxProps) {
  const [listening, setListening] = useState(false)
  const [voiceError, setVoiceError] = useState('')
  const recognition = useRef<SpeechRecognitionLike | null>(null)
  const textarea = useRef<HTMLTextAreaElement>(null)
  const valueRef = useRef(value)
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => { valueRef.current = value }, [value])
  useEffect(() => {
    const field = textarea.current
    if (!field) return
    field.style.height = '28px'
    field.style.height = `${Math.min(Math.max(field.scrollHeight, 28), 88)}px`
  }, [value, listening])
  useEffect(() => () => {
    try { recognition.current?.stop() } catch { /* already stopped */ }
  }, [])

  function finishListening() {
    const current = recognition.current
    recognition.current = null
    setListening(false)
    try { current?.stop() } catch { /* already stopped */ }
    window.setTimeout(() => textarea.current?.focus(), 0)
  }

  function toggleVoice() {
    if (listening) {
      finishListening()
      return
    }

    const speechWindow = window as SpeechWindow
    const Recognition = speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition
    if (!Recognition) {
      setVoiceError('El dictado por voz no está disponible en este navegador. Usa Chrome o Edge actualizado.')
      return
    }

    const instance = new Recognition()
    recognition.current = instance
    instance.lang = 'es-CL'
    instance.continuous = true
    instance.interimResults = false
    instance.onresult = event => {
      let transcript = ''
      for (let index = event.resultIndex ?? 0; index < event.results.length; index++) {
        transcript += `${event.results[index]?.[0]?.transcript ?? ''} `
      }
      const spoken = transcript.trim()
      if (!spoken) return
      const current = valueRef.current.trim()
      const next = current ? `${current} ${spoken}` : spoken
      valueRef.current = next
      onChange(next)
    }
    instance.onerror = event => {
      setListening(false)
      recognition.current = null
      if (event.error === 'not-allowed' || event.error === 'service-not-allowed') {
        setVoiceError('Debes permitir el acceso al micrófono para usar el dictado.')
      } else if (event.error && event.error !== 'aborted') {
        setVoiceError('El micrófono se detuvo. Revisa el texto y vuelve a dictar si necesitas agregar algo.')
      }
    }
    instance.onend = () => {
      setListening(false)
      recognition.current = null
      window.setTimeout(() => textarea.current?.focus(), 0)
    }

    try {
      setVoiceError('')
      instance.start()
      setListening(true)
    } catch {
      setVoiceError('No fue posible iniciar el micrófono. Inténtalo nuevamente.')
      setListening(false)
      recognition.current = null
    }
  }

  function clearSearch() {
    finishListening()
    valueRef.current = ''
    onChange('')
    setVoiceError('')
    textarea.current?.focus()
  }

  function submitSearch() {
    if (listening) {
      finishListening()
      return
    }
    const query = valueRef.current.trim()
    if (!query) return
    if (requestsTrashAction(query)) {
      const params = new URLSearchParams()
      params.set('q', query)
      const activeAccount = accountIdFromPath(location.pathname)
      const searchAccount = location.pathname === '/search' || location.pathname === '/search-action'
        ? new URLSearchParams(location.search).get('account') ?? undefined
        : undefined
      const accountId = activeAccount ?? searchAccount
      if (accountId) params.set('account', accountId)
      navigate(`/search-action?${params.toString()}`)
      return
    }
    onSubmit()
  }

  const showGuidance = value.length > 72 && !listening && !voiceError

  return <div className={`search top-search ${listening ? 'listening expanded' : value.length > 72 ? 'expanded' : ''}`} role="search" title={voiceError || 'Busca o dale instrucciones a Nexi con lenguaje normal'}>
    <Search size={18} className="top-search-icon" />
    <div className="top-search-editor">
      <textarea
        ref={textarea}
        rows={1}
        maxLength={6000}
        value={value}
        onChange={event => onChange(event.target.value)}
        onKeyDown={event => {
          if (event.key !== 'Enter' || event.shiftKey) return
          event.preventDefault()
          if (listening) finishListening()
          else submitSearch()
        }}
        placeholder={listening ? 'Escuchando… dicta tu instrucción completa' : 'Busca o pregúntale a Nexi'}
        aria-label="Buscar o dar una instrucción a Nexi"
      />
      {(listening || voiceError || showGuidance) && <span className={`top-search-inline-status ${voiceError ? 'error' : ''}`} aria-live="polite">
        {voiceError || (listening
          ? 'Escuchando… pulsa Listo cuando termines. Después revisa el texto y pulsa Enviar.'
          : `Enter envía · Shift+Enter nueva línea · ${value.length}/6000 caracteres`)}
      </span>}
    </div>
    {value && <button type="button" className="top-search-action clear" onClick={clearSearch} aria-label="Borrar búsqueda" title="Borrar búsqueda"><X size={16} /></button>}
    {listening
      ? <button type="button" className="top-search-done" onClick={finishListening} aria-label="Terminar dictado" title="Terminar dictado"><Check size={15} />Listo</button>
      : <button type="button" className="top-search-action voice" onClick={toggleVoice} aria-label="Dictar búsqueda por voz" title="Hablarle a Nexi"><Mic size={17} /></button>}
    {value.trim() && !listening && <button type="button" className="top-search-submit" onClick={submitSearch} aria-label="Enviar a Nexi" title="Enviar a Nexi"><SendHorizontal size={15} /><span>Enviar</span></button>}
  </div>
}
