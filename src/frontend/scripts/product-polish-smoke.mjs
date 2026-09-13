import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = process.cwd()
const read = path => readFileSync(resolve(root, path), 'utf8')

const weather = read('src/components/WeatherWidget.tsx')
const share = read('src/components/PerspectiveShareMenu.tsx')
const perspectives = read('src/pages/PerspectivesPage.tsx')
const landing = read('src/pages/LandingPage.tsx')
const landingCss = read('src/styles/landing.css')
const landingCommercialCss = read('src/styles/landing-commercial.css')
const landingStyles = `${landingCss}\n${landingCommercialCss}`
const perspectiveStyles = [
  read('src/styles/nexo-perspective.css'),
  read('src/styles/perspective-share-cleanup.css'),
].join('\n')
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

const perspectiveGridBlocks = [...perspectiveStyles.matchAll(/\.perspectives-grid\s*\{[\s\S]*?\}/g)].map(match => match[0])
if (!perspectiveGridBlocks.some(block => block.includes('align-items: start'))) {
  throw new Error('La grilla de perspectivas no debe estirar todas las tarjetas cuando una reflexión se expande')
}

if (packageJson.includes('"tailwindcss"')) {
  throw new Error('tailwindcss continúa declarado aunque no se utiliza')
}

const requiredLandingMarkers = [
  'landing-commercial-preview',
  'NexoMail le muestra lo que su bandeja no le dice.',
  'Vea NexoMail en acción',
  'landing-video-stage',
  'metric-amber',
  'metric-orange',
  'Priorización asistida por Nexi',
]

for (const marker of requiredLandingMarkers) {
  if (!landing.includes(marker)) throw new Error(`Falta ajuste comercial en landing: ${marker}`)
}

const requiredDemoVideoMarkers = [
  '<video',
  'className="landing-video-player"',
  'controls',
  'playsInline',
  'preload="metadata"',
  'https://cdn.creativeclaw.co/u/372f7075/videos/d1cab7f4-113a-43c9-bc48-ea80643f0088.mp4',
]

for (const marker of requiredDemoVideoMarkers) {
  if (!landing.includes(marker)) throw new Error(`La landing no incorpora el video demostrativo: ${marker}`)
}

if (landing.includes('Este espacio queda preparado para incorporar el video demostrativo.')) {
  throw new Error('La landing conserva el placeholder anterior del video demostrativo.')
}

if (!landingCommercialCss.includes('.landing-video-player')) {
  throw new Error('Faltan estilos responsivos para el reproductor de la demo.')
}

for (const forbidden of [
  'landing-product-preview',
  'landing-value-strip',
  "name: 'White Label',",
  "title: 'Nexi, su asistente'",
  "title: 'Privacidad desde el diseño'",
]) {
  if (landing.includes(forbidden)) throw new Error(`La landing conserva contenido redundante: ${forbidden}`)
}

if (!landing.includes('Gmail, Microsoft 365 e IMAP / SMTP Beta')) {
  throw new Error('El hero debe condensar la compatibilidad de proveedores en una sola línea.')
}

if (!landing.includes('Conecte sus cuentas actuales y trabaje desde una sola interfaz, sin cambiar su proveedor de correo.')) {
  throw new Error('Integraciones debe usar el texto comercial simplificado.')
}

if (!landingStyles.includes('padding: 56px 0')) {
  throw new Error('La landing debe reducir el espaciado vertical general a 56px.')
}

if (!landingStyles.includes('--warm-yellow: #f2b34f')) {
  throw new Error('La demo comercial debe incorporar el acento amarillo/anaranjado aprobado.')
}

if (!landingCommercialCss.includes('grid-template-columns: repeat(3, minmax(0, 1fr))')) {
  throw new Error('Los planes comerciales deben quedar en tres columnas.')
}

console.log('PASS product polish')
