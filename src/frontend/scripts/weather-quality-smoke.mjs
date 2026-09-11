import { readFile } from 'node:fs/promises'

async function source(path) {
  return readFile(new URL(`../${path}`, import.meta.url), 'utf8')
}

function ensure(condition, message) {
  if (!condition) throw new Error(message)
}

const [weather, packageJson] = await Promise.all([
  source('src/components/WeatherWidget.tsx'),
  source('package.json'),
])

const pkg = JSON.parse(packageJson)

ensure(!pkg.devDependencies?.tailwindcss && !pkg.dependencies?.tailwindcss,
  'Tailwind CSS no debe quedar instalado si NexoMail no lo utiliza.')
ensure(weather.includes('enableHighAccuracy: true'),
  'El clima debe solicitar geolocalización de alta precisión.')
ensure(weather.includes('const GEOLOCATION_MAX_AGE_MS = 60_000'),
  'La ubicación meteorológica no debe reutilizar coordenadas antiguas por más de un minuto.')
ensure(weather.includes('const WEATHER_REFRESH_MS = 5 * 60_000'),
  'El clima debe actualizar la ubicación y las condiciones cada cinco minutos.')
ensure(weather.includes("updatedAt: string") && weather.includes('Actualizado'),
  'El clima debe informar cuándo se actualizaron las condiciones mostradas.')
ensure(weather.includes("void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)"),
  'Santiago debe conservarse únicamente como respaldo cuando no sea posible obtener la ubicación real.')
ensure(!weather.includes("void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)\n    requestActualLocation()"),
  'El widget no debe cargar Santiago antes de intentar la ubicación real del usuario.')

console.log('PASS: clima preciso y dependencias frontend limpias')
