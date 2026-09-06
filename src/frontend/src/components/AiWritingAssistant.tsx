import { useMemo, useState } from 'react'
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

type IntentOption = {
  value: string
  label: string
  description: string
  instruction: string
}

const tones: Array<{ value: AiTone; label: string; description: string }> = [
  { value: 'profesional', label: 'Profesional', description: 'Claro y cordial' },
  { value: 'formal', label: 'Formal', description: 'Protocolar y respetuoso' },
  { value: 'informal', label: 'Informal', description: 'Natural y cercano' },
  { value: 'breve', label: 'Breve', description: 'Sólo lo esencial' },
  { value: 'explicito', label: 'Explícito', description: 'Preciso y detallado' },
]

const replyIntents: IntentOption[] = [
  { value: 'responder', label: 'Responder', description: 'Contestar el punto principal', instruction: 'Responde directamente el punto principal planteado en el correo.' },
  { value: 'confirmar', label: 'Confirmar', description: 'Recepción, acuerdo o gestión', instruction: 'La respuesta debe confirmar de forma inequívoca la recepción, el acuerdo o la gestión mencionada, sin inventar datos.' },
  { value: 'solicitar', label: 'Solicitar', description: 'Pedir información o una acción', instruction: 'La respuesta debe solicitar claramente la información o acción necesaria.' },
  { value: 'aclarar', label: 'Aclarar', description: 'Precisar o corregir un punto', instruction: 'La respuesta debe aclarar o precisar el punto relevante de forma constructiva y clara.' },
  { value: 'agradecer', label: 'Agradecer', description: 'Reconocer y cerrar correctamente', instruction: 'La respuesta debe agradecer de forma natural y responder lo necesario sin extenderse innecesariamente.' },
  { value: 'seguimiento', label: 'Seguimiento', description: 'Retomar un asunto pendiente', instruction: 'La respuesta debe hacer seguimiento del asunto pendiente de manera clara, profesional y no agresiva.' },
]

const composeIntents: IntentOption[] = [
  { value: 'informar', label: 'Informar', description: 'Comunicar un antecedente', instruction: 'El correo debe informar el antecedente principal de forma ordenada y comprensible.' },
  { value: 'solicitar', label: 'Solicitar', description: 'Pedir algo concretamente', instruction: 'El correo debe formular una solicitud concreta, indicando con claridad qué se necesita.' },
  { value: 'coordinar', label: 'Coordinar', description: 'Organizar reunión o acción', instruction: 'El correo debe facilitar una coordinación, dejando claro el propósito y el siguiente paso.' },
  { value: 'confirmar', label: 'Confirmar', description: 'Dejar constancia o acuerdo', instruction: 'El correo debe confirmar de manera inequívoca el antecedente, acuerdo o gestión descrita.' },
  { value: 'agradecer', label: 'Agradecer', description: 'Reconocer una gestión o apoyo', instruction: 'El correo debe expresar agradecimiento de manera natural y profesional.' },
  { value: 'seguimiento', label: 'Seguimiento', description: 'Retomar un tema pendiente', instruction: 'El correo debe realizar seguimiento de un asunto pendiente con claridad y buen tono.' },
]

export function AiWritingAssistant(props: Props) {
  const intents = props.mode === 'reply' ? replyIntents : composeIntents
  const [tone, setTone] = useState<AiTone>('profesional')
  const [intent, setIntent] = useState(intents[0].value)
  const [context, setContext] = useState('')
  const [suggestion, setSuggestion] = useState('')

  const selectedIntent = useMemo(() => intents.find(option => option.value === intent) ?? intents[0], [intent, intents])
  const selectedTone = tones.find(option => option.value === tone) ?? tones[0]

  const generate = useMutation({
    mutationFn: () => {
      const guidance = [selectedIntent.instruction, context.trim()].filter(Boolean).join('\n\n')
      return props.mode === 'reply'
        ? mailApi.aiReply(props.accountId, props.messageId, tone, guidance)
        : mailApi.aiDraft(guidance, tone)
    },
    onSuccess: result => setSuggestion(result.text),
  })

  const canGenerate = props.mode === 'reply' || context.trim().length > 0

  return <section className={`ai-writing-assistant ${props.mode}`} aria-label="Asistente de redacción con IA">
    <header className="ai-writing-header">
      <div className="ai-writing-brand"><span className="ai-writing-icon"><Sparkles size={17} /></span><div><span>Nexo IA</span><strong>{props.mode === 'reply' ? 'Construir una respuesta' : 'Construir un correo'}</strong></div></div>
      <span className="ai-writing-context-badge">{props.mode === 'reply' ? 'Lee el hilo' : 'Desde una idea'}</span>
    </header>

    <div className="ai-writing-intro">
      <strong>{props.mode === 'reply' ? '¿Qué quieres lograr con esta respuesta?' : '¿Qué quieres lograr con este correo?'}</strong>
      <span>{props.mode === 'reply' ? 'NexoMail toma en cuenta el mensaje original y el contexto reciente de la conversación.' : 'Elige una intención y escribe sólo las ideas esenciales. NexoMail las convertirá en un correo completo.'}</span>
    </div>

    <div className="ai-intent-grid" role="group" aria-label="Objetivo del mensaje">
      {intents.map(option => <button type="button" key={option.value} className={`ai-intent-card ${intent === option.value ? 'active' : ''}`} onClick={() => setIntent(option.value)}>
        <span className="ai-intent-check">{intent === option.value ? <Check size={13} /> : null}</span>
        <strong>{option.label}</strong>
        <small>{option.description}</small>
      </button>)}
    </div>

    <div className="ai-writing-step">
      <div className="ai-step-heading"><span>Estilo</span><small>{selectedTone.description}</small></div>
      <div className="ai-tone-selector" aria-label="Tono de redacción">
        {tones.map(option => <button type="button" key={option.value} className={tone === option.value ? 'active' : ''} onClick={() => setTone(option.value)}>{option.label}</button>)}
      </div>
    </div>

    <label className="ai-context-field">
      <span>{props.mode === 'reply' ? 'Guía adicional' : 'Tu idea en pocas palabras'}</span>
      <textarea value={context} maxLength={3500} onChange={event => setContext(event.target.value)} placeholder={props.mode === 'reply' ? 'Opcional. Ej.: indicar que revisaré el documento, pero no comprometer una fecha todavía.' : 'Ej.: pedir reunión el martes para revisar avance del proyecto, pendientes y próximos hitos.'} />
      <small>{props.mode === 'reply' ? 'Puede dejarlo vacío: la IA propondrá una respuesta usando el hilo.' : 'No hace falta redactar bien. Basta con escribir los puntos que quiere comunicar.'}</small>
    </label>

    <div className="ai-generate-row">
      <div><strong>{selectedIntent.label}</strong><span> · {selectedTone.label}</span></div>
      <button type="button" className="primary-button ai-generate-button" disabled={!canGenerate || generate.isPending} onClick={() => generate.mutate()}>{suggestion ? <RefreshCw size={15} /> : <Sparkles size={15} />} {generate.isPending ? 'Preparando propuesta…' : suggestion ? 'Crear otra versión' : 'Crear propuesta'}</button>
    </div>

    {generate.isError && <div className="notice ai-writing-error">{generate.error instanceof Error ? generate.error.message : 'No fue posible generar la propuesta.'}</div>}

    {suggestion && <div className="ai-suggestion">
      <div className="ai-suggestion-heading"><div><span>Propuesta de Nexo IA</span><strong>Revise y ajuste antes de enviar</strong></div><small>La IA nunca envía el correo automáticamente.</small></div>
      <textarea value={suggestion} onChange={event => setSuggestion(event.target.value)} aria-label="Propuesta generada por IA" />
      <div className="ai-suggestion-actions"><button type="button" className="primary-button" disabled={!suggestion.trim()} onClick={() => props.onUse(suggestion.trim())}><Check size={15} /> Usar esta propuesta</button></div>
    </div>}
  </section>
}
