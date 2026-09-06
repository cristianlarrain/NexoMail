import { useEffect, useMemo, useState } from 'react'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useParams, useNavigate, useSearchParams } from 'react-router-dom'
import { Archive, ChevronDown, ChevronUp, MailOpen, Paperclip, RefreshCw, Trash2, X } from 'lucide-react'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import type { MailSummary } from '../types/mail'

type SortKey = 'sender' | 'subject' | 'date'
type SortDirection = 'asc' | 'desc'
type ConfirmAction =
  | { kind: 'emptyTrash' }
  | { kind: 'moveSelected'; target: 'inbox' | 'trash' }
  | { kind: 'moveOne'; item: MailSummary }

function dateLabel(value: string) {
  const d = new Date(value)
  const today = new Date()
  return d.toDateString() === today.toDateString()
    ? d.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('es-CL', { day: '2-digit', month: 'short' })
}

function itemKey(item: MailSummary) { return `${item.accountId}:${item.providerMessageId}` }

export function InboxPage({ folder = 'inbox' }: { folder?: string }) {
  const { accountId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const [params] = useSearchParams()
  const search = params.get('q') ?? ''
  const queryClient = useQueryClient()
  const [sortKey, setSortKey] = useState<SortKey>('date')
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirmation, setConfirmation] = useState<ConfirmAction | null>(null)

  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const selectedAccount = accountId ? accounts.find(account => account.id === accountId) : undefined
  const messagesQuery = useInfiniteQuery({
    queryKey: ['messages', accountId, folder, search],
    queryFn: ({ pageParam }) => mailApi.messages(accountId, folder, search, pageParam || undefined),
    initialPageParam: '',
    getNextPageParam: lastPage => lastPage.nextCursor ?? undefined,
    staleTime: 5 * 60_000,
    gcTime: 30 * 60_000,
    refetchInterval: 10 * 60_000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  })

  const refreshMailbox = useMutation({
    mutationFn: mailApi.refreshMail,
    onSuccess: async () => { await messagesQuery.refetch() },
  })

  const emptyTrash = useMutation({
    mutationFn: () => mailApi.emptyFolder('trash', accountId),
    onSuccess: () => { setConfirmation(null); queryClient.invalidateQueries({ queryKey: ['messages'] }); void messagesQuery.refetch() },
  })

  const moveMessages = useMutation({
    mutationFn: async ({ items, target }: { items: MailSummary[]; target: 'inbox' | 'trash' }) => {
      await Promise.all(items.map(item => mailApi.move(item.accountId, item.providerMessageId, target)))
    },
    onSuccess: () => {
      setConfirmation(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
    },
  })

  const markReadMessages = useMutation({
    mutationFn: async (items: MailSummary[]) => {
      await Promise.all(items.filter(item => !item.isRead).map(item => mailApi.read(item.accountId, item.providerMessageId, true)))
    },
    onSuccess: () => {
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
      void messagesQuery.refetch()
    },
  })

  useEffect(() => { setSelected(new Set()); setConfirmation(null) }, [accountId, folder, search])

  const items = useMemo(() => {
    const seen = new Set<string>()
    const result: MailSummary[] = []
    for (const page of messagesQuery.data?.pages ?? []) {
      for (const item of page.items) {
        const key = itemKey(item)
        if (seen.has(key)) continue
        seen.add(key)
        result.push(item)
      }
    }
    return result
  }, [messagesQuery.data])

  const sortedItems = useMemo(() => [...items].sort((left, right) => {
    let comparison = 0
    if (sortKey === 'sender') comparison = (left.senderName || left.senderAddress).localeCompare(right.senderName || right.senderAddress, 'es', { sensitivity: 'base' })
    else if (sortKey === 'subject') comparison = left.subject.localeCompare(right.subject, 'es', { sensitivity: 'base' })
    else comparison = new Date(left.receivedAt).getTime() - new Date(right.receivedAt).getTime()
    return sortDirection === 'asc' ? comparison : -comparison
  }), [items, sortDirection, sortKey])

  const selectedItems = useMemo(() => items.filter(item => selected.has(itemKey(item))), [items, selected])
  const selectedUnreadItems = useMemo(() => selectedItems.filter(item => !item.isRead), [selectedItems])
  const allVisibleSelected = sortedItems.length > 0 && sortedItems.every(item => selected.has(itemKey(item)))
  const isUnreadView = search.trim().toLowerCase() === 'is:unread'
  const pageTitle = isUnreadView
    ? 'Correos sin leer'
    : search
      ? `Resultados para “${search}”`
      : folder === 'inbox'
        ? 'Bandeja de entrada'
        : folder === 'sent'
          ? 'Enviados'
          : folder === 'drafts'
            ? 'Borradores'
            : 'Papelera'
  const contextLabel = accountId ? selectedAccount?.displayName ?? 'Cuenta seleccionada' : 'Todas las cuentas'
  const navigationItems = sortedItems.map(item => ({ accountId: item.accountId, messageId: item.providerMessageId }))
  const returnTo = `${location.pathname}${location.search}`
  const confirmDetails = confirmation?.kind === 'emptyTrash'
    ? { title: 'Vaciar Papelera', message: 'Esta acción intenta eliminar permanentemente todos los correos de la Papelera.', label: 'Vaciar Papelera', tone: 'danger' as const }
    : confirmation?.kind === 'moveOne'
      ? { title: 'Mover correo a Papelera', message: 'El correo dejará de aparecer en esta bandeja y podrá restaurarse desde Papelera.', label: 'Mover a Papelera', tone: 'danger' as const }
      : confirmation?.kind === 'moveSelected' && confirmation.target === 'trash'
        ? { title: 'Mover correos a Papelera', message: `Se moverán ${selectedItems.length} correo${selectedItems.length === 1 ? '' : 's'} a Papelera.`, label: 'Mover a Papelera', tone: 'danger' as const }
        : { title: 'Restaurar correos', message: `Se restaurarán ${selectedItems.length} correo${selectedItems.length === 1 ? '' : 's'} a la Bandeja de entrada.`, label: 'Restaurar', tone: 'default' as const }

  function changeSort(key: SortKey) {
    if (sortKey === key) setSortDirection(current => current === 'asc' ? 'desc' : 'asc')
    else { setSortKey(key); setSortDirection(key === 'date' ? 'desc' : 'asc') }
  }

  function toggleSelected(item: MailSummary) {
    const key = itemKey(item)
    setSelected(current => {
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })
  }

  function toggleAllVisible() {
    setSelected(current => {
      const next = new Set(current)
      if (allVisibleSelected) sortedItems.forEach(item => next.delete(itemKey(item)))
      else sortedItems.forEach(item => next.add(itemKey(item)))
      return next
    })
  }

  function moveSelected(target: 'inbox' | 'trash') {
    if (!selectedItems.length) return
    setConfirmation({ kind: 'moveSelected', target })
  }

  function confirmCurrentAction() {
    if (!confirmation) return
    if (confirmation.kind === 'emptyTrash') emptyTrash.mutate()
    else if (confirmation.kind === 'moveOne') moveMessages.mutate({ items: [confirmation.item], target: 'trash' })
    else if (selectedItems.length) moveMessages.mutate({ items: selectedItems, target: confirmation.target })
  }

  function refreshAll() {
    if (!refreshMailbox.isPending) refreshMailbox.mutate()
  }

  const confirmationPending = confirmation?.kind === 'emptyTrash' ? emptyTrash.isPending : moveMessages.isPending

  return <section className="mail-view">
    <div className="view-header"><div><h1>{pageTitle}</h1><p className="view-context">{contextLabel}</p></div><div className="view-actions">{folder === 'trash' && <button className="secondary-button danger-button" disabled={emptyTrash.isPending} onClick={() => setConfirmation({ kind: 'emptyTrash' })}><Trash2 size={16} /> {emptyTrash.isPending ? 'Vaciando…' : 'Vaciar papelera'}</button>}<button className="icon-button" disabled={refreshMailbox.isPending} onClick={refreshAll} aria-label="Actualizar mensajes" title="Actualizar"><RefreshCw size={18} className={refreshMailbox.isPending ? 'spin' : ''} /></button></div></div>

    {(location.state as { sent?: boolean; trashed?: boolean } | null)?.sent && <div className="success-notice">Correo enviado correctamente.</div>}
    {(location.state as { trashed?: boolean } | null)?.trashed && <div className="success-notice">Correo movido a Papelera.</div>}
    {emptyTrash.isSuccess && <div className="success-notice">Papelera vaciada permanentemente.</div>}
    {emptyTrash.isError && <div className="notice">{emptyTrash.error instanceof Error ? emptyTrash.error.message : 'No se pudo vaciar la Papelera. Reintenta.'}</div>}
    {moveMessages.isError && <div className="notice">{moveMessages.error instanceof Error ? moveMessages.error.message : 'No se pudieron mover los correos seleccionados.'}</div>}
    {markReadMessages.isError && <div className="notice">{markReadMessages.error instanceof Error ? markReadMessages.error.message : 'No se pudieron marcar los correos como leídos.'}</div>}
    {refreshMailbox.isError && <div className="notice">No fue posible actualizar la bandeja. Los datos almacenados siguen disponibles y puede reintentar.</div>}

    {selected.size > 0 && <div className="bulk-actions"><strong>{selected.size} seleccionado{selected.size === 1 ? '' : 's'}</strong>{selectedUnreadItems.length > 0 && <button className="secondary-button" onClick={() => markReadMessages.mutate(selectedUnreadItems)} disabled={markReadMessages.isPending || moveMessages.isPending}><MailOpen size={15} /> {markReadMessages.isPending ? 'Marcando…' : `Marcar como leído${selectedUnreadItems.length === 1 ? '' : 's'}`}</button>}<button className="secondary-button" onClick={() => moveSelected(folder === 'trash' ? 'inbox' : 'trash')} disabled={moveMessages.isPending || markReadMessages.isPending}>{folder === 'trash' ? 'Restaurar a Bandeja' : <><Trash2 size={15} /> Mover a Papelera</>}</button><button className="icon-button" onClick={() => setSelected(new Set())} aria-label="Cancelar selección"><X size={17} /></button></div>}

    {isUnreadView && selected.size === 0 && items.length > 0 && <div className="unread-management-hint"><MailOpen size={16} /><span>Seleccione varios correos o use el checkbox superior para marcarlos como leídos en una sola acción.</span></div>}

    {messagesQuery.isLoading && <section className="inbox-mail-loading" aria-label="Cargando correos"><div className="inbox-loading-heading"><strong>Cargando correos</strong><span>Actualizando la bandeja.</span></div><MailSkeleton /></section>}
    {messagesQuery.isError && <div className="notice">No se pudo actualizar una de sus cuentas. <button onClick={() => messagesQuery.refetch()}>Reintentar</button></div>}
    {!messagesQuery.isLoading && items.length === 0 && <div className="empty-state"><Archive size={28} /><h2>No hay mensajes aquí</h2><p>{isUnreadView ? 'No quedan correos sin leer en esta vista.' : 'Los mensajes de esta carpeta aparecerán en este espacio.'}</p></div>}

    {items.length > 0 && <div className="message-list" aria-label="Lista de mensajes">
      <div className="message-list-header">
        <label className="row-check" title="Seleccionar todos los mensajes visibles"><input type="checkbox" checked={allVisibleSelected} onChange={toggleAllVisible} /></label>
        <span aria-hidden="true" />
        <SortButton label="Remitente" column="sender" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <SortButton label="Asunto" column="subject" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <span aria-hidden="true" />
        <SortButton label="Fecha" column="date" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <span aria-label="Acciones" />
      </div>
      {sortedItems.map(item => {
        const account = accounts.find(a => a.id === item.accountId)
        const key = itemKey(item)
        const openMessage = () => navigate(`/message/${item.accountId}/${item.providerMessageId}`, { state: { navigationItems, returnTo } })
        return <div key={key} className={`message-row ${item.isRead ? '' : 'unread'} ${selected.has(key) ? 'selected' : ''}`} role="button" tabIndex={0} onClick={openMessage} onKeyDown={event => { if (event.key === 'Enter' && event.target === event.currentTarget) openMessage() }}>
          <label className="row-check" onClick={event => event.stopPropagation()}><input type="checkbox" checked={selected.has(key)} onChange={() => toggleSelected(item)} aria-label={`Seleccionar ${item.subject}`} /></label>
          <i className="account-dot" style={{ background: account?.color }} />
          <span className="sender">{item.senderName}</span>
          <span className="subject"><strong>{item.subject}</strong><span> — {item.preview}</span></span>
          <span className="attachment-slot">{item.hasAttachments && <Paperclip size={15} className="attachment-icon" />}</span>
          <time>{dateLabel(item.receivedAt)}</time>
          {folder !== 'trash' ? <button className="row-delete" title="Mover a Papelera" aria-label={`Mover ${item.subject} a Papelera`} disabled={moveMessages.isPending} onClick={event => { event.stopPropagation(); setConfirmation({ kind: 'moveOne', item }) }}><Trash2 size={15} /></button> : <button className="row-restore" title="Restaurar a Bandeja" aria-label={`Restaurar ${item.subject} a Bandeja`} disabled={moveMessages.isPending} onClick={event => { event.stopPropagation(); moveMessages.mutate({ items: [item], target: 'inbox' }) }}>↩</button>}
        </div>
      })}
    </div>}

    {items.length > 0 && <div className="message-pagination"><span>{items.length} correo{items.length === 1 ? '' : 's'} cargado{items.length === 1 ? '' : 's'}</span>{messagesQuery.hasNextPage && <button className="secondary-button" disabled={messagesQuery.isFetchingNextPage} onClick={() => messagesQuery.fetchNextPage()}>{messagesQuery.isFetchingNextPage ? 'Cargando…' : 'Cargar más correos'}</button>}</div>}

    <ConfirmDialog open={Boolean(confirmation)} title={confirmDetails.title} message={confirmDetails.message} confirmLabel={confirmDetails.label} tone={confirmDetails.tone} pending={confirmationPending} onCancel={() => setConfirmation(null)} onConfirm={confirmCurrentAction} />
  </section>
}

function SortButton({ label, column, active, direction, onSort }: { label: string; column: SortKey; active: SortKey; direction: SortDirection; onSort: (key: SortKey) => void }) {
  const isActive = active === column
  return <button type="button" className={`sort-button ${isActive ? 'active' : ''}`} onClick={() => onSort(column)}>{label}{isActive ? direction === 'asc' ? <ChevronUp size={14} /> : <ChevronDown size={14} /> : null}</button>
}

function MailSkeleton() { return <div className="message-list inbox-skeleton-list">{Array.from({ length: 5 }, (_, i) => <div className="skeleton-row" key={i}><span /><span /><span /></div>)}</div> }
