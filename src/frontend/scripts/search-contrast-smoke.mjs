import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const searchCss = readFileSync(resolve(root, 'src/styles/universal-search.css'), 'utf8')
const darkTheme = readFileSync(resolve(root, 'src/styles/cinematic-dark-theme.css'), 'utf8')

if (!darkTheme.includes('--muted: #10252e;') || !darkTheme.includes('--muted-foreground: #83a6b1;')) {
  throw new Error('La prueba necesita la paleta oscura actual para validar contraste.')
}

if (searchCss.includes('color: var(--muted);')) {
  throw new Error('La página de resultados usa --muted como color de texto; en modo oscuro ese token es un fondo y pierde contraste.')
}

for (const selector of [
  '.nexi-search-interpretation span',
  '.universal-result-tabs button',
  '.universal-result-primary > small',
  '.universal-result-meta',
  '.universal-search-loading',
]) {
  const index = searchCss.indexOf(selector)
  if (index < 0) throw new Error(`Falta el selector esperado: ${selector}`)
  const nearby = searchCss.slice(index, index + 260)
  if (!nearby.includes('var(--muted-foreground)')) {
    throw new Error(`El texto secundario de ${selector} debe usar --muted-foreground.`)
  }
}

console.log('PASS search result contrast')
