import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const layout = readFileSync(new URL('../src/frontend/src/layouts/AppLayout.tsx', import.meta.url), 'utf8')
const search = readFileSync(new URL('../src/frontend/src/pages/SearchPage.tsx', import.meta.url), 'utf8')
const users = readFileSync(new URL('../src/frontend/src/pages/AdminUsersPage.tsx', import.meta.url), 'utf8')

assert.match(layout, /isOwner \? <NavLink to="\/admin\/users"[^>]*data-sidebar-tooltip="Panel de Administración"/)
assert.match(layout, /<span>Panel de Administración<\/span>/)
assert.match(layout, /: <NavLink to="\/settings\/plan"[^>]*data-sidebar-tooltip="Plan y uso"/)
assert.match(layout, /navigate\(isOwner \? '\/admin\/users' : '\/settings\/plan'\)/)

assert.match(search, /function returnFromSearch\(\)/)
assert.match(search, /navigate\(-1\)/)
assert.match(search, /\/control-center\?account=/)
assert.match(search, /<ArrowLeft size=\{16\} \/> Volver/)

for (const destination of ['/admin/plans', '/admin/ai-usage', '/settings/plan']) {
  assert.ok(users.includes(`to="${destination}"`), `Falta acceso administrativo a ${destination}`)
}

console.log('PASS: navegación diferenciada para owner y retorno desde búsqueda')
