import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useLocation } from 'react-router-dom'
import { Paperclip, RefreshCw, Sparkles } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { AiTone, AiWritingSuggestion, MailAttachment } from '../types/mail'

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

type ComposeLocationState = {
  mode?: 'reply' | 'replyAll' | 'forward' | 'followUp'
  message?: { attachments?: MailAttachment[] }
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
  { value: 'informar', label: 'Informar', instruction: 'Redacta el mensaje para informar de manera clara, ordenada y natural.' },
  { value: 'solicitar', label: 'Solicitar', instruction: 'Redacta el mensaje para formular una solicitud concreta y fácil de responder.' },
  { value: 'responder', label: 'Responder', instruction: 'Redacta una respuesta pertinente al correo original y a lo que el usuario quiere expresar.' },
  { value: 'aclarar', label: 'Aclarar', instruction: 'Redacta el mensaje para aclarar o precisar el punto del usuario.' },
  { value: 'seguimiento', label: 'Seguimiento', instruction: 'Redacta un seguimiento claro, cordial y orientado al siguiente paso.' },
]

function plainText(html: string) {
  if (!html.trim()) return ''
  const documentValue = new DOMParser().parseFromString(html, 'text/html')
  return (documentValue.body.textContent ?? '')
    .replace(/\u00a0/g, ' ')
    .replace(/[ \t]+/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .trim()
}

function sizeLabel(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function AiInlineWritingAssistant({ currentHtml, recipient, accountId, messageId, onUse }: Props) {
  const location = useLocation()
  const composeState = (location.state ?? {}) as ComposeLocationState
  const isForwardContext = composeState.mode === 'forward'
  const forwardedAttachments = isForwardContext ? composeState.message?.attachments ?? [] : []
  const isReplyContext = Boolean(accountId && messageId)
  const [tone, setTone] = useState<AiTone>('profesional')
  const [intent, setIntent] = useState(isReplyContext ? 'responder' : 'mejorar')
  const [generated, setGenerated] = useState(false)

  const currentText = useMemo(() => plainText(currentHtml), [currentHtml])
  const selectedIntent = intents.find(option => option.value === intent) ?? intents[0]
  const selectedTone = tones.find(option => option.value === tone) ?? tones[0]
  const canGenerate = Boolean(currentText || isReplyContext)

  const generate = useMutation({
    mutationFn: async () => {
      const parts = [selectedIntent.instruction]
      if (currentText) {
        parts.push(`Texto actual del usuario. Reescríbelo y mejóralo sin cambiar los hechos, nombres, fechas ni compromisos:\n${currentText}`)
      } else if (isReplyContext) {
        parts.push('El usuario todavía no escribió un borrador. Propón una respuesta completa a partir del correo original, sin inventar información que no esté en la conversación.')
      }
      parts.push('Devuelve únicamente una versión lista para enviar, sin explicar el proceso de edición.')
      const context = parts.join('\n\n')

      return isReplyContext
        ? mailApi.aiReply(accountId!, messageId!, tone, context)
        : mailApi.aiDraft(context, tone, recipient)
    },
    onSuccess: result => {
      setGenerated(true)
      onUse(result)
    },
  })

  const generateLabel = generate.isPending
    ? generated ? 'Regenerando…' : 'Generando…'
    : generated
      ? isReplyContext ? 'Regenerar respuesta' : 'Regenerar texto'
      : currentText
        ? isReplyContext ? 'Mejorar respuesta' : 'Mejorar texto'
        : isReplyContext ? 'Generar respuesta' : 'Generar texto'

  return <section className="ai-inline-writing" aria-label="Opciones de redacción con Nexo IA">
    <div className="ai-inline-choice-strip">
      <fieldset className="ai-inline-radio-group">
        <legend>Objetivo</legend>
        <div>
          {intents.map(option => <label key={option.value} className={intent === option.value ? 'active' : ''} title={option.instruction}>
            <input type="radio" name="ai-writing-intent" value={option.value} checked={intent === option.value} onChange={() => setIntent(option.value)} />
            <span>{option.label}</span>
          </label>)}
        </div>
      </fieldset>

      <fieldset className="ai-inline-radio-group ai-inline-tone-group">
        <legend>Tono</legend>
        <div>
          {tones.map(option => <label key={option.value} className={tone === option.value ? 'active' : ''}>
            <input type="radio" name="ai-writing-tone" value={option.value} checked={tone === option.value} onChange={() => setTone(option.value)} />
            <span>{option.label}</span>
          </label>)}
        </div>
      </fieldset>
    </div>

    {isForwardContext && <div className="ai-forward-attachments" aria-label="Adjuntos originales del correo reenviado">
      <div className="ai-forward-attachments-heading">
        <Paperclip size={14} />
        <strong>{forwardedAttachments.length ? `Adjuntos originales · ${forwardedAttachments.length}` : 'Sin adjuntos originales'}</strong>
        {forwardedAttachments.length > 0 && <span>Se incluirán al reenviar.</span>}
      </div>
      {forwardedAttachments.length > 0 && <div className="ai-forward-attachment-list">
        {forwardedAttachments.map(file => <span key={file.id} title={file.name}><Paperclip size={12} /><b>{file.name}</b><small>{sizeLabel(file.size)}</small></span>)}
      </div>}
    </div>}

    <div className="ai-inline-generation-row">
      <div className="ai-inline-generation-copy">
        <Sparkles size={13} />
        <span>Nexo IA · {selectedIntent.label} · {selectedTone.label}</span>
        {generate.isError && <em>{generate.error instanceof Error ? generate.error.message : 'No fue posible generar la propuesta.'}</em>}
      </div>
      <button type="button" className="primary-button ai-inline-generation-button" disabled={!canGenerate || generate.isPending} onClick={() => generate.mutate()}>
        {generated ? <RefreshCw size={14} /> : <Sparkles size={14} />}
        {generateLabel}
      </button>
    </div>
  </section>
}
