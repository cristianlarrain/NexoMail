import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const read = path => readFileSync(resolve(root, path), 'utf8')

const weather = read('src/components/WeatherWidget.tsx')
const share = read('src/components/PerspectiveShareMenu.tsx')
const perspectives = read('src/pages/PerspectivesPage.tsx')
const packageJson = read('package.json')

const requiredWeatherMarkers = [
  'enableHighAccuracy: true',
  'maximumAge: 0',
  'fallbackToSantiago',
]

for (const marker of requiredWeatherMarkers) {
  if (!weather.includes(marker)) throw new Error(`Falta ajuste de precisión meteorológica: ${marker}`)
}

if (weather.includes('void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)\n    requestActualLocation()')) {
  throw new Error('El clima no debe cargar Santiago antes de intentar obtener la ubicación real')
}

const requiredShareMarkers = [
  'utm_source=perspective_share',
  'utm_medium=referral',
  'utm_campaign=nexomail_perspectives',
  'Conoce NexoMail',
]

for (const marker of requiredShareMarkers) {
  if (!share.includes(marker)) throw new Error(`Falta enlace comercial al compartir perspectivas: ${marker}`)
}

if (!perspectives.includes('const collapsed = collapsedReflections[item.id] ?? true')) {
  throw new Error('Las reflexiones de perspectivas guardadas deben iniciar colapsadas')
}

if (packageJson.includes('"tailwindcss"')) {
  throw new Error('tailwindcss continúa declarado aunque no se utiliza')
}

console.log('PASS product polish')
