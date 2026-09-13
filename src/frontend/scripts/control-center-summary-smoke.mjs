import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const page = readFileSync(resolve(root, 'src/pages/ControlCenterPage.tsx'), 'utf8')
const controlCenter = readFileSync(resolve(root, 'src/components/ControlCenter.tsx'), 'utf8')
const priorityQueue = readFileSync(resolve(root, 'src/components/NexiPriorityQueue.tsx'), 'utf8')
const cleanupCss = readFileSync(resolve(root, 'src/styles/control-center-cleanup.css'), 'utf8')
const spacingCss = readFileSync(resolve(root, 'src/styles/control-center-spacing.css'), 'utf8')

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

if (!controlCenter.includes('control-metrics nexi-control-metrics compact')) {
  throw new Error('Los cuatro indicadores deben usar la variante compacta.')
}

const metricsIndex = controlCenter.indexOf('control-metrics nexi-control-metrics compact')
const priorityIndex = controlCenter.indexOf('<NexiPriorityQueue')

if (!(metricsIndex >= 0 && metricsIndex < priorityIndex)) {
  throw new Error('El orden debe ser indicadores compactos > Priorización inteligente.')
}

if (controlCenter.includes('nexi-insights-panel nexi-control-summary')) {
  throw new Error('Los indicadores no deben estar dentro de otra caja de sección.')
}

if (controlCenter.includes('control-summary-strip')) {
  throw new Error('La franja de resumen operativo debe eliminarse del DOM por ser redundante con las tarjetas.')
}

if (controlCenter.includes('control-center-meta')) {
  throw new Error('Actualizado no debe ocupar una fila propia sobre las tarjetas.')
}

if (!controlCenter.includes('onUpdatedAtChange')) {
  throw new Error('ControlCenter debe comunicar la hora de actualización al encabezado de la página.')
}

if (!page.includes('control-page-updated') || !page.includes('onUpdatedAtChange={setUpdatedAt}')) {
  throw new Error('La hora Actualizado debe mostrarse en la línea superior del Control Center.')
}

const metricsSpacingStart = spacingCss.indexOf('.nexi-control-center .nexi-control-metrics')
if (metricsSpacingStart < 0) throw new Error('Falta la regla de espaciado de las métricas.')
const metricsSpacingBlock = spacingCss.slice(metricsSpacingStart, metricsSpacingStart + 180)
if (!metricsSpacingBlock.includes('padding: 0;')) {
  throw new Error('Las tarjetas deben ocupar el mismo ancho útil que Priorización inteligente, sin inset lateral.')
}

if (!cleanupCss.includes('.control-center-page .control-page-updated')) {
  throw new Error('Falta el estilo discreto para Actualizado en el encabezado.')
}

if (!priorityQueue.includes('Priorización inteligente') || !priorityQueue.includes('Qué atender primero')) {
  throw new Error('Debe mantenerse la jerarquía Priorización inteligente > Qué atender primero.')
}

if (priorityQueue.includes('Revisar con Nexi')) {
  throw new Error('El botón principal debe decir Analizar con Nexi.')
}

if (!priorityQueue.includes("'Analizar con Nexi'")) {
  throw new Error('Falta la etiqueta Analizar con Nexi en el botón principal.')
}

if (!priorityQueue.includes('nexi-priority-refine nexi-glow-action')) {
  throw new Error('El botón Analizar con Nexi debe usar la variante turquesa glow.')
}

if (priorityQueue.includes('Nexi ordena las conversaciones y permite resumir, responder o dar seguimiento desde la misma grilla.')) {
  throw new Error('La priorización no debe incluir texto explicativo redundante.')
}

for (const marker of ['.nexi-control-metrics.compact', '.control-center-page .control-tabs-inline', '.control-center-page .nexi-priority-refine.nexi-glow-action']) {
  if (!cleanupCss.includes(marker)) throw new Error(`Falta el estilo minimalista requerido: ${marker}`)
}

if (!cleanupCss.includes('background: var(--primary);') || !cleanupCss.includes('box-shadow: 0 0 18px')) {
  throw new Error('El botón Analizar con Nexi debe usar el turquesa principal con glow.')
}

console.log('PASS minimalist Control Center hierarchy')
