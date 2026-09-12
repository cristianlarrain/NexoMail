import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const layout = readFileSync(resolve(root, 'src/layouts/AppLayout.tsx'), 'utf8')

const requiredMarkers = [
  "const showGlobalPerspective = location.pathname === '/inbox'",
  "location.pathname.startsWith('/account/')",
  "location.pathname.startsWith('/message/')",
  '{showGlobalPerspective && <div className="global-nexo-perspective"><NexoPerspective contextKey={location.pathname} /></div>}',
]

for (const marker of requiredMarkers) {
  if (!layout.includes(marker)) throw new Error(`Falta limitar Perspectiva a bandeja y detalle de correo: ${marker}`)
}

if (layout.includes('<div className="global-nexo-perspective"><NexoPerspective contextKey={location.pathname} /></div>\n      <Outlet />')) {
  throw new Error('Perspectiva no debe renderizarse globalmente en todas las páginas autenticadas.')
}

console.log('PASS perspective visibility')
