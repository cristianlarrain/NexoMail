import { readFileSync } from 'node:fs'

const root = new URL('../', import.meta.url)
const main = readFileSync(new URL('src/frontend/src/main.tsx', root), 'utf8')
const inbox = readFileSync(new URL('src/frontend/src/pages/InboxPage.tsx', root), 'utf8')
const cssPath = new URL('src/frontend/src/styles/universal-mobile-layout.css', root)

if (!main.includes("import './styles/universal-mobile-layout.css'")) {
  throw new Error('La capa responsiva universal no está cargada.')
}

const css = readFileSync(cssPath, 'utf8')
const required = [
  '@media (max-width: 767px)',
  'grid-template-areas:',
  '"menu avatar"',
  '"search search"',
  '.topbar .top-search',
  '.message-list-header',
  'display: none',
  '.message-row',
  'grid-template-areas:',
  '"check dot sender time actions"',
  '"subject subject subject subject subject"',
  '.message-row .subject',
  'overflow-x: auto',
]

for (const token of required) {
  if (!css.includes(token)) throw new Error(`Falta contrato móvil: ${token}`)
}

for (const className of ['row-check', 'account-dot', 'sender', 'subject', 'row-mail-actions']) {
  if (!inbox.includes(`className="${className}`) && !inbox.includes(`className={\`${className}`)) {
    throw new Error(`La bandeja ya no expone la clase móvil ${className}.`)
  }
}

console.log('PASS: estructura responsiva universal y bandeja móvil')
