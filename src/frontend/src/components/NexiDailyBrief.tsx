import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, CalendarDays, Clock3, Sparkles, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import type { ControlCenterPendingItem } from '../types/mail'

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
    const people = topCounterparts(data.pendingItems)
    const subjects = repeatedSubjects(data.pendingItems)

    let priority = 'No hay una prioridad operativa crítica detectada en este momento.'
    let priorityTone = 'ok'
    if (data.overdue > 0) {
      priority = 'Hay conversaciones que superan las 48 horas; conviene revisarlas antes que el resto.'
      priorityTone = 'critical'
    } else if (data.receivedWithoutReply > 0) {
      priority = 'Hay conversaciones recibidas esperando respuesta; conviene resolverlas antes de iniciar nuevos seguimientos.'
      priorityTone = 'attention'
    } else if (data.sentWithoutResponse > 0) {
      priority = 'Hay conversaciones enviadas que siguen sin respuesta; conviene revisar cuáles necesitan seguimiento.'
      priorityTone = 'attention'
    }

    return { data, people, subjects, priority, priorityTone }
  }, [snapshot.data])

  if (!brief) return null

  return <section className="nexi-daily-brief" aria-label="Resumen inteligente del día">
    <header className="nexi-daily-brief-header">
      <div>
        <span className="nexi-daily-brief-kicker"><Sparkles size={14} /> Nexi · Resumen inteligente del día</span>
        <strong>Qué requiere atención ahora</strong>
        <p>Interpretación operativa sin repetir los indicadores numéricos del Centro de Control.</p>
      </div>
      <button type="button" className="secondary-button nexi-daily-report-button" onClick={() => navigate('/control-center?tab=report&period=today')}><CalendarDays size={14} /> Reporte de hoy</button>
    </header>

    <div className="nexi-daily-brief-grid">
      <article className={`nexi-daily-priority ${brief.priorityTone}`}>
        <span className="nexi-daily-card-icon"><AlertTriangle size={17} /></span>
        <div><small>Prioridad</small><strong>{brief.priority}</strong></div>
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
