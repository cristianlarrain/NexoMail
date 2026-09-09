import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { BarChart3, Mail, Search, SendHorizontal, Sparkles, Users } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { nexiApi } from '../api/nexiApi'
import type { NexiContextResponse } from '../types/nexi'
import { detectNexiMailAction } from '../utils/nexiSearchIntent'

type Turn = {
  id: string
  role: 'user' | 'nexi'
  text: string
}

type StoredContext = {
  query: string
  accountId?: string
  turns: Turn[]
  lastResult: NexiContextResponse | null
}

const QUICK_QUESTIONS = [
  'Resúmeme estos correos y dime de qué se tratan.',
  '¿Qué me están pidiendo y qué requiere acción?',
  '¿Qué temas se repiten en estos correos?',
  'Dame un informe ejecutivo de este conjunto.',
]

function shortDate(value: string) {
  const date = new Date(`${value}T12:00:00`)
  return date.toLocaleDateString('es-CL', { day: '2-digit', month: 'short' }).replace(/\./g, '')
}

function contextHash(value: string) {
  let hash = 2166136261
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }
  return (hash >>> 0).toString(36)
}

function storageKey(query: string, accountId?: string) {
  return `nexomail-nexi-context-v1:${accountId ?? 'all'}:${contextHash(query)}`
}

function readStoredContext(query: string, accountId?: string): StoredContext | null {
  if (!query) return null
  try {
    const raw = sessionStorage.getItem(storageKey(query, accountId))
    if (!raw) return null
    const parsed = JSON.parse(raw) as StoredContext
    if (parsed.query !== query || (parsed.accountId ?? undefined) !== accountId || !Array.isArray(parsed.turns)) return null
    return parsed
  } catch {
    return null
  }
}

function writeStoredContext(query: string, accountId: string | undefined, turns: Turn[], lastResult: NexiContextResponse | null) {
  if (!query) return
  try {
    const payload: StoredContext = { query, accountId, turns: turns.slice(-30), lastResult }
    sessionStorage.setItem(storageKey(query, accountId), JSON.stringify(payload))
  } catch {
    // El contexto conversacional es una mejora de UX; si el navegador no permite sessionStorage, Nexi sigue funcionando.
  }
}

export function NexiContextWorkspace() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const query = params.get('q')?.trim() ?? ''
  const accountId = params.get('account') ?? undefined
  const auto = params.get('auto') === '1'
  const note = params.get('note')?.trim() ?? ''
  const [prompt, setPrompt] = useState('')
  const [turns, setTurns] = useState<Turn[]>([])
  const [lastResult, setLastResult] = useState<NexiContextResponse | null>(null)
  const autoStarted = useRef('')
  const noteHandled = useRef('')

  const context = useMutation({
    mutationFn: (instruction: string) => nexiApi.context(query, instruction, accountId),
    onMutate: instruction => {
      setTurns(current => {
        const next = [...current, { id: crypto.randomUUID(), role: 'user' as const, text: instruction }]
        writeStoredContext(query, accountId, next, lastResult)
        return next
      })
    },
    onSuccess: result => {
      setLastResult(result)
      setTurns(current => {
        const next = [...current, { id: crypto.randomUUID(), role: 'nexi' as const, text: result.answer }]
        writeStoredContext(query, accountId, next, result)
        return next
      })
    },
  })

  useEffect(() => {
    const restored = readStoredContext(query, accountId)
    setTurns(restored?.turns ?? [])
    setLastResult(restored?.lastResult ?? null)
    setPrompt('')
    autoStarted.current = ''
    noteHandled.current = ''
  }, [query, accountId])

  useEffect(() => {
    if (!query || !note || noteHandled.current === note) return
    noteHandled.current = note
    setTurns(current => {
      const next = [...current, { id: crypto.randomUUID(), role: 'nexi' as const, text: `Acción completada: ${note} Puedes seguir trabajando sobre el mismo contexto.` }]
      writeStoredContext(query, accountId, next, lastResult)
      return next
    })
    const nextParams = new URLSearchParams(params)
    nextParams.delete('note')
    setParams(nextParams, { replace: true })
  }, [accountId, lastResult, note, params, query, setParams])

  useEffect(() => {
    if (!query || !auto || context.isPending || turns.length > 0 || autoStarted.current === query) return
    autoStarted.current = query
    context.mutate('Dame una visión general breve de estos resultados: cuántos correos hay, de qué tratan y qué debería revisar primero.')
    const next = new URLSearchParams(params)
    next.delete('auto')
    setParams(next, { replace: true })
  }, [auto, context, params, query, setParams, turns.length])

  function ask(value: string) {
    const instruction = value.trim()
    if (!query || !instruction || context.isPending) return
    setPrompt('')

    const action = detectNexiMailAction(instruction)
    if (action) {
      const actionTurn: Turn = { id: crypto.randomUUID(), role: 'user', text: instruction }
      const nextTurns = [...turns, actionTurn]
      setTurns(nextTurns)
      writeStoredContext(query, accountId, nextTurns, lastResult)

      const next = new URLSearchParams({ q: `${instruction} ${query}`.trim(), base: query })
      if (accountId) next.set('account', accountId)
      navigate(`/search-action?${next.toString()}`)
      return
    }

    context.mutate(instruction)
  }

  function openResults() {
    const next = new URLSearchParams({ q: query })
    if (accountId) next.set('account', accountId)
    navigate(`/search?${next.toString()}`)
  }

  const senderMax = useMemo(() => Math.max(1, ...(lastResult?.senders.map(item => item.count) ?? [1])), [lastResult?.senders])
  const dayMax = useMemo(() => Math.max(1, ...(lastResult?.days.map(item => item.count) ?? [1])), [lastResult?.days])

  if (!query) return <section className="nexi-context-empty">
    <Sparkles size={22} />
    <div><strong>Nexi está listo para trabajar con un contexto</strong><span>Haz una búsqueda desde la barra superior y luego abre “Analizar con Nexi”. El conjunto encontrado quedará activo aquí para seguir preguntando sin empezar de nuevo.</span></div>
  </section>

  return <section className="nexi-context-workspace">
    <div className="nexi-context-main">
      <header className="nexi-context-header">
        <div className="nexi-context-identity"><div><span>Contexto activo</span><strong>{query}</strong></div></div>
        <button type="button" className="secondary-button" onClick={openResults}><Search size={14} /> Ver resultados</button>
      </header>

      <div className="nexi-context-quick" aria-label="Preguntas rápidas">
        {QUICK_QUESTIONS.map(question => <button type="button" key={question} disabled={context.isPending} onClick={() => ask(question)}>{question}</button>)}
      </div>

      <div className="nexi-context-conversation" aria-live="polite">
        {turns.length === 0 && <div className="nexi-context-welcome"><Sparkles size={18} /><div><strong>Sigue preguntando o actúa sobre este mismo conjunto</strong><span>Por ejemplo: “archiva sólo los no leídos”, “prepara respuestas para los pendientes”, “archiva los informativos” o “pon en seguimiento los que tienen adjuntos”.</span></div></div>}
        {turns.map(turn => <article key={turn.id} className={`nexi-context-turn ${turn.role}`}>
          <span>{turn.role === 'nexi' ? 'Nexi' : 'Tú'}</span>
          <p>{turn.text}</p>
        </article>)}
        {context.isPending && <article className="nexi-context-turn nexi loading"><span>Nexi</span><p>Analizando los correos del contexto…</p></article>}
        {context.isError && <div className="notice">{context.error instanceof Error ? context.error.message : 'No fue posible analizar este contexto.'}</div>}
      </div>

      <form className="nexi-context-composer" onSubmit={event => { event.preventDefault(); ask(prompt) }}>
        <textarea value={prompt} onChange={event => setPrompt(event.target.value)} rows={2} maxLength={3500} placeholder="Pregunta o pídele una acción a Nexi sobre estos mismos correos…" onKeyDown={event => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault()
            ask(prompt)
          }
        }} />
        <div><small>{prompt.length}/3500 · Enter envía · Shift+Enter nueva línea</small><button type="submit" className="primary-button" disabled={!prompt.trim() || context.isPending}><SendHorizontal size={15} /> Enviar a Nexi</button></div>
      </form>
    </div>

    <aside className="nexi-context-data">
      <div className="nexi-context-stat">
        <Mail size={17} />
        <div><strong>{lastResult?.messageCount ?? '—'}</strong><span>correos encontrados</span></div>
      </div>
      {lastResult && lastResult.analyzedCount < lastResult.messageCount && <small className="nexi-context-sample">Nexi analizó en detalle {lastResult.analyzedCount} de los {lastResult.messageCount} correos y usa el total para las métricas.</small>}

      <section className="nexi-context-chart">
        <header><Users size={15} /><strong>Por remitente</strong></header>
        {lastResult?.senders.length ? lastResult.senders.map(item => <div className="nexi-context-bar-row" key={item.label}>
          <span title={item.label}>{item.label}</span>
          <i><b style={{ width: `${Math.max(7, item.count / senderMax * 100)}%` }} /></i>
          <strong>{item.count}</strong>
        </div>) : <small>Los datos aparecerán al analizar el contexto.</small>}
      </section>

      <section className="nexi-context-chart">
        <header><BarChart3 size={15} /><strong>Por fecha</strong></header>
        {lastResult?.days.length ? lastResult.days.map(item => <div className="nexi-context-bar-row date" key={item.date}>
          <span>{shortDate(item.date)}</span>
          <i><b style={{ width: `${Math.max(7, item.count / dayMax * 100)}%` }} /></i>
          <strong>{item.count}</strong>
        </div>) : <small>Los datos aparecerán al analizar el contexto.</small>}
      </section>
    </aside>
  </section>
}
