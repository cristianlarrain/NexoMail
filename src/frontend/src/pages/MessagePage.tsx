import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, ChevronLeft, ChevronRight, Download, FileText, Forward, Paperclip, Reply, ReplyAll, Trash2, X } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { MailAttachment } from '../types/mail'
import { sanitizeEmailHtml } from '../utils/sanitizeEmailHtml'

type MessageNavigationItem = { accountId: string; messageId: string }
type MessageNavigationState = { navigationItems?: MessageNavigationItem[]; returnTo?: string }

function canPreview(file: MailAttachment) {
  return file.contentType.startsWith('image/') || file.contentType === 'application/pdf' || /^text\/(plain|csv)|application\/(json|xml)/i.test(file.contentType) || /\.(txt|csv|json|xml|log|md)$/i.test(file.name)
}

export function MessagePage() {
  const { accountId = '', messageId = '' } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const navigationState = location.state as MessageNavigationState | null
  const navigationItems = navigationState?.navigationItems ?? []
  const currentIndex = navigationItems.findIndex(item => item.accountId === accountId && item.messageId === messageId)
  const previousMessage = currentIndex > 0 ? navigationItems[currentIndex - 1] : null
  const nextMessage = currentIndex >= 0 && currentIndex < navigationItems.length - 1 ? navigationItems[currentIndex + 1] : null
  const [preview, setPreview] = useState<MailAttachment | null>(null)
  const { data: message, isLoading } = useQuery({ queryKey: ['message', accountId, messageId], queryFn: () => mailApi.message(accountId, messageId), enabled: Boolean(accountId && messageId) })
  const read = useMutation({ mutationFn: () => mailApi.read(accountId, messageId, true) })
  const trash = useMutation({ mutationFn: () => mailApi.trash(accountId, messageId), onSuccess: () => navigate('/inbox', { state: { trashed: true } }) })
  useEffect(() => { if (message && !message.isRead) read.mutate() }, [message])
  useEffect(() => { setPreview(null) }, [accountId, messageId])
  if (isLoading || !message) return <section className="mail-view"><div className="reading-skeleton" /></section>
  const compose = (mode: 'reply' | 'replyAll' | 'forward') => navigate('/compose', { state: { mode, message } })
  const goToMessage = (item: MessageNavigationItem | null) => {
    if (!item) return
    navigate(`/message/${item.accountId}/${item.messageId}`, { state: navigationState })
  }
  const returnToInbox = () => navigationState?.returnTo ? navigate(navigationState.returnTo) : navigate(-1)
  const previewUrl = preview ? mailApi.attachmentUrl(accountId, messageId, preview) : ''
  const downloadUrl = preview ? mailApi.attachmentUrl(accountId, messageId, preview, true) : ''
  return <article className={`mail-view message-reader ${preview ? 'with-preview' : ''}`}><section className="message-reading-pane"><div className="message-navigation"><button className="back-link" onClick={returnToInbox}><ArrowLeft size={17} /> Volver a bandeja</button><div className="message-navigation-arrows" aria-label="Navegación entre correos"><button className="icon-button" onClick={() => goToMessage(previousMessage)} disabled={!previousMessage} aria-label="Correo anterior" title="Correo anterior"><ChevronLeft size={19} /></button><button className="icon-button" onClick={() => goToMessage(nextMessage)} disabled={!nextMessage} aria-label="Correo siguiente" title="Correo siguiente"><ChevronRight size={19} /></button></div></div><div className="message-title-row"><h1>{message.subject}</h1><div className="message-actions"><button className="icon-button" aria-label="Responder" onClick={() => compose('reply')}><Reply size={18} /></button><button className="icon-button" aria-label="Responder a todos" onClick={() => compose('replyAll')}><ReplyAll size={18} /></button><button className="icon-button" aria-label="Reenviar" onClick={() => compose('forward')}><Forward size={18} /></button><button className="icon-button danger-icon" aria-label="Mover a Papelera" title="Mover a Papelera" disabled={trash.isPending} onClick={() => { if (window.confirm('¿Mover este correo a la Papelera?')) trash.mutate() }}><Trash2 size={18} /></button></div></div><div className="message-meta"><div className="sender-avatar">{message.from.name.slice(0, 1)}</div><div><strong>{message.from.name}</strong><span>{message.from.address}</span><small>para {message.to.map(x => x.address).join(', ')} · {new Date(message.receivedAt).toLocaleString('es-CL')}</small></div></div>{message.thread && message.thread.length > 1 ? <section className="thread-view"><h2>Conversación</h2>{message.thread.map(item => <article className={`thread-message ${item.isCurrent ? 'current' : ''}`} key={item.providerMessageId}><header><strong>{item.from.name}</strong><span>{item.from.address} · {new Date(item.receivedAt).toLocaleString('es-CL')}</span></header><div dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(item.htmlBody) }} /></article>)}</section> : <div className="message-body" dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(message.htmlBody) }} />}{message.attachments.length > 0 && <div className="attachments"><h2><Paperclip size={16} /> Adjuntos</h2><div className="attachment-list">{message.attachments.map(file => <div key={file.id} className={`attachment-card ${preview?.id === file.id ? 'selected' : ''}`}><button type="button" className="attachment-preview-button" onClick={() => setPreview(file)} title="Abrir vista previa"><Paperclip size={17} /><span><strong>{file.name}</strong><small>{Math.max(1, Math.round(file.size / 1000))} KB · Vista previa</small></span></button><a href={mailApi.attachmentUrl(accountId, messageId, file, true)} className="attachment-download" title={`Descargar ${file.name}`} aria-label={`Descargar ${file.name}`}><Download size={16} /></a></div>)}</div></div>}<div className="reply-bar"><button className="secondary-button" onClick={() => compose('reply')}><Reply size={16} /> Responder</button><button className="secondary-button" onClick={() => compose('replyAll')}><ReplyAll size={16} /> Responder a todos</button><button className="secondary-button" onClick={() => compose('forward')}><Forward size={16} /> Reenviar</button></div></section>{preview && <aside className="attachment-preview" aria-label="Vista previa de adjunto"><header><div><p className="eyebrow">Vista previa</p><strong title={preview.name}>{preview.name}</strong></div><button className="icon-button" onClick={() => setPreview(null)} aria-label="Cerrar vista previa"><X size={18} /></button></header><div className="attachment-preview-content">{preview.contentType.startsWith('image/') ? <img src={previewUrl} alt={preview.name} /> : canPreview(preview) ? <iframe src={previewUrl} title={`Vista previa: ${preview.name}`} /> : <div className="unsupported-preview"><FileText size={32} /><h2>Vista previa no disponible</h2><p>Este tipo de archivo no puede mostrarse de forma segura en el navegador.</p></div>}</div><footer><a href={downloadUrl} className="primary-button"><Download size={16} /> Descargar</a></footer></aside>}</article>
}
