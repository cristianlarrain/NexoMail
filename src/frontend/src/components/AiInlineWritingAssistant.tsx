import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Check, RefreshCw, Sparkles } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { AiTone, AiWritingSuggestion } from '../types/mail'

type Props = {
  currentHtml: string
  recipient: string
  accountId?: string
  messageId?: string
  onUse: (suggestion: AiWritingSuggestion) => void
}

type Intent = {
  value: string
  label: string
  instruction: string
}

const tones: Array<{ value: AiTone; label: string }> = [
  { value: 'profesional', label: 'Profesional' },
  { value: 'formal', label: 'Formal' },
  { value: 'informal', label: 'Informal' },
  { value: 'breve', label: 'Breve' },
  { value: 'explicito', label: 'Explícito' },
]

const intents: Intent[] = [
  { value: 'mejorar', label: 'Mejorar', instruction: 'Mejora la redacción conservando exactamente el sentido y los hechos del usuario.' },
  { value: 'informar', label: 'Informar', instruction: 'Redacta el mensaje para informar de manera clara y ordenada.' },
  { value: 'solicitar', label: 'Solicitar', instruction: 'Redacta el mensaje para formular una solicitud concreta y fácil de responder.' },
  { value: 'responder', label: 'Responder', instruction: 'Redacta una respuesta pertinente al correo original y a lo que el usuario quiere expresar.' },
  { value: 'aclarar', label: 'Aclarar', instruction: 'Redacta el mensaje para aclarar o precisar el punto del usuario.' },
  { value: 'seguimiento', label: 'Seguimiento', instruction: 'Redacta un seguimiento claro, cordial y orientado al siguiente paso.' },
]

function plainText(html: string) {
  if (!html.trim()) return ''
  const documentValue = new DOMParser().parseFromString(html, 'text/html')
  return (documentValue.body.textContent ?? '').replace(/\u00a0/g, ' ').replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim()
}

export function AiInlineWritingAssistant({ currentHtml, recipient, accountId, messageId, onUse }: Props) {
  const [tone, setTone] = useState<AiTone>('profesional')
  const [intent, setIntent] = useState('mejorar')
  const [instruction, setInstruction] = useState('')
  const [suggestion, setSuggestion] = useState<AiWritingSuggestion | null>(null)

  const currentText = useMemo(() => plainText(currentHtml), [currentHtml])
  const selectedIntent = intents.find(option => option.value === intent) ?? intents[0]
  const hasInput = Boolean(currentText || instruction.trim())
  const isReplyContext = Boolean(accountId && messageId)

  const generate = useMutation({
    mutationFn: async () => {
      const parts = [selectedIntent.instruction]
      if (instruction.trim()) parts.push(`Lo que el usuario quiere expresar:\n${instruction.trim()}`)
      if (currentText) parts.push(`Texto actual del usuario. Reescríbelo y mejóralo; no agregues hechos, fechas, nombres ni compromisos que no estén aquí:\n${currentText}`)
      parts.push('Devuelve una versión lista para enviar, natural y sin explicaciones sobre el proceso de edición.')
      const context = parts.join('\n\n')

      return isReplyContext
        ? mailApi.aiReply(accountId!, messageId!, tone, context)
        : mailApi.aiDraft(context, tone, recipient)
    },
    onSuccess: result => setSuggestion(result),
  })

  return <section className="ai-inline-writing" aria-label="Redactar con Nexo IA">
    <header className="ai-inline-header">
      <div><span className="ai-inline-mark"><Sparkles size={14} /></span><div><strong>Redactar con IA</strong><small>{currentText ? 'Mejora o reescribe lo que ya tienes.' : 'Indica qué quieres expresar y Nexo IA preparará una propuesta.'}</small></div></div>
      <span className="ai-inline-context">{isReplyContext ? 'Usa el correo original como contexto' : 'Correo nuevo'}</span>
    </header>

    <div className="ai-inline-controls">
      <label className="ai-inline-instruction">
        <span>¿Qué quieres expresar?</span>
        <textarea value={instruction} maxLength={3500} onChange={event => setInstruction(event.target.value)} placeholder={currentText ? 'Opcional: indica qué quieres cambiar, enfatizar o agregar sin perder el sentido.' : 'Ej.: informar que estaré con licencia esta semana y que enviaré los contenidos para avanzar en clases.'} />
      </label>

      <div className="ai-inline-option-row">
        <span>Objetivo</span>
        <div>{intents.map(option => <button type="button" key={option.value} className={intent === option.value ? 'active' : ''} onClick={() => setIntent(option.value)}>{option.label}</button>)}</div>
      </div>

      <div className="ai-inline-option-row">
        <span>Tono</span>
        <div>{tones.map(option => <button type="button" key={option.value} className={tone === option.value ? 'active' : ''} onClick={() => setTone(option.value)}>{option.label}</button>)}</div>
      </div>

      {generate.isError && <div className="ai-inline-error">{generate.error instanceof Error ? generate.error.message : 'No fue posible generar la propuesta.'}</div>}

      <div className="ai-inline-generate">
        <small>{selectedIntent.label} · {tones.find(option => option.value === tone)?.label}</small>
        <button type="button" className="primary-button" disabled={!hasInput || generate.isPending} onClick={() => generate.mutate()}><Sparkles size={14} /> {generate.isPending ? 'Generando…' : currentText ? 'Proponer versión mejorada' : 'Redactar propuesta'}</button>
      </div>
    </div>

    {suggestion && <div className="ai-inline-result">
      <div className="ai-inline-result-heading"><div><span>Propuesta de Nexo IA</span><strong>Revísala antes de usarla</strong></div><small>Puedes editar esta propuesta directamente.</small></div>
      <textarea value={suggestion.text} onChange={event => setSuggestion(current => current ? { ...current, text: event.target.value } : current)} />
      <div className="ai-inline-result-actions">
        <button type="button" className="secondary-button" disabled={generate.isPending} onClick={() => generate.mutate()}><RefreshCw size={14} /> {generate.isPending ? 'Regenerando…' : 'Regenerar'}</button>
        <button type="button" className="primary-button" disabled={!suggestion.text.trim()} onClick={() => onUse(suggestion)}><Check size={14} /> Usar propuesta</button>
      </div>
    </div>}
  </section>
}
