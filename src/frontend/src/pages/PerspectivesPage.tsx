import { useEffect, useState } from 'react'
import { BookMarked, Share2, Sparkles, Trash2, X } from 'lucide-react'
import { nexiApi } from '../api/nexiApi'
import { NexiVisual } from '../components/nexi/NexiVisual'
import {
  getSavedPerspectives,
  removeSavedPerspective,
  sharePerspective,
  subscribeToPerspectiveChanges,
  type SavedPerspective,
} from '../utils/perspectiveCollection'

export function PerspectivesPage() {
  const [items, setItems] = useState<SavedPerspective[]>(() => getSavedPerspectives())
  const [feedback, setFeedback] = useState<string | null>(null)
  const [loadingId, setLoadingId] = useState<string | null>(null)
  const [expansions, setExpansions] = useState<Record<string, string>>({})
  const [expansionErrors, setExpansionErrors] = useState<Record<string, string>>({})

  useEffect(() => subscribeToPerspectiveChanges(() => setItems(getSavedPerspectives())), [])

  async function share(item: SavedPerspective) {
    try {
      const result = await sharePerspective(item)
      setFeedback(result === 'copied' ? 'Pensamiento copiado para compartir.' : 'Pensamiento compartido.')
      window.setTimeout(() => setFeedback(null), 2600)
    } catch (error) {
      if ((error as DOMException)?.name === 'AbortError') return
      setFeedback('No fue posible compartir este pensamiento.')
      window.setTimeout(() => setFeedback(null), 2600)
    }
  }

  async function expandWithNexi(item: SavedPerspective) {
    if (loadingId) return
    setLoadingId(item.id)
    setExpansionErrors(current => ({ ...current, [item.id]: '' }))
    try {
      const result = await nexiApi.expandPerspective(item.text, item.source, item.area)
      setExpansions(current => ({ ...current, [item.id]: result.text }))
    } catch (error) {
      setExpansionErrors(current => ({
        ...current,
        [item.id]: error instanceof Error ? error.message : 'Nexi no pudo ampliar esta perspectiva.',
      }))
    } finally {
      setLoadingId(null)
    }
  }

  function remove(item: SavedPerspective) {
    removeSavedPerspective(item.id)
    setExpansions(current => {
      const next = { ...current }
      delete next[item.id]
      return next
    })
    setExpansionErrors(current => {
      const next = { ...current }
      delete next[item.id]
      return next
    })
  }

  return <section className="mail-view perspectives-page">
    <div className="view-header perspectives-header">
      <div>
        <p className="eyebrow">Colección personal</p>
        <h1>Perspectivas guardadas</h1>
        <p>Ideas y pensamientos que decidiste conservar mientras usabas NexoMail. Puedes pedirle a Nexi que desarrolle cualquiera de ellos.</p>
      </div>
      <span className="perspectives-count"><BookMarked size={16} /> {items.length} guardadas</span>
    </div>

    {feedback && <div className="perspectives-feedback" role="status">{feedback}</div>}

    {items.length === 0
      ? <div className="perspectives-empty">
          <BookMarked size={28} />
          <h2>Todavía no guardas perspectivas</h2>
          <p>Cuando una perspectiva te interese, pulsa Guardar. Aparecerá aquí para volver a leerla, compartirla o profundizarla con Nexi.</p>
        </div>
      : <div className="perspectives-grid">
          {items.map(item => <article className="perspective-card" key={item.id}>
            <header>
              <span>Perspectiva · {item.area}</span>
              <time dateTime={new Date(item.savedAt).toISOString()}>{new Date(item.savedAt).toLocaleDateString('es-CL')}</time>
            </header>
            <blockquote>“{item.text}”</blockquote>
            <cite>— {item.source}</cite>
            <footer>
              <button type="button" className="perspective-nexi-expand" disabled={Boolean(loadingId)} onClick={() => void expandWithNexi(item)}>
                {loadingId === item.id ? <NexiVisual size="small" /> : <Sparkles size={14} />}
                {loadingId === item.id ? 'Nexi está pensando…' : expansions[item.id] ? 'Ampliar nuevamente' : 'Ampliar con Nexi'}
              </button>
              <button type="button" onClick={() => void share(item)}><Share2 size={14} /> Compartir</button>
              <button type="button" className="danger-action" onClick={() => remove(item)}><Trash2 size={14} /> Quitar</button>
            </footer>

            {expansionErrors[item.id] && <div className="perspective-expansion-error" role="alert">{expansionErrors[item.id]}</div>}

            {expansions[item.id] && <section className="perspective-expansion">
              <header>
                <div className="perspective-expansion-title"><NexiVisual size="small" /><div><strong>Nexi amplía la idea</strong><span>Una lectura más profunda de este pensamiento.</span></div></div>
                <button type="button" className="icon-button" aria-label="Cerrar ampliación" title="Cerrar ampliación" onClick={() => setExpansions(current => ({ ...current, [item.id]: '' }))}><X size={15} /></button>
              </header>
              {expansions[item.id].split(/\n{2,}/).filter(Boolean).map((paragraph, index) => <p key={`${item.id}-${index}`}>{paragraph}</p>)}
            </section>}
          </article>)}
        </div>}
  </section>
}