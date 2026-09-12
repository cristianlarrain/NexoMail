import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const layout = readFileSync(resolve(root, 'src/layouts/AppLayout.tsx'), 'utf8')
const cssPath = resolve(root, 'src/styles/sidebar-tooltips.css')
let css = ''
try {
  css = readFileSync(cssPath, 'utf8')
} catch {
  // Expected to fail until the tooltip stylesheet exists.
}

const requiredLayoutMarkers = [
  'data-sidebar-tooltip="Inicio"',
  'data-sidebar-tooltip="Bandeja de entrada"',
  'data-sidebar-tooltip="Redactar"',
  'data-sidebar-tooltip="Nexi Control Center"',
  'data-sidebar-tooltip={account.displayName}',
  'data-sidebar-tooltip={foldersCollapsed ? \'Mostrar carpetas\' : \'Ocultar carpetas\'}',
  'data-sidebar-tooltip="Archivados"',
  'data-sidebar-tooltip="Ignorados"',
  'data-sidebar-tooltip="Enviados"',
  'data-sidebar-tooltip="Borradores"',
  'data-sidebar-tooltip="Spam"',
  'data-sidebar-tooltip="Papelera"',
  'data-sidebar-tooltip="Perspectivas"',
  'data-sidebar-tooltip="Plan y uso"',
  'data-sidebar-tooltip="Configuración"',
]

for (const marker of requiredLayoutMarkers) {
  if (!layout.includes(marker)) {
    throw new Error(`Falta tooltip en AppLayout: ${marker}`)
  }
}

const requiredCssMarkers = [
  '.sidebar.collapsed [data-sidebar-tooltip]',
  '.sidebar.collapsed [data-sidebar-tooltip]::after',
  'content: attr(data-sidebar-tooltip)',
  ':hover::after',
  ':focus-visible::after',
]

for (const marker of requiredCssMarkers) {
  if (!css.includes(marker)) {
    throw new Error(`Falta comportamiento de tooltip en CSS: ${marker}`)
  }
}

console.log('PASS sidebar tooltips')
