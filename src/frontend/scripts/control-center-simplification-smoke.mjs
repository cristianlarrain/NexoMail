import { readFileSync } from 'node:fs'

function ensure(condition, message) {
  if (!condition) throw new Error(message)
}

const appLayout = readFileSync('src/layouts/AppLayout.tsx', 'utf8')
const controlCenterPage = readFileSync('src/pages/ControlCenterPage.tsx', 'utf8')
const priorityQueue = readFileSync('src/components/NexiPriorityQueue.tsx', 'utf8')

ensure(appLayout.includes('<TopSearchBox'), 'El buscador superior debe mantenerse como parte del layout base.')
ensure(!appLayout.includes('<WeatherWidget'), 'El widget de clima debe retirarse del layout superior.')
ensure(!appLayout.includes('className="operations-clock"'), 'El widget de fecha y hora debe retirarse del layout superior.')
ensure(!controlCenterPage.includes('control-classic-link'), 'El botón Vista clásica debe retirarse del encabezado del Control Center.')
ensure(!priorityQueue.includes('className="nexi-priority-summary"'), 'La fila de filtros duplicados debe retirarse de Priorización inteligente.')
ensure(!priorityQueue.includes("'Analizar con Nexi'"), 'El botón Analizar con Nexi debe retirarse de Priorización inteligente.')
ensure(!priorityQueue.includes("'Analizado con Nexi'"), 'El estado Analizado con Nexi no debe mostrarse en el encabezado.')

console.log('PASS control center simplification')
