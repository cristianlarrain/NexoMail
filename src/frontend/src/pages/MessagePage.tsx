import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { Archive, ArrowLeft, Ban, Check, ChevronLeft, ChevronRight, Clock3, Download, EyeOff, FileText, Forward, Paperclip, Reply, ReplyAll, ShieldAlert, Trash2, Undo2, X } from 'lucide-react'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { commercialApi } from '../api/commercialApi'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem, ControlCenterSnapshot, MailAttachment, MailSummary, PagedResult } from '../types/mail'
import { commercialEntitlements } from '../utils/commercialEntitlements'
import { sanitizeEmailHtml } from '../utils/sanitizeEmailHtml'

type MessageNavigationItem = { accountId: string; messageId: string }
type MessageNavigationState = { navigationItems?: MessageNavigationItem[]; returnTo?: string; controlCenterItem?: ControlCenterPendingItem; manualTracking?: boolean }

function canPreview(file: MailAttachment) {
  return file.contentType.startsWith('image/') || file.contentType === 'application/pdf' || /^text\/(plain|csv)|application\/(json|xml)/i.test(file.contentType) || /\.(txt|csv|json|xml|log|md)$/i.test(file.name)
}

function trackingKey(item: ControlCenterPendingItem) {
  return `${item.accountId}:${item.conversationId}:${item.messageId}`
}

function trackingIsOverdue(item: ControlCenterPendingItem) {
  return Date.now() - new Date(item.since).getTime() >= 48 * 60 * 60 * 1000
}

export function MessagePage() {
  const { accountId = '', messageId = '' } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const navigationState = location.state as MessageNavigationState | null
  const navigationItems = navigationState?.navigationItems ?? []
  const controlCenterItem = navigationState?.controlCenterItem
  const currentIndex = navigationItems.findIndex(item => item.accountId === accountId && item.messageId === messageId)
  const previousMessage = currentIndex > 0 ? navigationItems[currentIndex - 1] : null
  const nextMessage = currentIndex >= 0 && currentIndex < navigationItems.length - 1 ? navigationItems[currentIndex + 1] : null
  const [preview, setPreview] = useState<MailAttachment | null>(null)
  const [confirmTrash, setConfirmTrash] = useState(false)
  const [finalizationNotice, setFinalizationNotice] = useState<'finalized' | 'reopened' | null>(null)
  const { data: commercialSubscription } = useQuery({
    queryKey: ['commercial-subscription'],
    queryFn: commercialApi.subscription,
    staleTime: 30_000,
  })
  const hasMailActions = commercialSubscription?.entitlements.includes(commercialEntitlements.mailActions) === true
  const hasTracking = commercialSubscription?.entitlements.includes(commercialEntitlements.trackingBasic) === true
  const { data: message, isLoading } = useQuery({
    queryKey: ['message', accountId, messageId],
    queryFn: () => mailApi.message(accountId, messageId),
    enabled: Boolean(accountId && messageId),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
  })
  const threadQuery = useQuery({
    queryKey: ['message-thread', accountId, messageId],
    queryFn: () => mailApi.thread(accountId, messageId),
    enabled: Boolean(accountId && messageId && message),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
    retry: false,
  })
  const canTrackOrFinalize = Boolean(message && !['drafts', 'trash', 'spam'].includes(message.folderId))
  const trackingState = useQuery({
    queryKey: ['control-center-tracking-state', accountId, messageId],
    queryFn: () => mailApi.controlCenterTrackingState(accountId, messageId),
    enabled: Boolean(accountId && messageId && hasTracking && canTrackOrFinalize),
    staleTime: 0,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
  })
  const controlCenterQuery = useQuery({
    queryKey: ['control-center', accountId],
    queryFn: () => mailApi.controlCenter(accountId),
    enabled: Boolean(accountId && messageId && hasTracking && canTrackOrFinalize),
    staleTime: 90_000,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
  })
  const messageStateQuery = useQuery({
    queryKey: ['control-center-message-state', accountId, messageId],
    queryFn: () => mailApi.controlCenterMessageState(accountId, messageId),
    enabled: Boolean(accountId && messageId && hasTracking && canTrackOrFinalize),
    staleTime: 30_000,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
  })
  const returnPath = navigationState?.returnTo ?? '/inbox'
  const openedFromIgnored = returnPath.startsWith('/ignored')
  const openedFromControlCenter = returnPath.startsWith('/control-center')
  const openedManualTracking = navigationState?.manualTracking ?? controlCenterItem?.conversationId.startsWith('manual:') ?? false
  const navigationAutomaticPending = controlCenterItem && !controlCenterItem.conversationId.startsWith('manual:') ? controlCenterItem : undefined
  const automaticPending = navigationAutomaticPending ?? controlCenterQuery.data?.pendingItems.find(item => item.accountId === accountId && item.messageId === messageId)
  const isFinalized = messageStateQuery.data?.status === 'resolved' && Boolean(messageStateQuery.data.conversationId)

  function removeMessageFromCachedLists() {
    queryClient.setQueriesData<InfiniteData<PagedResult<MailSummary>>>({ queryKey: ['messages'] }, current => current ? {
      ...current,
      pages: current.pages.map(page => ({
        ...page,
        items: page.items.filter(item => !(item.accountId === accountId && item.providerMessageId === messageId)),
      })),
    } : current)
  }

  function removeSenderFromCachedFolder(folderId: string) {
    const sender = message?.from.address.trim().toLowerCase()
    if (!sender) return
    queryClient.setQueriesData<InfiniteData<PagedResult<MailSummary>>>({
      predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === folderId,
    }, current => current ? {
      ...current,
      pages: current.pages.map(page => ({
        ...page,
        items: page.items.filter(item => !(item.accountId === accountId && item.senderAddress.trim().toLowerCase() === sender)),
      })),
    } : current)
  }

  function removePendingFromCachedControlCenter(item: ControlCenterPendingItem) {
    const resolvedKey = trackingKey(item)
    queryClient.setQueriesData<ControlCenterSnapshot>({ queryKey: ['control-center'] }, current => {
      if (!current) return current
      const pending = current.pendingItems.find(value => trackingKey(value) === resolvedKey)
      if (!pending) return current
      const overdueAdjustment = trackingIsOverdue(pending) ? 1 : 0
      return {
        ...current,
        receivedWithoutReply: Math.max(0, current.receivedWithoutReply - (pending.direction === 'received' ? 1 : 0)),
        sentWithoutResponse: Math.max(0, current.sentWithoutResponse - (pending.direction === 'sent' ? 1 : 0)),
        overdue: Math.max(0, current.overdue - overdueAdjustment),
        priorityItems: current.priorityItems.filter(value => trackingKey(value) !== resolvedKey),
        pendingItems: current.pendingItems.filter(value => trackingKey(value) !== resolvedKey),
        accounts: current.accounts.map(account => account.accountId !== pending.accountId ? account : {
          ...account,
          receivedWithoutReply: Math.max(0, account.receivedWithoutReply - (pending.direction === 'received' ? 1 : 0)),
          sentWithoutResponse: Math.max(0, account.sentWithoutResponse - (pending.direction === 'sent' ? 1 : 0)),
        }),
      }
    })
  }

  function refreshControlCenter() {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'all' })
  }

  function refreshManualTracking() {
    void queryClient.invalidateQueries({ queryKey: ['control-center-tracking'], refetchType: 'all' })
    refreshControlCenter()
  }

  function finishMailboxMove(targetFolder: 'inbox' | 'archive' | 'spam' | 'trash') {
    removeMessageFromCachedLists()
    void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
    void queryClient.invalidateQueries({
      predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === targetFolder,
      refetchType: 'all',
    })
    refreshControlCenter()
    navigate(returnPath)
  }

  const read = useMutation({ mutationFn: () => mailApi.read(accountId, messageId, true) })
  const trash = useMutation({ mutationFn: () => mailApi.trash(accountId, messageId), onSuccess: () => finishMailboxMove('trash') })
  const move = useMutation({ mutationFn: (target: 'inbox' | 'archive' | 'spam') => mailApi.move(accountId, messageId, target), onSuccess: (_data, target) => finishMailboxMove(target) })
  const ignore = useMutation({
    mutationFn: () => mailApi.ignoreSender(accountId, message?.from.address ?? ''),
    onSuccess: () => {
      removeSenderFromCachedFolder('inbox')
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      void queryClient.invalidateQueries({ predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === 'ignored', refetchType: 'all' })
      refreshControlCenter()
      navigate(returnPath)
    },
  })
  const unignore = useMutation({
    mutationFn: () => mailApi.unignoreSender(accountId, message?.from.address ?? ''),
    onSuccess: () => {
      removeSenderFromCachedFolder('ignored')
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      void queryClient.invalidateQueries({ predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === 'inbox', refetchType: 'all' })
      refreshControlCenter()
      navigate(returnPath)
    },
  })
  const trackMessage = useMutation({
    mutationFn: () => mailApi.trackMessage(accountId, messageId),
    onSuccess: () => {
      queryClient.setQueryData(['control-center-tracking-state', accountId, messageId], { isTracked: true })
      refreshManualTracking()
    },
  })
  const untrackMessage = useMutation({
    mutationFn: () => mailApi.untrackMessage(accountId, messageId),
    onSuccess: () => {
      queryClient.setQueryData(['control-center-tracking-state', accountId, messageId], { isTracked: false })
      refreshManualTracking()
    },
  })
  const finalizeMessage = useMutation({
    mutationFn: async () => {
      if (!automaticPending) throw new Error('Este correo no tiene una acción automática pendiente.')
      await mailApi.updateControlCenterState(automaticPending.accountId, automaticPending.conversationId, { messageId: automaticPending.messageId, action: 'resolved' })
      return automaticPending
    },
    onSuccess: item => {
      removePendingFromCachedControlCenter(item)
      queryClient.setQueryData(['control-center-tracking-state', accountId, messageId], { isTracked: false })
      queryClient.setQueryData(['control-center-message-state', accountId, messageId], { status: 'resolved', conversationId: item.conversationId })
      setFinalizationNotice('finalized')
      refreshManualTracking()
    },
  })
  const reopenMessage = useMutation({
    mutationFn: async () => {
      const conversationId = messageStateQuery.data?.conversationId
      if (!conversationId) throw new Error('No fue posible identificar la conversación finalizada.')
      await mailApi.updateControlCenterState(accountId, conversationId, { messageId, action: 'active' })
    },
    onSuccess: () => {
      queryClient.setQueryData(['control-center-message-state', accountId, messageId], { status: 'active', conversationId: null })
      setFinalizationNotice('reopened')
      refreshControlCenter()
    },
  })

  useEffect(() => { if (message && !message.isRead) read.mutate() }, [message])
  useEffect(() => { setPreview(null); setConfirmTrash(false); setFinalizationNotice(null) }, [accountId, messageId])
  useEffect(() => {
    if (!message) return
    for (const item of [previousMessage, nextMessage]) {
      if (!item) continue
      void queryClient.prefetchQuery({
        queryKey: ['message', item.accountId, item.messageId],
        queryFn: () => mailApi.message(item.accountId, item.messageId),
        staleTime: 10 * 60_000,
      })
    }
  }, [message, nextMessage?.accountId, nextMessage?.messageId, previousMessage?.accountId, previousMessage?.messageId, queryClient])

  if (isLoading || !message) return <section className="mail-view"><div className="reading-skeleton" /></section>
  const compose = (mode: 'reply' | 'replyAll' | 'forward', initialBody?: string) => navigate('/compose', { state: { mode, message, initialBody, returnTo: location.pathname, returnState: navigationState } })
  const goToMessage = (item: MessageNavigationItem | null) => {
    if (!item) return
    void queryClient.prefetchQuery({
      queryKey: ['message', item.accountId, item.messageId],
      queryFn: () => mailApi.message(item.accountId, item.messageId),
      staleTime: 10 * 60_000,
    })
    navigate(`/message/${item.accountId}/${item.messageId}`, { state: navigationState })
  }
  const returnToPreviousView = () => navigationState?.returnTo ? navigate(navigationState.returnTo) : navigate(-1)
  const previewUrl = preview ? mailApi.attachmentUrl(accountId, messageId, preview) : ''
  const downloadUrl = preview ? mailApi.attachmentUrl(accountId, messageId, preview, true) : ''
  const mailboxActionPending = move.isPending || ignore.isPending || unignore.isPending || trash.isPending || finalizeMessage.isPending || reopenMessage.isPending || trackMessage.isPending || untrackMessage.isPending
  const isDraft = message.folderId === 'drafts'
  const isArchived = message.folderId === 'archive'
  const canRestoreToInbox = isArchived || message.folderId === 'spam' || message.folderId === 'trash'
  const canArchive = !canRestoreToInbox && message.folderId !== 'sent' && message.folderId !== 'drafts'
  const canOrganize = message.folderId !== 'sent' && message.folderId !== 'drafts'
  const canManualTrack = !['drafts', 'trash', 'spam'].includes(message.folderId)
  const isManuallyTracked = trackingState.data?.isTracked ?? openedManualTracking
  const trackingMutationError = trackMessage.error ?? untrackMessage.error
  const finalizationError = finalizeMessage.error ?? reopenMessage.error
  const destructiveActionLabel = isDraft ? 'Descartar borrador' : 'Mover a Papelera'
  const thread = [...(threadQuery.data ?? [])].sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())

  return <article className={`mail-view message-reader ${preview ? 'with-preview' : ''}`}>
    <section className="message-reading-pane">
      <div className="message-navigation"><button className="back-link" onClick={returnToPreviousView}><ArrowLeft size={17} /> {openedFromControlCenter ? 'Volver al Centro de control' : 'Volver'}</button><div className="message-navigation-arrows" aria-label="Navegación entre correos"><button className="icon-button" onClick={() => goToMessage(previousMessage)} disabled={!previousMessage} aria-label="Correo anterior" title="Correo anterior"><ChevronLeft size={19} /></button><button className="icon-button" onClick={() => goToMessage(nextMessage)} disabled={!nextMessage} aria-label="Correo siguiente" title="Correo siguiente"><ChevronRight size={19} /></button></div></div>
      <div className="message-title-row"><h1>{message.subject}</h1></div>
      {(hasMailActions || hasTracking || message.unsubscribeUrl) && <div className="message-actions message-action-toolbar unified-message-actions" aria-label="Acciones del correo">
        {hasMailActions && <button className="message-action-button primary-action" aria-label="Responder" title="Responder" onClick={() => compose('reply')}><Reply size={16} /><span>Responder</span></button>}
        {hasMailActions && <button className="message-action-button" aria-label="Responder a todos" title="Responder a todos" onClick={() => compose('replyAll')}><ReplyAll size={16} /><span>Responder a todos</span></button>}
        {hasMailActions && <button className="message-action-button" aria-label="Reenviar" title="Reenviar" onClick={() => compose('forward')}><Forward size={16} /><span>Reenviar</span></button>}
        {hasMailActions && canOrganize && <span className="message-action-separator" aria-hidden="true" />}
        {hasMailActions && canRestoreToInbox && <button className="message-action-button" aria-label="Restaurar a Bandeja" title="Restaurar a Bandeja" disabled={mailboxActionPending} onClick={() => move.mutate('inbox')}><Undo2 size={16} /><span>Restaurar a Bandeja</span></button>}
        {hasMailActions && canArchive && <button className="message-action-button" aria-label="Archivar" title="Archivar" disabled={mailboxActionPending} onClick={() => move.mutate('archive')}><Archive size={16} /><span>Archivar</span></button>}
        {hasMailActions && canOrganize && (openedFromIgnored ? <button className="message-action-button" aria-label="Dejar de ignorar remitente" title="Dejar de ignorar remitente" disabled={mailboxActionPending} onClick={() => unignore.mutate()}><Undo2 size={16} /><span>Dejar de ignorar</span></button> : <button className="message-action-button" aria-label="Ignorar remitente" title="Ignorar remitente" disabled={mailboxActionPending} onClick={() => ignore.mutate()}><EyeOff size={16} /><span>Ignorar remitente</span></button>)}
        {hasMailActions && canOrganize && message.folderId !== 'spam' && <button className="message-action-button" aria-label="Marcar como spam" title="Marcar como spam" disabled={mailboxActionPending} onClick={() => move.mutate('spam')}><ShieldAlert size={16} /><span>Spam</span></button>}
        {message.unsubscribeUrl && <a className="message-action-button" href={message.unsubscribeUrl} target="_blank" rel="noopener noreferrer" aria-label="Desuscribirse" title="Desuscribirse"><Ban size={16} /><span>Desuscribirse</span></a>}
        {hasTracking && canManualTrack && !controlCenterItem && <><span className="message-action-separator" aria-hidden="true" />{isManuallyTracked ? <button className="message-action-button" aria-label="Quitar seguimiento" title="Quitar este correo del seguimiento manual" disabled={mailboxActionPending} onClick={() => untrackMessage.mutate()}><Check size={16} /><span>Quitar seguimiento</span></button> : <button className="message-action-button" aria-label="Hacer seguimiento" title="Añadir este correo a Seguimiento prioritario" disabled={mailboxActionPending || trackingState.isLoading} onClick={() => trackMessage.mutate()}><Clock3 size={16} /><span>Hacer seguimiento</span></button>}</>}
        {hasTracking && openedManualTracking && <><span className="message-action-separator" aria-hidden="true" /><button className="message-action-button" aria-label="Quitar seguimiento" title="Quitar este correo del seguimiento manual" disabled={mailboxActionPending} onClick={() => untrackMessage.mutate()}><Check size={16} /><span>Quitar seguimiento</span></button></>}
        {hasTracking && canTrackOrFinalize && (isFinalized || automaticPending) && <span className="message-action-separator" aria-hidden="true" />}
        {hasTracking && canTrackOrFinalize && isFinalized && <button className="message-action-button" aria-label="Deshacer finalización" title="Volver a evaluar esta conversación como pendiente" disabled={mailboxActionPending} onClick={() => reopenMessage.mutate()}><Undo2 size={16} /><span>Deshacer finalización</span></button>}
        {hasTracking && canTrackOrFinalize && !isFinalized && automaticPending && <button className="message-action-button finalize-action" aria-label="Finalizar" title="Este correo no requiere ninguna acción. Retirarlo de pendientes." disabled={mailboxActionPending} onClick={() => finalizeMessage.mutate()}><Check size={16} /><span>Finalizar</span></button>}
        {hasMailActions && <span className="message-action-separator" aria-hidden="true" />}
        {hasMailActions && <button className="message-action-button danger-action" aria-label={destructiveActionLabel} title={destructiveActionLabel} disabled={mailboxActionPending} onClick={() => setConfirmTrash(true)}><Trash2 size={16} /><span>{destructiveActionLabel}</span></button>}
      </div>}
      <div className="message-meta"><div className="sender-avatar">{message.from.name.slice(0, 1)}</div><div><strong>{message.from.name}</strong><span>{message.from.address}</span><small>para {message.to.map(x => x.address).join(', ')} · {new Date(message.receivedAt).toLocaleString('es-CL')}</small></div></div>

      {hasTracking && trackMessage.isSuccess && <div className="success-notice">Correo añadido a Seguimiento prioritario.</div>}
      {hasTracking && untrackMessage.isSuccess && <div className="success-notice">Seguimiento manual retirado. El correo no fue movido ni eliminado.</div>}
      {hasTracking && finalizationNotice === 'finalized' && <div className="success-notice">Finalizado. Este correo ya no se considera pendiente y no fue movido ni eliminado.</div>}
      {hasTracking && finalizationNotice === 'reopened' && <div className="success-notice">Finalización deshecha. NexoMail volverá a evaluar esta conversación.</div>}
      {hasTracking && trackingMutationError && <div className="notice message-mailbox-error">{trackingMutationError instanceof Error ? trackingMutationError.message : 'No fue posible actualizar el seguimiento manual.'}</div>}
      {hasTracking && finalizationError && <div className="notice message-mailbox-error">{finalizationError instanceof Error ? finalizationError.message : 'No fue posible actualizar el estado de la conversación.'}</div>}
      {hasMailActions && (move.isError || ignore.isError || unignore.isError) && <div className="notice message-mailbox-error">No fue posible completar la acción sobre este correo.</div>}

      {thread.length > 1 ? <section className="thread-view"><h2>Conversación</h2>{thread.map(item => <article className={`thread-message ${item.isCurrent ? 'current' : ''}`} key={item.providerMessageId}><header><strong>{item.from.name}</strong><span>{item.from.address} · {new Date(item.receivedAt).toLocaleString('es-CL')}</span></header><div dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(item.htmlBody) }} /></article>)}</section> : <div className="message-body" dangerouslySetInnerHTML={{ __html: sanitizeEmailHtml(message.htmlBody) }} />}
      {message.attachments.length > 0 && <div className="attachments"><h2><Paperclip size={16} /> Adjuntos</h2><div className="attachment-list">{message.attachments.map(file => <div key={file.id} className={`attachment-card ${preview?.id === file.id ? 'selected' : ''}`}><button type="button" className="attachment-preview-button" onClick={() => setPreview(file)} title="Abrir vista previa"><Paperclip size={17} /><span><strong>{file.name}</strong><small>{Math.max(1, Math.round(file.size / 1000))} KB · Vista previa</small></span></button><a href={mailApi.attachmentUrl(accountId, messageId, file, true)} className="attachment-download" title={`Descargar ${file.name}`} aria-label={`Descargar ${file.name}`}><Download size={16} /></a></div>)}</div></div>}
      {hasMailActions && <div className="reply-bar message-action-footer"><button className="message-action-button primary-action" onClick={() => compose('reply')}><Reply size={16} /> Responder</button><button className="message-action-button" onClick={() => compose('replyAll')}><ReplyAll size={16} /> Responder a todos</button><button className="message-action-button" onClick={() => compose('forward')}><Forward size={16} /> Reenviar</button></div>}
    </section>
    {preview && <aside className="attachment-preview" aria-label="Vista previa de adjunto"><header><div><p className="eyebrow">Vista previa</p><strong title={preview.name}>{preview.name}</strong></div><button className="icon-button" onClick={() => setPreview(null)} aria-label="Cerrar vista previa"><X size={18} /></button></header><div className="attachment-preview-content">{preview.contentType.startsWith('image/') ? <img src={previewUrl} alt={preview.name} /> : canPreview(preview) ? <iframe src={previewUrl} title={`Vista previa: ${preview.name}`} /> : <div className="unsupported-preview"><FileText size={32} /><h2>Vista previa no disponible</h2><p>Este tipo de archivo no puede mostrarse de forma segura en el navegador.</p></div>}</div><footer><a href={downloadUrl} className="primary-button"><Download size={16} /> Descargar</a></footer></aside>}
    {hasMailActions && <ConfirmDialog open={confirmTrash} title={isDraft ? 'Descartar borrador' : 'Mover correo a Papelera'} message={isDraft ? 'Este borrador se eliminará definitivamente. Esta acción no se puede deshacer.' : 'El correo dejará de aparecer en esta bandeja y podrá restaurarse desde Papelera.'} confirmLabel={destructiveActionLabel} pending={trash.isPending} onCancel={() => setConfirmTrash(false)} onConfirm={() => trash.mutate()} />}
  </article>
}
