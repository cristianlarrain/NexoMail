import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { FileText, MessagesSquare, Sparkles, X } from 'lucide-react'
import { nexiApi } from '../api/nexiApi'
import type { AiMessageInsight } from '../types/mail'
import { NexiVisual } from './nexi/NexiVisual'

export function MessageNexiReaderTools({ accountId, messageId }: { accountId: string; messageId: string }) {
  const [insight, setInsight] = useState<AiMessageInsight | null>(null)
  const [mode, setMode] = useState<'message' | 'thread' | null>(null)

  const analyze = useMutation({
    mutationFn: (includeThread: boolean) => nexiApi.summarizeMessage(accountId, messageId, includeThread),
    onSuccess: (result, includeThread) => {
      setInsight(result)
      setMode(includeThread ? 'thread' : 'message')
    },
  })

  return <section className="message-nexi-reader" aria-label="Herramientas de Nexi para este correo">
    <div className="message-nexi-reader-bar">
      <div className="message-nexi-reader-identity">
        <NexiVisual size="small" />
        <div><strong>Nexi</strong><span>Entiende este correo y su conversación.</span></div>
      </div>
      <div className="message-nexi-reader-actions">
        <button type="button" disabled={analyze.isPending} onClick={() => analyze.mutate(false)}>
          <FileText size={15} /> {analyze.isPending && mode !== 'thread' ? 'Resumiendo…' : 'Resumir correo'}
        </button>
        <button type="button" disabled={analyze.isPending} onClick={() => analyze.mutate(true)}>
          <MessagesSquare size={15} /> {analyze.isPending && mode === 'thread' ? 'Analizando…' : 'Analizar conversación'}
        </button>
      </div>
    </div>

    {analyze.isError && <div className="message-nexi-error">{analyze.error instanceof Error ? analyze.error.message : 'Nexi no pudo analizar este correo.'}</div>}

    {insight && <article className="message-nexi-result">
      <header>
        <div><Sparkles size={16} /><strong>{mode === 'thread' ? 'Análisis de la conversación' : 'Resumen del correo'}</strong></div>
        <button type="button" className="icon-button" onClick={() => { setInsight(null); setMode(null) }} aria-label="Cerrar análisis"><X size={16} /></button>
      </header>
      <div className="message-nexi-result-grid">
        <section><span>Resumen</span><p>{insight.summary}</p></section>
        <section><span>Qué significa</span><p>{insight.meaning}</p></section>
        {insight.requestedAction && <section className="message-nexi-action"><span>Acción solicitada</span><p>{insight.requestedAction}</p></section>}
        {insight.keyPoints.length > 0 && <section><span>Puntos clave</span><ul>{insight.keyPoints.map(point => <li key={point}>{point}</li>)}</ul></section>}
      </div>
    </article>}
  </section>
}
