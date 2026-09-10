import { useEffect, useState } from 'react'
import { Bookmark, BookOpenText, Check, RefreshCw, Sparkles } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { PerspectiveShareMenu } from './PerspectiveShareMenu'
import {
  isPerspectiveSaved,
  removeSavedPerspectiveByContent,
  savePerspective,
} from '../utils/perspectiveCollection'

type PerspectiveArea =
  | 'Filosofía'
  | 'Psicología'
  | 'Academia'
  | 'Pensamiento crítico'
  | 'Contemporáneo'
  | 'Budismo'
  | 'Judaísmo'
  | 'Cristianismo'
  | 'Estoicismo'
  | 'Nexo'

type Perspective = {
  text: string
  source: string
  area: PerspectiveArea
}

const perspectives: Perspective[] = [
  { text: 'La vida examinada exige atención, preguntas y disposición a corregir nuestras propias certezas.', source: 'Inspirado en Sócrates', area: 'Filosofía' },
  { text: 'Pensar con libertad también exige aceptar que una idea valiosa puede obligarnos a cambiar de opinión.', source: 'Perspectiva filosófica', area: 'Filosofía' },
  { text: 'La duda bien utilizada no paraliza: abre espacio para comprender mejor aquello que parecía evidente.', source: 'Inspirado en la tradición filosófica', area: 'Filosofía' },
  { text: 'Conocerse también implica reconocer aquello que preferimos no mirar.', source: 'Inspirado en Carl Jung', area: 'Psicología' },
  { text: 'La intuición decide rápido; el juicio riguroso necesita detenerse y revisar sus supuestos.', source: 'Inspirado en Daniel Kahneman', area: 'Psicología' },
  { text: 'Comprender una reacción propia suele ser más útil que justificarla inmediatamente.', source: 'Perspectiva psicológica', area: 'Psicología' },
  { text: 'Lo que evitamos observar puede seguir influyendo en nuestras decisiones aunque no lo nombremos.', source: 'Perspectiva psicológica', area: 'Psicología' },
  { text: 'Aprender no es acumular respuestas, sino mejorar la calidad de las preguntas.', source: 'Perspectiva académica', area: 'Academia' },
  { text: 'Una educación amplia no sólo prepara para producir: también prepara para comprender a otros y deliberar mejor.', source: 'Inspirado en Martha Nussbaum', area: 'Academia' },
  { text: 'Los problemas complejos rara vez pertenecen a una sola disciplina; comprenderlos exige conectar saberes.', source: 'Inspirado en Edgar Morin', area: 'Academia' },
  { text: 'La universidad no sólo transmite conocimiento: enseña a contrastarlo, justificarlo y ponerlo a prueba.', source: 'Perspectiva académica', area: 'Academia' },
  { text: 'Una buena investigación comienza cuando distinguimos con claridad lo que sabemos, lo que suponemos y lo que todavía debemos comprobar.', source: 'Método académico', area: 'Academia' },
  { text: 'El conocimiento se fortalece cuando puede ser explicado con claridad, contrastado con evidencia y revisado por otros.', source: 'Perspectiva académica', area: 'Academia' },
  { text: 'Una idea adquiere valor cuando puede ser examinada, discutida y, si corresponde, refutada.', source: 'Método y pensamiento crítico', area: 'Pensamiento crítico' },
  { text: 'Toda conclusión mejora cuando sabemos qué evidencia la sostiene y qué podría demostrar que estamos equivocados.', source: 'Pensamiento científico', area: 'Pensamiento crítico' },
  { text: 'Antes de aceptar una afirmación conviene preguntar quién la sostiene, con qué evidencia y qué explicación alternativa existe.', source: 'Pensamiento crítico', area: 'Pensamiento crítico' },
  { text: 'El exceso de información no garantiza comprensión; también necesitamos silencio, selección y criterio.', source: 'Inspirado en Byung-Chul Han', area: 'Contemporáneo' },
  { text: 'Estar permanentemente conectado no significa necesariamente estar mejor informado ni más cerca de los demás.', source: 'Perspectiva contemporánea', area: 'Contemporáneo' },
  { text: 'La atención al presente permite distinguir entre lo que realmente ocurre y la historia que nuestra mente construye alrededor de ello.', source: 'Inspirado en la tradición budista', area: 'Budismo' },
  { text: 'Aferrarse a que todo permanezca igual produce sufrimiento cuando la realidad inevitablemente cambia.', source: 'Inspirado en la enseñanza budista sobre la impermanencia', area: 'Budismo' },
  { text: 'La compasión no elimina el discernimiento; permite actuar con firmeza sin convertir al otro en enemigo.', source: 'Perspectiva budista', area: 'Budismo' },
  { text: 'Una mente entrenada observa el pensamiento antes de convertirlo automáticamente en acción.', source: 'Inspirado en la práctica contemplativa budista', area: 'Budismo' },
  { text: 'Preguntar, discutir y volver sobre un texto puede ser una forma de respeto: comprender exige conversación.', source: 'Inspirado en la tradición de estudio judía', area: 'Judaísmo' },
  { text: 'La responsabilidad no termina en la intención; también considera el efecto que nuestras acciones tienen sobre los demás.', source: 'Perspectiva ética judía', area: 'Judaísmo' },
  { text: 'La memoria adquiere sentido cuando no sólo conserva el pasado, sino que orienta la manera de actuar en el presente.', source: 'Inspirado en la tradición judía', area: 'Judaísmo' },
  { text: 'La justicia cotidiana se construye también en decisiones pequeñas: cumplir la palabra, actuar con honestidad y reparar cuando corresponde.', source: 'Perspectiva ética judía', area: 'Judaísmo' },
  { text: 'La fe que no se traduce en acciones concretas pierde parte de su sentido práctico.', source: 'Inspirado en la tradición cristiana bíblica', area: 'Cristianismo' },
  { text: 'Tratar al otro como quisiéramos ser tratados convierte una convicción espiritual en una regla concreta de convivencia.', source: 'Inspirado en la enseñanza de Jesús', area: 'Cristianismo' },
  { text: 'Perdonar no obliga a negar el daño ni a renunciar a los límites; puede significar dejar de vivir gobernado por el resentimiento.', source: 'Perspectiva cristiana', area: 'Cristianismo' },
  { text: 'La humildad intelectual comienza cuando aceptamos que escuchar puede ser más importante que tener inmediatamente una respuesta.', source: 'Inspirado en la tradición cristiana', area: 'Cristianismo' },
  { text: 'No controlamos todo lo que ocurre, pero sí podemos trabajar sobre la respuesta que elegimos frente a ello.', source: 'Inspirado en Epicteto', area: 'Estoicismo' },
  { text: 'La serenidad aumenta cuando dejamos de exigir que la realidad obedezca siempre a nuestras expectativas.', source: 'Inspirado en Marco Aurelio', area: 'Estoicismo' },
  { text: 'Prepararse para la dificultad no es pesimismo: es reducir la sorpresa y fortalecer la capacidad de actuar con criterio.', source: 'Inspirado en Séneca', area: 'Estoicismo' },
  { text: 'El carácter se revela menos en lo que decimos valorar que en aquello que hacemos repetidamente.', source: 'Perspectiva estoica', area: 'Estoicismo' },
  { text: 'La atención ordena la información; el criterio decide qué merece convertirse en acción.', source: 'Nexo', area: 'Nexo' },
  { text: 'Pensar bien también supone distinguir entre lo urgente, lo importante y lo simplemente ruidoso.', source: 'Nexo', area: 'Nexo' },
  { text: 'Una herramienta es verdaderamente inteligente cuando ayuda a decidir mejor, no sólo cuando entrega más información.', source: 'Nexo', area: 'Nexo' },
]

function perspectiveIndex(contextKey: string) {
  const today = new Date()
  const dayKey = Number(`${today.getFullYear()}${today.getMonth() + 1}${today.getDate()}`)
  const contextHash = [...contextKey].reduce((total, character) => total + character.charCodeAt(0), 0)
  return (dayKey + contextHash) % perspectives.length
}

export function NexoPerspective({ contextKey = 'general' }: { contextKey?: string }) {
  const navigate = useNavigate()
  const [index, setIndex] = useState(() => perspectiveIndex(contextKey))
  const [saved, setSaved] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const perspective = perspectives[index]

  useEffect(() => {
    setIndex(perspectiveIndex(contextKey))
    const timer = window.setInterval(() => setIndex(current => (current + 1) % perspectives.length), 30000)
    return () => window.clearInterval(timer)
  }, [contextKey])

  useEffect(() => {
    setSaved(isPerspectiveSaved(perspective.text, perspective.source))
    setFeedback(null)
  }, [perspective.text, perspective.source])

  function showAnotherPerspective() {
    setIndex(current => (current + 1) % perspectives.length)
  }

  function toggleSaved() {
    if (saved) {
      removeSavedPerspectiveByContent(perspective.text, perspective.source)
      setSaved(false)
      setFeedback('Quitado de tu colección')
    } else {
      savePerspective(perspective)
      setSaved(true)
      setFeedback('Guardado en Perspectivas')
    }
    window.setTimeout(() => setFeedback(null), 2200)
  }

  function analyzePerspective() {
    const savedPerspective = savePerspective(perspective)
    setSaved(true)
    navigate(`/perspectives?focus=${encodeURIComponent(savedPerspective.id)}&analyze=1`)
  }

  return <aside className="nexo-perspective" aria-label="Perspectiva intelectual de Nexo">
    <span className="nexo-perspective-icon" aria-hidden="true"><BookOpenText size={15} /></span>
    <div className="nexo-perspective-copy">
      <span className="nexo-perspective-label">Perspectiva · {perspective.area}</span>
      <p>{perspective.text} <cite>— {perspective.source}</cite></p>
      {feedback && <small className="nexo-perspective-feedback" role="status">{feedback}</small>}
    </div>
    <div className="nexo-perspective-actions">
      <button type="button" className="nexo-perspective-action nexo-perspective-analyze" onClick={analyzePerspective} title="Analizar esta perspectiva con Nexi" aria-label="Analizar esta perspectiva con Nexi">
        <Sparkles size={13} /><span>Analizar</span>
      </button>
      <button type="button" className={`nexo-perspective-action ${saved ? 'is-saved' : ''}`} onClick={toggleSaved} title={saved ? 'Quitar de Perspectivas' : 'Guardar en Perspectivas'} aria-label={saved ? 'Quitar de Perspectivas' : 'Guardar en Perspectivas'}>
        {saved ? <Check size={13} /> : <Bookmark size={13} />}<span>{saved ? 'Guardado' : 'Guardar'}</span>
      </button>
      <PerspectiveShareMenu perspective={perspective} compact />
      <button type="button" className="nexo-perspective-action" onClick={showAnotherPerspective} title="Mostrar otra perspectiva" aria-label="Mostrar otra perspectiva">
        <RefreshCw size={13} /><span>Otro mensaje</span>
      </button>
    </div>
  </aside>
}