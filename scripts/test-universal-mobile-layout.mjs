import { readFileSync } from 'node:fs'

const root = new URL('../', import.meta.url)
const main = readFileSync(new URL('src/frontend/src/main.tsx', root), 'utf8')
const inbox = readFileSync(new URL('src/frontend/src/pages/InboxPage.tsx', root), 'utf8')
const cssPath = new URL('src/frontend/src/styles/universal-mobile-layout.css', root)
const controlCenterCss = readFileSync(new URL('src/frontend/src/styles/control-center-cleanup.css', root), 'utf8')

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
  '.global-nexo-perspective',
  '.nexi-control-header',
  '.control-tabs-inline',
  'font-size: 15px',
  'font-size: 13px',
  'min-height: 44px',
  'grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))',
]

for (const token of required) {
  if (!css.includes(token)) throw new Error(`Falta contrato móvil: ${token}`)
}

const perspectiveRule = css.match(/\.global-nexo-perspective\s*\{([^}]*)\}/)?.[1] ?? ''
if (!perspectiveRule.includes('display: none !important')) {
  throw new Error('Perspectiva debe ocultarse completamente en teléfonos.')
}

const metricGrid = controlCenterCss.match(/\.nexi-control-metrics\.compact\s*\{([^}]*)\}/)?.[1] ?? ''
if (!metricGrid.includes('grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))')) {
  throw new Error('Las métricas deben completar el ancho central también en escritorio.')
}

for (const className of ['row-check', 'account-dot', 'sender', 'subject', 'row-mail-actions']) {
  if (!inbox.includes(`className="${className}`) && !inbox.includes(`className={\`${className}`)) {
    throw new Error(`La bandeja ya no expone la clase móvil ${className}.`)
  }
}

console.log('PASS: estructura responsiva universal y bandeja móvil')
