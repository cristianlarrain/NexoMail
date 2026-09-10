import { useEffect, useState } from 'react'
import { BookMarked, ChevronDown, ChevronUp, Sparkles, Trash2 } from 'lucide-react'
import { nexiApi } from '../api/nexiApi'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { PerspectiveShareMenu } from '../components/PerspectiveShareMenu'
import {
  getSavedPerspectives,
  removeSavedPerspective,
  savePerspectiveReflection,
  subscribeToPerspectiveChanges,
  type SavedPerspective,
} from '../utils/perspectiveCollection'

export function PerspectivesPage() {
  const [items, setItems] = useState<SavedPerspective[]>(() => getSavedPerspectives())
  const [loadingId, setLoadingId] = useState<string | null>(null)
  const [collapsedReflections, setCollapsedReflections] = useState<Record<string, boolean>>({})
  const [expansionErrors, setExpansionErrors] = useState<Record<string, string>>({})

  useEffect(() => subscribeToPerspectiveChanges(() => setItems(getSavedPerspectives())), [])

  async function expandWithNexi(item: SavedPerspective) {
    if (item.nexiReflection) {
      setCollapsedReflections(current => ({ ...current, [item.id]: false }))
      return
    }
    if (loadingId) return
    setLoadingId(item.id)
    setExpansionErrors(current => ({ ...current, [item.id]: '' }))
    try {
      const result = await nexiApi.expandPerspective(item.text, item.source, item.area)
      savePerspectiveReflection(item.id, result.text)
      setCollapsedReflections(current => ({ ...current, [item.id]: false }))
    } catch (error) {
      setExpansionErrors(current => ({
        ...current,
        [item.id]: error instanceof Error ? error.message : 'Nexi no pudo ampliar esta perspectiva.',
      }))
    } finally {
      setLoadingId(null)
    }
  }

  function toggleReflection(itemId: string) {
    setCollapsedReflections(current => ({ ...current, [itemId]: !current[itemId] }))
  }

  function remove(item: SavedPerspective) {
    removeSavedPerspective(item.id)
    setCollapsedReflections(current => {
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
        <p>Ideas y pensamientos que decidiste conservar mientras usabas NexoMail. Nexi puede desarrollar una reflexión final y conservarla como propuesta activa para volver a revisarla.</p>
      </div>
      <span className="perspectives-count"><BookMarked size={16} /> {items.length} guardadas</span>
    </div>

    {items.length === 0
      ? <div className="perspectives-empty">
          <BookMarked size={28} />
          <h2>Todavía no guardas perspectivas</h2>
          <p>Cuando una perspectiva te interese, pulsa Guardar. Aparecerá aquí para volver a leerla, compartirla o profundizarla con Nexi.</p>
        </div>
      : <div className="perspectives-grid">
          {items.map(item => {
            const reflection = item.nexiReflection?.trim() ?? ''
            const collapsed = Boolean(collapsedReflections[item.id])
            return <article className="perspective-card" key={item.id}>
              <header>
                <span>Perspectiva · {item.area}</span>
                <time dateTime={new Date(item.savedAt).toISOString()}>{new Date(item.savedAt).toLocaleDateString('es-CL')}</time>
              </header>
              <blockquote>“{item.text}”</blockquote>
              <cite>— {item.source}</cite>
              <footer>
                {!reflection && <button type="button" className="perspective-nexi-expand" disabled={Boolean(loadingId)} onClick={() => void expandWithNexi(item)}>
                  {loadingId === item.id ? <NexiVisual size="small" /> : <Sparkles size={14} />}
                  {loadingId === item.id ? 'Nexi está pensando…' : 'Profundizar con Nexi'}
                </button>}
                {reflection && collapsed && <button type="button" className="perspective-nexi-expand" onClick={() => toggleReflection(item.id)}>
                  <ChevronDown size={14} /> Revisar reflexión de Nexi
                </button>}
                <PerspectiveShareMenu perspective={item} />
                <button type="button" className="danger-action" onClick={() => remove(item)}><Trash2 size={14} /> Quitar</button>
              </footer>

              {expansionErrors[item.id] && <div className="perspective-expansion-error" role="alert">{expansionErrors[item.id]}</div>}

              {reflection && !collapsed && <section className="perspective-expansion">
                <header>
                  <div className="perspective-expansion-title"><NexiVisual size="small" /><div><strong>Propuesta activa de Nexi</strong><span>Reflexión final guardada para volver a revisarla cuando quieras.</span></div></div>
                  <button type="button" className="nexo-perspective-action" aria-label="Colapsar reflexión" title="Colapsar reflexión" onClick={() => toggleReflection(item.id)}><ChevronUp size={14} /><span>Colapsar</span></button>
                </header>
                {reflection.split(/\n{2,}/).filter(Boolean).map((paragraph, index) => <p key={`${item.id}-${index}`}>{paragraph}</p>)}
              </section>}
            </article>
          })}
        </div>}
  </section>
}