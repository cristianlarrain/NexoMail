import { useEffect, useMemo, useState } from 'react'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { useLocation, useParams, useNavigate, useSearchParams } from 'react-router-dom'
import { Archive, ChevronDown, ChevronUp, Clock3, EyeOff, MailOpen, MoreHorizontal, Paperclip, RefreshCw, ShieldAlert, Trash2, Undo2, X } from 'lucide-react'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { NexiEmptyState } from '../components/nexi/NexiEmptyState'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem, MailSummary, PagedResult } from '../types/mail'

type SortKey = 'sender' | 'subject' | 'date'
type SortDirection = 'asc' | 'desc'
type MoveTarget = 'inbox' | 'archive' | 'spam' | 'trash'
type ConfirmAction =
  | { kind: 'emptyTrash' }
  | { kind: 'trashSelected' }
  | { kind: 'trashOne'; item: MailSummary }

function dateLabel(value: string) {
  const d = new Date(value)
  const today = new Date()
  return d.toDateString() === today.toDateString()
    ? d.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' })
    : d.toLocaleDateString('es-CL', { day: '2-digit', month: 'short' })
}

function itemKey(item: MailSummary) { return `${item.accountId}:${item.providerMessageId}` }
function pendingKey(item: ControlCenterPendingItem) { return `${item.accountId}:${item.messageId}` }

export function InboxPage({ folder = 'inbox' }: { folder?: string }) {
  const { accountId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const search = params.get('q') ?? ''
  const priorityOnly = folder === 'inbox' && params.get('priority') === '1'
  const queryClient = useQueryClient()
  const [sortKey, setSortKey] = useState<SortKey>('date')
  const [sortDirection, setSortDirection] = useState<SortDirection>('desc')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirmation, setConfirmation] = useState<ConfirmAction | null>(null)
  const [openActionMenu, setOpenActionMenu] = useState<string | null>(null)
  const [priorityHidden, setPriorityHidden] = useState<Set<string>>(new Set())

  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const selectedAccount = accountId ? accounts.find(account => account.id === accountId) : undefined
  const microsoftAccountIds = useMemo(() => new Set(accounts.filter(account => account.provider === 'MicrosoftGraph').map(account => account.id)), [accounts])
  const isMicrosoftAccountView = selectedAccount?.provider === 'MicrosoftGraph'

  const prioritySnapshot = useQuery({
    queryKey: ['control-center', accountId],
    queryFn: () => mailApi.controlCenter(accountId),
    enabled: priorityOnly && !isMicrosoftAccountView,
    staleTime: 30_000,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
  })
  const trackedItemsQuery = useQuery({
    queryKey: ['control-center-tracking', accountId],
    queryFn: () => mailApi.controlCenterTrackedItems(accountId),
    enabled: priorityOnly && !isMicrosoftAccountView,
    staleTime: 15_000,
    refetchOnMount: 'always',
    refetchOnWindowFocus: false,
  })

  const messagesQuery = useInfiniteQuery({
    queryKey: ['messages', accountId, folder, search],
    queryFn: ({ pageParam }) => mailApi.messages(accountId, folder, search, pageParam || undefined),
    initialPageParam: '',
    getNextPageParam: lastPage => lastPage.nextCursor ?? undefined,
    enabled: !priorityOnly || isMicrosoftAccountView,
    staleTime: 5 * 60_000,
    gcTime: 30 * 60_000,
    refetchInterval: 10 * 60_000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: false,
    refetchOnMount: false,
  })

  function removeFromCurrentList(itemsToRemove: MailSummary[]) {
    const removed = new Set(itemsToRemove.map(itemKey))
    queryClient.setQueryData<InfiniteData<PagedResult<MailSummary>>>(['messages', accountId, folder, search], current => current ? {
      ...current,
      pages: current.pages.map(page => ({ ...page, items: page.items.filter(item => !removed.has(itemKey(item))) })),
    } : current)
    if (priorityOnly) setPriorityHidden(current => new Set([...current, ...removed]))
  }

  function removeSendersFromCurrentList(itemsToRemove: MailSummary[]) {
    const senders = new Set(itemsToRemove.map(item => `${item.accountId}:${item.senderAddress.trim().toLowerCase()}`))
    queryClient.setQueryData<InfiniteData<PagedResult<MailSummary>>>(['messages', accountId, folder, search], current => current ? {
      ...current,
      pages: current.pages.map(page => ({
        ...page,
        items: page.items.filter(item => !senders.has(`${item.accountId}:${item.senderAddress.trim().toLowerCase()}`)),
      })),
    } : current)
  }

  function markCurrentItemsRead(itemsToMark: MailSummary[]) {
    const marked = new Set(itemsToMark.map(itemKey))
    queryClient.setQueryData<InfiniteData<PagedResult<MailSummary>>>(['messages', accountId, folder, search], current => current ? {
      ...current,
      pages: current.pages.map(page => ({
        ...page,
        items: page.items.map(item => marked.has(itemKey(item)) ? { ...item, isRead: true } : item),
      })),
    } : current)
  }

  function refreshFolder(folderId: string) {
    void queryClient.invalidateQueries({
      predicate: query => query.queryKey[0] === 'messages' && query.queryKey[2] === folderId,
      refetchType: 'all',
    })
  }

  function refreshControlCenter() {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'all' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-tracking'], refetchType: 'all' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'all' })
  }

  const refreshMailbox = useMutation({
    mutationFn: mailApi.refreshMail,
    onSuccess: async () => {
      if (priorityOnly && !isMicrosoftAccountView) await Promise.all([prioritySnapshot.refetch(), trackedItemsQuery.refetch()])
      else await messagesQuery.refetch()
    },
  })

  const emptyTrash = useMutation({
    mutationFn: () => mailApi.emptyFolder('trash', accountId),
    onSuccess: () => {
      queryClient.setQueryData<InfiniteData<PagedResult<MailSummary>>>(['messages', accountId, folder, search], current => current ? {
        ...current,
        pages: current.pages.map(page => ({ ...page, items: [] })),
      } : current)
      setConfirmation(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      refreshControlCenter()
    },
  })

  const moveMessages = useMutation({
    mutationFn: async ({ items, target }: { items: MailSummary[]; target: MoveTarget }) => {
      await Promise.all(items.map(item => mailApi.move(item.accountId, item.providerMessageId, target)))
    },
    onSuccess: (_data, variables) => {
      removeFromCurrentList(variables.items)
      setConfirmation(null)
      setOpenActionMenu(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      refreshFolder(variables.target)
      refreshControlCenter()
    },
  })

  const ignoreSenders = useMutation({
    mutationFn: async (items: MailSummary[]) => {
      const unique = new Map<string, MailSummary>()
      items.forEach(item => unique.set(`${item.accountId}:${item.senderAddress.trim().toLowerCase()}`, item))
      await Promise.all([...unique.values()].map(item => mailApi.ignoreSender(item.accountId, item.senderAddress)))
    },
    onSuccess: (_data, items) => {
      if (folder === 'inbox') removeSendersFromCurrentList(items)
      setOpenActionMenu(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      refreshFolder('ignored')
      refreshControlCenter()
    },
  })

  const unignoreSenders = useMutation({
    mutationFn: async (items: MailSummary[]) => {
      const unique = new Map<string, MailSummary>()
      items.forEach(item => unique.set(`${item.accountId}:${item.senderAddress.trim().toLowerCase()}`, item))
      await Promise.all([...unique.values()].map(item => mailApi.unignoreSender(item.accountId, item.senderAddress)))
    },
    onSuccess: (_data, items) => {
      if (folder === 'ignored') removeSendersFromCurrentList(items)
      setOpenActionMenu(null)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      refreshFolder('inbox')
      refreshControlCenter()
    },
  })

  const markReadMessages = useMutation({
    mutationFn: async (items: MailSummary[]) => {
      await Promise.all(items.filter(item => !item.isRead).map(item => mailApi.read(item.accountId, item.providerMessageId, true)))
    },
    onSuccess: (_data, items) => {
      if (isUnreadView) removeFromCurrentList(items)
      else markCurrentItemsRead(items)
      setSelected(new Set())
      void queryClient.invalidateQueries({ queryKey: ['messages'], refetchType: 'none' })
      refreshControlCenter()
    },
  })

  useEffect(() => {
    setSelected(new Set())
    setConfirmation(null)
    setOpenActionMenu(null)
    setPriorityHidden(new Set())
  }, [accountId, folder, search, priorityOnly])

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

  const priorityReferences = useMemo(() => {
    if (!priorityOnly || isMicrosoftAccountView) return []
    const merged = new Map<string, ControlCenterPendingItem>()
    for (const item of prioritySnapshot.data?.pendingItems ?? []) {
      if (item.direction === 'received') merged.set(pendingKey(item), item)
    }
    for (const item of trackedItemsQuery.data ?? []) {
      if (item.direction === 'received') merged.set(pendingKey(item), item)
    }
    return [...merged.values()]
  }, [isMicrosoftAccountView, priorityOnly, prioritySnapshot.data, trackedItemsQuery.data])

  const priorityReferenceByKey = useMemo(() => new Map(priorityReferences.map(item => [pendingKey(item), item])), [priorityReferences])

  const displayItems = useMemo(() => {
    if (!priorityOnly || isMicrosoftAccountView) return items
    const loaded = new Map(items.map(item => [itemKey(item), item]))
    const normalizedSearch = search.trim().toLowerCase()
    return priorityReferences
      .filter(reference => !priorityHidden.has(pendingKey(reference)))
      .map(reference => loaded.get(pendingKey(reference)) ?? ({
        providerMessageId: reference.messageId,
        accountId: reference.accountId,
        senderName: reference.counterpart || 'Remitente',
        senderAddress: '',
        subject: reference.subject,
        preview: reference.conversationId.startsWith('manual:') ? 'Marcado manualmente para seguimiento' : 'Pendiente de respuesta',
        receivedAt: reference.since,
        isRead: reference.isRead,
        hasAttachments: false,
        folderId: 'inbox',
      } satisfies MailSummary))
      .filter(item => !normalizedSearch || normalizedSearch === 'is:unread' || `${item.senderName} ${item.senderAddress} ${item.subject} ${item.preview}`.toLowerCase().includes(normalizedSearch))
  }, [isMicrosoftAccountView, items, priorityHidden, priorityOnly, priorityReferences, search])

  const sortedItems = useMemo(() => [...displayItems].sort((left, right) => {
    let comparison = 0
    if (sortKey === 'sender') comparison = (left.senderName || left.senderAddress).localeCompare(right.senderName || right.senderAddress, 'es', { sensitivity: 'base' })
    else if (sortKey === 'subject') comparison = left.subject.localeCompare(right.subject, 'es', { sensitivity: 'base' })
    else comparison = new Date(left.receivedAt).getTime() - new Date(right.receivedAt).getTime()
    return sortDirection === 'asc' ? comparison : -comparison
  }), [displayItems, sortDirection, sortKey])

  const selectedItems = useMemo(() => displayItems.filter(item => selected.has(itemKey(item))), [displayItems, selected])
  const selectedUnreadItems = useMemo(() => selectedItems.filter(item => !item.isRead), [selectedItems])
  const selectedContainsMicrosoft = selectedItems.some(item => microsoftAccountIds.has(item.accountId))
  const allVisibleSelected = sortedItems.length > 0 && sortedItems.every(item => selected.has(itemKey(item)))
  const isUnreadView = !priorityOnly && search.trim().toLowerCase() === 'is:unread'
  const pageTitle = isUnreadView
    ? 'Correos sin leer'
    : search && !priorityOnly
      ? `Resultados para “${search}”`
      : folder === 'inbox'
        ? 'Bandeja de entrada'
        : folder === 'archive'
          ? 'Archivados'
          : folder === 'ignored'
            ? 'Ignorados'
            : folder === 'sent'
              ? 'Enviados'
              : folder === 'drafts'
                ? 'Borradores'
                : folder === 'spam'
                  ? 'Spam'
                  : 'Papelera'
  const baseContextLabel = accountId ? selectedAccount?.displayName ?? 'Cuenta seleccionada' : 'Todas las cuentas'
  const contextLabel = priorityOnly && !isMicrosoftAccountView ? `${baseContextLabel} · Seguimiento prioritario` : baseContextLabel
  const navigationItems = sortedItems.map(item => ({ accountId: item.accountId, messageId: item.providerMessageId }))
  const returnTo = `${location.pathname}${location.search}`
  const priorityLoading = priorityOnly && !isMicrosoftAccountView && (prioritySnapshot.isLoading || trackedItemsQuery.isLoading)
  const priorityError = priorityOnly && !isMicrosoftAccountView && (prioritySnapshot.isError || trackedItemsQuery.isError)
  const listLoading = priorityOnly && !isMicrosoftAccountView ? priorityLoading : messagesQuery.isLoading
  const listError = priorityOnly && !isMicrosoftAccountView ? priorityError : messagesQuery.isError
  const confirmDetails = confirmation?.kind === 'emptyTrash'
    ? { title: 'Vaciar Papelera', message: 'Esta acción eliminará permanentemente todos los correos de la Papelera.', label: 'Vaciar Papelera', tone: 'danger' as const }
    : folder === 'drafts'
      ? confirmation?.kind === 'trashOne'
        ? { title: 'Descartar borrador', message: 'Este borrador se eliminará definitivamente. Esta acción no se puede deshacer.', label: 'Descartar borrador', tone: 'danger' as const }
        : { title: 'Descartar borradores', message: `Se eliminarán definitivamente ${selectedItems.length} borrador${selectedItems.length === 1 ? '' : 'es'}. Esta acción no se puede deshacer.`, label: 'Descartar borradores', tone: 'danger' as const }
      : confirmation?.kind === 'trashOne'
        ? { title: 'Mover correo a Papelera', message: 'El correo dejará de aparecer en esta bandeja y podrá restaurarse desde Papelera.', label: 'Mover a Papelera', tone: 'danger' as const }
        : { title: 'Mover correos a Papelera', message: `Se moverán ${selectedItems.length} correo${selectedItems.length === 1 ? '' : 's'} a Papelera.`, label: 'Mover a Papelera', tone: 'danger' as const }

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

  function togglePriorityFilter() {
    const next = new URLSearchParams(params)
    if (priorityOnly) next.delete('priority')
    else {
      next.set('priority', '1')
      next.delete('q')
    }
    setParams(next)
  }

  function confirmCurrentAction() {
    if (!confirmation) return
    if (confirmation.kind === 'emptyTrash') emptyTrash.mutate()
    else if (confirmation.kind === 'trashOne') moveMessages.mutate({ items: [confirmation.item], target: 'trash' })
    else if (selectedItems.length) moveMessages.mutate({ items: selectedItems, target: 'trash' })
  }

  function refreshAll() {
    if (!refreshMailbox.isPending) refreshMailbox.mutate()
  }

  function clearSearch() {
    const next = new URLSearchParams(params)
    next.delete('q')
    setParams(next)
  }

  const actionPending = moveMessages.isPending || ignoreSenders.isPending || unignoreSenders.isPending || markReadMessages.isPending
  const confirmationPending = confirmation?.kind === 'emptyTrash' ? emptyTrash.isPending : moveMessages.isPending
  const moveSuccess = moveMessages.isSuccess ? moveMessages.variables : null
  const emptyTitle = priorityOnly && !isMicrosoftAccountView
    ? 'Todo al día en seguimiento'
    : isUnreadView
      ? 'No quedan correos sin leer'
      : search
        ? 'No encontré correos'
        : folder === 'inbox'
          ? 'Bandeja al día'
          : folder === 'drafts'
            ? 'No hay borradores'
            : folder === 'ignored'
              ? 'No hay remitentes ignorados'
              : folder === 'archive'
                ? 'No hay correos archivados'
                : folder === 'sent'
                  ? 'No hay correos enviados'
                  : folder === 'spam'
                    ? 'No hay correo en Spam'
                    : 'La Papelera está vacía'
  const emptyDescription = priorityOnly && !isMicrosoftAccountView
    ? 'Nexi no encontró conversaciones pendientes de respuesta ni correos marcados manualmente para seguimiento.'
    : isUnreadView
      ? 'Nexi no encontró mensajes pendientes de lectura en esta vista.'
      : search
        ? `Nexi no encontró mensajes que coincidan con “${search}”.`
        : folder === 'inbox'
          ? 'No hay mensajes en esta bandeja en este momento.'
          : folder === 'ignored'
            ? 'Los remitentes que decida ignorar aparecerán aquí sin eliminar sus correos.'
            : 'Los mensajes de esta carpeta aparecerán aquí cuando estén disponibles.'
  const emptyAction = priorityOnly && !isMicrosoftAccountView
    ? <button type="button" className="secondary-button" onClick={togglePriorityFilter}>Volver a Bandeja</button>
    : search
      ? <button type="button" className="secondary-button" onClick={clearSearch}>Limpiar búsqueda</button>
      : undefined

  return <section className="mail-view">
    <div className="view-header"><div><h1>{pageTitle}</h1><p className="view-context">{contextLabel}</p></div><div className="view-actions">{folder === 'inbox' && !isMicrosoftAccountView && <button type="button" className={`secondary-button priority-filter-button ${priorityOnly ? 'active' : ''}`} onClick={togglePriorityFilter} aria-pressed={priorityOnly} title="Mostrar sólo los correos que requieren seguimiento"><Clock3 size={16} /><span>Seguimiento prioritario</span>{priorityOnly && <span className="priority-count">{displayItems.length}</span>}</button>}{folder === 'trash' && !isMicrosoftAccountView && <button className="secondary-button danger-button" disabled={emptyTrash.isPending} onClick={() => setConfirmation({ kind: 'emptyTrash' })}><Trash2 size={16} /> {emptyTrash.isPending ? 'Vaciando…' : 'Vaciar Papelera'}</button>}<button className="icon-button" disabled={refreshMailbox.isPending} onClick={refreshAll} aria-label="Actualizar mensajes" title="Actualizar"><RefreshCw size={18} className={refreshMailbox.isPending ? 'spin' : ''} /></button></div></div>

    {isMicrosoftAccountView && <div className="notice">Microsoft 365 está en Phase 1: puedes leer, abrir y marcar correos como leídos o no leídos. Las acciones de mover, eliminar, ignorar y seguimiento aún no están disponibles.</div>}
    {(location.state as { sent?: boolean; trashed?: boolean } | null)?.sent && <div className="success-notice">Correo enviado correctamente.</div>}
    {(location.state as { trashed?: boolean } | null)?.trashed && <div className="success-notice">Correo movido a Papelera.</div>}
    {folder === 'drafts' && moveSuccess?.target === 'trash' && <div className="success-notice">{moveSuccess.items.length === 1 ? 'Borrador descartado correctamente.' : `${moveSuccess.items.length} borradores descartados correctamente.`}</div>}
    {folder !== 'drafts' && moveSuccess?.target === 'archive' && <div className="success-notice">{moveSuccess.items.length === 1 ? 'Correo archivado correctamente.' : `${moveSuccess.items.length} correos archivados correctamente.`}</div>}
    {folder !== 'drafts' && moveSuccess?.target === 'inbox' && <div className="success-notice">{moveSuccess.items.length === 1 ? 'Correo restaurado a Bandeja de entrada.' : `${moveSuccess.items.length} correos restaurados a Bandeja de entrada.`}</div>}
    {folder !== 'drafts' && moveSuccess?.target === 'spam' && <div className="success-notice">{moveSuccess.items.length === 1 ? 'Correo marcado como spam.' : `${moveSuccess.items.length} correos marcados como spam.`}</div>}
    {folder !== 'drafts' && moveSuccess?.target === 'trash' && <div className="success-notice">{moveSuccess.items.length === 1 ? 'Correo movido a Papelera.' : `${moveSuccess.items.length} correos movidos a Papelera.`}</div>}
    {ignoreSenders.isSuccess && <div className="success-notice">Remitente ignorado. Sus correos permanecen disponibles en Ignorados.</div>}
    {unignoreSenders.isSuccess && <div className="success-notice">El remitente volvió a la Bandeja de entrada.</div>}
    {emptyTrash.isSuccess && <div className="success-notice">Papelera vaciada permanentemente.</div>}
    {emptyTrash.isError && <div className="notice">{emptyTrash.error instanceof Error ? emptyTrash.error.message : 'No se pudo vaciar la Papelera. Reintenta.'}</div>}
    {moveMessages.isError && <div className="notice">{moveMessages.error instanceof Error ? moveMessages.error.message : folder === 'drafts' ? 'No se pudo descartar el borrador.' : 'No se pudieron mover los correos seleccionados.'}</div>}
    {ignoreSenders.isError && <div className="notice">{ignoreSenders.error instanceof Error ? ignoreSenders.error.message : 'No se pudo ignorar el remitente.'}</div>}
    {unignoreSenders.isError && <div className="notice">{unignoreSenders.error instanceof Error ? unignoreSenders.error.message : 'No se pudo restaurar el remitente.'}</div>}
    {markReadMessages.isError && <div className="notice">{markReadMessages.error instanceof Error ? markReadMessages.error.message : 'No se pudieron marcar los correos como leídos.'}</div>}
    {refreshMailbox.isError && <div className="notice">No fue posible actualizar la bandeja. Los datos disponibles siguen visibles y puede reintentar.</div>}

    {selected.size > 0 && <div className="bulk-actions"><strong>{selected.size} seleccionado{selected.size === 1 ? '' : 's'}</strong>{folder === 'drafts' ? !selectedContainsMicrosoft && <button className="secondary-button danger-button" onClick={() => setConfirmation({ kind: 'trashSelected' })} disabled={actionPending}><Trash2 size={15} /> Descartar borrador{selectedItems.length === 1 ? '' : 'es'}</button> : <>{selectedUnreadItems.length > 0 && <button className="secondary-button" onClick={() => markReadMessages.mutate(selectedUnreadItems)} disabled={actionPending}><MailOpen size={15} /> Marcar como leído{selectedUnreadItems.length === 1 ? '' : 's'}</button>}{!selectedContainsMicrosoft && <>{folder !== 'archive' && folder !== 'trash' && <button className="secondary-button" onClick={() => moveMessages.mutate({ items: selectedItems, target: 'archive' })} disabled={actionPending}><Archive size={15} /> Archivar</button>}{!priorityOnly && (folder === 'ignored' ? <button className="secondary-button" onClick={() => unignoreSenders.mutate(selectedItems)} disabled={actionPending}><Undo2 size={15} /> Dejar de ignorar</button> : folder !== 'trash' && <button className="secondary-button" onClick={() => ignoreSenders.mutate(selectedItems)} disabled={actionPending}><EyeOff size={15} /> Ignorar remitente</button>)}{folder === 'archive' || folder === 'spam' || folder === 'trash' ? <button className="secondary-button" onClick={() => moveMessages.mutate({ items: selectedItems, target: 'inbox' })} disabled={actionPending}><Undo2 size={15} /> Restaurar a Bandeja</button> : null}{folder !== 'trash' && <button className="secondary-button" onClick={() => setConfirmation({ kind: 'trashSelected' })} disabled={actionPending}><Trash2 size={15} /> Mover a Papelera</button>}</>}</>}<button className="icon-button" onClick={() => setSelected(new Set())} aria-label="Cancelar selección"><X size={17} /></button></div>}

    {isUnreadView && selected.size === 0 && displayItems.length > 0 && <div className="unread-management-hint"><MailOpen size={16} /><span>Seleccione varios correos o use el checkbox superior para marcarlos como leídos en una sola acción.</span></div>}

    {listLoading && <section className="inbox-mail-loading" aria-label="Cargando correos"><div className="inbox-loading-heading"><strong>{priorityOnly && !isMicrosoftAccountView ? 'Recuperando seguimiento' : 'Cargando correos'}</strong><span>{priorityOnly && !isMicrosoftAccountView ? 'Consultando pendientes y seguimientos manuales.' : 'Actualizando la bandeja.'}</span></div><MailSkeleton /></section>}
    {listError && <div className="notice">{priorityOnly && !isMicrosoftAccountView ? 'No fue posible recuperar el seguimiento prioritario.' : 'No se pudo actualizar una de sus cuentas.'} <button onClick={() => priorityOnly && !isMicrosoftAccountView ? void Promise.all([prioritySnapshot.refetch(), trackedItemsQuery.refetch()]) : void messagesQuery.refetch()}>Reintentar</button></div>}
    {!listLoading && !listError && displayItems.length === 0 && <NexiEmptyState title={emptyTitle} description={emptyDescription} action={emptyAction} />}

    {displayItems.length > 0 && <div className="message-list" aria-label="Lista de mensajes">
      <div className="message-list-header">
        <label className="row-check" title="Seleccionar todos los mensajes visibles"><input type="checkbox" checked={allVisibleSelected} onChange={toggleAllVisible} /></label>
        <span aria-hidden="true" />
        <SortButton label={priorityOnly && !isMicrosoftAccountView ? 'Contacto' : 'Remitente'} column="sender" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <SortButton label="Asunto" column="subject" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <span aria-hidden="true" />
        <SortButton label="Fecha" column="date" active={sortKey} direction={sortDirection} onSort={changeSort} />
        <span aria-label="Acciones" />
      </div>
      {sortedItems.map(item => {
        const account = accounts.find(a => a.id === item.accountId)
        const isMicrosoftGraph = account?.provider === 'MicrosoftGraph'
        const key = itemKey(item)
        const priorityReference = priorityReferenceByKey.get(key)
        const openMessage = () => navigate(`/message/${item.accountId}/${item.providerMessageId}`, { state: { navigationItems, returnTo, ...(priorityReference ? { controlCenterItem: priorityReference, manualTracking: priorityReference.conversationId.startsWith('manual:') } : {}) } })
        const primaryAction = isMicrosoftGraph
          ? null
          : folder === 'drafts'
            ? { label: 'Descartar borrador', icon: <Trash2 size={15} />, action: () => setConfirmation({ kind: 'trashOne', item }) }
            : folder === 'trash' || folder === 'archive' || folder === 'spam'
              ? { label: 'Restaurar a Bandeja', icon: <Undo2 size={15} />, action: () => moveMessages.mutate({ items: [item], target: 'inbox' as const }) }
              : folder === 'ignored'
                ? { label: 'Dejar de ignorar', icon: <Undo2 size={15} />, action: () => unignoreSenders.mutate([item]) }
                : { label: 'Archivar', icon: <Archive size={15} />, action: () => moveMessages.mutate({ items: [item], target: 'archive' as const }) }
        return <div key={key} className={`message-row ${item.isRead ? '' : 'unread'} ${selected.has(key) ? 'selected' : ''}`} role="button" tabIndex={0} onClick={openMessage} onKeyDown={event => { if (event.key === 'Enter' && event.target === event.currentTarget) openMessage() }}>
          <label className="row-check" onClick={event => event.stopPropagation()}><input type="checkbox" checked={selected.has(key)} onChange={() => toggleSelected(item)} aria-label={`Seleccionar ${item.subject}`} /></label>
          <i className="account-dot" style={{ background: account?.color }} />
          <span className="sender">{item.senderName}</span>
          <span className="subject"><strong>{item.subject}</strong><span> — {item.preview}</span></span>
          <span className="attachment-slot">{item.hasAttachments && <Paperclip size={15} className="attachment-icon" />}</span>
          <time>{dateLabel(item.receivedAt)}</time>
          <div className="row-mail-actions" onClick={event => event.stopPropagation()}>
            {primaryAction && <button className="row-mail-action" type="button" title={primaryAction.label} aria-label={`${primaryAction.label}: ${item.subject}`} disabled={actionPending} onClick={primaryAction.action}>{primaryAction.icon}</button>}
            {!isMicrosoftGraph && folder !== 'trash' && folder !== 'drafts' && <div className="row-more-wrap">
              <button className="row-mail-action" type="button" title="Más acciones" aria-label={`Más acciones para ${item.subject}`} aria-expanded={openActionMenu === key} onClick={() => setOpenActionMenu(current => current === key ? null : key)}><MoreHorizontal size={16} /></button>
              {openActionMenu === key && <div className="row-action-menu" role="menu">
                {folder !== 'archive' && <button type="button" onClick={() => moveMessages.mutate({ items: [item], target: 'archive' })}><Archive size={14} /> Archivar</button>}
                {!priorityOnly && (folder === 'ignored' ? <button type="button" onClick={() => unignoreSenders.mutate([item])}><Undo2 size={14} /> Dejar de ignorar</button> : <button type="button" onClick={() => ignoreSenders.mutate([item])}><EyeOff size={14} /> Ignorar remitente</button>)}
                {folder !== 'spam' && <button type="button" onClick={() => moveMessages.mutate({ items: [item], target: 'spam' })}><ShieldAlert size={14} /> Marcar como spam</button>}
                <button type="button" className="danger" onClick={() => { setOpenActionMenu(null); setConfirmation({ kind: 'trashOne', item }) }}><Trash2 size={14} /> Mover a Papelera</button>
              </div>}
            </div>}
          </div>
        </div>
      })}
    </div>}

    {displayItems.length > 0 && <div className="message-pagination"><span>{displayItems.length} correo{displayItems.length === 1 ? '' : 's'} {priorityOnly && !isMicrosoftAccountView ? 'en seguimiento' : `cargado${displayItems.length === 1 ? '' : 's'}`}</span>{(!priorityOnly || isMicrosoftAccountView) && messagesQuery.hasNextPage && <button className="secondary-button" disabled={messagesQuery.isFetchingNextPage} onClick={() => messagesQuery.fetchNextPage()}>{messagesQuery.isFetchingNextPage ? 'Cargando…' : 'Cargar más correos'}</button>}</div>}

    <ConfirmDialog open={Boolean(confirmation)} title={confirmDetails.title} message={confirmDetails.message} confirmLabel={confirmDetails.label} tone={confirmDetails.tone} pending={confirmationPending} onCancel={() => setConfirmation(null)} onConfirm={confirmCurrentAction} />
  </section>
}

function SortButton({ label, column, active, direction, onSort }: { label: string; column: SortKey; active: SortKey; direction: SortDirection; onSort: (key: SortKey) => void }) {
  const isActive = active === column
  return <button type="button" className={`sort-button ${isActive ? 'active' : ''}`} onClick={() => onSort(column)}>{label}{isActive ? direction === 'asc' ? <ChevronUp size={14} /> : <ChevronDown size={14} /> : null}</button>
}

function MailSkeleton() { return <div className="message-list inbox-skeleton-list">{Array.from({ length: 5 }, (_, i) => <div className="skeleton-row" key={i}><span /><span /><span /></div>)}</div> }
