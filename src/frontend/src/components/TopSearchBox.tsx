import { useEffect, useRef, useState } from 'react'
import { Check, Mic, MicOff, Search, SendHorizontal, X } from 'lucide-react'
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
  const keepListening = useRef(false)
  const textarea = useRef<HTMLTextAreaElement>(null)
  const valueRef = useRef(value)
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => { valueRef.current = value }, [value])
  useEffect(() => {
    const field = textarea.current
    if (!field) return
    field.style.height = '28px'
    field.style.height = `${Math.min(Math.max(field.scrollHeight, 28), 68)}px`
  }, [value, listening])
  useEffect(() => () => {
    keepListening.current = false
    recognition.current?.stop()
  }, [])

  function finishListening() {
    keepListening.current = false
    recognition.current?.stop()
    recognition.current = null
    setListening(false)
    setVoiceError('')
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
    keepListening.current = true
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
      const permissionDenied = event.error === 'not-allowed' || event.error === 'service-not-allowed'
      if (permissionDenied) {
        keepListening.current = false
        setVoiceError('Debes permitir el acceso al micrófono para usar el dictado.')
        setListening(false)
        recognition.current = null
        return
      }
      if (event.error && event.error !== 'no-speech' && event.error !== 'aborted') {
        keepListening.current = false
        setVoiceError('No fue posible continuar con el dictado. Inténtalo nuevamente.')
        setListening(false)
        recognition.current = null
      }
    }
    instance.onend = () => {
      if (!keepListening.current) {
        setListening(false)
        recognition.current = null
        return
      }
      window.setTimeout(() => {
        if (!keepListening.current || recognition.current !== instance) return
        try {
          instance.start()
        } catch {
          keepListening.current = false
          setListening(false)
          recognition.current = null
          setVoiceError('El micrófono se detuvo. Puedes revisar el texto y enviarlo o volver a dictar.')
        }
      }, 180)
    }

    try {
      setVoiceError('')
      instance.start()
      setListening(true)
    } catch {
      keepListening.current = false
      setVoiceError('No fue posible iniciar el micrófono. Inténtalo nuevamente.')
      setListening(false)
      recognition.current = null
    }
  }

  function clearSearch() {
    keepListening.current = false
    recognition.current?.stop()
    recognition.current = null
    setListening(false)
    valueRef.current = ''
    onChange('')
    setVoiceError('')
    textarea.current?.focus()
  }

  function submitSearch() {
    if (listening) finishListening()
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

  return <div className={`search top-search ${listening ? 'listening expanded' : value.length > 72 ? 'expanded' : ''}`} role="search" title={voiceError || 'Busca correos, contactos y documentos con lenguaje normal'}>
    <Search size={18} className="top-search-icon" />
    <div className="top-search-editor">
      <textarea
        ref={textarea}
        rows={1}
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
      {(listening || voiceError) && <span className={`top-search-inline-status ${voiceError ? 'error' : ''}`} aria-live="polite">
        {voiceError || 'Escuchando… pulsa Listo cuando termines. Después revisa y envía.'}
      </span>}
    </div>
    {value && <button type="button" className="top-search-action clear" onClick={clearSearch} aria-label="Borrar búsqueda" title="Borrar búsqueda"><X size={16} /></button>}
    {listening
      ? <button type="button" className="top-search-done" onClick={finishListening} aria-label="Terminar dictado" title="Terminar dictado"><Check size={15} />Listo</button>
      : <button type="button" className="top-search-action voice" onClick={toggleVoice} aria-label="Dictar búsqueda por voz" title="Hablarle a Nexi"><Mic size={17} /></button>}
    {value.trim() && !listening && <button type="button" className="top-search-action submit" onClick={submitSearch} aria-label="Enviar a Nexi" title="Enviar a Nexi"><SendHorizontal size={17} /></button>}
    {listening && <MicOff size={14} className="top-search-listening-mark" aria-hidden="true" />}
  </div>
}
