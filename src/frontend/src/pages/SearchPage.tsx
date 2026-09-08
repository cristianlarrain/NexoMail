import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { Bookmark, BookmarkCheck, Clock3, FileText, Inbox, Mail, Paperclip, Search, Send, Sparkles, Users, X } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import { searchApi } from '../api/searchApi'
import type { AiSearchInterpretation, ContactAnalyticsItem, DocumentIndexItem, MailSummary } from '../types/mail'

const SAVED_SEARCHES_KEY = 'nexomail-saved-searches-v1'

type SearchFolder = 'all' | 'inbox' | 'sent'
type SearchScope = 'all' | 'mail' | 'contacts' | 'documents'
type DocumentType = 'all' | 'pdf' | 'word' | 'excel' | 'image'
type SavedSearch = {
  id: string
  name: string
  query: string
  accountId: string
  folder: SearchFolder
  unread: boolean
  attachments: boolean
  days: number | null
  documentType: DocumentType
  scope: SearchScope
}

function readSavedSearches(): SavedSearch[] {
  try {
    const raw = localStorage.getItem(SAVED_SEARCHES_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as SavedSearch[]
    return Array.isArray(parsed) ? parsed.slice(0, 12) : []
  } catch {
    return []
  }
}

function dateLabel(value: string) {
  const date = new Date(value)
  return date.toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: date.getFullYear() === new Date().getFullYear() ? undefined : 'numeric' })
}

function sizeLabel(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

function appendOperator(query: string, operator: string, pattern: RegExp) {
  return pattern.test(query) ? query : `${query} ${operator}`.trim()
}

function isScope(value: string | null): value is SearchScope {
  return value === 'all' || value === 'mail' || value === 'contacts' || value === 'documents'
}

function isFolder(value: string | null): value is SearchFolder {
  return value === 'all' || value === 'inbox' || value === 'sent'
}

function isDocumentType(value: string | null): value is DocumentType {
  return value === 'all' || value === 'pdf' || value === 'word' || value === 'excel' || value === 'image'
}

export function SearchPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const location = useLocation()
  const [savedSearches, setSavedSearches] = useState<SavedSearch[]>(readSavedSearches)
  const query = params.get('q')?.trim() ?? ''
  const explicitAccount = params.get('account') ?? ''

  const accountsQuery = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const interpretationQuery = useQuery({
    queryKey: ['ai-search-interpretation', query],
    queryFn: () => searchApi.interpret(query),
    enabled: query.length > 0,
    staleTime: 30 * 60_000,
    retry: false,
  })

  const interpretation = interpretationQuery.data
  const explicitFolder = isFolder(params.get('folder')) ? params.get('folder') as SearchFolder : null
  const folder: SearchFolder = explicitFolder ?? interpretation?.folder ?? 'all'
  const unread = params.has('unread') ? params.get('unread') === '1' : interpretation?.unread ?? false
  const attachments = params.has('attachments') ? params.get('attachments') === '1' : interpretation?.hasAttachments ?? false
  const explicitDays = params.has('days') ? Number(params.get('days')) : undefined
  const days = explicitDays !== undefined ? (Number.isFinite(explicitDays) && explicitDays > 0 ? explicitDays : null) : interpretation?.days ?? null
  const explicitType = isDocumentType(params.get('type')) ? params.get('type') as DocumentType : null
  const documentType: DocumentType = explicitType ?? interpretation?.documentType ?? 'all'
  const explicitScope = isScope(params.get('scope')) ? params.get('scope') as SearchScope : null
  const scope: SearchScope = explicitScope ?? interpretation?.scope ?? 'all'
  const special = interpretation?.special ?? 'none'
  const textQuery = interpretation?.textQuery?.trim() || query
  const selectedAccount = accountsQuery.data?.find(account => account.id === explicitAccount)

  const gmailQuery = useMemo(() => {
    let value = interpretation?.gmailQuery?.trim() || query
    if (unread) value = appendOperator(value, 'is:unread', /(?:^|\s)is:unread(?:\s|$)/i)
    if (attachments) value = appendOperator(value, 'has:attachment', /(?:^|\s)has:attachment(?:\s|$)/i)
    if (days) value = appendOperator(value, `newer_than:${days}d`, /(?:^|\s)(?:newer_than:|after:)/i)
    if (documentType === 'pdf') value = appendOperator(value, 'filename:pdf', /filename:pdf/i)
    if (documentType === 'word') value = appendOperator(value, '{filename:doc filename:docx}', /filename:doc/i)
    if (documentType === 'excel') value = appendOperator(value, '{filename:xls filename:xlsx filename:csv}', /filename:(?:xls|xlsx|csv)/i)
    return value.trim()
  }, [attachments, days, documentType, interpretation?.gmailQuery, query, unread])

  const messagesQuery = useQuery({
    queryKey: ['universal-search-mail', explicitAccount, folder, gmailQuery],
    queryFn: async () => {
      const folders: Array<'inbox' | 'sent' | 'archive'> = folder === 'all' ? ['inbox', 'sent', 'archive'] : [folder]
      const results = await Promise.all(folders.map(value => searchApi.messages(explicitAccount || undefined, value, gmailQuery)))
      const unique = new Map<string, MailSummary>()
      for (const result of results) {
        for (const item of result.items) unique.set(`${item.accountId}:${item.providerMessageId}`, item)
      }
      return [...unique.values()].sort((left, right) => new Date(right.receivedAt).getTime() - new Date(left.receivedAt).getTime())
    },
    enabled: query.length > 0 && (scope === 'all' || scope === 'mail') && special === 'none',
    staleTime: 2 * 60_000,
    retry: false,
  })

  const specialQuery = useQuery({
    queryKey: ['universal-search-special', explicitAccount, special],
    queryFn: () => searchApi.controlCenter(explicitAccount || undefined),
    enabled: query.length > 0 && (scope === 'all' || scope === 'mail') && special !== 'none',
    staleTime: 60_000,
    retry: false,
  })

  const contactsQuery = useQuery({
    queryKey: ['universal-search-contacts'],
    queryFn: searchApi.contacts,
    enabled: query.length > 0 && (scope === 'all' || scope === 'contacts'),
    staleTime: 5 * 60_000,
    retry: false,
  })

  const documentsQuery = useQuery({
    queryKey: ['universal-search-documents', textQuery, documentType],
    queryFn: () => searchApi.documents(textQuery, documentType),
    enabled: query.length > 0 && (scope === 'all' || scope === 'documents'),
    staleTime: 5 * 60_000,
    retry: false,
  })

  const mailItems = useMemo(() => {
    if (special === 'none') return messagesQuery.data ?? []
    const expectedDirection = special === 'sent_without_response' ? 'sent' : 'received'
    const term = textQuery.toLocaleLowerCase('es')
    return (specialQuery.data?.pendingItems ?? [])
      .filter(item => item.direction === expectedDirection)
      .filter(item => !term || `${item.counterpart} ${item.subject}`.toLocaleLowerCase('es').includes(term))
      .map(item => ({
        providerMessageId: item.messageId,
        accountId: item.accountId,
        senderName: item.counterpart,
        senderAddress: '',
        subject: item.subject,
        preview: expectedDirection === 'sent' ? 'Enviado y aún sin respuesta' : 'Recibido y pendiente de responder',
        receivedAt: item.since,
        isRead: item.isRead,
        hasAttachments: false,
        folderId: expectedDirection === 'sent' ? 'sent' : 'inbox',
      } satisfies MailSummary))
  }, [messagesQuery.data, special, specialQuery.data?.pendingItems, textQuery])

  const contactItems = useMemo(() => {
    const terms = textQuery.toLocaleLowerCase('es').split(/\s+/).filter(term => term.length > 1)
    const accountLabels = selectedAccount ? [selectedAccount.displayName.toLocaleLowerCase('es'), selectedAccount.emailAddress.toLocaleLowerCase('es')] : []
    return (contactsQuery.data?.contacts ?? [])
      .filter(contact => {
        const haystack = `${contact.name} ${contact.email} ${contact.subjects.join(' ')}`.toLocaleLowerCase('es')
        return terms.length === 0 || terms.every(term => haystack.includes(term))
      })
      .filter(contact => accountLabels.length === 0 || contact.accounts.some(account => accountLabels.some(label => account.toLocaleLowerCase('es').includes(label))))
      .slice(0, 30)
  }, [contactsQuery.data?.contacts, selectedAccount, textQuery])

  const documentItems = useMemo(() => {
    const minimumDate = days ? Date.now() - days * 24 * 60 * 60 * 1000 : null
    return (documentsQuery.data?.items ?? [])
      .filter(item => !explicitAccount || item.accountId === explicitAccount)
      .filter(item => minimumDate === null || new Date(item.receivedAt).getTime() >= minimumDate)
      .slice(0, 50)
  }, [days, documentsQuery.data?.items, explicitAccount])

  const mailCount = mailItems.length
  const contactCount = contactItems.length
  const documentCount = documentItems.length
  const totalCount = mailCount + contactCount + documentCount
  const loading = interpretationQuery.isLoading || messagesQuery.isLoading || specialQuery.isLoading || contactsQuery.isLoading || documentsQuery.isLoading
  const hasError = messagesQuery.isError || specialQuery.isError || contactsQuery.isError || documentsQuery.isError

  function updateParam(name: string, value: string | null) {
    const next = new URLSearchParams(params)
    if (value === null) next.delete(name)
    else next.set(name, value)
    setParams(next)
  }

  function startSearch(value: string) {
    const next = new URLSearchParams()
    next.set('q', value)
    setParams(next)
  }

  function openMessage(item: MailSummary) {
    const navigationItems = mailItems.map(message => ({ accountId: message.accountId, messageId: message.providerMessageId }))
    navigate(`/message/${encodeURIComponent(item.accountId)}/${encodeURIComponent(item.providerMessageId)}`, {
      state: { returnTo: `${location.pathname}${location.search}`, navigationItems },
    })
  }

  function showContactMail(contact: ContactAnalyticsItem) {
    const next = new URLSearchParams()
    next.set('q', contact.email)
    next.set('scope', 'mail')
    next.set('folder', 'all')
    setParams(next)
  }

  function openDocument(item: DocumentIndexItem) {
    navigate(`/message/${encodeURIComponent(item.accountId)}/${encodeURIComponent(item.messageId)}`, {
      state: { returnTo: `${location.pathname}${location.search}` },
    })
  }

  const currentSavedKey = JSON.stringify({ query, accountId: explicitAccount, folder, unread, attachments, days, documentType, scope })
  const currentSaved = savedSearches.find(item => JSON.stringify({ query: item.query, accountId: item.accountId, folder: item.folder, unread: item.unread, attachments: item.attachments, days: item.days, documentType: item.documentType, scope: item.scope }) === currentSavedKey)

  function toggleSave() {
    if (!query) return
    const next = currentSaved
      ? savedSearches.filter(item => item.id !== currentSaved.id)
      : [{ id: crypto.randomUUID(), name: query.length > 48 ? `${query.slice(0, 45)}…` : query, query, accountId: explicitAccount, folder, unread, attachments, days, documentType, scope }, ...savedSearches].slice(0, 12)
    setSavedSearches(next)
    localStorage.setItem(SAVED_SEARCHES_KEY, JSON.stringify(next))
  }

  function applySaved(item: SavedSearch) {
    const next = new URLSearchParams()
    next.set('q', item.query)
    if (item.accountId) next.set('account', item.accountId)
    next.set('folder', item.folder)
    next.set('unread', item.unread ? '1' : '0')
    next.set('attachments', item.attachments ? '1' : '0')
    next.set('days', item.days ? String(item.days) : '0')
    next.set('type', item.documentType)
    next.set('scope', item.scope)
    setParams(next)
  }

  if (!query) return <section className="mail-view universal-search-page">
    <div className="universal-search-empty">
      <div className="universal-search-mark"><Search size={28} /></div>
      <h1>Buscar en todo NexoMail</h1>
      <p>Usa el buscador superior con lenguaje normal. Nexi buscará en correos, contactos y documentos.</p>
      <div className="universal-search-examples">
        {['PDF recibidos este mes', 'Correos enviados sin respuesta', 'Correos sin leer de los últimos 7 días', 'Documentos sobre presupuesto'].map(example =>
          <button key={example} type="button" onClick={() => startSearch(example)}>{example}</button>)}
      </div>
      {savedSearches.length > 0 && <div className="universal-saved-block"><strong>Búsquedas guardadas</strong><div>{savedSearches.map(item => <button type="button" key={item.id} onClick={() => applySaved(item)}><Bookmark size={14} />{item.name}</button>)}</div></div>}
    </div>
  </section>

  return <section className="mail-view universal-search-page">
    <div className="view-header universal-search-heading">
      <div><h1>Resultados de búsqueda</h1><p className="view-context">{selectedAccount ? selectedAccount.displayName : 'Todas las cuentas'} · “{query}”</p></div>
      <button type="button" className="secondary-button universal-save-button" onClick={toggleSave}>{currentSaved ? <BookmarkCheck size={16} /> : <Bookmark size={16} />}{currentSaved ? 'Guardada' : 'Guardar búsqueda'}</button>
    </div>

    <div className={`nexi-search-interpretation ${interpretationQuery.isLoading ? 'loading' : ''}`}>
      <Sparkles size={17} />
      <div><strong>Nexi</strong><span>{interpretationQuery.isLoading ? 'Interpretando lo que quieres encontrar…' : interpretation?.explanation ?? 'Búsqueda literal activa.'}</span></div>
      {interpretationQuery.isError && <small>La interpretación inteligente no estuvo disponible; se está usando la consulta tal como la escribiste.</small>}
    </div>

    <div className="universal-search-filters" aria-label="Filtros de búsqueda">
      <select value={explicitAccount} onChange={event => updateParam('account', event.target.value || null)} aria-label="Cuenta">
        <option value="">Todas las cuentas</option>
        {(accountsQuery.data ?? []).map(account => <option key={account.id} value={account.id}>{account.displayName}</option>)}
      </select>
      <select value={folder} onChange={event => updateParam('folder', event.target.value)} aria-label="Origen">
        <option value="all">Recibidos, enviados y archivados</option>
        <option value="inbox">Recibidos</option>
        <option value="sent">Enviados</option>
      </select>
      <button type="button" className={unread ? 'active' : ''} onClick={() => updateParam('unread', unread ? '0' : '1')}><Inbox size={15} />Sin leer</button>
      <button type="button" className={attachments ? 'active' : ''} onClick={() => updateParam('attachments', attachments ? '0' : '1')}><Paperclip size={15} />Con adjuntos</button>
      <select value={days ?? 0} onChange={event => updateParam('days', event.target.value)} aria-label="Fecha">
        <option value="0">Cualquier fecha</option><option value="7">7 días</option><option value="30">30 días</option><option value="90">90 días</option><option value="365">1 año</option>
      </select>
      <select value={documentType} onChange={event => updateParam('type', event.target.value)} aria-label="Tipo de documento">
        <option value="all">Todos los archivos</option><option value="pdf">PDF</option><option value="word">Word</option><option value="excel">Excel</option><option value="image">Imágenes</option>
      </select>
      {(explicitFolder || params.has('unread') || params.has('attachments') || params.has('days') || explicitType || explicitAccount) && <button type="button" className="filter-reset" onClick={() => {
        const next = new URLSearchParams(); next.set('q', query); if (explicitScope) next.set('scope', explicitScope); setParams(next)
      }}><X size={14} />Restablecer</button>}
    </div>

    {savedSearches.length > 0 && <div className="universal-saved-row"><span>Guardadas:</span>{savedSearches.slice(0, 6).map(item => <button type="button" key={item.id} onClick={() => applySaved(item)}><Bookmark size={12} />{item.name}</button>)}</div>}

    <div className="universal-result-tabs" role="tablist">
      <button type="button" className={scope === 'all' ? 'active' : ''} onClick={() => updateParam('scope', 'all')}>Todo <b>{totalCount}</b></button>
      <button type="button" className={scope === 'mail' ? 'active' : ''} onClick={() => updateParam('scope', 'mail')}><Mail size={14} />Correos <b>{mailCount}</b></button>
      <button type="button" className={scope === 'contacts' ? 'active' : ''} onClick={() => updateParam('scope', 'contacts')}><Users size={14} />Contactos <b>{contactCount}</b></button>
      <button type="button" className={scope === 'documents' ? 'active' : ''} onClick={() => updateParam('scope', 'documents')}><FileText size={14} />Documentos <b>{documentCount}</b></button>
    </div>

    {loading && <div className="universal-search-loading"><Sparkles size={18} /><span>Buscando en NexoMail…</span></div>}
    {hasError && <div className="notice">Una fuente no respondió. Los resultados disponibles siguen visibles.</div>}

    {!loading && totalCount === 0 && <div className="universal-no-results"><Search size={24} /><strong>No encontré coincidencias</strong><span>Prueba quitando algún filtro o escribe la búsqueda de otra forma.</span></div>}

    {(scope === 'all' || scope === 'mail') && mailItems.length > 0 && <section className="universal-result-section">
      <header><div><Mail size={17} /><strong>Correos</strong></div><span>{mailCount}</span></header>
      <div className="universal-mail-results">
        {mailItems.map(item => {
          const account = accountsQuery.data?.find(value => value.id === item.accountId)
          return <button type="button" key={`${item.accountId}:${item.providerMessageId}`} className={`universal-mail-result ${item.isRead ? '' : 'unread'}`} onClick={() => openMessage(item)}>
            <i className="account-dot" style={{ background: account?.color }} />
            <span className="universal-result-primary"><strong>{item.senderName || item.senderAddress || 'Correo'}</strong><span>{item.subject}</span><small>{item.preview}</small></span>
            {item.hasAttachments && <Paperclip size={14} />}
            <span className="universal-result-meta"><small>{account?.displayName}</small><time>{dateLabel(item.receivedAt)}</time>{item.folderId === 'sent' ? <Send size={13} /> : <Inbox size={13} />}</span>
          </button>
        })}
      </div>
    </section>}

    {(scope === 'all' || scope === 'contacts') && contactItems.length > 0 && <section className="universal-result-section">
      <header><div><Users size={17} /><strong>Contactos relacionados</strong></div><span>{contactCount}</span></header>
      <div className="universal-contact-results">
        {contactItems.map(contact => <button type="button" key={contact.email} onClick={() => showContactMail(contact)}>
          <span className="universal-contact-avatar">{(contact.name || contact.email).slice(0, 1).toUpperCase()}</span>
          <span><strong>{contact.name || contact.email}</strong><small>{contact.email}</small></span>
          <span className="universal-contact-stats"><b>{contact.received}</b> recibidos · <b>{contact.sent}</b> enviados{contact.awaiting > 0 ? ` · ${contact.awaiting} pendientes` : ''}</span>
        </button>)}
      </div>
    </section>}

    {(scope === 'all' || scope === 'documents') && documentItems.length > 0 && <section className="universal-result-section">
      <header><div><FileText size={17} /><strong>Documentos</strong></div><span>{documentCount}</span></header>
      <div className="universal-document-results">
        {documentItems.map(item => <button type="button" key={`${item.accountId}:${item.messageId}:${item.attachmentId}`} onClick={() => openDocument(item)}>
          <span className="universal-file-icon"><FileText size={18} /></span>
          <span className="universal-result-primary"><strong>{item.fileName}</strong><span>{item.subject}</span><small>{item.senderName || item.senderAddress}</small></span>
          <span className="universal-result-meta"><small>{item.documentType.toUpperCase()} · {sizeLabel(item.size)}</small><time>{dateLabel(item.receivedAt)}</time></span>
        </button>)}
      </div>
    </section>}

    {special !== 'none' && mailItems.length > 0 && <div className="universal-special-note"><Clock3 size={15} />Estos resultados provienen del seguimiento real del Centro de control.</div>}
  </section>
}
