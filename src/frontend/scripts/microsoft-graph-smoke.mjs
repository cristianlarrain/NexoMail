import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const pageNames = [
  'AccountsPage.tsx',
  'InboxPage.tsx',
  'MessagePage.tsx',
  'ComposePage.tsx',
  'NexiActionPlanPage.tsx',
  'NexiSearchActionPage.tsx',
]

const pages = new Map(pageNames.map(name => [
  name,
  readFileSync(resolve(root, 'src/pages', name), 'utf8'),
]))

for (const [name, source] of pages) {
  if (!source.includes('MicrosoftGraph')) {
    throw new Error(`${name} debe reconocer explícitamente el proveedor MicrosoftGraph`)
  }
}

const accounts = pages.get('AccountsPage.tsx') ?? ''
const requiredAccountMarkers = [
  'Agregar Microsoft 365',
  '/api/oauth/microsoft/start',
  "params.get('connected') === 'microsoft'",
]

for (const marker of requiredAccountMarkers) {
  if (!accounts.includes(marker)) {
    throw new Error(`AccountsPage debe incluir soporte Microsoft 365: ${marker}`)
  }
}

console.log('PASS Microsoft Graph Phase 1 UI')
