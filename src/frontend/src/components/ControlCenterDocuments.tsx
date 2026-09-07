import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Download, Eye, FileArchive, FileSpreadsheet, FileText, FileType2, Mail, Search, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { DocumentIndexItem, MailAttachment } from '../types/mail'

const PAGE_SIZE = 80

function fileIcon(type: string) {
  if (type === 'Planilla') return <FileSpreadsheet size={15} />
  if (type === 'Comprimido') return <FileArchive size={15} />
  if (type === 'PDF' || type === 'Documento') return <FileText size={15} />
  return <FileType2 size={15} />
}

function dateLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: '2-digit' }).replace(/\./g, '')
}

function attachmentFrom(item: DocumentIndexItem): MailAttachment {
  return { id: item.attachmentId, name: item.fileName, contentType: item.contentType, size: item.size }
}

function canPreview(item: DocumentIndexItem) {
  return item.contentType.startsWith('image/') || item.contentType === 'application/pdf' || /^text\/(plain|csv)|application\/(json|xml)/i.test(item.contentType) || /\.(pdf|txt|csv|json|xml|log|md)$/i.test(item.fileName)
}

function indexedLabel(value?: string | null) {
  if (!value) return 'Índice pendiente'
  return `Actualizado ${new Date(value).toLocaleString('es-CL', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).replace(/\./g, '')}`
}

export function ControlCenterDocuments() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('all')
  const [page, setPage] = useState(0)
  const [preview, setPreview] = useState<DocumentIndexItem | null>(null)

  useEffect(() => { setPage(0) }, [search, typeFilter])
  useEffect(() => {
    if (!preview) return
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') setPreview(null) }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [preview])

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

  const types = useMemo(() => ['all', 'PDF', 'Documento', 'Planilla', 'Presentación', 'Comprimido', 'Texto / datos'], [])

  if (query.isLoading) return <section className="documents-control documents-loading"><p>Cargando índice local…</p></section>
  if (query.isError || !query.data) return <section className="documents-control"><div className="notice">No fue posible leer el índice documental. <button type="button" className="auth-link" onClick={() => query.refetch()}>Reintentar</button></div></section>

  const data = query.data
  const firstIndex = data.indexedMessages === 0
  const previewAttachment = preview ? attachmentFrom(preview) : null
  const previewUrl = preview && previewAttachment ? mailApi.attachmentUrl(preview.accountId, preview.messageId, previewAttachment) : ''
  const previewDownloadUrl = preview && previewAttachment ? mailApi.attachmentUrl(preview.accountId, preview.messageId, previewAttachment, true) : ''

  return <section className="documents-control" aria-label="Documentos recibidos">
    <header className="documents-header">
      <div><p className="eyebrow">Registro documental</p><h2>Documentos recibidos</h2></div>
      <strong>{data.total} documento{data.total === 1 ? '' : 's'}</strong>
    </header>

    {firstIndex && <div className="documents-index-empty">
      <span>No hay documentos indexados todavía. Esta vista no consulta Gmail automáticamente.</span>
      <button type="button" className="secondary-button compact-action" disabled={sync.isPending} onClick={() => sync.mutate(40)}>{sync.isPending ? 'Creando índice…' : 'Crear índice'}</button>
    </div>}
    {!firstIndex && sync.isPending && <div className="documents-sync-status"><span className="index-loading-dot" aria-hidden="true" />Actualizando metadatos en segundo plano. Puedes seguir usando esta vista.</div>}
    {sync.isError && <div className="notice documents-error">No fue posible actualizar el índice. Los datos ya indexados siguen disponibles.</div>}

    <div className="documents-toolbar">
      <label><Search size={14} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar documento, emisor o asunto" /></label>
      <select value={typeFilter} onChange={event => setTypeFilter(event.target.value)} aria-label="Filtrar por tipo">
        {types.map(type => <option key={type} value={type}>{type === 'all' ? 'Todos los tipos' : type}</option>)}
      </select>
      <button type="button" className="secondary-button compact-action" disabled={sync.isPending} onClick={() => sync.mutate(120)}>{sync.isPending ? 'Actualizando…' : 'Actualizar índice'}</button>
    </div>

    <div className="documents-list">
      {data.items.map(item => <article className="document-row" key={`${item.accountId}:${item.messageId}:${item.attachmentId}`}>
        <div className="document-type-icon" title={item.documentType}>{fileIcon(item.documentType)}</div>
        <div className="document-file">
          <strong title={item.fileName}>{item.fileName}</strong>
          <small>{item.documentType}</small>
        </div>
        <div className="document-sender" title={`${item.senderName} · ${item.senderAddress}`}>
          <strong>{item.senderName}</strong>
          <small>{item.accountName}</small>
        </div>
        <div className="document-subject" title={item.context || item.subject}>
          <strong>{item.subject}</strong>
        </div>
        <time className="document-date" dateTime={item.receivedAt}>{dateLabel(item.receivedAt)}</time>
        <div className="document-actions">
          <button type="button" className="document-icon-action" title="Previsualizar" aria-label={`Previsualizar ${item.fileName}`} onClick={() => setPreview(item)}><Eye size={14} /></button>
          <button type="button" className="document-icon-action" title="Ver correo" aria-label={`Ver correo de ${item.fileName}`} onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`)}><Mail size={14} /></button>
          <a className="document-icon-action" title="Descargar" aria-label={`Descargar ${item.fileName}`} href={mailApi.attachmentUrl(item.accountId, item.messageId, attachmentFrom(item), true)}><Download size={14} /></a>
        </div>
      </article>)}
      {data.items.length === 0 && !sync.isPending && !firstIndex && <div className="documents-empty">No hay documentos indexados que coincidan con el filtro.</div>}
    </div>

    <div className="documents-more">
      <button type="button" className="secondary-button compact-action" disabled={page === 0 || query.isFetching} onClick={() => setPage(current => Math.max(0, current - 1))}>Anterior</button>
      <span>Pág. {page + 1} · {indexedLabel(data.indexedAt)}</span>
      <button type="button" className="secondary-button compact-action" disabled={!data.hasMore || query.isFetching} onClick={() => setPage(current => current + 1)}>Siguiente</button>
    </div>

    {preview && <div className="document-preview-layer" role="dialog" aria-modal="true" aria-label={`Vista previa de ${preview.fileName}`}>
      <button type="button" className="document-preview-backdrop" aria-label="Cerrar vista previa" onClick={() => setPreview(null)} />
      <aside className="document-preview-panel">
        <header>
          <div><p className="eyebrow">Vista previa</p><strong title={preview.fileName}>{preview.fileName}</strong><small>{preview.senderName} · {dateLabel(preview.receivedAt)}</small></div>
          <button type="button" className="document-preview-close" onClick={() => setPreview(null)} aria-label="Cerrar vista previa"><X size={17} /></button>
        </header>
        <div className="document-preview-content">
          {preview.contentType.startsWith('image/') ? <img src={previewUrl} alt={preview.fileName} /> : canPreview(preview) ? <iframe src={previewUrl} title={`Vista previa: ${preview.fileName}`} /> : <div className="document-preview-unsupported"><FileText size={34} /><strong>Vista previa no disponible</strong><span>Este formato no puede mostrarse directamente en el navegador.</span><button type="button" className="secondary-button compact-action" onClick={() => { setPreview(null); navigate(`/message/${preview.accountId}/${preview.messageId}`) }}><Mail size={14} /> Ver correo</button></div>}
        </div>
        <footer><span>{preview.documentType}</span><a className="primary-button" href={previewDownloadUrl}><Download size={14} /> Descargar</a></footer>
      </aside>
    </div>}
  </section>
}
