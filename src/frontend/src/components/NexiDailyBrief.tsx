import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, ArrowDownRight, ArrowRight, ArrowUpRight, CalendarDays, Clock3, Mail, Sparkles, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem } from '../types/mail'

function localDateKey(date: Date) {
  const year = date.getFullYear()
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${year}-${month}-${day}`
}

function normalizeSubject(value: string) {
  return value
    .replace(/^\s*(?:(?:re|rv|fwd|fw)\s*:\s*)+/i, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLocaleLowerCase('es')
}

function topCounterparts(items: ControlCenterPendingItem[]) {
  const counts = new Map<string, { label: string; count: number }>()
  for (const item of items) {
    const label = item.counterpart.trim() || 'Sin identificar'
    const key = label.toLocaleLowerCase('es')
    const current = counts.get(key)
    counts.set(key, { label, count: (current?.count ?? 0) + 1 })
  }
  return [...counts.values()]
    .sort((left, right) => right.count - left.count || left.label.localeCompare(right.label, 'es'))
    .slice(0, 3)
}

function repeatedSubjects(items: ControlCenterPendingItem[]) {
  const counts = new Map<string, { label: string; count: number }>()
  for (const item of items) {
    const normalized = normalizeSubject(item.subject)
    if (!normalized) continue
    const current = counts.get(normalized)
    counts.set(normalized, {
      label: current?.label ?? item.subject.replace(/^\s*(?:(?:re|rv|fwd|fw)\s*:\s*)+/i, '').trim(),
      count: (current?.count ?? 0) + 1,
    })
  }
  return [...counts.values()]
    .filter(item => item.count > 1)
    .sort((left, right) => right.count - left.count || left.label.localeCompare(right.label, 'es'))
    .slice(0, 3)
}

function changeCopy(today: number, yesterday: number) {
  if (today === yesterday) return { icon: <ArrowRight size={15} />, tone: 'neutral', text: `Sin cambio frente a ayer (${today}).` }
  if (yesterday === 0) return { icon: <ArrowUpRight size={15} />, tone: 'up', text: `Hoy van ${today}; ayer no hubo actividad registrada.` }
  const percentage = Math.round(Math.abs((today - yesterday) / yesterday) * 100)
  if (today > yesterday) return { icon: <ArrowUpRight size={15} />, tone: 'up', text: `${percentage}% más actividad que ayer (${yesterday}).` }
  return { icon: <ArrowDownRight size={15} />, tone: 'down', text: `${percentage}% menos actividad que ayer (${yesterday}).` }
}

export function NexiDailyBrief() {
  const navigate = useNavigate()
  const snapshot = useQuery({
    queryKey: ['control-center', 'all'],
    queryFn: () => mailApi.controlCenter(),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchInterval: false,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
  })

  const brief = useMemo(() => {
    if (!snapshot.data) return null
    const data = snapshot.data
    const now = new Date()
    const yesterdayDate = new Date(now)
    yesterdayDate.setDate(now.getDate() - 1)
    const todayKey = localDateKey(now)
    const yesterdayKey = localDateKey(yesterdayDate)
    const today = data.activity.find(item => item.date.slice(0, 10) === todayKey)
    const yesterday = data.activity.find(item => item.date.slice(0, 10) === yesterdayKey)
    const todayReceived = today?.received ?? 0
    const todaySent = today?.sent ?? 0
    const yesterdayReceived = yesterday?.received ?? 0
    const yesterdaySent = yesterday?.sent ?? 0
    const todayTotal = todayReceived + todaySent
    const yesterdayTotal = yesterdayReceived + yesterdaySent
    const pendingTotal = data.receivedWithoutReply + data.sentWithoutResponse
    const people = topCounterparts(data.pendingItems)
    const subjects = repeatedSubjects(data.pendingItems)
    const comparison = changeCopy(todayTotal, yesterdayTotal)

    let priority = 'No hay pendientes críticos detectados en este momento.'
    let priorityTone = 'ok'
    if (data.overdue > 0) {
      priority = `${data.overdue} pendiente${data.overdue === 1 ? '' : 's'} supera${data.overdue === 1 ? '' : 'n'} las 48 horas y conviene revisarlo${data.overdue === 1 ? '' : 's'} primero.`
      priorityTone = 'critical'
    } else if (data.receivedWithoutReply > 0) {
      priority = `${data.receivedWithoutReply} correo${data.receivedWithoutReply === 1 ? '' : 's'} recibido${data.receivedWithoutReply === 1 ? '' : 's'} sigue${data.receivedWithoutReply === 1 ? '' : 'n'} esperando respuesta.`
      priorityTone = 'attention'
    } else if (data.sentWithoutResponse > 0) {
      priority = `${data.sentWithoutResponse} correo${data.sentWithoutResponse === 1 ? '' : 's'} enviado${data.sentWithoutResponse === 1 ? '' : 's'} sigue${data.sentWithoutResponse === 1 ? '' : 'n'} sin respuesta.`
      priorityTone = 'attention'
    }

    return { data, todayReceived, todaySent, todayTotal, pendingTotal, people, subjects, comparison, priority, priorityTone }
  }, [snapshot.data])

  if (!brief) return null

  return <section className="nexi-daily-brief" aria-label="Resumen inteligente del día">
    <header className="nexi-daily-brief-header">
      <div>
        <span className="nexi-daily-brief-kicker"><Sparkles size={14} /> Nexi · Resumen inteligente del día</span>
        <strong>Qué requiere atención ahora</strong>
        <p>Lectura operativa construida con datos reales del Centro de Control. El reporte con IA profundiza en el contenido de los correos.</p>
      </div>
      <button type="button" className="secondary-button nexi-daily-report-button" onClick={() => navigate('/control-center?tab=report&period=today')}><CalendarDays size={14} /> Reporte de hoy</button>
    </header>

    <div className="nexi-daily-brief-grid">
      <article className={`nexi-daily-priority ${brief.priorityTone}`}>
        <span className="nexi-daily-card-icon"><AlertTriangle size={17} /></span>
        <div><small>Prioridad</small><strong>{brief.priority}</strong><span>{brief.pendingTotal} pendiente{brief.pendingTotal === 1 ? '' : 's'} operativo{brief.pendingTotal === 1 ? '' : 's'} en total.</span></div>
      </article>

      <article className="nexi-daily-stat">
        <span className="nexi-daily-card-icon"><Mail size={17} /></span>
        <div><small>Actividad de hoy</small><strong>{brief.todayTotal}</strong><span>{brief.todayReceived} recibidos · {brief.todaySent} enviados</span><em className={brief.comparison.tone}>{brief.comparison.icon}{brief.comparison.text}</em></div>
      </article>

      <article className="nexi-daily-list-card">
        <span className="nexi-daily-card-icon"><Users size={17} /></span>
        <div><small>Personas con más pendientes</small>{brief.people.length > 0 ? <ul>{brief.people.map(person => <li key={person.label}><span title={person.label}>{person.label}</span><b>{person.count}</b></li>)}</ul> : <strong>Sin concentración de pendientes</strong>}</div>
      </article>

      <article className="nexi-daily-list-card">
        <span className="nexi-daily-card-icon"><Clock3 size={17} /></span>
        <div><small>Temas repetidos entre pendientes</small>{brief.subjects.length > 0 ? <ul>{brief.subjects.map(subject => <li key={subject.label}><span title={subject.label}>{subject.label}</span><b>{subject.count}</b></li>)}</ul> : <strong>No hay asuntos repetidos relevantes</strong>}</div>
      </article>
    </div>

    {brief.data.unavailableAccounts > 0 && <small className="nexi-daily-brief-footnote">El resumen considera sólo las cuentas disponibles; {brief.data.unavailableAccounts === 1 ? '1 cuenta no pudo consultarse.' : `${brief.data.unavailableAccounts} cuentas no pudieron consultarse.`}</small>}
  </section>
}
