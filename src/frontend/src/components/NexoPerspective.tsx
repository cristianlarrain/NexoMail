import { useEffect, useState } from 'react'
import { BookOpenText } from 'lucide-react'

type Perspective = {
  text: string
  source: string
  area: 'Filosofía' | 'Psicología' | 'Academia' | 'Pensamiento crítico' | 'Nexo'
}

const perspectives: Perspective[] = [
  {
    text: 'La vida examinada exige atención, preguntas y disposición a corregir nuestras propias certezas.',
    source: 'Inspirado en Sócrates',
    area: 'Filosofía',
  },
  {
    text: 'Conocerse también implica reconocer aquello que preferimos no mirar.',
    source: 'Inspirado en Carl Jung',
    area: 'Psicología',
  },
  {
    text: 'Aprender no es acumular respuestas, sino mejorar la calidad de las preguntas.',
    source: 'Perspectiva académica',
    area: 'Academia',
  },
  {
    text: 'Una idea adquiere valor cuando puede ser examinada, discutida y, si corresponde, refutada.',
    source: 'Método y pensamiento crítico',
    area: 'Pensamiento crítico',
  },
  {
    text: 'La atención ordena la información; el criterio decide qué merece convertirse en acción.',
    source: 'Nexo',
    area: 'Nexo',
  },
  {
    text: 'Pensar bien también supone distinguir entre lo urgente, lo importante y lo simplemente ruidoso.',
    source: 'Nexo',
    area: 'Nexo',
  },
  {
    text: 'La universidad no sólo transmite conocimiento: enseña a contrastarlo, justificarlo y ponerlo a prueba.',
    source: 'Perspectiva académica',
    area: 'Academia',
  },
  {
    text: 'Toda conclusión mejora cuando sabemos qué evidencia la sostiene y qué podría demostrar que estamos equivocados.',
    source: 'Pensamiento científico',
    area: 'Pensamiento crítico',
  },
]

function initialPerspectiveIndex() {
  const today = new Date()
  const dayKey = Number(`${today.getFullYear()}${today.getMonth() + 1}${today.getDate()}`)
  return dayKey % perspectives.length
}

export function NexoPerspective() {
  const [index, setIndex] = useState(initialPerspectiveIndex)
  const perspective = perspectives[index]

  useEffect(() => {
    const timer = window.setInterval(() => {
      setIndex(current => (current + 1) % perspectives.length)
    }, 30000)

    return () => window.clearInterval(timer)
  }, [])

  return <aside className="nexo-perspective" aria-label="Perspectiva intelectual de Nexo">
    <span className="nexo-perspective-icon" aria-hidden="true"><BookOpenText size={15} /></span>
    <div className="nexo-perspective-copy">
      <span className="nexo-perspective-label">Perspectiva · {perspective.area}</span>
      <p>“{perspective.text}” <cite>— {perspective.source}</cite></p>
    </div>
  </aside>
}
