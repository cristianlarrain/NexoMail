import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarDays, Download, FileArchive, FileSpreadsheet, FileText, FileType2, Mail, Search, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { DocumentIndexItem, MailAttachment } from '../types/mail'

const PAGE_SIZE = 50

function fileIcon(type: string) {
  if (type === 'Planilla') return <FileSpreadsheet size={18} />
  if (type === 'Comprimido') return <FileArchive size={18} />
  if (type === 'PDF' || type === 'Documento') return <FileText size={18} />
  return <FileType2 size={18} />
}

function dateLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' }).replace(/\./g, '')
}

function attachmentFrom(item: DocumentIndexItem): MailAttachment {
  return { id: item.attachmentId, name: item.fileName, contentType: item.contentType, size: item.size }
}

function indexedLabel(value?: string | null) {
  if (!value) return 'Índice pendiente'
  return `Actualizado ${new Date(value).toLocaleString('es-CL', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).replace(/\./g, '')}`
}

export function ControlCenterDocuments() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const initialSyncAttempted = useRef(false)
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('all')
  const [page, setPage] = useState(0)

  useEffect(() => { setPage(0) }, [search, typeFilter])

  const query = useQuery({
    queryKey: ['control-center-documents-index', search, typeFilter, page],
    queryFn: () => mailApi.controlCenterDocuments(search, typeFilter, PAGE_SIZE, page * PAGE_SIZE),
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  })

  const sync = useMutation({
    mutationFn: (limitPerAccount: number) => mailApi.syncMetadataIndex(90, limitPerAccount),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['control-center-documents-index'] }),
        queryClient.invalidateQueries({ queryKey: ['control-center-contacts-index'] }),
      ])
    },
  })

  useEffect(() => {
    if (!query.data || query.data.indexedMessages > 0 || initialSyncAttempted.current || sync.isPending) return
    initialSyncAttempted.current = true
    sync.mutate(25)
  }, [query.data, sync])

  const types = useMemo(() => ['all', 'PDF', 'Documento', 'Planilla', 'Presentación', 'Comprimido', 'Texto / datos'], [])

  if (query.isLoading) return <section className="documents-control documents-start"><FileText size={30} /><h2>Documentos recibidos</h2><p>Cargando registro local de documentos…</p></section>
  if (query.isError || !query.data) return <section className="documents-control"><div className="notice">No fue posible leer el índice documental. <button type="button" className="auth-link" onClick={() => query.refetch()}>Reintentar</button></div></section>

  const data = query.data
  const firstIndex = data.indexedMessages === 0

  return <section className="documents-control" aria-label="Documentos recibidos">
    <header className="documents-header">
      <div><p className="eyebrow">Registro documental</p><h2>Documentos recibidos</h2><p>Listado generado desde metadatos locales. Los documentos siguen almacenados en Gmail y solo se descargan cuando usted los abre.</p></div>
      <strong>{data.total} documento{data.total === 1 ? '' : 's'}</strong>
    </header>

    {firstIndex && sync.isPending && <div className="notice documents-error"><span className="index-loading-dot" aria-hidden="true" />Creando índice inicial. Se están leyendo solo metadatos recientes; después esta vista abrirá desde SQLite.</div>}
    {sync.isError && <div className="notice documents-error">No fue posible actualizar el índice. Los datos ya indexados siguen disponibles.</div>}

    <div className="documents-toolbar">
      <label><Search size={15} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar documento, emisor, asunto o contexto" /></label>
      <select value={typeFilter} onChange={event => setTypeFilter(event.target.value)} aria-label="Filtrar por tipo">
        {types.map(type => <option key={type} value={type}>{type === 'all' ? 'Todos los tipos' : type}</option>)}
      </select>
      <button type="button" className="secondary-button compact-action" disabled={sync.isPending} onClick={() => sync.mutate(120)}>{sync.isPending ? 'Actualizando índice…' : 'Actualizar índice'}</button>
    </div>

    <div className="documents-list">
      {data.items.map(item => <article className="document-row" key={`${item.accountId}:${item.messageId}:${item.attachmentId}`}>
        <div className="document-type-icon">{fileIcon(item.documentType)}</div>
        <div className="document-main">
          <strong title={item.fileName}>{item.fileName}</strong>
          <div className="document-meta">
            <span><FileType2 size={12} /> {item.documentType}</span>
            <span><CalendarDays size={12} /> {dateLabel(item.receivedAt)}</span>
            <span><UserRound size={12} /> {item.senderName}</span>
            <span>{item.accountName}</span>
          </div>
          <p><b>{item.subject}</b>{item.context && item.context !== item.subject ? ` · ${item.context}` : ''}</p>
          <small>{item.senderAddress}</small>
        </div>
        <div className="document-actions">
          <button type="button" className="secondary-button compact-action" onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`)}><Mail size={14} /> Ver correo</button>
          <a className="secondary-button compact-action" href={mailApi.attachmentUrl(item.accountId, item.messageId, attachmentFrom(item), true)}><Download size={14} /> Descargar</a>
        </div>
      </article>)}
      {data.items.length === 0 && !sync.isPending && <div className="documents-empty">No hay documentos indexados que coincidan con el filtro.</div>}
    </div>

    <div className="documents-more">
      <button type="button" className="secondary-button" disabled={page === 0 || query.isFetching} onClick={() => setPage(current => Math.max(0, current - 1))}>Anterior</button>
      <span>Página {page + 1} · {indexedLabel(data.indexedAt)}</span>
      <button type="button" className="secondary-button" disabled={!data.hasMore || query.isFetching} onClick={() => setPage(current => current + 1)}>Siguiente</button>
    </div>
  </section>
}
