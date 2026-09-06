import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { ArrowLeft, Bold, ChevronDown, Italic, Link, List, ListOrdered, Mic, MicOff, Paperclip, Send, Sparkles, Underline, X } from 'lucide-react'
import { AiWritingAssistant } from '../components/AiWritingAssistant'
import { mailApi } from '../api/mailApi'
import type { AiWritingSuggestion, MailMessage, OutgoingAttachment } from '../types/mail'
import { sanitizeEmailHtml } from '../utils/sanitizeEmailHtml'

type ComposeState = {
  mode?: 'reply' | 'replyAll' | 'forward' | 'followUp'
  message?: MailMessage
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

export function ComposePage() {
  const location = useLocation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const state = (location.state ?? {}) as ComposeState
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const origin = state.message
  const [from, setFrom] = useState(origin?.accountId ?? '')
  const [to, setTo] = useState(origin ? state.mode === 'forward' ? '' : state.mode === 'followUp' ? origin.to.map(item => item.address).join(', ') : origin.from.address : '')
  const [cc, setCc] = useState('')
  const [bcc, setBcc] = useState('')
  const [showCc, setShowCc] = useState(false)
  const [subject, setSubject] = useState(() => origin ? `${state.mode === 'forward' ? 'Fwd:' : 'Re:'} ${origin.subject}` : '')
  const [body, setBody] = useState(() => state.initialBody ? textToHtml(state.initialBody) : '')
  const [attachments, setAttachments] = useState<OutgoingAttachment[]>([])
  const [attachmentError, setAttachmentError] = useState('')
  const [recipientFocused, setRecipientFocused] = useState(false)
  const [listening, setListening] = useState(false)
  const [dictationError, setDictationError] = useState('')
  const [composeReady, setComposeReady] = useState(Boolean(origin))
  const [manualCompose, setManualCompose] = useState(false)
  const editor = useRef<HTMLDivElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)
  const recognition = useRef<SpeechRecognitionLike | null>(null)

  const fromAccountId = from || accounts[0]?.id
  const recipientTerm = to.slice(to.lastIndexOf(',') + 1).trim()
  const contacts = useQuery({ queryKey: ['contacts', fromAccountId, recipientTerm], queryFn: () => mailApi.contacts(fromAccountId!, recipientTerm), enabled: Boolean(fromAccountId && recipientFocused && recipientTerm.length >= 2), retry: false })
  const action = useMemo(() => state.mode === 'reply' ? 'Responder' : state.mode === 'replyAll' ? 'Responder a todos' : state.mode === 'forward' ? 'Reenviar' : state.mode === 'followUp' ? 'Enviar seguimiento' : 'Enviar', [state.mode])
  const showComposer = Boolean(origin) || composeReady || manualCompose
  const showReplyAssistant = Boolean(origin && state.mode && state.mode !== 'forward')

  const send = useMutation({
    mutationFn: () => {
      const payload = {
        fromAccountId,
        to: to.split(',').map(v => v.trim()).filter(Boolean),
        cc: cc.split(',').map(v => v.trim()).filter(Boolean),
        bcc: bcc.split(',').map(v => v.trim()).filter(Boolean),
        subject,
        htmlBody: body || '<p></p>',
        attachments,
      }
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

  useEffect(() => () => recognition.current?.stop(), [])
  useEffect(() => {
    if (!state.initialBody || !editor.current) return
    const html = textToHtml(state.initialBody)
    editor.current.innerHTML = html
    setBody(html)
  }, [state.initialBody])
  useEffect(() => {
    if (!composeReady || !editor.current || !body) return
    editor.current.innerHTML = body
  }, [composeReady])

  function submit(event: FormEvent) { event.preventDefault(); recognition.current?.stop(); send.mutate() }
  function format(command: string, value?: string) { editor.current?.focus(); document.execCommand(command, false, value); setBody(editor.current?.innerHTML ?? '') }
  function selectContact(emailAddress: string) { const separator = to.lastIndexOf(','); const prefix = separator < 0 ? '' : `${to.slice(0, separator + 1).trimEnd()} `; setTo(`${prefix}${emailAddress}, `); setRecipientFocused(false) }
  function useAiProposal(suggestion: AiWritingSuggestion) {
    if (!origin && suggestion.subject?.trim()) setSubject(suggestion.subject.trim())
    const html = textToHtml(suggestion.text)
    setBody(html)
    if (!origin) setComposeReady(true)
    if (editor.current) { editor.current.innerHTML = html; editor.current.focus() }
  }

  function closeComposer() {
    recognition.current?.stop()
    if (state.returnTo) {
      navigate(state.returnTo, { state: state.returnState })
      return
    }
    if (origin) {
      navigate(`/message/${origin.accountId}/${origin.providerMessageId}`)
      return
    }
    navigate(-1)
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
    if (listening) { recognition.current?.stop(); return }
    const speechWindow = window as SpeechWindow
    const Recognition = speechWindow.SpeechRecognition ?? speechWindow.webkitSpeechRecognition
    if (!Recognition) { setDictationError('El dictado por voz no está disponible en este navegador. Use Chrome o Edge actualizado.'); return }

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
    instance.onend = () => { setListening(false); recognition.current = null }
    try { setDictationError(''); instance.start(); setListening(true) }
    catch { setDictationError('No fue posible iniciar el micrófono. Inténtelo nuevamente.'); setListening(false); recognition.current = null }
  }

  async function addFiles(files: FileList | null) {
    if (!files?.length) return
    const selected = [...files]
    const total = attachments.reduce((sum, file) => sum + Math.ceil(file.base64Content.length * 0.75), 0) + selected.reduce((sum, file) => sum + file.size, 0)
    if (selected.some(file => file.size > 8 * 1024 * 1024) || total > 15 * 1024 * 1024) { setAttachmentError('Cada archivo admite hasta 8 MB y el total hasta 15 MB.'); return }
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

  return <section className="compose-page"><div className={`compose-card ai-compose-card ${!showComposer ? 'ai-first-compose' : ''}`}>
    <header className="ai-compose-header">
      <div className="ai-compose-heading"><span className="ai-compose-mark"><Sparkles size={18} /></span><div><p className="eyebrow">Nexo IA</p><h1>{!origin && !showComposer ? 'Asistente de redacción' : origin ? action : 'Revisar y enviar'}</h1></div></div>
      <button type="button" className="icon-button" onClick={closeComposer} aria-label={origin ? 'Volver al correo' : 'Cerrar'} title={origin ? 'Volver al correo' : 'Cerrar'}>{origin ? <ArrowLeft size={19} /> : <X size={19} />}</button>
    </header>

    <form onSubmit={submit}>
      {!origin && !showComposer && <AiWritingAssistant mode="compose" recipient={to} onRecipientChange={setTo} onUse={useAiProposal} onManual={() => setManualCompose(true)} />}

      {showComposer && <section className="ai-compose-surface">
        {!origin && composeReady && <div className="ai-review-banner"><div><span>Nexo IA</span><strong>Propuesta lista para revisar y enviar</strong></div><button type="button" className="text-button" onClick={() => { setComposeReady(false); setManualCompose(false) }}>Volver al asistente</button></div>}

        <div className="ai-compose-fields" aria-label="Datos del correo">
          {!origin && <div className="ai-compose-section-label"><Sparkles size={14} /><span>Datos de envío</span></div>}
          <div className="compose-field"><label>De</label><div className="select-wrap"><select value={from} onChange={e => setFrom(e.target.value)}>{accounts.map(a => <option key={a.id} value={a.id}>{a.displayName} · {a.emailAddress}</option>)}</select><ChevronDown size={16} /></div></div>
          <div className="compose-field recipient-field"><label>Para</label><div className="recipient-control"><input value={to} onChange={e => setTo(e.target.value)} onFocus={() => setRecipientFocused(true)} onBlur={() => window.setTimeout(() => setRecipientFocused(false), 150)} placeholder="Escribe al menos 2 letras para buscar en Contactos" required />{recipientFocused && recipientTerm.length >= 2 && <div className="contact-suggestions">{contacts.isFetching ? <p>Buscando en Contactos de Google…</p> : contacts.isError ? <p className="contact-error">{contacts.error instanceof Error ? contacts.error.message : 'No se pudieron consultar los contactos.'}</p> : contacts.data?.length ? contacts.data.map(contact => <button type="button" key={contact.emailAddress} onMouseDown={event => event.preventDefault()} onClick={() => selectContact(contact.emailAddress)}><strong>{contact.name}</strong><span>{contact.emailAddress}</span></button>) : <p>Sin contactos que coincidan.</p>}</div>}</div></div>
          {showCc ? <>
            <div className="compose-field"><label>CC</label><input value={cc} onChange={e => setCc(e.target.value)} placeholder="copia@dominio.cl" /></div>
            <div className="compose-field"><label>CCO</label><input value={bcc} onChange={e => setBcc(e.target.value)} placeholder="copia.oculta@dominio.cl" /></div>
          </> : <button type="button" className="text-button ai-add-copy" onClick={() => setShowCc(true)}>Agregar CC / CCO</button>}
          <div className="compose-field subject-field"><label>Asunto</label><input value={subject} onChange={e => setSubject(e.target.value)} required /></div>
        </div>

        {origin && state.mode !== 'forward' && <section className="ai-reply-source" aria-label="Mensaje original">
          <div className="ai-reply-source-body" dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(origin.htmlBody) }} />
        </section>}

        {showReplyAssistant && origin && <AiWritingAssistant mode="reply" accountId={origin.accountId} messageId={origin.providerMessageId} onUse={useAiProposal} />}

        <section className="ai-compose-editor" aria-label="Editor del mensaje">
          <div className="ai-compose-editor-heading">
            <div>{origin ? <strong>{body ? 'Respuesta propuesta' : 'Respuesta'}</strong> : <><span>Nexo IA</span><strong>Tu mensaje</strong></>}</div>
            <small>{origin ? 'Revísala antes de responder.' : 'Puede editar libremente el texto antes de enviarlo.'}</small>
          </div>
          <div className="format-toolbar" aria-label="Formato"><button type="button" title="Negrita" onMouseDown={e => e.preventDefault()} onClick={() => format('bold')}><Bold size={16} /></button><button type="button" title="Cursiva" onMouseDown={e => e.preventDefault()} onClick={() => format('italic')}><Italic size={16} /></button><button type="button" title="Subrayado" onMouseDown={e => e.preventDefault()} onClick={() => format('underline')}><Underline size={16} /></button><button type="button" title="Lista" onMouseDown={e => e.preventDefault()} onClick={() => format('insertUnorderedList')}><List size={16} /></button><button type="button" title="Lista numerada" onMouseDown={e => e.preventDefault()} onClick={() => format('insertOrderedList')}><ListOrdered size={16} /></button><button type="button" title="Insertar enlace" onMouseDown={e => e.preventDefault()} onClick={() => { const url = window.prompt('Pega una URL segura (https://...)'); if (url?.startsWith('https://')) format('createLink', url) }}><Link size={16} /></button><button type="button" className={`dictation-button ${listening ? 'listening' : ''}`} title={listening ? 'Detener dictado' : 'Dictar mensaje'} aria-label={listening ? 'Detener dictado' : 'Dictar mensaje con micrófono'} onMouseDown={e => e.preventDefault()} onClick={toggleDictation}>{listening ? <MicOff size={16} /> : <Mic size={16} />}</button>{listening && <span className="dictation-status">Escuchando…</span>}</div>
          {dictationError && <p className="dictation-error">{dictationError}</p>}
          <div ref={editor} className="editor rich-editor" contentEditable suppressContentEditableWarning role="textbox" aria-multiline="true" data-placeholder="Escribe tu mensaje o utiliza Nexo IA para preparar una propuesta…" onInput={event => setBody(event.currentTarget.innerHTML)} />
          <div className="outgoing-attachments">{attachments.map((file, index) => <span key={`${file.name}-${index}`}><Paperclip size={14} />{file.name}<button type="button" onClick={() => setAttachments(current => current.filter((_, itemIndex) => itemIndex !== index))} aria-label={`Quitar ${file.name}`}><X size={14} /></button></span>)}</div>
          {attachmentError && <p className="attachment-error">{attachmentError}</p>}
          {send.isError && <p className="attachment-error">{send.error instanceof Error ? send.error.message : 'No se pudo enviar el correo.'}</p>}
        </section>

        <footer className="ai-compose-footer"><input ref={fileInput} className="file-picker" type="file" multiple onChange={event => void addFiles(event.target.files)} /><button type="button" className="attachment-action" onClick={() => fileInput.current?.click()}><Paperclip size={17} /> Adjuntar</button><button className="primary-button" disabled={send.isPending}><Send size={16} /> {send.isPending ? 'Enviando…' : action}</button></footer>
      </section>}
    </form>
  </div></section>
}
