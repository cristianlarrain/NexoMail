import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { BookMarked, ChevronDown, ChevronUp, Sparkles, Trash2 } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { commercialApi } from '../api/commercialApi'
import { nexiApi } from '../api/nexiApi'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { PerspectiveShareMenu } from '../components/PerspectiveShareMenu'
import { commercialEntitlements } from '../utils/commercialEntitlements'
import {
  getSavedPerspectives,
  removeSavedPerspective,
  savePerspectiveReflection,
  subscribeToPerspectiveChanges,
  type SavedPerspective,
} from '../utils/perspectiveCollection'

export function PerspectivesPage() {
  const [searchParams] = useSearchParams()
  const [items, setItems] = useState<SavedPerspective[]>(() => getSavedPerspectives())
  const [loadingId, setLoadingId] = useState<string | null>(null)
  const [collapsedReflections, setCollapsedReflections] = useState<Record<string, boolean>>({})
  const [expansionErrors, setExpansionErrors] = useState<Record<string, string>>({})
  const handledAutoAnalysis = useRef<string | null>(null)
  const { data: commercialSubscription } = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const hasNexi = commercialSubscription?.entitlements.includes(commercialEntitlements.nexiAi) === true
  const focusId = searchParams.get('focus') ?? ''
  const autoAnalyze = searchParams.get('analyze') === '1'
  const shouldAutoAnalyze = autoAnalyze && hasNexi

  useEffect(() => subscribeToPerspectiveChanges(() => setItems(getSavedPerspectives())), [])

  useEffect(() => {
    if (!focusId) return
    window.requestAnimationFrame(() => {
      document.getElementById(`perspective-${focusId}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
    })

    const focused = items.find(item => item.id === focusId)
    if (!focused || !shouldAutoAnalyze || focused.nexiReflection || handledAutoAnalysis.current === focusId) return
    handledAutoAnalysis.current = focusId
    void expandWithNexi(focused)
  }, [focusId, items, shouldAutoAnalyze])

  async function expandWithNexi(item: SavedPerspective) {
    if (!hasNexi) return
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
      handledAutoAnalysis.current = null
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
        <p>Ideas y pensamientos que decidiste conservar mientras usabas NexoMail.{hasNexi ? ' Nexi puede desarrollar una reflexión final y conservarla como propuesta activa para volver a revisarla.' : ''}</p>
      </div>
      <span className="perspectives-count"><BookMarked size={16} /> {items.length} guardadas</span>
    </div>

    {items.length === 0
      ? <div className="perspectives-empty">
          <BookMarked size={28} />
          <h2>Todavía no guardas perspectivas</h2>
          <p>Cuando una perspectiva te interese, pulsa Guardar. Aparecerá aquí para volver a leerla y compartirla{hasNexi ? ' o profundizarla con Nexi' : ''}.</p>
        </div>
      : <div className="perspectives-grid">
          {items.map(item => {
            const reflection = item.nexiReflection?.trim() ?? ''
            const collapsed = collapsedReflections[item.id] ?? true
            const focused = item.id === focusId
            return <article id={`perspective-${item.id}`} className={`perspective-card ${focused ? 'is-focused' : ''}`} key={item.id}>
              <header>
                <span>Perspectiva · {item.area}</span>
                <time dateTime={new Date(item.savedAt).toISOString()}>{new Date(item.savedAt).toLocaleDateString('es-CL')}</time>
              </header>
              <blockquote>“{item.text}”</blockquote>
              <cite>— {item.source}</cite>
              <footer>
                {hasNexi && !reflection && <button type="button" className="perspective-nexi-expand" disabled={Boolean(loadingId)} onClick={() => void expandWithNexi(item)}>
                  {loadingId === item.id ? <NexiVisual size="small" /> : <Sparkles size={14} />}
                  {loadingId === item.id ? 'Nexi está pensando…' : 'Analizar con Nexi'}
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
