import { useMemo, useState } from 'react'
import { ArrowLeft, ArrowRight, Check, RefreshCw, Sparkles } from 'lucide-react'
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
  onManual?: () => void
}

type Props = ReplyProps | ComposeProps

type IntentOption = {
  value: string
  label: string
  description: string
}

const tones: Array<{ value: AiTone; label: string; description: string }> = [
  { value: 'profesional', label: 'Profesional', description: 'Claro y cordial' },
  { value: 'formal', label: 'Formal', description: 'Protocolar y respetuoso' },
  { value: 'informal', label: 'Informal', description: 'Natural y cercano' },
  { value: 'breve', label: 'Breve', description: 'Sólo lo esencial' },
  { value: 'explicito', label: 'Explícito', description: 'Preciso y detallado' },
]

const replyIntents: IntentOption[] = [
  { value: 'responder', label: 'Responder', description: 'Contestar el punto principal' },
  { value: 'confirmar', label: 'Confirmar', description: 'Recepción, acuerdo o gestión' },
  { value: 'solicitar', label: 'Solicitar', description: 'Pedir información o una acción' },
  { value: 'aclarar', label: 'Aclarar', description: 'Precisar o corregir un punto' },
  { value: 'agradecer', label: 'Agradecer', description: 'Reconocer y cerrar correctamente' },
  { value: 'seguimiento', label: 'Seguimiento', description: 'Retomar un asunto pendiente' },
]

const composeIntents: IntentOption[] = [
  { value: 'informar', label: 'Informar', description: 'Comunicar un antecedente' },
  { value: 'solicitar', label: 'Solicitar', description: 'Pedir algo concretamente' },
  { value: 'coordinar', label: 'Coordinar', description: 'Organizar reunión o acción' },
  { value: 'confirmar', label: 'Confirmar', description: 'Dejar constancia o acuerdo' },
  { value: 'agradecer', label: 'Agradecer', description: 'Reconocer una gestión o apoyo' },
  { value: 'seguimiento', label: 'Seguimiento', description: 'Retomar un tema pendiente' },
]

function subjectFor(intent: string, context: string) {
  const topic = context.trim().replace(/\s+/g, ' ').split(' ').slice(0, 7).join(' ').replace(/[.,;:!?]+$/g, '')
  const prefix = intent === 'solicitar' ? 'Solicitud'
    : intent === 'coordinar' ? 'Coordinación'
      : intent === 'confirmar' ? 'Confirmación'
        : intent === 'agradecer' ? 'Agradecimiento'
          : intent === 'seguimiento' ? 'Seguimiento'
            : 'Información'
  return topic ? `${prefix}: ${topic}` : prefix
}

function greeting(recipient: string, tone: AiTone) {
  const value = recipient.trim()
  const name = value.includes('@') ? '' : value.split(/[;,]/)[0]?.trim()
  if (tone === 'informal') return name ? `Hola ${name},` : 'Hola,'
  return name ? `Estimado/a ${name}:` : 'Estimado/a:'
}

function intentLead(intent: string) {
  if (intent === 'solicitar') return 'Quisiera solicitar lo siguiente'
  if (intent === 'coordinar') return 'Quisiera coordinar lo siguiente'
  if (intent === 'confirmar') return 'Quisiera confirmar lo siguiente'
  if (intent === 'agradecer') return 'Quisiera agradecer y señalar lo siguiente'
  if (intent === 'seguimiento') return 'Quisiera dar seguimiento al siguiente asunto'
  if (intent === 'aclarar') return 'Quisiera aclarar el siguiente punto'
  if (intent === 'responder') return 'Respecto de lo planteado, quisiera responder lo siguiente'
  return 'Quisiera informar lo siguiente'
}

function mockSuggestion(mode: 'compose' | 'reply', recipient: string, intent: string, tone: AiTone, context: string): AiWritingSuggestion {
  const idea = context.trim() || 'tomar conocimiento del mensaje y responder adecuadamente al asunto planteado'
  const lead = `${intentLead(intent)}: ${idea.replace(/[.\s]+$/g, '')}.`

  let text: string
  if (tone === 'breve') {
    text = mode === 'compose'
      ? `${greeting(recipient, tone)}\n\n${lead}\n\nQuedo atento/a.\n\nSaludos,`
      : `${lead}\n\nGracias. Quedo atento/a.`
  } else if (tone === 'informal') {
    text = mode === 'compose'
      ? `${greeting(recipient, tone)}\n\n${lead}\n\nSi te parece, quedo atento para coordinar o complementar lo necesario.\n\nSaludos,`
      : `${lead}\n\nGracias por el mensaje. Quedo atento para avanzar con lo necesario.`
  } else if (tone === 'formal') {
    text = mode === 'compose'
      ? `${greeting(recipient, tone)}\n\nJunto con saludar, ${lead.charAt(0).toLowerCase()}${lead.slice(1)}\n\nAgradeceré considerar lo anterior y quedo atento/a a cualquier antecedente adicional que sea necesario.\n\nSaluda atentamente,`
      : `Junto con saludar, ${lead.charAt(0).toLowerCase()}${lead.slice(1)}\n\nAgradezco la información remitida y quedo atento/a a cualquier antecedente adicional que corresponda.`
  } else if (tone === 'explicito') {
    text = mode === 'compose'
      ? `${greeting(recipient, tone)}\n\n${lead}\n\nEl propósito de este correo es dejar claramente planteado este punto y facilitar el siguiente paso. Si se requiere algún antecedente adicional, agradeceré indicarlo para incorporarlo oportunamente.\n\nQuedo atento/a.\n\nSaludos,`
      : `${lead}\n\nCon esto, la intención es dejar claramente respondido el punto principal y facilitar la continuidad de la gestión. Si existe algún antecedente adicional que deba considerar, agradeceré indicarlo.`
  } else {
    text = mode === 'compose'
      ? `${greeting(recipient, tone)}\n\n${lead}\n\nAgradezco desde ya su atención y quedo disponible para complementar cualquier antecedente necesario.\n\nSaludos cordiales,`
      : `${lead}\n\nGracias por la información. Quedo atento/a a los próximos pasos o antecedentes que sean necesarios.`
  }

  return {
    subject: mode === 'compose' ? subjectFor(intent, idea) : null,
    text,
  }
}

export function AiWritingAssistant(props: Props) {
  const intents = props.mode === 'reply' ? replyIntents : composeIntents
  const [step, setStep] = useState(1)
  const [tone, setTone] = useState<AiTone>('profesional')
  const [intent, setIntent] = useState(intents[0].value)
  const [context, setContext] = useState('')
  const [suggestion, setSuggestion] = useState<AiWritingSuggestion | null>(null)

  const selectedIntent = useMemo(() => intents.find(option => option.value === intent) ?? intents[0], [intent, intents])
  const selectedTone = tones.find(option => option.value === tone) ?? tones[0]
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

  function prepareSuggestion() {
    setSuggestion(mockSuggestion(props.mode, props.mode === 'compose' ? props.recipient : '', intent, tone, context))
  }

  return <section className={`ai-writing-assistant assistant-mode ${props.mode}`} aria-label="Asistente de redacción Nexo IA">
    <header className="ai-writing-header">
      <div className="ai-writing-brand"><span className="ai-writing-icon"><Sparkles size={17} /></span><div><span>Nexo IA</span><strong>{props.mode === 'reply' ? 'Preparemos tu respuesta' : 'Preparemos tu correo'}</strong></div></div>
      <span className="ai-writing-context-badge">Prueba local · Paso {step} de 3</span>
    </header>

    <div className="ai-assistant-progress" aria-hidden="true">
      {[1, 2, 3].map(value => <span key={value} className={value <= step ? 'active' : ''} />)}
    </div>

    {props.mode === 'compose' && step === 1 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿A quién quieres dirigir este correo?</strong>
        <p>Escribe un nombre, una dirección o varios destinatarios. Después podrás revisar y corregir el campo “Para”.</p>
        <input value={props.recipient} onChange={event => props.onRecipientChange(event.target.value)} placeholder="Ej.: Claudio Astudillo o claudio@empresa.cl" autoComplete="off" />
      </div>
    </div>}

    {props.mode === 'reply' && step === 1 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿Qué quieres lograr con tu respuesta?</strong>
        <p>En la versión conectada, Nexo IA leerá el mensaje inicial y el contexto reciente del hilo antes de proponerte la respuesta.</p>
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
        <p>Es opcional. En la versión final, si lo dejas vacío, la propuesta se construirá a partir del hilo.</p>
        <textarea value={context} maxLength={3500} onChange={event => setContext(event.target.value)} placeholder="Ej.: agradecer, indicar que revisaré el documento y evitar comprometer una fecha todavía." />
      </div>
    </div>}

    {step === 3 && <div className="ai-assistant-turn">
      <span className="ai-assistant-avatar"><Sparkles size={18} /></span>
      <div className="ai-assistant-bubble">
        <strong>¿Cómo quieres que suene?</strong>
        <p>Elige el tono. Esta prueba genera un texto simulado localmente y no utiliza ninguna API.</p>
        <div className="ai-tone-selector assistant-tones" aria-label="Tono de redacción">
          {tones.map(option => <button type="button" key={option.value} className={tone === option.value ? 'active' : ''} onClick={() => setTone(option.value)}><strong>{option.label}</strong><small>{option.description}</small></button>)}
        </div>
        <div className="ai-assistant-summary"><span>{selectedIntent.label}</span><span>{selectedTone.label}</span>{props.mode === 'compose' && <span>{props.recipient}</span>}</div>
        <button type="button" className="primary-button ai-generate-button" onClick={prepareSuggestion}>{suggestion ? <RefreshCw size={15} /> : <Sparkles size={15} />} {suggestion ? 'Crear otra versión' : 'Preparar propuesta'}</button>
      </div>
    </div>}

    <div className="ai-assistant-navigation">
      <button type="button" className="secondary-button" onClick={back} disabled={step === 1}><ArrowLeft size={15} /> Atrás</button>
      {props.mode === 'compose' && props.onManual && <button type="button" className="text-button ai-manual-link" onClick={props.onManual}>Redactar manualmente</button>}
      {step < 3 && <button type="button" className="primary-button" onClick={next} disabled={!canAdvance}>Continuar <ArrowRight size={15} /></button>}
    </div>

    {suggestion && <div className="ai-suggestion assistant-result">
      <div className="ai-suggestion-heading"><div><span>Nexo IA preparó esta propuesta</span><strong>Revísala antes de pasar al envío</strong></div><small>Propuesta simulada para validar la experiencia.</small></div>
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
      <strong>{option.label}</strong><small>{option.description}</small>
    </button>)}
  </div>
}
