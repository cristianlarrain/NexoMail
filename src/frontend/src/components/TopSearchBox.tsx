import { useEffect, useRef, useState } from 'react'
import { Mic, MicOff, Search, X } from 'lucide-react'
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
  const input = useRef<HTMLInputElement>(null)
  const valueRef = useRef(value)
  const navigate = useNavigate()
  const location = useLocation()

  useEffect(() => { valueRef.current = value }, [value])
  useEffect(() => () => recognition.current?.stop(), [])

  function stopListening() {
    recognition.current?.stop()
  }

  function toggleVoice() {
    if (listening) {
      stopListening()
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
      input.current?.focus()
    }
    instance.onerror = event => {
      setVoiceError(event.error === 'not-allowed' || event.error === 'service-not-allowed'
        ? 'Debes permitir el acceso al micrófono para usar el dictado.'
        : 'No fue posible continuar con el dictado. Inténtalo nuevamente.')
      setListening(false)
    }
    instance.onend = () => {
      setListening(false)
      recognition.current = null
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
    recognition.current?.stop()
    valueRef.current = ''
    onChange('')
    setVoiceError('')
    input.current?.focus()
  }

  function submitSearch() {
    const query = value.trim()
    if (query && requestsTrashAction(query)) {
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

  return <div className={`search top-search ${listening ? 'listening' : ''}`} role="search" title={voiceError || 'Busca correos, contactos y documentos con lenguaje normal'}>
    <Search size={18} className="top-search-icon" />
    <input
      ref={input}
      value={value}
      onChange={event => onChange(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') submitSearch() }}
      placeholder={listening ? 'Escuchando… habla con Nexi' : 'Busca o pregúntale a Nexi'}
      aria-label="Buscar en NexoMail"
    />
    {value && <button type="button" className="top-search-action clear" onClick={clearSearch} aria-label="Borrar búsqueda" title="Borrar búsqueda"><X size={16} /></button>}
    <button type="button" className={`top-search-action voice ${listening ? 'active' : ''}`} onClick={toggleVoice} aria-label={listening ? 'Detener dictado' : 'Dictar búsqueda por voz'} title={listening ? 'Detener dictado' : 'Hablarle a Nexi'}>
      {listening ? <MicOff size={17} /> : <Mic size={17} />}
    </button>
    <span className="top-search-voice-status" aria-live="polite">{voiceError || (listening ? 'Escuchando' : '')}</span>
  </div>
}
