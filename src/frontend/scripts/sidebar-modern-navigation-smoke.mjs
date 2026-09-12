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
]

for (const marker of layoutMarkers) {
  if (!layout.includes(marker)) throw new Error(`Falta navegación moderna en AppLayout: ${marker}`)
}

if (layout.includes('<button className="theme-switch"')) {
  throw new Error('El selector de tema no debe duplicarse en el sidebar; ya está disponible en el menú de perfil.')
}

const cssMarkers = [
  '.sidebar-nav-main',
  'overflow-y: auto',
  'scrollbar-width: none',
  '.sidebar-nav-main::-webkit-scrollbar',
  '.sidebar-nav-utilities',
  '.account-switcher-popover',
]
for (const marker of cssMarkers) {
  if (!modernCss.includes(marker)) throw new Error(`Falta estilo de navegación moderna: ${marker}`)
}

if (uiPolish.includes('scrollbar-gutter: stable')) {
  throw new Error('El sidebar no debe reservar espacio para una barra de desplazamiento visible.')
}

if (!main.includes("import './styles/sidebar-modern.css'")) {
  throw new Error('Falta importar sidebar-modern.css')
}

console.log('PASS modern sidebar navigation')
