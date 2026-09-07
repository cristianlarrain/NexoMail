import { useMemo, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Clock3, Mail, MessageSquareReply, Search, Send, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'

function responseTimeLabel(minutes: number | null) {
  if (minutes === null) return '—'
  if (minutes < 60) return `${minutes} min`
  const hours = minutes / 60
  if (hours < 24) return `${hours < 10 ? hours.toFixed(1) : Math.round(hours)} h`
  const days = hours / 24
  return `${days < 10 ? days.toFixed(1) : Math.round(days)} d`
}

function lastInteractionLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: '2-digit' }).replace(/\./g, '')
}

function indexedLabel(value?: string | null) {
  if (!value) return 'Índice pendiente'
  return `Actualizado ${new Date(value).toLocaleString('es-CL', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).replace(/\./g, '')}`
}

export function ControlCenterContacts() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const expanded90 = useRef(false)
  const [days, setDays] = useState<30 | 90>(30)
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<'sent' | 'awaiting' | 'response' | 'recent'>('sent')

  const query = useQuery({
    queryKey: ['control-center-contacts-index', days],
    queryFn: () => mailApi.controlCenterContacts(days),
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  })

  const sync = useMutation({
    mutationFn: (limitPerAccount: number) => mailApi.syncMetadataIndex(90, limitPerAccount),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['control-center-contacts-index'] }),
        queryClient.invalidateQueries({ queryKey: ['control-center-documents-index'] }),
      ])
    },
  })

  function selectPeriod(next: 30 | 90) {
    setDays(next)
    if (next === 90 && (query.data?.indexedMessages ?? 0) > 0 && !expanded90.current && !sync.isPending) {
      expanded90.current = true
      sync.mutate(180)
    }
  }

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase()
    const rows = (query.data?.contacts ?? []).filter(contact => !term || [contact.name, contact.email, ...contact.subjects].some(value => value.toLowerCase().includes(term)))
    return [...rows].sort((left, right) => {
      if (sort === 'awaiting') return right.awaiting - left.awaiting || right.sent - left.sent
      if (sort === 'response') return (left.averageResponseMinutes ?? Number.MAX_SAFE_INTEGER) - (right.averageResponseMinutes ?? Number.MAX_SAFE_INTEGER)
      if (sort === 'recent') return new Date(right.lastInteraction).getTime() - new Date(left.lastInteraction).getTime()
      return right.sent - left.sent || right.received - left.received
    })
  }, [query.data?.contacts, search, sort])

  if (query.isLoading) return <section className="contact-control contact-control-loading"><p>Cargando índice local…</p></section>
  if (query.isError || !query.data) return <section className="contact-control"><div className="notice">No fue posible leer el índice de contactos. <button type="button" className="auth-link" onClick={() => query.refetch()}>Reintentar</button></div></section>

  const data = query.data
  const firstIndex = data.indexedMessages === 0

  return <section className="contact-control" aria-label="Estadísticas de contactos">
    <header className="contact-control-header">
      <div><p className="eyebrow">Interacción real</p><h2>Contactos</h2></div>
      <div className="contact-period" aria-label="Período de análisis">
        <button type="button" className={days === 30 ? 'active' : ''} onClick={() => selectPeriod(30)}>30 días</button>
        <button type="button" className={days === 90 ? 'active' : ''} onClick={() => selectPeriod(90)}>90 días</button>
      </div>
    </header>

    {firstIndex && <div className="contact-index-empty">
      <span>No hay metadatos indexados todavía.</span>
      <button type="button" className="secondary-button compact-action" disabled={sync.isPending} onClick={() => sync.mutate(40)}>{sync.isPending ? 'Creando índice…' : 'Crear índice'}</button>
    </div>}
    {!firstIndex && sync.isPending && <div className="contact-sync-status"><span className="index-loading-dot" aria-hidden="true" />{days === 90 ? 'Ampliando el historial a 90 días en segundo plano…' : 'Actualizando metadatos en segundo plano…'}</div>}
    {sync.isError && <div className="notice contact-limit-notice">No fue posible actualizar el índice. Los datos ya indexados siguen disponibles.</div>}

    <div className="contact-summary-grid">
      <article><Users size={14} /><div><strong>{data.contacts.length}</strong><span>Contactos</span></div></article>
      <article><Send size={14} /><div><strong>{data.totalSent}</strong><span>Enviados</span></div></article>
      <article><MessageSquareReply size={14} /><div><strong>{data.totalReplies}</strong><span>Respuestas</span></div></article>
      <article><Mail size={14} /><div><strong>{data.totalAwaiting}</strong><span>Pendientes</span></div></article>
      <article><Clock3 size={14} /><div><strong>{responseTimeLabel(data.averageResponseMinutes)}</strong><span>Promedio</span></div></article>
    </div>

    <div className="contact-toolbar">
      <label className="contact-search"><Search size={13} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar contacto o asunto" /></label>
      <select value={sort} onChange={event => setSort(event.target.value as typeof sort)} aria-label="Ordenar contactos">
        <option value="sent">Más interacción</option><option value="awaiting">Más pendientes</option><option value="response">Respuesta más rápida</option><option value="recent">Más reciente</option>
      </select>
      <button type="button" className="secondary-button compact-action" disabled={sync.isPending} onClick={() => sync.mutate(days === 90 ? 180 : 80)}>{sync.isPending ? 'Actualizando…' : 'Actualizar índice'}</button>
    </div>

    <div className="contact-list contact-list-dense">
      {filtered.map(contact => <article className="contact-row-card contact-row-dense" key={contact.email}>
        <button type="button" className="contact-identity" onClick={() => navigate(`/inbox?q=${encodeURIComponent(contact.email)}`)}>
          <span className="contact-avatar">{(contact.name || contact.email).trim().charAt(0).toUpperCase()}</span>
          <span className="contact-identity-copy"><strong>{contact.name}</strong><small>{contact.email}</small></span>
        </button>
        <div className="contact-row-metrics">
          <span title="Enviados"><small>Env.</small><strong>{contact.sent}</strong></span>
          <span title="Recibidos"><small>Rec.</small><strong>{contact.received}</strong></span>
          <span title="Respuestas"><small>Resp.</small><strong className="positive">{contact.replies}</strong></span>
          <span title="Sin respuesta"><small>Pend.</small><strong className={contact.awaiting > 0 ? 'attention' : ''}>{contact.awaiting}</strong></span>
          <span title="Tiempo medio de respuesta"><small>Prom.</small><strong>{responseTimeLabel(contact.averageResponseMinutes)}</strong></span>
        </div>
        <div className="contact-subject-inline" title={contact.subjects[0] ?? ''}>{contact.subjects[0] ?? 'Sin asunto destacado'}</div>
        <div className="contact-last"><strong>{lastInteractionLabel(contact.lastInteraction)}</strong></div>
      </article>)}
      {filtered.length === 0 && !sync.isPending && <div className="contact-empty">No hay contactos indexados para este período.</div>}
    </div>

    <p className="contact-footnote">{indexedLabel(data.indexedAt)} · {data.indexedMessages} mensajes indexados · {days} días seleccionados.</p>
  </section>
}
