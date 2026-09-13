import { readFileSync } from 'node:fs'

const root = new URL('../', import.meta.url)
const read = path => readFileSync(new URL(path, root), 'utf8')
const layout = read('src/frontend/src/layouts/AppLayout.tsx')
const router = read('src/frontend/src/router.tsx')
const page = read('src/frontend/src/pages/ControlCenterPage.tsx')
const center = read('src/frontend/src/components/ControlCenter.tsx')
const queue = read('src/frontend/src/components/NexiPriorityQueue.tsx')
const api = read('src/frontend/src/api/mailApi.ts')
const endpoints = read('src/backend/NexoMail.Api/ControlCenterTrackingEndpoints.cs')
const service = read('src/backend/NexoMail.Infrastructure/ControlCenterTrackingService.cs')
const mobile = read('src/frontend/src/styles/universal-mobile-layout.css')

const checks = [
  [layout, "navigate('/control-center')", 'El logotipo no abre Nexi Control Center.'],
  [layout, '>Inicio</span>', 'La navegación no presenta Inicio.'],
  [layout, '>Vista clásica</span>', 'La navegación no presenta Vista clásica.'],
  [router, "path: '/inbox'", 'La Vista clásica perdió su ruta.'],
  [page, 'Vista clásica', 'El encabezado no ofrece acceso a Vista clásica.'],
  [layout, "location.pathname === '/control-center'", 'El selector lateral no aplica el filtro al Control Center.'],
  [queue, 'Quitar urgencia', 'Falta la acción para quitar urgencia.'],
  [queue, '>Resolver<', 'Falta la acción directa Resolver.'],
  [queue, '>Posponer<', 'Falta la acción directa Posponer.'],
  [queue, '>Eliminar<', 'Falta la acción directa Eliminar.'],
  [api, 'priorityOverrides', 'El cliente no consulta las exclusiones de urgencia.'],
  [endpoints, 'priority-overrides', 'El backend no expone exclusiones de urgencia.'],
  [service, 'not_urgent', 'El backend no persiste la exclusión de urgencia.'],
  [mobile, '.nexi-priority-row', 'Faltan reglas móviles para las filas operativas.'],
]

for (const [source, token, message] of checks) {
  if (!source.includes(token)) throw new Error(message)
}

console.log('PASS: Nexi funciona como bandeja operativa principal')
