import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const controlCenter = readFileSync(resolve(root, 'src/components/ControlCenter.tsx'), 'utf8')
const priorityQueue = readFileSync(resolve(root, 'src/components/NexiPriorityQueue.tsx'), 'utf8')
const cleanupCss = readFileSync(resolve(root, 'src/styles/control-center-cleanup.css'), 'utf8')

const findingsIndex = controlCenter.indexOf('Hallazgos y sugerencias')
const summaryIndex = controlCenter.indexOf('Resumen operativo')
const metricsIndex = controlCenter.indexOf('control-metrics nexi-control-metrics')
const priorityIndex = controlCenter.indexOf('<NexiPriorityQueue')

if ([findingsIndex, summaryIndex, metricsIndex, priorityIndex].some(index => index < 0)) {
  throw new Error('Falta alguno de los bloques requeridos del nuevo orden del Control Center.')
}

if (!(findingsIndex < summaryIndex && summaryIndex < metricsIndex && metricsIndex < priorityIndex)) {
  throw new Error('El orden debe ser Hallazgos y sugerencias > Resumen operativo > indicadores > Priorización inteligente.')
}

for (const redundantCopy of [
  'Indicadores clave',
  'Vista rápida de pendientes, lectura y antigüedad.',
]) {
  if (controlCenter.includes(redundantCopy)) {
    throw new Error(`Debe eliminarse el texto redundante: ${redundantCopy}`)
  }
}

if (cleanupCss.includes('content: "Nexi";')) {
  throw new Error('El título Nexi no debe generarse por CSS dentro de Hallazgos y sugerencias.')
}

if (cleanupCss.includes('content: "Hallazgos y sugerencias de esta vista.";')) {
  throw new Error('Hallazgos y sugerencias debe existir como título real, no como texto generado por CSS.')
}

if (!priorityQueue.includes('Priorización inteligente') || !priorityQueue.includes('Qué atender primero')) {
  throw new Error('Debe mantenerse la jerarquía Priorización inteligente > Qué atender primero.')
}

if (priorityQueue.includes('Nexi ordena las conversaciones y permite resumir, responder o dar seguimiento desde la misma grilla.')) {
  throw new Error('La priorización no debe incluir el texto explicativo redundante.')
}

console.log('PASS concise Control Center hierarchy')
