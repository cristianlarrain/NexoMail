import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const read = path => readFileSync(resolve(root, path), 'utf8')
const ensure = (condition, message) => {
  if (!condition) throw new Error(message)
}

const weather = read('src/components/WeatherWidget.tsx')
const packageJson = read('package.json')
const main = read('src/main.tsx')
const composeStyles = read('src/styles/compose-ai-integration.css')

ensure(weather.includes('enableHighAccuracy: true'), 'El clima debe solicitar ubicación con alta precisión.')
ensure(weather.includes('const GEOLOCATION_MAX_AGE_MS = 60_000') && weather.includes('maximumAge: GEOLOCATION_MAX_AGE_MS'), 'El clima no debe reutilizar una ubicación antigua por más de un minuto.')
ensure(weather.includes('loadFallbackWeather'), 'Santiago debe usarse sólo como respaldo cuando falle la ubicación real.')
ensure(!weather.includes('void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)\n    requestActualLocation()'), 'No se debe mostrar primero Santiago cuando la geolocalización está disponible.')

ensure(!packageJson.includes('"tailwindcss"'), 'Tailwind debe eliminarse porque NexoMail no lo utiliza.')
ensure(!main.includes("./styles/ai-writing.css"), 'No debe cargarse el CSS del asistente de escritura legado.')
ensure(!main.includes("./styles/control-center-variants.css"), 'No debe cargarse el CSS del dashboard de variantes legado.')
ensure(!main.includes("./styles/unified-reply-composer.css"), 'No debe cargarse el CSS del reply composer legado.')
ensure(!main.includes("./styles/compose-cleanup.css"), 'El pequeño override de compose debe integrarse en el CSS vigente.')

for (const path of [
  'src/components/AiWritingAssistant.tsx',
  'src/components/control-center/ControlCenterVariantDashboard.tsx',
  'src/components/control-center/DashboardParts.tsx',
  'src/styles/ai-writing.css',
  'src/styles/control-center-variants.css',
  'src/styles/unified-reply-composer.css',
  'src/styles/compose-cleanup.css',
]) {
  ensure(!existsSync(resolve(root, path)), `Debe eliminarse el archivo sin uso: ${path}`)
}

ensure(!composeStyles.includes('.ai-writing-assistant'), 'Deben eliminarse selectores del asistente legado.')
ensure(!composeStyles.includes('.ai-review-banner'), 'Deben eliminarse selectores del banner legado.')
ensure(!composeStyles.includes('.ai-first-compose'), 'Deben eliminarse selectores de compose legado.')
ensure(composeStyles.includes('.ai-compose-heading .eyebrow::after'), 'El estilo vigente del rótulo Nexi debe conservarse al eliminar compose-cleanup.css.')

console.log('PASS: clima preciso y frontend sin legado comprobado')
