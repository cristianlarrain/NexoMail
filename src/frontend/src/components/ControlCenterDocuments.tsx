import { useMemo, useState } from 'react'
import { CalendarDays, Download, FileArchive, FileSpreadsheet, FileText, FileType2, Mail, Search, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { MailAttachment, MailMessage, MailSummary } from '../types/mail'

type DocumentRow = {
  key: string
  accountId: string
  messageId: string
  fileName: string
  contentType: string
  documentType: string
  receivedAt: string
  senderName: string
  senderAddress: string
  subject: string
  context: string
  attachment: MailAttachment
}

type FolderCursor = { inbox?: string; archive?: string }
type FolderFinished = { inbox: boolean; archive: boolean }

const DETAIL_BATCH = 4
const DETAILS_PER_LOAD = 12
const DETAIL_TIMEOUT_MS = 5_000

function documentType(attachment: MailAttachment) {
  const name = attachment.name.toLowerCase()
  if (attachment.contentType === 'application/pdf' || name.endsWith('.pdf')) return 'PDF'
  if (/\.(doc|docx|odt|rtf)$/i.test(name)) return 'Documento'
  if (/\.(xls|xlsx|ods|csv)$/i.test(name)) return 'Planilla'
  if (/\.(ppt|pptx|odp)$/i.test(name)) return 'Presentación'
  if (/\.(zip|rar|7z)$/i.test(name)) return 'Comprimido'
  if (/\.(txt|xml|json)$/i.test(name)) return 'Texto / datos'
  return attachment.contentType.split('/')[1]?.toUpperCase() || 'Archivo'
}

function isUsefulDocument(attachment: MailAttachment) {
  const name = attachment.name.trim().toLowerCase()
  if (!name) return false
  if (/\.(png|jpg|jpeg|gif|webp|bmp|svg|ico)$/i.test(name)) return false
  if (/^(image\d*|logo|firma|signature|facebook|instagram|linkedin|twitter|x-logo)/i.test(name)) return false
  return true
}

function fileIcon(type: string) {
  if (type === 'Planilla') return <FileSpreadsheet size={18} />
  if (type === 'Comprimido') return <FileArchive size={18} />
  if (type === 'PDF' || type === 'Documento') return <FileText size={18} />
  return <FileType2 size={18} />
}

function dateLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' }).replace(/\./g, '')
}

async function messageWithTimeout(item: MailSummary) {
  let timer: ReturnType<typeof setTimeout> | undefined
  try {
    return await Promise.race<MailMessage | null>([
      mailApi.message(item.accountId, item.providerMessageId).catch(() => null),
      new Promise<null>(resolve => { timer = setTimeout(() => resolve(null), DETAIL_TIMEOUT_MS) }),
    ])
  } finally {
    if (timer) clearTimeout(timer)
  }
}

async function loadDetails(items: MailSummary[]) {
  const result: MailMessage[] = []
  for (let index = 0; index < items.length; index += DETAIL_BATCH) {
    const batch = items.slice(index, index + DETAIL_BATCH)
    const values = await Promise.all(batch.map(messageWithTimeout))
    result.push(...values.filter((value): value is MailMessage => Boolean(value)))
  }
  return result
}

function rowsFromMessages(messages: MailMessage[]) {
  const rows: DocumentRow[] = []
  for (const message of messages) {
    for (const attachment of message.attachments.filter(isUsefulDocument)) {
      rows.push({
        key: `${message.accountId}:${message.providerMessageId}:${attachment.id}`,
        accountId: message.accountId,
        messageId: message.providerMessageId,
        fileName: attachment.name,
        contentType: attachment.contentType,
        documentType: documentType(attachment),
        receivedAt: message.receivedAt,
        senderName: message.from.name || message.senderName || message.from.address,
        senderAddress: message.from.address || message.senderAddress,
        subject: message.subject,
        context: message.preview || message.subject,
        attachment,
      })
    }
  }
  return rows
}

function uniqueSummaries(items: MailSummary[]) {
  return [...new Map(items.map(item => [`${item.accountId}:${item.providerMessageId}`, item])).values()]
    .filter(item => item.hasAttachments)
    .sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())
}

export function ControlCenterDocuments() {
  const navigate = useNavigate()
  const [documents, setDocuments] = useState<DocumentRow[]>([])
  const [pendingSummaries, setPendingSummaries] = useState<MailSummary[]>([])
  const [cursors, setCursors] = useState<FolderCursor>({})
  const [folderFinished, setFolderFinished] = useState<FolderFinished>({ inbox: false, archive: false })
  const [started, setStarted] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [search, setSearch] = useState('')
  const [typeFilter, setTypeFilter] = useState('all')

  async function loadNext(reset = false) {
    if (loading) return
    setLoading(true)
    setError('')
    try {
      let queue = reset ? [] : pendingSummaries
      let nextCursors = reset ? {} as FolderCursor : cursors
      let nextFinished = reset ? { inbox: false, archive: false } : folderFinished

      if (queue.length === 0 && !(nextFinished.inbox && nextFinished.archive)) {
        const [inboxPage, archivePage] = await Promise.all([
          nextFinished.inbox ? Promise.resolve(null) : mailApi.messages(undefined, 'inbox', 'has:attachment', nextCursors.inbox),
          nextFinished.archive ? Promise.resolve(null) : mailApi.messages(undefined, 'archive', 'has:attachment', nextCursors.archive),
        ])

        queue = uniqueSummaries([
          ...(inboxPage?.items ?? []),
          ...(archivePage?.items ?? []),
        ])

        nextCursors = {
          inbox: inboxPage?.nextCursor,
          archive: archivePage?.nextCursor,
        }
        nextFinished = {
          inbox: nextFinished.inbox || !inboxPage?.nextCursor,
          archive: nextFinished.archive || !archivePage?.nextCursor,
        }
      }

      const currentBatch = queue.slice(0, DETAILS_PER_LOAD)
      const remaining = queue.slice(DETAILS_PER_LOAD)
      const details = await loadDetails(currentBatch)
      const nextRows = rowsFromMessages(details)

      setDocuments(current => {
        const base = reset ? [] : current
        const map = new Map(base.map(item => [item.key, item]))
        nextRows.forEach(item => map.set(item.key, item))
        return [...map.values()].sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())
      })
      setPendingSummaries(remaining)
      setCursors(nextCursors)
      setFolderFinished(nextFinished)
      setStarted(true)
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : 'No fue posible consultar los documentos recibidos.')
      setStarted(true)
    } finally {
      setLoading(false)
    }
  }

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    return documents.filter(item => {
      if (typeFilter !== 'all' && item.documentType !== typeFilter) return false
      if (!term) return true
      return [item.fileName, item.documentType, item.senderName, item.senderAddress, item.subject, item.context]
        .some(value => value.toLowerCase().includes(term))
    })
  }, [documents, search, typeFilter])

  const types = useMemo(() => [...new Set(documents.map(item => item.documentType))].sort((a, b) => a.localeCompare(b, 'es')), [documents])
  const finished = folderFinished.inbox && folderFinished.archive && pendingSummaries.length === 0

  if (!started) return <section className="documents-control documents-start">
    <FileText size={30} />
    <h2>Documentos recibidos</h2>
    <p>Lista documentos adjuntos recibidos en sus cuentas, incluidos correos archivados: archivo, tipo, fecha de recepción, emisor y contexto.</p>
    <button type="button" className="primary-button" onClick={() => void loadNext(true)} disabled={loading}>{loading ? 'Consultando…' : 'Consultar documentos'}</button>
  </section>

  return <section className="documents-control" aria-label="Documentos recibidos">
    <header className="documents-header">
      <div><p className="eyebrow">Registro documental</p><h2>Documentos recibidos</h2><p>Incluye Bandeja de entrada y Archivados. La fecha corresponde a la recepción del correo; la fecha interna escrita dentro de un PDF o Word requerirá análisis del contenido del documento.</p></div>
      <strong>{documents.length} documento{documents.length === 1 ? '' : 's'} cargado{documents.length === 1 ? '' : 's'}</strong>
    </header>

    <div className="documents-toolbar">
      <label><Search size={15} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar documento, emisor, asunto o contexto" /></label>
      <select value={typeFilter} onChange={event => setTypeFilter(event.target.value)} aria-label="Filtrar por tipo">
        <option value="all">Todos los tipos</option>
        {types.map(type => <option key={type} value={type}>{type}</option>)}
      </select>
    </div>

    {error && <div className="notice documents-error">{error}</div>}

    <div className="documents-list">
      {filtered.map(item => <article className="document-row" key={item.key}>
        <div className="document-type-icon">{fileIcon(item.documentType)}</div>
        <div className="document-main">
          <strong title={item.fileName}>{item.fileName}</strong>
          <div className="document-meta">
            <span><FileType2 size={12} /> {item.documentType}</span>
            <span><CalendarDays size={12} /> {dateLabel(item.receivedAt)}</span>
            <span><UserRound size={12} /> {item.senderName}</span>
          </div>
          <p><b>{item.subject}</b>{item.context && item.context !== item.subject ? ` · ${item.context}` : ''}</p>
          <small>{item.senderAddress}</small>
        </div>
        <div className="document-actions">
          <button type="button" className="secondary-button compact-action" onClick={() => navigate(`/message/${item.accountId}/${item.messageId}`)}><Mail size={14} /> Ver correo</button>
          <a className="secondary-button compact-action" href={mailApi.attachmentUrl(item.accountId, item.messageId, item.attachment, true)}><Download size={14} /> Descargar</a>
        </div>
      </article>)}
      {filtered.length === 0 && <div className="documents-empty">No hay documentos que coincidan con el filtro.</div>}
    </div>

    <div className="documents-more">
      {!finished && <button type="button" className="secondary-button" onClick={() => void loadNext()} disabled={loading}>{loading ? 'Cargando…' : pendingSummaries.length > 0 ? 'Cargar siguientes documentos' : 'Buscar más documentos'}</button>}
      {finished && <span>Se alcanzó el final de los documentos recibidos disponibles en Bandeja de entrada y Archivados.</span>}
    </div>
  </section>
}
