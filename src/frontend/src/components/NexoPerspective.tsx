import { useEffect, useState } from 'react'
import { BookOpenText } from 'lucide-react'

type Perspective = {
  text: string
  source: string
  area: 'Filosofía' | 'Psicología' | 'Academia' | 'Pensamiento crítico' | 'Contemporáneo' | 'Nexo'
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
    text: 'Pensar con claridad exige reconocer los sesgos que afectan incluso nuestras decisiones más razonadas.',
    source: 'Inspirado en Daniel Kahneman',
    area: 'Psicología',
  },
  {
    text: 'Una sociedad del rendimiento puede confundir actividad permanente con verdadero sentido.',
    source: 'Inspirado en Byung-Chul Han',
    area: 'Contemporáneo',
  },
  {
    text: 'La educación amplía nuestra capacidad de comprender vidas, argumentos y realidades distintas de la propia.',
    source: 'Inspirado en Martha Nussbaum',
    area: 'Academia',
  },
  {
    text: 'El conocimiento mejora cuando conecta disciplinas y acepta la complejidad en lugar de reducirla demasiado pronto.',
    source: 'Inspirado en Edgar Morin',
    area: 'Academia',
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
    text: 'Toda conclusión mejora cuando sabemos qué evidencia la sostiene y qué podría demostrar que estamos equivocados.',
    source: 'Pensamiento científico',
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
    text: 'Organizar no es sólo clasificar: es construir una forma más clara de comprender lo que tenemos delante.',
    source: 'Nexo',
    area: 'Nexo',
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
