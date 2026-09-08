import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Inbox, Mail, Search, Sparkles, Trash2 } from 'lucide-react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import { searchApi } from '../api/searchApi'
import type { MailSummary } from '../types/mail'
import { requestsInboxScope, requestsTrashAction, sanitizeActionSearch } from '../utils/nexiSearchIntent'

const MAX_ACTION_RESULTS = 250

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

async function moveToTrash(items: MailSummary[]) {
  let trashed = 0
  let failed = 0

  for (let offset = 0; offset < items.length; offset += 5) {
    const batch = items.slice(offset, offset + 5)
    const results = await Promise.allSettled(batch.map(item => mailApi.trash(item.accountId, item.providerMessageId)))
    for (const result of results) {
      if (result.status === 'fulfilled') trashed += 1
      else failed += 1
    }
  }

  return { trashed, failed }
}

export function NexiSearchActionPage() {
  const [params] = useSearchParams()
  const query = params.get('q')?.trim() ?? ''
  const explicitAccount = params.get('account') ?? ''
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [operationResult, setOperationResult] = useState<{ trashed: number; failed: number } | null>(null)

  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const interpretation = useQuery({
    queryKey: ['ai-search-interpretation', query],
    queryFn: () => searchApi.interpret(query),
    enabled: query.length > 0,
    staleTime: 30 * 60_000,
    retry: false,
  })

  const deleteRequested = requestsTrashAction(query)
  const folder: 'inbox' | 'sent' = requestsInboxScope(query) ? 'inbox' : interpretation.data?.folder === 'sent' ? 'sent' : 'inbox'
  const searchText = useMemo(() => {
    const interpreted = interpretation.data?.gmailQuery?.trim() ?? ''
    const cleaned = sanitizeActionSearch(interpreted || query)
    return cleaned
  }, [interpretation.data?.gmailQuery, query])

  const preview = useQuery({
    queryKey: ['nexi-mail-action-preview', 'trash', explicitAccount, folder, searchText],
    queryFn: () => loadMatchingMessages(explicitAccount || undefined, folder, searchText),
    enabled: deleteRequested && query.length > 0 && !interpretation.isLoading && searchText.length > 0,
    staleTime: 0,
    retry: false,
  })

  const selectedAccount = accounts.data?.find(account => account.id === explicitAccount)
  const items = preview.data?.items ?? []

  const trashMutation = useMutation({
    mutationFn: () => moveToTrash(items),
    onSuccess: async result => {
      setConfirmOpen(false)
      setOperationResult(result)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'all' }),
        queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' }),
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

  if (!deleteRequested) return <section className="mail-view nexi-action-page">
    <div className="nexi-action-empty"><Search size={26} /><strong>No detecté una acción de eliminación.</strong><button type="button" className="secondary-button" onClick={() => navigate(`/search?q=${encodeURIComponent(query)}`)}>Buscar normalmente</button></div>
  </section>

  return <section className="mail-view nexi-action-page">
    <div className="view-header nexi-action-heading">
      <div><h1>Acción de Nexi</h1><p className="view-context">{selectedAccount?.displayName ?? 'Todas las cuentas'} · {folder === 'inbox' ? 'Bandeja de entrada' : 'Enviados'}</p></div>
    </div>

    <section className="nexi-action-command">
      <span className="nexi-action-command-icon"><Sparkles size={18} /></span>
      <div><strong>Nexi entendió que quieres eliminar correos</strong><span>Primero verifica los resultados. Al confirmar, se enviarán a Papelera; no se borrarán definitivamente.</span><small>“{query}”</small></div>
    </section>

    {interpretation.isLoading && <div className="universal-search-loading"><Sparkles size={18} /><span>Interpretando el criterio de búsqueda…</span></div>}
    {!interpretation.isLoading && searchText.length === 0 && <div className="notice">Para eliminar varios correos necesito un criterio concreto, por ejemplo un remitente, asunto o palabra clave.</div>}
    {preview.isLoading && <div className="universal-search-loading"><Search size={18} /><span>Buscando los correos que coinciden…</span></div>}
    {preview.isError && <div className="notice">{preview.error instanceof Error ? preview.error.message : 'No fue posible obtener los correos para esta acción.'}</div>}

    {operationResult && <div className={`nexi-action-result ${operationResult.failed > 0 ? 'warning' : 'success'}`}>
      <Trash2 size={16} /><span><strong>{operationResult.trashed} correo{operationResult.trashed === 1 ? '' : 's'} enviado{operationResult.trashed === 1 ? '' : 's'} a Papelera.</strong>{operationResult.failed > 0 ? ` ${operationResult.failed} no pudieron procesarse.` : ' La acción se completó correctamente.'}</span>
    </div>}

    {!preview.isLoading && items.length > 0 && <>
      <section className="nexi-action-summary">
        <div><Inbox size={17} /><span><strong>{items.length}</strong> correo{items.length === 1 ? '' : 's'} coincide{items.length === 1 ? '' : 'n'} con el criterio</span></div>
        <button type="button" className="nexi-trash-action-button" disabled={trashMutation.isPending} onClick={() => setConfirmOpen(true)}><Trash2 size={15} />Enviar {items.length} a Papelera</button>
      </section>
      {preview.data?.limited && <div className="nexi-action-limit"><AlertTriangle size={14} />La búsqueda supera {MAX_ACTION_RESULTS} resultados. Por seguridad, esta operación incluye sólo los primeros {MAX_ACTION_RESULTS}; puedes acotar el criterio para continuar.</div>}

      <section className="universal-result-section">
        <header><div><Mail size={17} /><strong>Correos que se procesarán</strong></div><span>{items.length}</span></header>
        <div className="universal-mail-results">
          {items.map(item => {
            const account = accounts.data?.find(value => value.id === item.accountId)
            return <button type="button" key={`${item.accountId}:${item.providerMessageId}`} className={`universal-mail-result ${item.isRead ? '' : 'unread'}`} onClick={() => openMessage(item)}>
              <i className="account-dot" style={{ background: account?.color }} />
              <span className="universal-result-primary"><strong>{item.senderName || item.senderAddress || 'Correo'}</strong><span>{item.subject}</span><small>{item.preview}</small></span>
              <span />
              <span className="universal-result-meta"><small>{account?.displayName}</small><time>{dateLabel(item.receivedAt)}</time></span>
            </button>
          })}
        </div>
      </section>
    </>}

    {!preview.isLoading && searchText.length > 0 && items.length === 0 && <div className="universal-no-results"><Search size={24} /><strong>No encontré correos para eliminar</strong><span>Nexi no ejecutó ninguna acción.</span></div>}

    <ConfirmDialog
      open={confirmOpen}
      title="Enviar correos a Papelera"
      message={`Se enviarán ${items.length} correo${items.length === 1 ? '' : 's'} encontrado${items.length === 1 ? '' : 's'} a Papelera. Podrás recuperarlos desde allí mientras no vacíes la Papelera.`}
      confirmLabel={`Enviar ${items.length} a Papelera`}
      pending={trashMutation.isPending}
      onConfirm={() => trashMutation.mutate()}
      onCancel={() => setConfirmOpen(false)}
    />
  </section>
}
