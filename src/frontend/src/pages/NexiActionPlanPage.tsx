import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Archive, CheckCircle2, Eye, EyeOff, Flag, FlagOff, Inbox, Mail, MessageSquareReply, ShieldAlert, Sparkles, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import { searchApi } from '../api/searchApi'
import type { MailSummary } from '../types/mail'
import { withActionIndicator } from '../utils/actionIndicator'
import { detectNexiMailPlan, detectNexiSourceFolder, requestsInboxScope, sanitizeActionSearch, type NexiMailAction, type NexiMailPlanStep, type NexiMailSourceFolder, type NexiMailSubset } from '../utils/nexiSearchIntent'

const MAX_ACTION_RESULTS = 250
const MAX_REPLY_DRAFTS = 8

type PendingRef = { accountId: string; messageId: string; conversationId: string }
type ActionResult = { completed: number; failed: number; skipped: number }
type PlanPreviewStep = NexiMailPlanStep & { items: MailSummary[]; limitedReplies: boolean }
type PlanResultStep = NexiMailPlanStep & ActionResult

function folderLabel(folder: NexiMailSourceFolder) {
  if (folder === 'inbox') return 'Bandeja de entrada'
  if (folder === 'sent') return 'Enviados'
  if (folder === 'archive') return 'Archivados'
  if (folder === 'spam') return 'Spam'
  return 'Papelera'
}

function uniqueMessages(items: MailSummary[]) {
  const unique = new Map<string, MailSummary>()
  for (const item of items) unique.set(`${item.accountId}:${item.providerMessageId}`, item)
  return [...unique.values()].sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())
}

function isMicrosoftReadAction(action: NexiMailAction) {
  return action === 'mark_read' || action === 'mark_unread'
}

async function loadMatchingMessages(accountId: string | undefined, folder: NexiMailSourceFolder, search: string) {
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
    case 'trash': return 'Enviar a Papelera'
    case 'archive': return 'Mover a Archivados'
    case 'move_inbox': return 'Mover a Bandeja'
    case 'move_spam': return 'Mover a Spam'
    case 'mark_read': return 'Marcar como leídos'
    case 'mark_unread': return 'Marcar como no leídos'
    case 'track': return 'Agregar seguimiento'
    case 'untrack': return 'Quitar seguimiento'
    case 'finalize': return 'Finalizar pendientes'
    case 'prepare_reply': return 'Preparar respuestas'
  }
}

function subsetLabel(subset: NexiMailSubset) {
  switch (subset) {
    case 'unread': return 'no leídos'
    case 'read': return 'leídos'
    case 'with_attachments': return 'con adjuntos'
    case 'pending': return 'requieren atención'
    case 'informational': return 'informativos / sin acción pendiente'
    default: return 'todos los resultados'
  }
}

function ActionIcon({ action, size = 17 }: { action: NexiMailAction; size?: number }) {
  if (action === 'trash') return <Trash2 size={size} />
  if (action === 'archive') return <Archive size={size} />
  if (action === 'move_inbox') return <Inbox size={size} />
  if (action === 'move_spam') return <ShieldAlert size={size} />
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

async function executeAction(action: NexiMailAction, items: MailSummary[], pendingByMessage: Map<string, PendingRef>, microsoftAccountIds: Set<string>): Promise<ActionResult> {
  let completed = 0
  let failed = 0
  let skipped = 0
  const batchSize = action === 'prepare_reply' ? 2 : 5

  for (let offset = 0; offset < items.length; offset += batchSize) {
    const batch = items.slice(offset, offset + batchSize)
    const results = await Promise.allSettled(batch.map(async item => {
      if (microsoftAccountIds.has(item.accountId) && !isMicrosoftReadAction(action)) return 'skipped' as const
      switch (action) {
        case 'trash':
          await mailApi.trash(item.accountId, item.providerMessageId)
          return 'completed' as const
        case 'archive':
          await mailApi.move(item.accountId, item.providerMessageId, 'archive')
          return 'completed' as const
        case 'move_inbox':
          await mailApi.move(item.accountId, item.providerMessageId, 'inbox')
          return 'completed' as const
        case 'move_spam':
          await mailApi.move(item.accountId, item.providerMessageId, 'spam')
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

export function NexiActionPlanPage() {
  const [params] = useSearchParams()
  const query = params.get('q')?.trim() ?? ''
  const baseQuery = params.get('base')?.trim() ?? ''
  const explicitAccount = params.get('account') ?? ''
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [result, setResult] = useState<PlanResultStep[] | null>(null)

  const plan = useMemo(() => detectNexiMailPlan(query), [query])
  const searchBasis = baseQuery || query
  const requiresControlCenter = plan.some(step => step.subset === 'pending' || step.subset === 'informational' || step.action === 'finalize' || step.action === 'prepare_reply')
  const includesReplyDrafts = plan.some(step => step.action === 'prepare_reply')
  const includesTrash = plan.some(step => step.action === 'trash')

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const microsoftAccountIds = useMemo(() => new Set((accounts.data ?? []).filter(account => account.provider === 'MicrosoftGraph').map(account => account.id)), [accounts.data])
  const interpretation = useQuery({
    queryKey: ['ai-search-interpretation', searchBasis],
    queryFn: () => searchApi.interpret(searchBasis),
    enabled: searchBasis.length > 0,
    staleTime: 30 * 60_000,
    retry: false,
  })

  const explicitSource = detectNexiSourceFolder(searchBasis)
  const movesToInbox = plan.some(step => step.action === 'move_inbox')
  const folder: NexiMailSourceFolder = includesReplyDrafts
    ? 'inbox'
    : explicitSource
      ? explicitSource
      : requestsInboxScope(searchBasis)
        ? 'inbox'
        : interpretation.data?.folder === 'sent'
          ? 'sent'
          : movesToInbox
            ? 'archive'
            : 'inbox'

  const searchText = useMemo(
    () => sanitizeActionSearch(interpretation.data?.gmailQuery?.trim() || searchBasis),
    [interpretation.data?.gmailQuery, searchBasis],
  )
  const hasConcreteSubset = plan.some(step => step.subset !== 'all')

  const preview = useQuery({
    queryKey: ['nexi-mail-plan-preview', explicitAccount, folder, searchText, plan.map(step => `${step.action}:${step.subset}`).join('|')],
    queryFn: () => loadMatchingMessages(explicitAccount || undefined, folder, searchText),
    enabled: plan.length > 1 && !interpretation.isLoading && (searchText.length > 0 || hasConcreteSubset),
    staleTime: 0,
    retry: false,
  })

  const controlCenter = useQuery({
    queryKey: ['control-center', explicitAccount || undefined],
    queryFn: () => mailApi.controlCenter(explicitAccount || undefined),
    enabled: requiresControlCenter && plan.length > 1,
    staleTime: 30_000,
    retry: false,
  })

  const pendingByMessage = useMemo(() => {
    const map = new Map<string, PendingRef>()
    for (const item of controlCenter.data?.pendingItems ?? []) map.set(`${item.accountId}:${item.messageId}`, item)
    return map
  }, [controlCenter.data?.pendingItems])

  const waitingForContext = requiresControlCenter && controlCenter.isLoading
  const items = preview.data?.items ?? []
  const previewSteps = useMemo<PlanPreviewStep[]>(() => {
    if (waitingForContext) return []
    return plan.map(step => {
      const subsetItems = filterSubset(items, step.subset, pendingByMessage)
      const limitedReplies = step.action === 'prepare_reply' && subsetItems.length > MAX_REPLY_DRAFTS
      return {
        ...step,
        items: step.action === 'prepare_reply' ? subsetItems.slice(0, MAX_REPLY_DRAFTS) : subsetItems,
        limitedReplies,
      }
    })
  }, [items, pendingByMessage, plan, waitingForContext])

  const totalOperations = previewSteps.reduce((sum, step) => sum + step.items.length, 0)
  const microsoftUnsupportedOperations = previewSteps.reduce((sum, step) => isMicrosoftReadAction(step.action) ? sum : sum + step.items.filter(item => microsoftAccountIds.has(item.accountId)).length, 0)
  const selectedAccount = accounts.data?.find(account => account.id === explicitAccount)

  const mutation = useMutation({
    mutationFn: () => withActionIndicator('Ejecutando plan de Nexi…', async () => {
      const results: PlanResultStep[] = []
      for (const step of previewSteps) {
        if (step.items.length === 0) {
          results.push({ ...step, completed: 0, failed: 0, skipped: 0 })
          continue
        }
        const stepResult = await executeAction(step.action, step.items, pendingByMessage, microsoftAccountIds)
        results.push({ action: step.action, subset: step.subset, clause: step.clause, ...stepResult })
      }
      return results
    }),
    onSuccess: async nextResult => {
      setConfirmOpen(false)
      setResult(nextResult)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center-tracking'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['universal-search-mail'], refetchType: 'all' }),
      ])
    },
  })

  function continueWithNexi() {
    if (!baseQuery || !result) return
    const completed = result.reduce((sum, step) => sum + step.completed, 0)
    const failed = result.reduce((sum, step) => sum + step.failed, 0)
    const note = `Plan de ${result.length} pasos completado: ${completed} operaciones correctas${failed > 0 ? ` y ${failed} con error` : ''}.`
    const next = new URLSearchParams({ tab: 'nexi', q: baseQuery, note })
    if (explicitAccount) next.set('account', explicitAccount)
    navigate(`/control-center?${next.toString()}`)
  }

  const confirmMessage = `Nexi ejecutará ${plan.length} pasos en orden, con ${totalOperations} operaciones sobre correos. ${includesTrash ? 'El plan incluye mover correos a Papelera. ' : ''}${includesReplyDrafts ? 'Las respuestas se guardarán como borradores y no se enviarán automáticamente. ' : ''}${microsoftUnsupportedOperations > 0 ? `${microsoftUnsupportedOperations} operaciones sobre Microsoft 365 se omitirán porque Phase 1 sólo permite marcar leído/no leído. ` : ''}Revisa el plan antes de confirmar.`

  if (plan.length <= 1) return null

  return <section className="mail-view nexi-action-page nexi-plan-page">
    <div className="view-header nexi-action-heading">
      <div><h1>Plan de Nexi</h1><p className="view-context">{selectedAccount?.displayName ?? 'Todas las cuentas'} · Origen: {folderLabel(folder)} · {plan.length} pasos</p></div>
    </div>

    <section className="nexi-action-command nexi-plan-command">
      <span className="nexi-action-command-icon"><Sparkles size={18} /></span>
      <div><strong>Nexi dividió tu instrucción en {plan.length} acciones</strong><span>Se ejecutarán en el orden indicado y con una sola confirmación.</span><small>“{query}”</small></div>
    </section>

    {interpretation.isLoading && <div className="universal-search-loading"><Sparkles size={18} /><span>Interpretando el contexto del plan…</span></div>}
    {preview.isLoading && <div className="universal-search-loading"><Mail size={18} /><span>Preparando los correos para cada paso…</span></div>}
    {waitingForContext && <div className="universal-search-loading"><Sparkles size={18} /><span>Contrastando pendientes e informativos con el Centro de Control…</span></div>}
    {preview.isError && <div className="notice">{preview.error instanceof Error ? preview.error.message : 'No fue posible preparar este plan.'}</div>}
    {controlCenter.isError && requiresControlCenter && <div className="notice">No fue posible comprobar el estado operativo de los correos. Nexi no ejecutará el plan.</div>}
    {microsoftUnsupportedOperations > 0 && <div className="notice">Microsoft 365 está en Phase 1: {microsoftUnsupportedOperations} operación{microsoftUnsupportedOperations === 1 ? '' : 'es'} no compatible{microsoftUnsupportedOperations === 1 ? '' : 's'} se omitirá{microsoftUnsupportedOperations === 1 ? '' : 'n'}. Sólo marcar leído/no leído puede ejecutarse sobre esas cuentas.</div>}

    {!preview.isLoading && !waitingForContext && items.length > 0 && <>
      <section className="nexi-plan-steps" aria-label="Plan de acciones">
        {previewSteps.map((step, index) => <article className="nexi-plan-step" key={`${step.action}:${step.subset}:${index}`}>
          <span className="nexi-plan-step-number">{index + 1}</span>
          <span className="nexi-plan-step-icon"><ActionIcon action={step.action} size={16} /></span>
          <div><strong>{actionCopy(step.action)}</strong><span>{subsetLabel(step.subset)}</span><small>{step.items.length} correo{step.items.length === 1 ? '' : 's'} se procesará{step.items.length === 1 ? '' : 'n'}</small></div>
          {step.items.length === 0 && <em>Sin coincidencias</em>}
        </article>)}
      </section>

      <section className="nexi-action-summary nexi-plan-summary">
        <div><Inbox size={17} /><span><strong>{items.length}</strong> correos en el contexto · <strong>{totalOperations}</strong> operaciones planificadas</span></div>
        <button type="button" className={includesTrash ? 'nexi-trash-action-button' : 'nexi-agent-action-button'} disabled={accounts.isLoading || mutation.isPending || totalOperations === 0 || (requiresControlCenter && controlCenter.isError)} onClick={() => setConfirmOpen(true)}><Sparkles size={15} />Ejecutar plan</button>
      </section>

      {preview.data?.limited && <div className="nexi-action-limit"><AlertTriangle size={14} />El contexto supera {MAX_ACTION_RESULTS} correos. Por seguridad, el plan se limita a los primeros {MAX_ACTION_RESULTS}; puedes acotar la búsqueda para continuar.</div>}
      {previewSteps.some(step => step.limitedReplies) && <div className="nexi-action-limit"><AlertTriangle size={14} />Cada paso de preparación de respuestas genera un máximo de {MAX_REPLY_DRAFTS} borradores por tanda.</div>}
    </>}

    {!preview.isLoading && !waitingForContext && items.length === 0 && (searchText.length > 0 || hasConcreteSubset) && <div className="universal-no-results"><Mail size={24} /><strong>No encontré correos para este plan</strong><span>Nexi no ejecutó ninguna acción.</span></div>}

    {result && <section className="nexi-plan-results">
      <header><CheckCircle2 size={17} /><strong>Resultado del plan</strong></header>
      {result.map((step, index) => <div className={`nexi-plan-result ${step.failed > 0 ? 'warning' : 'success'}`} key={`${step.action}:${index}`}>
        <span>{index + 1}</span><ActionIcon action={step.action} size={15} /><strong>{actionCopy(step.action)}</strong><small>{step.completed} correctas{step.failed > 0 ? ` · ${step.failed} con error` : ''}{step.skipped > 0 ? ` · ${step.skipped} omitidas` : ''}</small>
      </div>)}
      <div className="nexi-action-result-actions">
        {includesReplyDrafts && <button type="button" className="secondary-button" onClick={() => navigate('/drafts')}>Ver borradores</button>}
        {baseQuery && <button type="button" className="secondary-button" onClick={continueWithNexi}>Continuar con Nexi</button>}
      </div>
    </section>}

    <ConfirmDialog
      open={confirmOpen}
      title={`Ejecutar plan de ${plan.length} pasos`}
      message={confirmMessage}
      confirmLabel="Confirmar y ejecutar"
      pending={mutation.isPending}
      onConfirm={() => mutation.mutate()}
      onCancel={() => setConfirmOpen(false)}
    />
  </section>
}
