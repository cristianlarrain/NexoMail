import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, ArrowRight, Check, RefreshCw, Sparkles } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { AiTone, AiWritingSuggestion } from '../types/mail'

type ReplyProps = {
  mode: 'reply'
  accountId: string
  messageId: string
  onUse: (suggestion: AiWritingSuggestion) => void
}

type ComposeProps = {
  mode: 'compose'
  recipient: string
  onRecipientChange: (value: string) => void
  onUse: (suggestion: AiWritingSuggestion) => void
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
  const [step, setStep] = useState(1)
  const [tone, setTone] = useState<AiTone>('profesional')
  const [intent, setIntent] = useState(intents[0].value)
  const [context, setContext] = useState('')
  const [suggestion, setSuggestion] = useState<AiWritingSuggestion | null>(null)

  const selectedIntent = useMemo(() => intents.find(option => option.value === intent) ?? intents[0], [intent, intents])
  const selectedTone = tones.find(option => option.value === tone) ?? tones[0]

  const generate = useMutation({
    mutationFn: () => {
      const guidance = [selectedIntent.instruction, context.trim()].filter(Boolean).join('\n\n')
      return props.mode === 'reply'
        ? mailApi.aiReply(props.accountId, props.messageId, tone, guidance)
        : mailApi.aiDraft(guidance, tone, props.recipient)
    },
    onSuccess: result => setSuggestion(result),
  })

  const canAdvance = props.mode === 'compose'
    ? step === 1 ? props.recipient.trim().length > 0 : step === 2 ? context.trim().length > 0 : true
    : true

  function next() {
    if (!canAdvance || step >= 3) return
    setStep(current => current + 1)
  }

  function back() {
    if (step <= 1) return
    setStep(current => current - 1)
  }

  return <section className={`ai-writing-assistant assistant-mode ${props.mode}`} aria-label="Asistente de redacción con IA">
    <header className="ai-writing-header">
      <div className="ai-writing-brand"><span className="ai-writing-icon"><Sparkles size={17} /></span><div><span>Nexo IA</span><strong>Asistente de redacción</strong></div></div>
      <span className="ai-writing-context-badge">Paso {step} de 3</span>
    </header>

    <div className="ai-assistant-progress" aria-hidden="true">
      {[1, 2, 3].map(value => <span key={value} className={value <= step ? 'active' : ''} />)}
    </div>

    {props.mode === 'compose' && step === 1 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿A quién quieres enviar este correo?</strong>
        <p>Puede ser una dirección, un nombre o varios destinatarios. Este dato también quedará en el campo “Para”.</p>
        <input value={props.recipient} onChange={event => props.onRecipientChange(event.target.value)} placeholder="Ej.: claudio@empresa.cl o Claudio Astudillo" autoComplete="off" />
      </div>
    </div>}

    {props.mode === 'reply' && step === 1 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>Ya leí el correo y el contexto reciente del hilo.</strong>
        <p>¿Qué quieres lograr con tu respuesta?</p>
        <IntentGrid intents={intents} selected={intent} onSelect={setIntent} />
      </div>
    </div>}

    {props.mode === 'compose' && step === 2 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿Qué quieres decir?</strong>
        <p>Escríbelo en primeras palabras. No hace falta redactarlo bien; basta con la idea.</p>
        <textarea value={context} maxLength={3500} onChange={event => setContext(event.target.value)} placeholder="Ej.: pedir reunión el martes para revisar avance del proyecto, pendientes y próximos hitos." />
        <span className="ai-assistant-subquestion">¿Cuál es el objetivo principal?</span>
        <IntentGrid intents={intents} selected={intent} onSelect={setIntent} />
      </div>
    </div>}

    {props.mode === 'reply' && step === 2 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿Quieres agregar alguna indicación?</strong>
        <p>Es opcional. Si lo dejas vacío, prepararé la respuesta usando sólo el hilo y el objetivo seleccionado.</p>
        <textarea value={context} maxLength={3500} onChange={event => setContext(event.target.value)} placeholder="Ej.: agradecer, indicar que revisaré el documento y evitar comprometer una fecha todavía." />
      </div>
    </div>}

    {step === 3 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿Cómo quieres que suene?</strong>
        <p>Elige el tono. La propuesta seguirá siendo completamente editable.</p>
        <div className="ai-tone-selector assistant-tones" aria-label="Tono de redacción">
          {tones.map(option => <button type="button" key={option.value} className={tone === option.value ? 'active' : ''} onClick={() => setTone(option.value)}><strong>{option.label}</strong><small>{option.description}</small></button>)}
        </div>
        <div className="ai-assistant-summary">
          <span>{selectedIntent.label}</span><span>{selectedTone.label}</span>{props.mode === 'compose' && <span>{props.recipient}</span>}
        </div>
        <button type="button" className="primary-button ai-generate-button" disabled={generate.isPending} onClick={() => generate.mutate()}>{suggestion ? <RefreshCw size={15} /> : <Sparkles size={15} />} {generate.isPending ? 'Preparando propuesta…' : suggestion ? 'Crear otra versión' : 'Preparar propuesta'}</button>
      </div>
    </div>}

    <div className="ai-assistant-navigation">
      <button type="button" className="secondary-button" onClick={back} disabled={step === 1}><ArrowLeft size={15} /> Atrás</button>
      {step < 3 && <button type="button" className="primary-button" onClick={next} disabled={!canAdvance}>Continuar <ArrowRight size={15} /></button>}
    </div>

    {generate.isError && <div className="notice ai-writing-error">{generate.error instanceof Error ? generate.error.message : 'No fue posible generar la propuesta.'}</div>}

    {suggestion && <div className="ai-suggestion assistant-result">
      <div className="ai-suggestion-heading"><div><span>Nexo IA preparó esta propuesta</span><strong>Revise y ajuste antes de enviar</strong></div><small>La IA nunca envía el correo automáticamente.</small></div>
      {props.mode === 'compose' && <label className="ai-subject-suggestion"><span>Asunto sugerido</span><input value={suggestion.subject ?? ''} onChange={event => setSuggestion(current => current ? { ...current, subject: event.target.value } : current)} /></label>}
      <label className="ai-body-suggestion"><span>{props.mode === 'reply' ? 'Respuesta sugerida' : 'Mensaje sugerido'}</span><textarea value={suggestion.text} onChange={event => setSuggestion(current => current ? { ...current, text: event.target.value } : current)} /></label>
      <div className="ai-suggestion-actions"><button type="button" className="primary-button" disabled={!suggestion.text.trim()} onClick={() => props.onUse(suggestion)}><Check size={15} /> Usar esta propuesta</button></div>
    </div>}
  </section>
}

function IntentGrid({ intents, selected, onSelect }: { intents: IntentOption[]; selected: string; onSelect: (value: string) => void }) {
  return <div className="ai-intent-grid" role="group" aria-label="Objetivo del mensaje">
    {intents.map(option => <button type="button" key={option.value} className={`ai-intent-card ${selected === option.value ? 'active' : ''}`} onClick={() => onSelect(option.value)}>
      <span className="ai-intent-check">{selected === option.value ? <Check size={13} /> : null}</span>
      <strong>{option.label}</strong>
      <small>{option.description}</small>
    </button>)}
  </div>
}
