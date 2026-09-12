import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { ArrowLeft, Bold, ChevronDown, Italic, Link, List, ListOrdered, Mic, MicOff, Paperclip, Save, Send, Sparkles, Trash2, Underline, X } from 'lucide-react'
import { AiInlineWritingAssistant } from '../components/AiInlineWritingAssistant'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { commercialApi } from '../api/commercialApi'
import { mailApi } from '../api/mailApi'
import type { AiWritingSuggestion, ComposeMessage, MailAttachment, MailMessage, OutgoingAttachment } from '../types/mail'
import { commercialEntitlements } from '../utils/commercialEntitlements'
import { sanitizeEmailHtml } from '../utils/sanitizeEmailHtml'

type ComposeState = {
  mode?: 'reply' | 'replyAll' | 'forward' | 'followUp' | 'editDraft'
  message?: MailMessage
  fromAccountId?: string
  initialBody?: string
  returnTo?: string
  returnState?: unknown
}
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

function escapeHtml(value: string) {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#39;')
}

function textToHtml(value: string) {
  return value.trim().split(/\n{2,}/).map(paragraph => `<p>${escapeHtml(paragraph).replaceAll('\n', '<br />')}</p>`).join('')
}

function normalizedAddress(value: string) {
  return value.trim().toLowerCase()
}

function uniqueAddresses(values: string[]) {
  const seen = new Set<string>()
  return values.map(value => value.trim()).filter(value => {
    const key = normalizedAddress(value)
    if (!key || seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export function ComposePage() {
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const state = (location.state ?? {}) as ComposeState
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const { data: commercialSubscription } = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const hasNexi = commercialSubscription?.entitlements.includes(commercialEntitlements.nexiAi) === true
  const origin = state.message
  const editingDraft = Boolean(origin && state.mode === 'editDraft')
  const [from, setFrom] = useState(origin?.accountId ?? state.fromAccountId ?? '')
  const [to, setTo] = useState(origin
    ? state.mode === 'forward'
      ? ''
      : state.mode === 'followUp' || state.mode === 'editDraft'
        ? origin.to.map(item => item.address).join(', ')
        : origin.from.address
    : '')
  const [cc, setCc] = useState(() => editingDraft ? origin?.cc.map(item => item.address).join(', ') ?? '' : '')
  const [bcc, setBcc] = useState('')
  const [showCc, setShowCc] = useState(() => Boolean(editingDraft && origin?.cc.length))
  const [subject, setSubject] = useState(() => origin
    ? state.mode === 'editDraft'
      ? origin.subject
      : `${state.mode === 'forward' ? 'Fwd:' : 'Re:'} ${origin.subject}`
    : '')
  const [body, setBody] = useState(() => editingDraft && origin ? origin.htmlBody : state.initialBody ? textToHtml(state.initialBody) : '')
  const [attachments, setAttachments] = useState<OutgoingAttachment[]>([])
  const [retainedDraftAttachments, setRetainedDraftAttachments] = useState<MailAttachment[]>(() => editingDraft && origin ? [...origin.attachments] : [])
  const [attachmentError, setAttachmentError] = useState('')
  const [recipientFocused, setRecipientFocused] = useState(false)
  const [listening, setListening] = useState(false)
  const [dictationError, setDictationError] = useState('')
  const [confirmDiscard, setConfirmDiscard] = useState(false)
  const editor = useRef<HTMLDivElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const recognition = useRef<SpeechRecognitionLike | null>(null)

  const fromAccountId = from || accounts[0]?.id
  const recipientTerm = to.slice(to.lastIndexOf(',') + 1).trim()
  const contacts = useQuery({
    queryKey: ['contacts', fromAccountId, recipientTerm],
    queryFn: () => mailApi.contacts(fromAccountId!, recipientTerm),
    enabled: Boolean(fromAccountId && recipientFocused && recipientTerm.length >= 2),
    retry: false,
  })
  const action = useMemo(() => state.mode === 'reply'
    ? 'Responder'
    : state.mode === 'replyAll'
      ? 'Responder a todos'
      : state.mode === 'forward'
        ? 'Reenviar'
        : state.mode === 'followUp'
          ? 'Enviar seguimiento'
          : state.mode === 'editDraft'
            ? 'Editar borrador'
            : 'Enviar', [state.mode])

  function buildPayload(): ComposeMessage {
    if (!fromAccountId) throw new Error('Selecciona una cuenta desde la cual enviar o guardar el correo.')
    return {
      fromAccountId,
      to: to.split(',').map(v => v.trim()).filter(Boolean),
      cc: cc.split(',').map(v => v.trim()).filter(Boolean),
      bcc: bcc.split(',').map(v => v.trim()).filter(Boolean),
      subject,
      htmlBody: body || '<p></p>',
      attachments,
    }
  }

  const send = useMutation({
    mutationFn: () => {
      const payload = buildPayload()
      if (origin && state.mode === 'editDraft') return mailApi.sendDraft(origin.accountId, origin.providerMessageId, payload, retainedDraftAttachments)
      if (origin && state.mode !== 'forward') return mailApi.reply(origin.accountId, origin.providerMessageId, payload, state.mode === 'replyAll')
      if (origin && state.mode === 'forward') return mailApi.forward(origin.accountId, origin.providerMessageId, payload)
      return mailApi.send(payload)
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
      void queryClient.invalidateQueries({ queryKey: ['control-center-activity'] })
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      navigate('/inbox', { state: { sent: true } })
    },
  })

  const saveDraft = useMutation({
    mutationFn: () => {
      const payload = buildPayload()
      if (origin && state.mode === 'editDraft') return mailApi.updateDraft(origin.accountId, origin.providerMessageId, payload, retainedDraftAttachments)
      const replyToMessageId = origin && state.mode !== 'forward' ? origin.providerMessageId : undefined
      return mailApi.saveDraft(payload, replyToMessageId)
    },
    onSuccess: () => {
      queryClient.removeQueries({ predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === 'drafts' })
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
      navigate('/drafts', { state: { draftSaved: true } })
    },
  })

  useEffect(() => () => recognition.current?.stop(), [])
  useEffect(() => {
    if (!origin || state.mode !== 'replyAll' || accounts.length === 0) return
    const ownAddress = normalizedAddress(accounts.find(account => account.id === origin.accountId)?.emailAddress ?? '')
    const toAddresses = uniqueAddresses([origin.from.address, ...origin.to.map(item => item.address)])
      .filter(address => normalizedAddress(address) !== ownAddress)
    const toSet = new Set(toAddresses.map(normalizedAddress))
    const ccAddresses = uniqueAddresses(origin.cc.map(item => item.address))
      .filter(address => normalizedAddress(address) !== ownAddress && !toSet.has(normalizedAddress(address)))
    setTo(toAddresses.join(', '))
    setCc(ccAddresses.join(', '))
    setShowCc(ccAddresses.length > 0)
  }, [accounts, origin, state.mode])
  useEffect(() => {
    if (!editor.current) return
    const html = editingDraft && origin ? origin.htmlBody : state.initialBody ? textToHtml(state.initialBody) : ''
    if (!html) return
    editor.current.innerHTML = html
    setBody(html)
  }, [editingDraft, origin, state.initialBody])

  function submit(event: FormEvent) {
    event.preventDefault()
    recognition.current?.stop()
    send.mutate()
  }

  function format(command: string, value?: string) {
    editor.current?.focus()
    document.execCommand(command, false, value)
    setBody(editor.current?.innerHTML ?? '')
  }

  function selectContact(emailAddress: string) {
    const separator = to.lastIndexOf(',')
    const prefix = separator < 0 ? '' : `${to.slice(0, separator + 1).trimEnd()} `
    setTo(`${prefix}${emailAddress}, `)
    setRecipientFocused(false)
  }

  function useAiProposal(suggestion: AiWritingSuggestion) {
    if ((!origin || editingDraft) && suggestion.subject?.trim()) setSubject(suggestion.subject.trim())
    const html = textToHtml(suggestion.text)
    setBody(html)
    if (editor.current) {
      editor.current.innerHTML = html
      editor.current.focus()
    }
  }

  function closeComposer() {
    recognition.current?.stop()
    if (state.returnTo) {
      navigate(state.returnTo, { state: state.returnState })
      return
    }
    if (origin && !editingDraft) {
      navigate(`/message/${origin.accountId}/${origin.providerMessageId}`)
      return
    }
    navigate(-1)
  }

  function discardComposer() {
    setConfirmDiscard(false)
    closeComposer()
  }

  function appendDictation(text: string) {
    const value = text.trim()
    if (!value || !editor.current) return
    const target = editor.current
    const needsSpace = Boolean(target.textContent?.trim())
    target.focus()
    const insertion = `${needsSpace ? ' ' : ''}${value}`
    if (!document.execCommand('insertText', false, insertion)) target.append(document.createTextNode(insertion))
    setBody(target.innerHTML)
  }

  function toggleDictation() {
    if (listening) {
      recognition.current?.stop()
      return
    }
    const speechWindow = window as SpeechWindow
    const Recognition = speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition
    if (!Recognition) {
      setDictationError('El dictado por voz no está disponible en este navegador. Use Chrome o Edge actualizado.')
      return
    }

    const instance = new Recognition()
    recognition.current = instance
    instance.lang = 'es-CL'
    instance.continuous = true
    instance.interimResults = false
    instance.onresult = event => {
      let transcript = ''
      for (let index = event.resultIndex ?? 0; index < event.results.length; index++) transcript += `${event.results[index]?.[0]?.transcript ?? ''} `
      appendDictation(transcript)
    }
    instance.onerror = event => {
      setDictationError(event.error === 'not-allowed' || event.error === 'service-not-allowed' ? 'Debe permitir el acceso al micrófono para usar el dictado.' : 'No fue posible continuar con el dictado. Inténtelo nuevamente.')
      setListening(false)
    }
    instance.onend = () => {
      setListening(false)
      recognition.current = null
    }
    try {
      setDictationError('')
      instance.start()
      setListening(true)
    } catch {
      setDictationError('No fue posible iniciar el micrófono. Inténtelo nuevamente.')
      setListening(false)
      recognition.current = null
    }
  }

  async function addFiles(files: FileList | null) {
    if (!files?.length) return
    const selected = [...files]
    const retainedBytes = editingDraft
      ? retainedDraftAttachments.reduce((sum, file) => sum + file.size, 0)
      : state.mode === 'forward' && origin
        ? origin.attachments.reduce((sum, file) => sum + file.size, 0)
        : 0
    const total = retainedBytes + attachments.reduce((sum, file) => sum + Math.ceil(file.base64Content.length * 0.75), 0) + selected.reduce((sum, file) => sum + file.size, 0)
    if (selected.some(file => file.size > 8 * 1024 * 1024) || total > 15 * 1024 * 1024) {
      setAttachmentError('Cada archivo admite hasta 8 MB y el total hasta 15 MB.')
      return
    }
    const values = await Promise.all(selected.map(file => new Promise<OutgoingAttachment>((resolve, reject) => {
      const reader = new FileReader()
      reader.onerror = () => reject(reader.error)
      reader.onload = () => resolve({ name: file.name, contentType: file.type || 'application/octet-stream', base64Content: String(reader.result).split(',')[1] })
      reader.readAsDataURL(file)
    })))
    setAttachments(current => [...current, ...values])
    setAttachmentError('')
    if (fileInput.current) fileInput.current.value = ''
  }

  const composerBusy = send.isPending || saveDraft.isPending

  return <section className="compose-page">
    <div className="compose-card ai-compose-card">
      <header className="ai-compose-header">
        <div className="ai-compose-heading">
          {hasNexi && <span className="ai-compose-mark"><Sparkles size={18} /></span>}
          <div><p className="eyebrow">{hasNexi ? 'Nexo IA' : 'NexoMail'}</p><h1>{origin ? action : 'Redactar correo'}</h1></div>
        </div>
        <button type="button" className="icon-button" onClick={closeComposer} aria-label={origin ? 'Volver' : 'Cerrar'} title={origin ? 'Volver' : 'Cerrar'}>{origin ? <ArrowLeft size={19} /> : <X size={19} />}</button>
      </header>

      <form onSubmit={submit}>
        <section className="ai-compose-surface">
          <div className="ai-compose-fields" aria-label="Datos del correo">
            <div className="compose-field">
              <label>De</label>
              <div className="select-wrap">
                <select value={fromAccountId ?? ''} onChange={event => setFrom(event.target.value)} disabled={Boolean(origin)}>
                  {accounts.map(account => <option key={account.id} value={account.id}>{account.displayName} · {account.emailAddress}</option>)}
                </select>
                <ChevronDown size={16} />
              </div>
            </div>
            <div className="compose-field recipient-field">
              <label>Para</label>
              <div className="recipient-control">
                <input value={to} onChange={event => setTo(event.target.value)} onFocus={() => setRecipientFocused(true)} onBlur={() => window.setTimeout(() => setRecipientFocused(false), 150)} placeholder="Escribe al menos 2 letras para buscar en Contactos" required />
                {recipientFocused && recipientTerm.length >= 2 && <div className="contact-suggestions">
                  {contacts.isFetching ? <p>Buscando en Contactos de Google…</p> : contacts.isError ? <p className="contact-error">{contacts.error instanceof Error ? contacts.error.message : 'No se pudieron consultar los contactos.'}</p> : contacts.data?.length ? contacts.data.map(contact => <button type="button" key={contact.emailAddress} onMouseDown={event => event.preventDefault()} onClick={() => selectContact(contact.emailAddress)}><strong>{contact.name}</strong><span>{contact.emailAddress}</span></button>) : <p>Sin contactos que coincidan.</p>}
                </div>}
              </div>
            </div>
            {showCc ? <>
              <div className="compose-field"><label>CC</label><input value={cc} onChange={event => setCc(event.target.value)} placeholder="copia@dominio.cl" /></div>
              <div className="compose-field"><label>CCO</label><input value={bcc} onChange={event => setBcc(event.target.value)} placeholder="copia.oculta@dominio.cl" /></div>
            </> : <button type="button" className="text-button ai-add-copy" onClick={() => setShowCc(true)}>Agregar CC / CCO</button>}
            <div className="compose-field subject-field"><label>Asunto</label><input value={subject} onChange={event => setSubject(event.target.value)} required /></div>
          </div>

          {origin && state.mode !== 'forward' && state.mode !== 'editDraft' && <section className="ai-reply-source" aria-label="Mensaje original">
            <div className="ai-reply-source-body" dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(origin.htmlBody) }} />
          </section>}

          <section className="ai-compose-editor" aria-label="Editor del mensaje">
            {origin && !editingDraft && <div className="ai-compose-editor-heading">
              <div><strong>{body ? 'Respuesta propuesta' : 'Respuesta'}</strong></div>
              <small>Revísala antes de responder.</small>
            </div>}
            <div className="format-toolbar" aria-label="Formato">
              <button type="button" title="Negrita" onMouseDown={event => event.preventDefault()} onClick={() => format('bold')}><Bold size={16} /></button>
              <button type="button" title="Cursiva" onMouseDown={event => event.preventDefault()} onClick={() => format('italic')}><Italic size={16} /></button>
              <button type="button" title="Subrayado" onMouseDown={event => event.preventDefault()} onClick={() => format('underline')}><Underline size={16} /></button>
              <button type="button" title="Lista" onMouseDown={event => event.preventDefault()} onClick={() => format('insertUnorderedList')}><List size={16} /></button>
              <button type="button" title="Lista numerada" onMouseDown={event => event.preventDefault()} onClick={() => format('insertOrderedList')}><ListOrdered size={16} /></button>
              <button type="button" title="Insertar enlace" onMouseDown={event => event.preventDefault()} onClick={() => { const url = window.prompt('Pega una URL segura (https://...)'); if (url?.startsWith('https://')) format('createLink', url) }}><Link size={16} /></button>
              <button type="button" className={`dictation-button ${listening ? 'listening' : ''}`} title={listening ? 'Detener dictado' : 'Dictar mensaje'} aria-label={listening ? 'Detener dictado' : 'Dictar mensaje con micrófono'} onMouseDown={event => event.preventDefault()} onClick={toggleDictation}>{listening ? <MicOff size={16} /> : <Mic size={16} />}</button>
              {listening && <span className="dictation-status">Escuchando…</span>}
            </div>
            {dictationError && <p className="dictation-error">{dictationError}</p>}
            <div ref={editor} className="editor rich-editor" contentEditable suppressContentEditableWarning role="textbox" aria-multiline="true" data-placeholder="Escribe tu mensaje…" onInput={event => setBody(event.currentTarget.innerHTML)} />

            {hasNexi && <AiInlineWritingAssistant
              currentHtml={body}
              recipient={to}
              accountId={origin && state.mode !== 'forward' && state.mode !== 'editDraft' ? origin.accountId : undefined}
              messageId={origin && state.mode !== 'forward' && state.mode !== 'editDraft' ? origin.providerMessageId : undefined}
              onUse={useAiProposal}
            />}

            <div className="outgoing-attachments">
              {retainedDraftAttachments.map(file => <span key={`draft-${file.id}`}><Paperclip size={14} />{file.name}<button type="button" onClick={() => setRetainedDraftAttachments(current => current.filter(item => item.id !== file.id))} aria-label={`Quitar ${file.name}`}><X size={14} /></button></span>)}
              {attachments.map((file, index) => <span key={`${file.name}-${index}`}><Paperclip size={14} />{file.name}<button type="button" onClick={() => setAttachments(current => current.filter((_, itemIndex) => itemIndex !== index))} aria-label={`Quitar ${file.name}`}><X size={14} /></button></span>)}
            </div>
            {attachmentError && <p className="attachment-error">{attachmentError}</p>}
            {send.isError && <p className="attachment-error">{send.error instanceof Error ? send.error.message : 'No se pudo enviar el correo.'}</p>}
            {saveDraft.isError && <p className="attachment-error">{saveDraft.error instanceof Error ? saveDraft.error.message : 'No se pudo guardar el borrador.'}</p>}
          </section>

          <footer className="ai-compose-footer">
            <div className="ai-compose-footer-left">
              <input ref={fileInput} className="file-picker" type="file" multiple onChange={event => void addFiles(event.target.files)} />
              <button type="button" className="attachment-action" disabled={composerBusy} onClick={() => fileInput.current?.click()}><Paperclip size={17} /> Adjuntar</button>
            </div>
            <div className="ai-compose-footer-actions">
              <button type="button" className="secondary-button compose-discard-button" disabled={composerBusy} onClick={() => setConfirmDiscard(true)}><Trash2 size={15} /> Descartar</button>
              <button type="button" className="secondary-button" disabled={composerBusy || !fromAccountId} onClick={() => { recognition.current?.stop(); saveDraft.mutate() }}><Save size={15} /> {saveDraft.isPending ? 'Guardando…' : 'Guardar borrador'}</button>
              <button type="submit" className="primary-button" disabled={composerBusy}><Send size={16} /> {send.isPending ? 'Enviando…' : 'Enviar'}</button>
            </div>
          </footer>
        </section>
      </form>
    </div>
    <ConfirmDialog open={confirmDiscard} title="Descartar cambios" message="Los cambios de este correo se perderán si no los guardas como borrador." confirmLabel="Descartar" tone="danger" pending={false} onCancel={() => setConfirmDiscard(false)} onConfirm={discardComposer} />
  </section>
}
