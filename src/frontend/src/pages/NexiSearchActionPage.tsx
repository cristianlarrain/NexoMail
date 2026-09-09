import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Archive, CheckCircle2, Eye, EyeOff, Flag, FlagOff, Inbox, Mail, Search, Sparkles, Trash2 } from 'lucide-react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import { searchApi } from '../api/searchApi'
import type { MailSummary } from '../types/mail'
import { detectNexiMailAction, requestsInboxScope, sanitizeActionSearch, type NexiMailAction } from '../utils/nexiSearchIntent'

const MAX_ACTION_RESULTS = 250

type ActionResult = { completed: number; failed: number; skipped: number }

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
  }
}

function ActionIcon({ action, size = 17 }: { action: NexiMailAction; size?: number }) {
  if (action === 'trash') return <Trash2 size={size} />
  if (action === 'archive') return <Archive size={size} />
  if (action === 'mark_read') return <Eye size={size} />
  if (action === 'mark_unread') return <EyeOff size={size} />
  if (action === 'track') return <Flag size={size} />
  if (action === 'untrack') return <FlagOff size={size} />
  return <CheckCircle2 size={size} />
}

async function executeAction(
  action: NexiMailAction,
  items: MailSummary[],
  pendingByMessage: Map<string, { accountId: string; messageId: string; conversationId: string }>,
): Promise<ActionResult> {
  let completed = 0
  let failed = 0
  let skipped = 0

  for (let offset = 0; offset < items.length; offset += 5) {
    const batch = items.slice(offset, offset + 5)
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
  const explicitAccount = params.get('account') ?? ''
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [operationResult, setOperationResult] = useState<ActionResult | null>(null)

  const action = detectNexiMailAction(query)
  const copy = action ? actionCopy(action) : null
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const interpretation = useQuery({
    queryKey: ['ai-search-interpretation', query],
    queryFn: () => searchApi.interpret(query),
    enabled: query.length > 0,
    staleTime: 30 * 60_000,
    retry: false,
  })

  const folder: 'inbox' | 'sent' = requestsInboxScope(query) ? 'inbox' : interpretation.data?.folder === 'sent' ? 'sent' : 'inbox'
  const searchText = useMemo(() => sanitizeActionSearch(interpretation.data?.gmailQuery?.trim() || query), [interpretation.data?.gmailQuery, query])

  const preview = useQuery({
    queryKey: ['nexi-mail-action-preview', action, explicitAccount, folder, searchText],
    queryFn: () => loadMatchingMessages(explicitAccount || undefined, folder, searchText),
    enabled: Boolean(action) && query.length > 0 && !interpretation.isLoading && searchText.length > 0,
    staleTime: 0,
    retry: false,
  })

  const controlCenter = useQuery({
    queryKey: ['control-center', explicitAccount || undefined],
    queryFn: () => mailApi.controlCenter(explicitAccount || undefined),
    enabled: action === 'finalize' && query.length > 0,
    staleTime: 30_000,
    retry: false,
  })

  const pendingByMessage = useMemo(() => {
    const map = new Map<string, { accountId: string; messageId: string; conversationId: string }>()
    for (const item of controlCenter.data?.pendingItems ?? []) map.set(`${item.accountId}:${item.messageId}`, item)
    return map
  }, [controlCenter.data?.pendingItems])

  const selectedAccount = accounts.data?.find(account => account.id === explicitAccount)
  const items = preview.data?.items ?? []
  const actionableItems = action === 'finalize' ? items.filter(item => pendingByMessage.has(`${item.accountId}:${item.providerMessageId}`)) : items

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

  if (!action || !copy) return <section className="mail-view nexi-action-page">
    <div className="nexi-action-empty"><Search size={26} /><strong>No detecté una acción operativa.</strong><span>Puedes pedirme archivar, eliminar, marcar leído/no leído, seguimiento o finalizar pendientes.</span><button type="button" className="secondary-button" onClick={() => navigate(`/search?q=${encodeURIComponent(query)}`)}>Buscar normalmente</button></div>
  </section>

  const waitingForFinalize = action === 'finalize' && controlCenter.isLoading
  const zeroFinalize = action === 'finalize' && !waitingForFinalize && items.length > 0 && actionableItems.length === 0
  const confirmMessage = action === 'trash'
    ? `Se enviarán ${actionableItems.length} correo${actionableItems.length === 1 ? '' : 's'} a Papelera. Podrás recuperarlos mientras no vacíes la Papelera.`
    : `Nexi aplicará “${copy.confirmTitle}” a ${actionableItems.length} correo${actionableItems.length === 1 ? '' : 's'}. Revisa la lista antes de confirmar.`

  return <section className="mail-view nexi-action-page">
    <div className="view-header nexi-action-heading">
      <div><h1>Acción de Nexi</h1><p className="view-context">{selectedAccount?.displayName ?? 'Todas las cuentas'} · {folder === 'inbox' ? 'Bandeja de entrada' : 'Enviados'}</p></div>
    </div>

    <section className="nexi-action-command">
      <span className="nexi-action-command-icon"><ActionIcon action={action} size={18} /></span>
      <div><strong>Nexi entendió que quieres {copy.title}</strong><span>Primero verifica los resultados. {copy.description}</span><small>“{query}”</small></div>
    </section>

    {interpretation.isLoading && <div className="universal-search-loading"><Sparkles size={18} /><span>Interpretando el criterio de búsqueda…</span></div>}
    {!interpretation.isLoading && searchText.length === 0 && <div className="notice">Para ejecutar una acción sobre varios correos necesito un criterio concreto, por ejemplo un remitente, asunto o palabra clave.</div>}
    {preview.isLoading && <div className="universal-search-loading"><Search size={18} /><span>Buscando los correos que coinciden…</span></div>}
    {waitingForFinalize && <div className="universal-search-loading"><Sparkles size={18} /><span>Comprobando cuáles siguen pendientes en el Centro de Control…</span></div>}
    {preview.isError && <div className="notice">{preview.error instanceof Error ? preview.error.message : 'No fue posible obtener los correos para esta acción.'}</div>}
    {controlCenter.isError && action === 'finalize' && <div className="notice">No fue posible comprobar el estado pendiente de los correos. Nexi no ejecutará la finalización.</div>}

    {operationResult && <div className={`nexi-action-result ${operationResult.failed > 0 ? 'warning' : 'success'}`}>
      <ActionIcon action={action} size={16} /><span><strong>{actionPastLabel(action, operationResult.completed)}</strong>{operationResult.failed > 0 ? ` ${operationResult.failed} no pudieron procesarse.` : ' La acción se completó correctamente.'}{operationResult.skipped > 0 ? ` ${operationResult.skipped} no correspondían a esta acción.` : ''}</span>
    </div>}

    {!preview.isLoading && items.length > 0 && <>
      <section className="nexi-action-summary">
        <div><Inbox size={17} /><span><strong>{items.length}</strong> correo{items.length === 1 ? '' : 's'} coincide{items.length === 1 ? '' : 'n'} con el criterio{action === 'finalize' ? ` · ${actionableItems.length} pendiente${actionableItems.length === 1 ? '' : 's'}` : ''}</span></div>
        <button type="button" className={action === 'trash' ? 'nexi-trash-action-button' : 'nexi-agent-action-button'} disabled={mutation.isPending || waitingForFinalize || actionableItems.length === 0 || (action === 'finalize' && controlCenter.isError)} onClick={() => setConfirmOpen(true)}><ActionIcon action={action} size={15} />{actionButtonLabel(action, actionableItems.length)}</button>
      </section>
      {preview.data?.limited && <div className="nexi-action-limit"><AlertTriangle size={14} />La búsqueda supera {MAX_ACTION_RESULTS} resultados. Por seguridad, esta operación incluye sólo los primeros {MAX_ACTION_RESULTS}; puedes acotar el criterio para continuar.</div>}
      {zeroFinalize && <div className="nexi-action-limit"><AlertTriangle size={14} />Los correos encontrados no figuran actualmente como pendientes. Nexi no ejecutará ninguna finalización sobre ellos.</div>}

      <section className="universal-result-section">
        <header><div><Mail size={17} /><strong>Correos que se procesarán</strong></div><span>{items.length}</span></header>
        <div className="universal-mail-results">
          {items.map(item => {
            const account = accounts.data?.find(value => value.id === item.accountId)
            const actionable = action !== 'finalize' || pendingByMessage.has(`${item.accountId}:${item.providerMessageId}`)
            return <button type="button" key={`${item.accountId}:${item.providerMessageId}`} className={`universal-mail-result ${item.isRead ? '' : 'unread'} ${actionable ? '' : 'nexi-action-not-applicable'}`} onClick={() => openMessage(item)}>
              <i className="account-dot" style={{ background: account?.color }} />
              <span className="universal-result-primary"><strong>{item.senderName || item.senderAddress || 'Correo'}</strong><span>{item.subject}</span><small>{item.preview}</small></span>
              <span />
              <span className="universal-result-meta"><small>{account?.displayName}{!actionable ? ' · no pendiente' : ''}</small><time>{dateLabel(item.receivedAt)}</time></span>
            </button>
          })}
        </div>
      </section>
    </>}

    {!preview.isLoading && searchText.length > 0 && items.length === 0 && <div className="universal-no-results"><Search size={24} /><strong>No encontré correos para esta acción</strong><span>Nexi no ejecutó ninguna operación.</span></div>}

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
