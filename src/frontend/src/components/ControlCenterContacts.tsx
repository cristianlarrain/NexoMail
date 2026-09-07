import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Clock3, Mail, MessageSquareReply, Search, Send, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { MailMessage, MailSummary } from '../types/mail'

type ContactAccumulator = {
  email: string
  name: string
  accounts: Set<string>
  sent: number
  received: number
  replies: number
  awaiting: number
  responseMinutes: number[]
  lastInteraction: number
  subjects: Map<string, { subject: string; count: number; lastAt: number }>
}

type ContactStat = {
  email: string
  name: string
  accounts: string[]
  sent: number
  received: number
  replies: number
  awaiting: number
  averageResponseMinutes: number | null
  lastInteraction: string
  subjects: string[]
}

type ContactsSnapshot = {
  days: 30 | 90
  contacts: ContactStat[]
  totalSent: number
  totalReceived: number
  totalReplies: number
  totalAwaiting: number
  averageResponseMinutes: number | null
  truncated: boolean
}

const MAX_PAGES = 10
const DETAIL_BATCH_SIZE = 8

function normalizeEmail(value: string) {
  return value.trim().toLowerCase()
}

function normalizeSubject(value: string) {
  return value.replace(/^\s*((re|rv|fw|fwd)\s*:\s*)+/i, '').trim() || '(sin asunto)'
}

function isNonPersonalAddress(value: string) {
  const email = normalizeEmail(value)
  const local = (email.split('@')[0] ?? '').replace(/[._-]+/g, '')
  return /^(noreply|donotreply|mailerdaemon|postmaster|newsletter|notifications?|alerts?|marketing|news|info|contacto|contact|soporte|support|ventas|sales|administracion|administrativo|admin|secretaria|recepcion|office|comunicaciones|communications|rrhh|recursoshumanos|facturacion|billing|cobranza|webmaster)$/i.test(local)
}

function addSubject(contact: ContactAccumulator, subject: string, at: number) {
  const normalized = normalizeSubject(subject)
  const key = normalized.toLowerCase()
  const current = contact.subjects.get(key)
  contact.subjects.set(key, current
    ? { subject: current.subject, count: current.count + 1, lastAt: Math.max(current.lastAt, at) }
    : { subject: normalized, count: 1, lastAt: at })
}

function contactName(name: string, email: string) {
  const clean = name.trim().replace(/^"|"$/g, '')
  return clean && normalizeEmail(clean) !== normalizeEmail(email) ? clean : email.split('@')[0]
}

async function loadFolder(folder: 'sent' | 'inbox', days: 30 | 90) {
  const items: MailSummary[] = []
  let cursor: string | undefined
  let pages = 0
  do {
    const page = await mailApi.messages(undefined, folder, `newer_than:${days}d`, cursor)
    items.push(...page.items)
    cursor = page.nextCursor
    pages++
  } while (cursor && pages < MAX_PAGES)
  return { items, truncated: Boolean(cursor) }
}

async function loadSentDetails(items: MailSummary[]) {
  const details: MailMessage[] = []
  for (let index = 0; index < items.length; index += DETAIL_BATCH_SIZE) {
    const batch = items.slice(index, index + DETAIL_BATCH_SIZE)
    const values = await Promise.all(batch.map(item => mailApi.message(item.accountId, item.providerMessageId).catch(() => null)))
    details.push(...values.filter((value): value is MailMessage => Boolean(value)))
  }
  return details
}

function computeThreadStats(contact: ContactAccumulator, message: MailMessage, ownEmail: string, counterpartEmail: string) {
  const thread = [...(message.thread ?? [])].sort((left, right) => new Date(left.receivedAt).getTime() - new Date(right.receivedAt).getTime())
  if (thread.length === 0) return

  let waitingSince: number | null = null
  for (const item of thread) {
    const from = normalizeEmail(item.from.address)
    const at = new Date(item.receivedAt).getTime()
    if (from === ownEmail) {
      waitingSince = at
      continue
    }
    if (from === counterpartEmail && waitingSince !== null && at >= waitingSince) {
      contact.replies++
      contact.responseMinutes.push(Math.max(0, Math.round((at - waitingSince) / 60_000)))
      waitingSince = null
    }
  }
  if (waitingSince !== null) contact.awaiting++
}

async function buildSnapshot(days: 30 | 90): Promise<ContactsSnapshot> {
  const [accounts, sentPage, inboxPage] = await Promise.all([
    mailApi.accounts(),
    loadFolder('sent', days),
    loadFolder('inbox', days),
  ])

  const accountById = new Map(accounts.map(account => [account.id, account]))
  const ownAddresses = new Set(accounts.map(account => normalizeEmail(account.emailAddress)))
  const sentDetails = await loadSentDetails(sentPage.items)
  const contacts = new Map<string, ContactAccumulator>()
  const processedThreads = new Set<string>()

  for (const message of sentDetails) {
    const account = accountById.get(message.accountId)
    if (!account) continue
    const ownEmail = normalizeEmail(account.emailAddress)
    const recipients = message.to
      .map(recipient => ({ name: recipient.name, email: normalizeEmail(recipient.address) }))
      .filter(recipient => recipient.email.includes('@') && !ownAddresses.has(recipient.email) && !isNonPersonalAddress(recipient.email))

    for (const recipient of recipients) {
      let contact = contacts.get(recipient.email)
      if (!contact) {
        contact = {
          email: recipient.email,
          name: contactName(recipient.name, recipient.email),
          accounts: new Set<string>(),
          sent: 0,
          received: 0,
          replies: 0,
          awaiting: 0,
          responseMinutes: [],
          lastInteraction: 0,
          subjects: new Map(),
        }
        contacts.set(recipient.email, contact)
      }

      contact.sent++
      contact.accounts.add(account.displayName)
      const sentAt = new Date(message.receivedAt).getTime()
      contact.lastInteraction = Math.max(contact.lastInteraction, sentAt)
      addSubject(contact, message.subject, sentAt)

      const firstThreadMessage = message.thread?.[0]?.providerMessageId ?? message.providerMessageId
      const threadKey = `${message.accountId}:${firstThreadMessage}:${recipient.email}`
      if (!processedThreads.has(threadKey)) {
        processedThreads.add(threadKey)
        computeThreadStats(contact, message, ownEmail, recipient.email)
      }
    }
  }

  for (const item of inboxPage.items) {
    const email = normalizeEmail(item.senderAddress)
    if (!email || ownAddresses.has(email) || isNonPersonalAddress(email)) continue
    const contact = contacts.get(email)
    if (!contact) continue
    contact.received++
    if (!contact.name || contact.name === contact.email.split('@')[0]) contact.name = contactName(item.senderName, email)
    const receivedAt = new Date(item.receivedAt).getTime()
    contact.lastInteraction = Math.max(contact.lastInteraction, receivedAt)
    addSubject(contact, item.subject, receivedAt)
  }

  const values = [...contacts.values()]
    .filter(contact => contact.sent > 0)
    .map<ContactStat>(contact => ({
      email: contact.email,
      name: contact.name,
      accounts: [...contact.accounts].sort((a, b) => a.localeCompare(b, 'es')),
      sent: contact.sent,
      received: contact.received,
      replies: contact.replies,
      awaiting: contact.awaiting,
      averageResponseMinutes: contact.responseMinutes.length
        ? Math.round(contact.responseMinutes.reduce((sum, value) => sum + value, 0) / contact.responseMinutes.length)
        : null,
      lastInteraction: new Date(contact.lastInteraction || Date.now()).toISOString(),
      subjects: [...contact.subjects.values()]
        .sort((left, right) => right.count - left.count || right.lastAt - left.lastAt)
        .slice(0, 3)
        .map(value => value.subject),
    }))
    .sort((left, right) => right.sent - left.sent || right.received - left.received)

  const allResponseMinutes = values.flatMap(contact => contacts.get(contact.email)?.responseMinutes ?? [])

  return {
    days,
    contacts: values,
    totalSent: values.reduce((sum, contact) => sum + contact.sent, 0),
    totalReceived: values.reduce((sum, contact) => sum + contact.received, 0),
    totalReplies: values.reduce((sum, contact) => sum + contact.replies, 0),
    totalAwaiting: values.reduce((sum, contact) => sum + contact.awaiting, 0),
    averageResponseMinutes: allResponseMinutes.length
      ? Math.round(allResponseMinutes.reduce((sum, value) => sum + value, 0) / allResponseMinutes.length)
      : null,
    truncated: sentPage.truncated || inboxPage.truncated,
  }
}

function responseTimeLabel(minutes: number | null) {
  if (minutes === null) return 'Sin datos'
  if (minutes < 60) return `${minutes} min`
  const hours = minutes / 60
  if (hours < 24) return `${hours < 10 ? hours.toFixed(1) : Math.round(hours)} h`
  const days = hours / 24
  return `${days < 10 ? days.toFixed(1) : Math.round(days)} días`
}

function lastInteractionLabel(value: string) {
  return new Date(value).toLocaleDateString('es-CL', { day: '2-digit', month: 'short', year: 'numeric' }).replace(/\./g, '')
}

export function ControlCenterContacts() {
  const navigate = useNavigate()
  const [days, setDays] = useState<30 | 90>(30)
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<'sent' | 'awaiting' | 'response' | 'recent'>('sent')

  const query = useQuery({
    queryKey: ['control-center-contacts', days],
    queryFn: () => buildSnapshot(days),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchOnWindowFocus: false,
  })

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

  if (query.isLoading) return <section className="contact-control contact-control-loading"><div className="reading-skeleton" /><p>Analizando contactos e interacciones del correo…</p></section>
  if (query.isError || !query.data) return <section className="contact-control"><div className="notice">No fue posible generar las estadísticas de contactos. <button type="button" className="auth-link" onClick={() => query.refetch()}>Reintentar</button></div></section>

  const data = query.data
  return <section className="contact-control" aria-label="Estadísticas de contactos">
    <header className="contact-control-header">
      <div><p className="eyebrow">Interacción real</p><h2>Contactos</h2><p>Personas con las que usted mantiene intercambio de correo. Se excluyen avisos, newsletters y direcciones genéricas o automáticas.</p></div>
      <div className="contact-period" aria-label="Período de análisis">
        <button type="button" className={days === 30 ? 'active' : ''} onClick={() => setDays(30)}>30 días</button>
        <button type="button" className={days === 90 ? 'active' : ''} onClick={() => setDays(90)}>90 días</button>
      </div>
    </header>

    <div className="contact-summary-grid">
      <article><Users size={18} /><div><strong>{data.contacts.length}</strong><span>Contactos activos</span></div></article>
      <article><Send size={18} /><div><strong>{data.totalSent}</strong><span>Enviados</span></div></article>
      <article><MessageSquareReply size={18} /><div><strong>{data.totalReplies}</strong><span>Respuestas</span></div></article>
      <article><Mail size={18} /><div><strong>{data.totalAwaiting}</strong><span>Sin respuesta</span></div></article>
      <article><Clock3 size={18} /><div><strong>{responseTimeLabel(data.averageResponseMinutes)}</strong><span>Tiempo medio</span></div></article>
    </div>

    {data.truncated && <div className="notice contact-limit-notice">El volumen del período es alto; se analizaron las interacciones más recientes disponibles.</div>}

    <div className="contact-toolbar">
      <label className="contact-search"><Search size={15} /><input value={search} onChange={event => setSearch(event.target.value)} placeholder="Buscar persona, correo o asunto" /></label>
      <select value={sort} onChange={event => setSort(event.target.value as typeof sort)} aria-label="Ordenar contactos">
        <option value="sent">Más interacción</option>
        <option value="awaiting">Más pendientes</option>
        <option value="response">Respuesta más rápida</option>
        <option value="recent">Más reciente</option>
      </select>
    </div>

    <div className="contact-list">
      {filtered.map(contact => <article className="contact-row-card" key={contact.email}>
        <button type="button" className="contact-identity" onClick={() => navigate(`/inbox?q=${encodeURIComponent(contact.email)}`)}>
          <span className="contact-avatar">{(contact.name || contact.email).trim().charAt(0).toUpperCase()}</span>
          <span className="contact-identity-copy"><strong>{contact.name}</strong><small>{contact.email}</small><em>{contact.accounts.join(' · ')}</em></span>
        </button>

        <div className="contact-row-metrics" aria-label={`Estadísticas de ${contact.name}`}>
          <span><small>Enviados</small><strong>{contact.sent}</strong></span>
          <span><small>Recibidos</small><strong>{contact.received}</strong></span>
          <span><small>Respuestas</small><strong className="positive">{contact.replies}</strong></span>
          <span><small>Sin respuesta</small><strong className={contact.awaiting > 0 ? 'attention' : ''}>{contact.awaiting}</strong></span>
          <span><small>Tiempo medio</small><strong>{responseTimeLabel(contact.averageResponseMinutes)}</strong></span>
        </div>

        <div className="contact-row-detail">
          <div className="contact-subjects"><small>Asuntos</small><div>{contact.subjects.length > 0 ? contact.subjects.map(subject => <span key={subject} title={subject}>{subject}</span>) : <span>Sin asuntos destacados</span>}</div></div>
          <div className="contact-last"><small>Última interacción</small><strong>{lastInteractionLabel(contact.lastInteraction)}</strong></div>
        </div>
      </article>)}
      {filtered.length === 0 && <div className="contact-empty">No hay contactos que coincidan con el filtro.</div>}
    </div>

    <p className="contact-footnote">“Sin respuesta” indica conversaciones cuyo último intercambio detectado fue enviado por usted. El tiempo medio se calcula hasta la siguiente respuesta del mismo contacto dentro del hilo.</p>
  </section>
}
