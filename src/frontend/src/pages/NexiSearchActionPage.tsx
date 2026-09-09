import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Archive, CheckCircle2, Eye, EyeOff, Flag, FlagOff, Inbox, Mail, MessageSquareReply, Search, Sparkles, Trash2 } from 'lucide-react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import { searchApi } from '../api/searchApi'
import type { MailSummary } from '../types/mail'
import { detectNexiMailAction, detectNexiMailSubset, requestsInboxScope, sanitizeActionSearch, type NexiMailAction, type NexiMailSubset } from '../utils/nexiSearchIntent'

const MAX_ACTION_RESULTS = 250
const MAX_REPLY_DRAFTS = 8

type ActionResult = { completed: number; failed: number; skipped: number }
type PendingRef = { accountId: string; messageId: string; conversationId: string }

function dateLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' })
}

function uniqueMessages(items: MailSummary[]) {
  const unique = new Map<string, MailSummary>()
  for (const item of items) unique.set(`${item.accountId}:${item.providerMessageId}`, item)
  return [...unique.values()].sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())
}

async function loadMatchingMessages(accountId: string | undefined, folder: 'inbox' | 'sent', search: string) {
  const items: MailSummary[] = []
  let cursor: string | undefined
  let limited = false

  do {
    const page = await searchApi.messages(accountId, folder, search, cursor)
    items.push(...page.items)
    cursor = page.nextCursor
    if (items.length >= MAX_ACTION_RESULTS) {
      limited = Boolean(cursor)
      break
    }
  } while (cursor)

  return { items: uniqueMessages(items).slice(0, MAX_ACTION_RESULTS), limited }
}

function actionCopy(action: NexiMailAction) {
  switch (action) {
    case 'trash': return { title: 'enviar correos a Papelera', description: 'Se moverán a Papelera; no se borrarán definitivamente.', confirmTitle: 'Enviar correos a Papelera' }
    case 'archive': return { title: 'archivar correos', description: 'Los correos saldrán de la Bandeja de entrada y permanecerán disponibles en Archivados.', confirmTitle: 'Archivar correos' }
    case 'mark_read': return { title: 'marcar correos como leídos', description: 'NexoMail actualizará el estado de lectura de todos los correos seleccionados.', confirmTitle: 'Marcar como leídos' }
    case 'mark_unread': return { title: 'marcar correos como no leídos', description: 'NexoMail dejará todos los correos seleccionados como pendientes de lectura.', confirmTitle: 'Marcar como no leídos' }
    case 'track': return { title: 'marcar correos para seguimiento', description: 'Los correos quedarán incorporados al seguimiento manual del Centro de Control.', confirmTitle: 'Agregar seguimiento' }
    case 'untrack': return { title: 'quitar seguimiento de correos', description: 'NexoMail retirará estos correos del seguimiento manual.', confirmTitle: 'Quitar seguimiento' }
    case 'finalize': return { title: 'finalizar correos pendientes', description: 'Sólo se finalizarán los correos que actualmente estén pendientes en el Centro de Control. No se moverán ni eliminarán.', confirmTitle: 'Finalizar pendientes' }
    case 'prepare_reply': return { title: 'preparar respuestas', description: 'Nexi redactará borradores de respuesta. No se enviará ningún correo automáticamente.', confirmTitle: 'Preparar borradores de respuesta' }
  }
}

function subsetLabel(subset: NexiMailSubset) {
  switch (subset) {
    case 'unread': return 'sólo no leídos'
    case 'read': return 'sólo leídos'
    case 'with_attachments': return 'sólo con adjuntos'
    case 'pending': return 'sólo los que requieren atención según Centro de Control'
    case 'informational': return 'sin acción pendiente según Centro de Control'
    default: return 'todos los resultados encontrados'
  }
}

function actionButtonLabel(action: NexiMailAction, count: number) {
  switch (action) {
    case 'trash': return `Enviar ${count} a Papelera`
    case 'archive': return `Archivar ${count}`
    case 'mark_read': return `Marcar ${count} como leídos`
    case 'mark_unread': return `Marcar ${count} como no leídos`
    case 'track': return `Seguir ${count}`
    case 'untrack': return `Quitar seguimiento a ${count}`
    case 'finalize': return `Finalizar ${count}`
    case 'prepare_reply': return `Preparar ${count} respuesta${count === 1 ? '' : 's'}`
  }
}

function actionPastLabel(action: NexiMailAction, count: number) {
  const plural = count === 1 ? '' : 's'
  switch (action) {
    case 'trash': return `${count} correo${plural} enviado${plural} a Papelera.`
    case 'archive': return `${count} correo${plural} archivado${plural}.`
    case 'mark_read': return `${count} correo${plural} marcado${plural} como leído${plural}.`
    case 'mark_unread': return `${count} correo${plural} marcado${plural} como no leído${plural}.`
    case 'track': return `${count} correo${plural} agregado${plural} a seguimiento.`
    case 'untrack': return `Se quitó el seguimiento de ${count} correo${plural}.`
    case 'finalize': return `${count} correo${plural} finalizado${plural}.`
    case 'prepare_reply': return `${count} borrador${plural} de respuesta preparado${plural}.`
  }
}

function ActionIcon({ action, size = 17 }: { action: NexiMailAction; size?: number }) {
  if (action === 'trash') return <Trash2 size={size} />
  if (action === 'archive') return <Archive size={size} />
  if (action === 'mark_read') return <Eye size={size} />
  if (action === 'mark_unread') return <EyeOff size={size} />
  if (action === 'track') return <Flag size={size} />
  if (action === 'untrack') return <FlagOff size={size} />
  if (action === 'prepare_reply') return <MessageSquareReply size={size} />
  return <CheckCircle2 size={size} />
}

function filterSubset(items: MailSummary[], subset: NexiMailSubset, pendingByMessage: Map<string, PendingRef>) {
  switch (subset) {
    case 'unread': return items.filter(item => !item.isRead)
    case 'read': return items.filter(item => item.isRead)
    case 'with_attachments': return items.filter(item => item.hasAttachments)
    case 'pending': return items.filter(item => pendingByMessage.has(`${item.accountId}:${item.providerMessageId}`))
    case 'informational': return items.filter(item => !pendingByMessage.has(`${item.accountId}:${item.providerMessageId}`))
    default: return items
  }
}

function escapeHtml(value: string) {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}

function textToHtml(value: string) {
  const paragraphs = value.trim().split(/\n{2,}/).map(part => `<p>${escapeHtml(part).replaceAll('\n', '<br>')}</p>`)
  return paragraphs.join('') || '<p></p>'
}

async function executeAction(
  action: NexiMailAction,
  items: MailSummary[],
  pendingByMessage: Map<string, PendingRef>,
): Promise<ActionResult> {
  let completed = 0
  let failed = 0
  let skipped = 0
  const batchSize = action === 'prepare_reply' ? 2 : 5

  for (let offset = 0; offset < items.length; offset += batchSize) {
    const batch = items.slice(offset, offset + batchSize)
    const results = await Promise.allSettled(batch.map(async item => {
      switch (action) {
        case 'trash':
          await mailApi.trash(item.accountId, item.providerMessageId)
          return 'completed' as const
        case 'archive':
          await mailApi.move(item.accountId, item.providerMessageId, 'archive')
          return 'completed' as const
        case 'mark_read':
          await mailApi.read(item.accountId, item.providerMessageId, true)
          return 'completed' as const
        case 'mark_unread':
          await mailApi.read(item.accountId, item.providerMessageId, false)
          return 'completed' as const
        case 'track':
          await mailApi.trackMessage(item.accountId, item.providerMessageId)
          return 'completed' as const
        case 'untrack':
          await mailApi.untrackMessage(item.accountId, item.providerMessageId)
          return 'completed' as const
        case 'finalize': {
          const pending = pendingByMessage.get(`${item.accountId}:${item.providerMessageId}`)
          if (!pending) return 'skipped' as const
          await mailApi.updateControlCenterState(pending.accountId, pending.conversationId, { messageId: pending.messageId, action: 'resolved' })
          return 'completed' as const
        }
        case 'prepare_reply': {
          const original = await mailApi.message(item.accountId, item.providerMessageId)
          const recipient = original.from.address?.trim()
          if (!recipient) return 'skipped' as const
          const suggestion = await mailApi.aiReply(
            item.accountId,
            item.providerMessageId,
            'profesional',
            'Prepara una respuesta clara, breve y profesional a este correo. No la envíes; sólo redacta un borrador listo para revisar.',
          )
          const subject = suggestion.subject?.trim() || (/^re:/i.test(original.subject) ? original.subject : `Re: ${original.subject}`)
          await mailApi.saveDraft({
            fromAccountId: item.accountId,
            to: [recipient],
            cc: [],
            bcc: [],
            subject,
            htmlBody: textToHtml(suggestion.text),
            attachments: [],
          }, item.providerMessageId)
          return 'completed' as const
        }
      }
    }))

    for (const result of results) {
      if (result.status === 'rejected') failed += 1
      else if (result.value === 'skipped') skipped += 1
      else completed += 1
    }
  }

  return { completed, failed, skipped }
}

export function NexiSearchActionPage() {
  const [params] = useSearchParams()
  const query = params.get('q')?.trim() ?? ''
  const baseQuery = params.get('base')?.trim() ?? ''
  const explicitAccount = params.get('account') ?? ''
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [operationResult, setOperationResult] = useState<ActionResult | null>(null)

  const action = detectNexiMailAction(query)
  const detectedSubset = detectNexiMailSubset(query)
  const effectiveSubset: NexiMailSubset = action === 'finalize'
    ? 'pending'
    : action === 'prepare_reply' && detectedSubset === 'all'
      ? 'pending'
      : detectedSubset
  const copy = action ? actionCopy(action) : null
  const requiresControlCenter = action === 'finalize' || action === 'prepare_reply' || effectiveSubset === 'pending' || effectiveSubset === 'informational'

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const interpretation = useQuery({
    queryKey: ['ai-search-interpretation', query],
    queryFn: () => searchApi.interpret(query),
    enabled: query.length > 0,
    staleTime: 30 * 60_000,
    retry: false,
  })

  const folder: 'inbox' | 'sent' = action === 'prepare_reply'
    ? 'inbox'
    : requestsInboxScope(query)
      ? 'inbox'
      : interpretation.data?.folder === 'sent' ? 'sent' : 'inbox'
  const searchText = useMemo(() => sanitizeActionSearch(interpretation.data?.gmailQuery?.trim() || query), [interpretation.data?.gmailQuery, query])
  const hasConcreteSubset = effectiveSubset !== 'all'

  const preview = useQuery({
    queryKey: ['nexi-mail-action-preview', action, effectiveSubset, explicitAccount, folder, searchText],
    queryFn: () => loadMatchingMessages(explicitAccount || undefined, folder, searchText),
    enabled: Boolean(action) && query.length > 0 && !interpretation.isLoading && (searchText.length > 0 || hasConcreteSubset),
    staleTime: 0,
    retry: false,
  })

  const controlCenter = useQuery({
    queryKey: ['control-center', explicitAccount || undefined],
    queryFn: () => mailApi.controlCenter(explicitAccount || undefined),
    enabled: requiresControlCenter && query.length > 0,
    staleTime: 30_000,
    retry: false,
  })

  const pendingByMessage = useMemo(() => {
    const map = new Map<string, PendingRef>()
    for (const item of controlCenter.data?.pendingItems ?? []) map.set(`${item.accountId}:${item.messageId}`, item)
    return map
  }, [controlCenter.data?.pendingItems])

  const selectedAccount = accounts.data?.find(account => account.id === explicitAccount)
  const items = preview.data?.items ?? []
  const waitingForContext = requiresControlCenter && controlCenter.isLoading
  const subsetItems = waitingForContext ? [] : filterSubset(items, effectiveSubset, pendingByMessage)
  const actionableItems = action === 'prepare_reply' ? subsetItems.slice(0, MAX_REPLY_DRAFTS) : subsetItems
  const excludedBySubset = Math.max(0, items.length - subsetItems.length)
  const replyLimited = action === 'prepare_reply' && subsetItems.length > MAX_REPLY_DRAFTS

  const mutation = useMutation({
    mutationFn: () => action ? executeAction(action, actionableItems, pendingByMessage) : Promise.resolve({ completed: 0, failed: 0, skipped: 0 }),
    onSuccess: async result => {
      setConfirmOpen(false)
      setOperationResult(result)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center-tracking'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['universal-search-mail'], refetchType: 'all' }),
      ])
      await preview.refetch()
    },
  })

  function openMessage(item: MailSummary) {
    navigate(`/message/${encodeURIComponent(item.accountId)}/${encodeURIComponent(item.providerMessageId)}`, {
      state: { returnTo: `${location.pathname}${location.search}` },
    })
  }

  function continueWithNexi() {
    if (!baseQuery || !action || !operationResult) return
    const note = `${actionPastLabel(action, operationResult.completed)}${operationResult.failed > 0 ? ` ${operationResult.failed} no pudieron procesarse.` : ''}`
    const next = new URLSearchParams({ tab: 'nexi', q: baseQuery, note })
    if (explicitAccount) next.set('account', explicitAccount)
    navigate(`/control-center?${next.toString()}`)
  }

  if (!action || !copy) return <section className="mail-view nexi-action-page">
    <div className="nexi-action-empty"><Search size={26} /><strong>No detecté una acción operativa.</strong><span>Puedes pedirme archivar, eliminar, marcar leído/no leído, seguimiento, finalizar pendientes o preparar respuestas.</span><button type="button" className="secondary-button" onClick={() => navigate(`/search?q=${encodeURIComponent(query)}`)}>Buscar normalmente</button></div>
  </section>

  const noSubsetMatches = !waitingForContext && items.length > 0 && subsetItems.length === 0
  const confirmMessage = action === 'trash'
    ? `Se enviarán ${actionableItems.length} correo${actionableItems.length === 1 ? '' : 's'} a Papelera. Podrás recuperarlos mientras no vacíes la Papelera.`
    : action === 'prepare_reply'
      ? `Nexi preparará ${actionableItems.length} borrador${actionableItems.length === 1 ? '' : 'es'} de respuesta para revisar. No se enviará ningún correo automáticamente.`
      : `Nexi aplicará “${copy.confirmTitle}” a ${actionableItems.length} correo${actionableItems.length === 1 ? '' : 's'}. Revisa la lista antes de confirmar.`

  return <section className="mail-view nexi-action-page">
    <div className="view-header nexi-action-heading">
      <div><h1>Acción de Nexi</h1><p className="view-context">{selectedAccount?.displayName ?? 'Todas las cuentas'} · {folder === 'inbox' ? 'Bandeja de entrada' : 'Enviados'}</p></div>
    </div>

    <section className="nexi-action-command">
      <span className="nexi-action-command-icon"><ActionIcon action={action} size={18} /></span>
      <div>
        <strong>Nexi entendió que quieres {copy.title}</strong>
        <span>Primero verifica los resultados. {copy.description}</span>
        <span className="nexi-action-scope">Subconjunto: <b>{subsetLabel(effectiveSubset)}</b></span>
        <small>“{query}”</small>
      </div>
    </section>

    {interpretation.isLoading && <div className="universal-search-loading"><Sparkles size={18} /><span>Interpretando el criterio de búsqueda…</span></div>}
    {!interpretation.isLoading && searchText.length === 0 && !hasConcreteSubset && <div className="notice">Para ejecutar una acción sobre varios correos necesito un criterio concreto, por ejemplo un remitente, asunto, palabra clave o subconjunto como “los no leídos”.</div>}
    {preview.isLoading && <div className="universal-search-loading"><Search size={18} /><span>Buscando los correos que coinciden…</span></div>}
    {waitingForContext && <div className="universal-search-loading"><Sparkles size={18} /><span>Contrastando el conjunto con el Centro de Control…</span></div>}
    {preview.isError && <div className="notice">{preview.error instanceof Error ? preview.error.message : 'No fue posible obtener los correos para esta acción.'}</div>}
    {controlCenter.isError && requiresControlCenter && <div className="notice">No fue posible comprobar el estado operativo de los correos. Nexi no ejecutará esta acción.</div>}

    {operationResult && <div className={`nexi-action-result ${operationResult.failed > 0 ? 'warning' : 'success'}`}>
      <ActionIcon action={action} size={16} />
      <span><strong>{actionPastLabel(action, operationResult.completed)}</strong>{operationResult.failed > 0 ? ` ${operationResult.failed} no pudieron procesarse.` : ' La acción se completó correctamente.'}{operationResult.skipped > 0 ? ` ${operationResult.skipped} no correspondían a esta acción.` : ''}</span>
      <div className="nexi-action-result-actions">
        {action === 'prepare_reply' && <button type="button" className="secondary-button" onClick={() => navigate('/drafts')}>Ver borradores</button>}
        {baseQuery && <button type="button" className="secondary-button" onClick={continueWithNexi}>Continuar con Nexi</button>}
      </div>
    </div>}

    {!preview.isLoading && items.length > 0 && <>
      <section className="nexi-action-summary">
        <div><Inbox size={17} /><span><strong>{items.length}</strong> encontrado{items.length === 1 ? '' : 's'} · <strong>{actionableItems.length}</strong> se procesará{actionableItems.length === 1 ? '' : 'n'}</span></div>
        <button type="button" className={action === 'trash' ? 'nexi-trash-action-button' : 'nexi-agent-action-button'} disabled={mutation.isPending || waitingForContext || actionableItems.length === 0 || (requiresControlCenter && controlCenter.isError)} onClick={() => setConfirmOpen(true)}><ActionIcon action={action} size={15} />{actionButtonLabel(action, actionableItems.length)}</button>
      </section>
      {preview.data?.limited && <div className="nexi-action-limit"><AlertTriangle size={14} />La búsqueda supera {MAX_ACTION_RESULTS} resultados. Por seguridad, esta operación incluye sólo los primeros {MAX_ACTION_RESULTS}; puedes acotar el criterio para continuar.</div>}
      {excludedBySubset > 0 && <div className="nexi-action-subset-note"><Sparkles size={14} />Nexi excluyó {excludedBySubset} correo{excludedBySubset === 1 ? '' : 's'} porque no pertenece{excludedBySubset === 1 ? '' : 'n'} al subconjunto “{subsetLabel(effectiveSubset)}”.</div>}
      {replyLimited && <div className="nexi-action-limit"><AlertTriangle size={14} />Hay {subsetItems.length} correos que podrían responderse. Por seguridad y para controlar el uso de IA, esta tanda preparará un máximo de {MAX_REPLY_DRAFTS} borradores.</div>}
      {noSubsetMatches && <div className="nexi-action-limit"><AlertTriangle size={14} />Encontré correos, pero ninguno pertenece al subconjunto “{subsetLabel(effectiveSubset)}”. Nexi no ejecutará ninguna acción.</div>}

      {actionableItems.length > 0 && <section className="universal-result-section">
        <header><div><Mail size={17} /><strong>Correos que se procesarán</strong></div><span>{actionableItems.length}</span></header>
        <div className="universal-mail-results">
          {actionableItems.map(item => {
            const account = accounts.data?.find(value => value.id === item.accountId)
            return <button type="button" key={`${item.accountId}:${item.providerMessageId}`} className={`universal-mail-result ${item.isRead ? '' : 'unread'}`} onClick={() => openMessage(item)}>
              <i className="account-dot" style={{ background: account?.color }} />
              <span className="universal-result-primary"><strong>{item.senderName || item.senderAddress || 'Correo'}</strong><span>{item.subject}</span><small>{item.preview}</small></span>
              <span />
              <span className="universal-result-meta"><small>{account?.displayName}</small><time>{dateLabel(item.receivedAt)}</time></span>
            </button>
          })}
        </div>
      </section>}
    </>}

    {!preview.isLoading && (searchText.length > 0 || hasConcreteSubset) && items.length === 0 && <div className="universal-no-results"><Search size={24} /><strong>No encontré correos para esta acción</strong><span>Nexi no ejecutó ninguna operación.</span></div>}

    <ConfirmDialog
      open={confirmOpen}
      title={copy.confirmTitle}
      message={confirmMessage}
      confirmLabel={actionButtonLabel(action, actionableItems.length)}
      pending={mutation.isPending}
      onConfirm={() => mutation.mutate()}
      onCancel={() => setConfirmOpen(false)}
    />
  </section>
}
