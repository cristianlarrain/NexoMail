import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const page = readFileSync(resolve(root, 'src/pages/ControlCenterPage.tsx'), 'utf8')
const controlCenter = readFileSync(resolve(root, 'src/components/ControlCenter.tsx'), 'utf8')
const priorityQueue = readFileSync(resolve(root, 'src/components/NexiPriorityQueue.tsx'), 'utf8')
const cleanupCss = readFileSync(resolve(root, 'src/styles/control-center-cleanup.css'), 'utf8')

if (page.includes('Nexi, la inteligencia que vive dentro de NexoMail.') || page.includes('Entiende, resume, prioriza y convierte tus correos en acciones.')) {
  throw new Error('El encabezado del Control Center debe ser compacto y no incluir el subtítulo promocional.')
}

for (const redundantCopy of [
  'Hallazgos y sugerencias',
  '<span>Resumen operativo</span>',
  'Abrir gestión',
  'Abrir seguimiento',
  'Ver correos',
  'Revisar pendientes',
]) {
  if (controlCenter.includes(redundantCopy)) {
    throw new Error(`Debe eliminarse el texto redundante: ${redundantCopy}`)
  }
}

if (!controlCenter.includes('control-summary-strip')) {
  throw new Error('El resumen operativo debe mostrarse como una franja compacta.')
}

if (!controlCenter.includes('control-metrics nexi-control-metrics compact')) {
  throw new Error('Los cuatro indicadores deben usar la variante compacta.')
}

const summaryIndex = controlCenter.indexOf('control-summary-strip')
const metricsIndex = controlCenter.indexOf('control-metrics nexi-control-metrics compact')
const priorityIndex = controlCenter.indexOf('<NexiPriorityQueue')

if (!(summaryIndex >= 0 && summaryIndex < metricsIndex && metricsIndex < priorityIndex)) {
  throw new Error('El orden debe ser resumen compacto > indicadores > Priorización inteligente.')
}

if (controlCenter.includes('nexi-insights-panel nexi-control-summary')) {
  throw new Error('El resumen y los indicadores no deben estar dentro de otra caja de sección.')
}

if (!priorityQueue.includes('Priorización inteligente') || !priorityQueue.includes('Qué atender primero')) {
  throw new Error('Debe mantenerse la jerarquía Priorización inteligente > Qué atender primero.')
}

if (priorityQueue.includes('Nexi ordena las conversaciones y permite resumir, responder o dar seguimiento desde la misma grilla.')) {
  throw new Error('La priorización no debe incluir texto explicativo redundante.')
}

for (const marker of ['.control-summary-strip', '.nexi-control-metrics.compact', '.control-center-page .control-tabs-inline']) {
  if (!cleanupCss.includes(marker)) throw new Error(`Falta el estilo minimalista requerido: ${marker}`)
}

console.log('PASS minimalist Control Center hierarchy')
