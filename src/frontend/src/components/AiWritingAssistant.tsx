import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Check, RefreshCw, Sparkles } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { AiTone } from '../types/mail'

type ReplyProps = {
  mode: 'reply'
  accountId: string
  messageId: string
  onUse: (text: string) => void
}

type ComposeProps = {
  mode: 'compose'
  onUse: (text: string) => void
}

type Props = ReplyProps | ComposeProps

const tones: Array<{ value: AiTone; label: string }> = [
  { value: 'profesional', label: 'Profesional' },
  { value: 'formal', label: 'Formal' },
  { value: 'informal', label: 'Informal' },
  { value: 'breve', label: 'Breve' },
  { value: 'explicito', label: 'Explícito' },
]

export function AiWritingAssistant(props: Props) {
  const [tone, setTone] = useState<AiTone>('profesional')
  const [context, setContext] = useState('')
  const [suggestion, setSuggestion] = useState('')

  const generate = useMutation({
    mutationFn: () => props.mode === 'reply'
      ? mailApi.aiReply(props.accountId, props.messageId, tone, context)
      : mailApi.aiDraft(context, tone),
    onSuccess: result => setSuggestion(result.text),
  })

  const canGenerate = props.mode === 'reply' || context.trim().length > 0

  return <section className={`ai-writing-assistant ${props.mode}`} aria-label="Asistente de redacción con IA">
    <header className="ai-writing-header">
      <div className="ai-writing-title"><span className="ai-writing-icon"><Sparkles size={16} /></span><div><strong>{props.mode === 'reply' ? 'Respuesta sugerida con IA' : 'Redactar con IA'}</strong><small>{props.mode === 'reply' ? 'Genera una respuesta relacionada con este correo y su conversación.' : 'Escribe unas pocas ideas y genera una propuesta de correo.'}</small></div></div>
    </header>

    <div className="ai-tone-selector" aria-label="Tono de redacción">
      {tones.map(option => <button type="button" key={option.value} className={tone === option.value ? 'active' : ''} onClick={() => setTone(option.value)}>{option.label}</button>)}
    </div>

    <label className="ai-context-field">
      <span>{props.mode === 'reply' ? '¿Qué quieres responder o recalcar? (opcional)' : '¿Qué es lo que quieres decir?'}</span>
      <textarea value={context} maxLength={3500} onChange={event => setContext(event.target.value)} placeholder={props.mode === 'reply' ? 'Ej.: confirmar recepción, agradecer y señalar que revisaré el documento mañana.' : 'Ej.: quiero pedir una reunión el martes para revisar el avance del proyecto y los pendientes.'} />
    </label>

    <div className="ai-generate-row">
      <button type="button" className="secondary-button ai-generate-button" disabled={!canGenerate || generate.isPending} onClick={() => generate.mutate()}>{suggestion ? <RefreshCw size={15} /> : <Sparkles size={15} />} {generate.isPending ? 'Generando…' : suggestion ? 'Generar otra propuesta' : 'Generar propuesta'}</button>
      <small>La IA no envía correos automáticamente.</small>
    </div>

    {generate.isError && <div className="notice ai-writing-error">{generate.error instanceof Error ? generate.error.message : 'No fue posible generar la propuesta.'}</div>}

    {suggestion && <div className="ai-suggestion">
      <div className="ai-suggestion-heading"><strong>Propuesta</strong><span>Puede editarla antes de usarla.</span></div>
      <textarea value={suggestion} onChange={event => setSuggestion(event.target.value)} aria-label="Propuesta generada por IA" />
      <div className="ai-suggestion-actions"><button type="button" className="primary-button" disabled={!suggestion.trim()} onClick={() => props.onUse(suggestion.trim())}><Check size={15} /> Usar propuesta</button></div>
    </div>}
  </section>
}
