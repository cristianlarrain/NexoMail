import { useEffect, useState } from 'react'
import { BookMarked, Share2, Trash2 } from 'lucide-react'
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

  return <section className="mail-view perspectives-page">
    <div className="view-header perspectives-header">
      <div>
        <p className="eyebrow">Colección personal</p>
        <h1>Perspectivas guardadas</h1>
        <p>Ideas y pensamientos que decidiste conservar mientras usabas NexoMail.</p>
      </div>
      <span className="perspectives-count"><BookMarked size={16} /> {items.length} guardadas</span>
    </div>

    {feedback && <div className="perspectives-feedback" role="status">{feedback}</div>}

    {items.length === 0
      ? <div className="perspectives-empty">
          <BookMarked size={28} />
          <h2>Todavía no guardas perspectivas</h2>
          <p>Cuando una perspectiva te interese, pulsa Guardar. Aparecerá aquí para volver a leerla o compartirla.</p>
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
              <button type="button" onClick={() => void share(item)}><Share2 size={14} /> Compartir</button>
              <button type="button" className="danger-action" onClick={() => removeSavedPerspective(item.id)}><Trash2 size={14} /> Quitar</button>
            </footer>
          </article>)}
        </div>}
  </section>
}
