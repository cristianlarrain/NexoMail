import { useEffect, useMemo, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { Bold, ChevronDown, Italic, Link, List, ListOrdered, Mic, MicOff, Paperclip, Send, Underline, X } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { MailMessage, OutgoingAttachment } from '../types/mail'

type ComposeState = { mode?: 'reply' | 'replyAll' | 'forward' | 'followUp'; message?: MailMessage }
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

export function ComposePage() {
  const location = useLocation(); const navigate = useNavigate(); const state = (location.state ?? {}) as ComposeState; const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts }); const origin = state.message; const [from, setFrom] = useState(origin?.accountId ?? ''); const [to, setTo] = useState(origin ? state.mode === 'forward' ? '' : state.mode === 'followUp' ? origin.to.map(item => item.address).join(', ') : origin.from.address : ''); const [cc, setCc] = useState(''); const [showCc, setShowCc] = useState(false); const [subject, setSubject] = useState(() => origin ? `${state.mode === 'forward' ? 'Fwd:' : 'Re:'} ${origin.subject}` : ''); const [body, setBody] = useState(''); const [attachments, setAttachments] = useState<OutgoingAttachment[]>([]); const [attachmentError, setAttachmentError] = useState(''); const [recipientFocused, setRecipientFocused] = useState(false); const [listening, setListening] = useState(false); const [dictationError, setDictationError] = useState(''); const editor = useRef<HTMLDivElement>(null); const fileInput = useRef<HTMLInputElement>(null); const recognition = useRef<SpeechRecognitionLike | null>(null)
  const fromAccountId = from || accounts[0]?.id
  const recipientTerm = to.slice(to.lastIndexOf(',') + 1).trim()
  const contacts = useQuery({ queryKey: ['contacts', fromAccountId, recipientTerm], queryFn: () => mailApi.contacts(fromAccountId!, recipientTerm), enabled: Boolean(fromAccountId && recipientFocused && recipientTerm.length >= 2), retry: false })
  const action = useMemo(() => state.mode === 'reply' ? 'Responder' : state.mode === 'replyAll' ? 'Responder a todos' : state.mode === 'forward' ? 'Reenviar' : state.mode === 'followUp' ? 'Enviar seguimiento' : 'Enviar', [state.mode])
  const send = useMutation({ mutationFn: () => { const payload = { fromAccountId, to: to.split(',').map(v => v.trim()).filter(Boolean), cc: cc.split(',').map(v => v.trim()).filter(Boolean), bcc: [], subject, htmlBody: body || '<p></p>', attachments }; if (origin && state.mode !== 'forward') return mailApi.reply(origin.accountId, origin.providerMessageId, payload, state.mode === 'replyAll'); if (origin && state.mode === 'forward') return mailApi.forward(origin.accountId, origin.providerMessageId, payload); return mailApi.send(payload) }, onSuccess: () => navigate('/sent', { state: { sent: true } }) })

  useEffect(() => () => recognition.current?.stop(), [])

  function submit(event: FormEvent) { event.preventDefault(); recognition.current?.stop(); send.mutate() }
  function format(command: string, value?: string) { editor.current?.focus(); document.execCommand(command, false, value); setBody(editor.current?.innerHTML ?? '') }
  function selectContact(emailAddress: string) { const separator = to.lastIndexOf(','); const prefix = separator < 0 ? '' : `${to.slice(0, separator + 1).trimEnd()} `; setTo(`${prefix}${emailAddress}, `); setRecipientFocused(false) }

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
    const selected = [...files]; const total = attachments.reduce((sum, file) => sum + Math.ceil(file.base64Content.length * 0.75), 0) + selected.reduce((sum, file) => sum + file.size, 0)
    if (selected.some(file => file.size > 8 * 1024 * 1024) || total > 15 * 1024 * 1024) { setAttachmentError('Cada archivo admite hasta 8 MB y el total hasta 15 MB.'); return }
    const values = await Promise.all(selected.map(file => new Promise<OutgoingAttachment>((resolve, reject) => { const reader = new FileReader(); reader.onerror = () => reject(reader.error); reader.onload = () => resolve({ name: file.name, contentType: file.type || 'application/octet-stream', base64Content: String(reader.result).split(',')[1] }); reader.readAsDataURL(file) })))
    setAttachments(current => [...current, ...values]); setAttachmentError(''); if (fileInput.current) fileInput.current.value = ''
  }

  return <section className="compose-page"><div className="compose-card"><header><div><p className="eyebrow">Nuevo mensaje</p><h1>{action}</h1></div><button className="icon-button" onClick={() => navigate(-1)} aria-label="Cerrar"><X size={19} /></button></header><form onSubmit={submit}><div className="compose-field"><label>De</label><div className="select-wrap"><select value={from} onChange={e => setFrom(e.target.value)}>{accounts.map(a => <option key={a.id} value={a.id}>{a.displayName} · {a.emailAddress}</option>)}</select><ChevronDown size={16} /></div></div><div className="compose-field recipient-field"><label>Para</label><div className="recipient-control"><input value={to} onChange={e => setTo(e.target.value)} onFocus={() => setRecipientFocused(true)} onBlur={() => window.setTimeout(() => setRecipientFocused(false), 150)} placeholder="Escribe al menos 2 letras para buscar en Contactos" required />{recipientFocused && recipientTerm.length >= 2 && <div className="contact-suggestions">{contacts.isFetching ? <p>Buscando en Contactos de Google…</p> : contacts.isError ? <p className="contact-error">{contacts.error instanceof Error ? contacts.error.message : 'No se pudieron consultar los contactos.'}</p> : contacts.data?.length ? contacts.data.map(contact => <button type="button" key={contact.emailAddress} onMouseDown={event => event.preventDefault()} onClick={() => selectContact(contact.emailAddress)}><strong>{contact.name}</strong><span>{contact.emailAddress}</span></button>) : <p>Sin contactos que coincidan.</p>}</div>}</div></div>{showCc ? <div className="compose-field"><label>CC</label><input value={cc} onChange={e => setCc(e.target.value)} placeholder="copia@dominio.cl" /></div> : <button type="button" className="text-button" onClick={() => setShowCc(true)}>Agregar CC / CCO</button>}<div className="compose-field"><label>Asunto</label><input value={subject} onChange={e => setSubject(e.target.value)} required /></div><div className="format-toolbar" aria-label="Formato"><button type="button" title="Negrita" onMouseDown={e => e.preventDefault()} onClick={() => format('bold')}><Bold size={16} /></button><button type="button" title="Cursiva" onMouseDown={e => e.preventDefault()} onClick={() => format('italic')}><Italic size={16} /></button><button type="button" title="Subrayado" onMouseDown={e => e.preventDefault()} onClick={() => format('underline')}><Underline size={16} /></button><button type="button" title="Lista" onMouseDown={e => e.preventDefault()} onClick={() => format('insertUnorderedList')}><List size={16} /></button><button type="button" title="Lista numerada" onMouseDown={e => e.preventDefault()} onClick={() => format('insertOrderedList')}><ListOrdered size={16} /></button><button type="button" title="Insertar enlace" onMouseDown={e => e.preventDefault()} onClick={() => { const url = window.prompt('Pega una URL segura (https://...)'); if (url?.startsWith('https://')) format('createLink', url) }}><Link size={16} /></button><button type="button" className={`dictation-button ${listening ? 'listening' : ''}`} title={listening ? 'Detener dictado' : 'Dictar mensaje'} aria-label={listening ? 'Detener dictado' : 'Dictar mensaje con micrófono'} onMouseDown={e => e.preventDefault()} onClick={toggleDictation}>{listening ? <MicOff size={16} /> : <Mic size={16} />}</button>{listening && <span className="dictation-status">Escuchando…</span>}</div>{dictationError && <p className="dictation-error">{dictationError}</p>}<div ref={editor} className="editor rich-editor" contentEditable suppressContentEditableWarning role="textbox" aria-multiline="true" data-placeholder="Escribe tu mensaje…" onInput={event => setBody(event.currentTarget.innerHTML)} /><div className="outgoing-attachments">{attachments.map((file, index) => <span key={`${file.name}-${index}`}><Paperclip size={14} />{file.name}<button type="button" onClick={() => setAttachments(current => current.filter((_, itemIndex) => itemIndex !== index))} aria-label={`Quitar ${file.name}`}><X size={14} /></button></span>)}</div>{attachmentError && <p className="attachment-error">{attachmentError}</p>}{send.isError && <p className="attachment-error">{send.error instanceof Error ? send.error.message : 'No se pudo enviar el correo.'}</p>}<footer><input ref={fileInput} className="file-picker" type="file" multiple onChange={event => void addFiles(event.target.files)} /><button type="button" className="attachment-action" onClick={() => fileInput.current?.click()}><Paperclip size={17} /> Adjuntar</button><button className="primary-button" disabled={send.isPending}><Send size={16} /> {send.isPending ? 'Enviando…' : action}</button></footer></form></div></section>
}
