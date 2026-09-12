import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const layout = readFileSync(resolve(root, 'src/layouts/AppLayout.tsx'), 'utf8')
const modernCss = readFileSync(resolve(root, 'src/styles/sidebar-modern.css'), 'utf8')
const uiPolish = readFileSync(resolve(root, 'src/styles/ui-polish.css'), 'utf8')
const main = readFileSync(resolve(root, 'src/main.tsx'), 'utf8')

const layoutMarkers = [
  "const [accountsOpen, setAccountsOpen] = useState(false)",
  "localStorage.getItem(FOLDERS_COLLAPSED_KEY) !== '0'",
  'className="sidebar-nav-main"',
  'account-switcher',
  'className="account-switcher-popover"',
  'account-switcher-option',
  'className="sidebar-nav-utilities"',
  'sidebar-primary-link',
]

for (const marker of layoutMarkers) {
  if (!layout.includes(marker)) throw new Error(`Falta navegación moderna en AppLayout: ${marker}`)
}

if (layout.includes('<button className="theme-switch"')) {
  throw new Error('El selector de tema no debe duplicarse en el sidebar; ya está disponible en el menú de perfil.')
}

if (!layout.includes('className={`nav-item compose-button sidebar-primary-link')) {
  throw new Error('Redactar debe usar el mismo lenguaje visual nav-item que el resto de la navegación.')
}

for (const marker of [
  'nav-item sidebar-primary-link inbox-primary-nav',
  'nav-item sidebar-primary-link control-center-nav',
]) {
  if (!layout.includes(marker)) throw new Error(`Falta navegación plana para: ${marker}`)
}

const inboxIcon = layout.indexOf('<Inbox className="primary-nav-icon"')
const inboxText = layout.indexOf('<span>Bandeja de Entrada</span>')
const composeIcon = layout.indexOf('<PenLine className="primary-nav-icon"')
const composeText = layout.indexOf('<span>Redactar</span>')
const nexiIcon = layout.indexOf('className="primary-nav-icon nexi-sidebar-icon"')
const nexiText = layout.indexOf('<span>Nexi Control Center</span>')

if (!(inboxIcon >= 0 && inboxIcon < inboxText)) throw new Error('Bandeja de entrada debe mostrar icono a la izquierda como los menús de carpetas.')
if (!(composeIcon >= 0 && composeIcon < composeText)) throw new Error('Redactar debe mostrar icono a la izquierda como los menús de carpetas.')
if (!(nexiIcon >= 0 && nexiIcon < nexiText)) throw new Error('Nexi Control Center debe mostrar icono a la izquierda como los menús de carpetas.')

const cssMarkers = [
  '.sidebar-nav-main',
  'overflow-y: auto',
  'scrollbar-width: none',
  '.sidebar-nav-main::-webkit-scrollbar',
  '.sidebar-nav-utilities',
  '.account-switcher-popover',
  '.sidebar .sidebar-primary-link',
  'justify-content: flex-start',
  'text-transform: none',
  'letter-spacing: normal',
  'box-shadow: inset 2px 0 0 var(--primary)',
]
for (const marker of cssMarkers) {
  if (!modernCss.includes(marker)) throw new Error(`Falta estilo de navegación moderna: ${marker}`)
}

for (const forbidden of [
  'drop-shadow(',
  '0 0 20px color-mix(in srgb, var(--primary)',
  '0 0 16px color-mix(in srgb, var(--primary)',
]) {
  if (modernCss.includes(forbidden)) throw new Error(`Los accesos principales no deben mantener glow permanente: ${forbidden}`)
}

if (uiPolish.includes('scrollbar-gutter: stable')) {
  throw new Error('El sidebar no debe reservar espacio para una barra de desplazamiento visible.')
}

if (!main.includes("import './styles/sidebar-modern.css'")) {
  throw new Error('Falta importar sidebar-modern.css')
}

console.log('PASS modern flat sidebar navigation')
