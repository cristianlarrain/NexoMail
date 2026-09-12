import assert from 'node:assert/strict'
import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const read = (path) => {
  const fullPath = resolve(root, path)
  return existsSync(fullPath) ? readFileSync(fullPath, 'utf8') : ''
}

const router = read('src/router.tsx')
const layout = read('src/layouts/AppLayout.tsx')
const page = read('src/pages/AdminAiUsagePage.tsx')
const api = read('src/api/aiUsageApi.ts')
const css = read('src/styles/ai-usage-admin.css')
const main = read('src/main.tsx')

assert(router.includes('/admin/ai-usage'), 'Owner usage route is required')
assert(layout.includes('Consumo Nexi'), 'Owner navigation entry is required')
assert(layout.includes("effectivePlanCode === 'owner'"), 'Owner navigation must depend on effective Owner code')
assert(page.includes('Costo estimado'), 'Usage page must show cost metrics')
assert(page.includes('Proyección'), 'Usage page must show projections')
assert(api.includes('/api/ai-usage/admin'), 'Typed Nexi usage API client is required')

for (const label of [
  'Esta semana',
  'Semana anterior',
  'Variación semanal',
  'Este mes',
  'Usuarios activos',
  'Costo promedio',
  'Costo acumulado',
  'Proyección',
  'Operaciones',
  'Tokens',
  'Ver detalle',
  'Umbral verde',
  'Umbral amarillo',
]) {
  assert(page.includes(label), `Usage page must include: ${label}`)
}

assert(css.includes('var(--muted-foreground)'), 'Usage styles must use theme-safe secondary text color')
assert(!css.includes('color: var(--muted)'), 'Usage styles must not use background token as text color')
assert(main.includes("./styles/ai-usage-admin.css"), 'Usage styles must be loaded')

console.log('Nexi usage admin smoke passed.')
