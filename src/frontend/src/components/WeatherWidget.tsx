import { useEffect, useRef, useState } from 'react'
import { ChevronDown, CloudDrizzle, CloudFog, CloudLightning, CloudMoon, CloudRain, CloudSun, Droplets, LoaderCircle, Moon, Snowflake, Sun } from 'lucide-react'

const SANTIAGO = { latitude: -33.4489, longitude: -70.6693, location: 'Santiago' }

type ForecastDay = {
  date: string
  code: number
  temperatureMax: number
  temperatureMin: number
  precipitationProbability: number
}

type WeatherState =
  | { status: 'loading'; location: string }
  | { status: 'ready'; temperature: number; apparentTemperature: number; code: number; isDay: boolean; description: string; location: string; forecast: ForecastDay[] }
  | { status: 'error'; location: string }

type OpenMeteoResponse = {
  current?: {
    temperature_2m?: number
    apparent_temperature?: number
    weather_code?: number
    is_day?: number
  }
  daily?: {
    time?: string[]
    weather_code?: number[]
    temperature_2m_max?: number[]
    temperature_2m_min?: number[]
    precipitation_probability_max?: number[]
  }
}

type ReverseGeocodeResponse = {
  city?: string
  locality?: string
  principalSubdivision?: string
}

function weatherDescription(code: number) {
  if (code === 0) return 'Despejado'
  if (code === 1) return 'Mayormente despejado'
  if (code === 2) return 'Parcialmente nublado'
  if (code === 3) return 'Nublado'
  if (code === 45 || code === 48) return 'Niebla'
  if ([51, 53, 55, 56, 57].includes(code)) return 'Llovizna'
  if ([61, 63, 65, 66, 67].includes(code)) return 'Lluvia'
  if ([71, 73, 75, 77].includes(code)) return 'Nieve'
  if ([80, 81, 82].includes(code)) return 'Chubascos'
  if ([95, 96, 99].includes(code)) return 'Tormenta'
  return 'Condición meteorológica'
}

function WeatherIcon({ code, isDay, size = 17 }: { code: number; isDay: boolean; size?: number }) {
  if (code === 0) return isDay ? <Sun size={size} /> : <Moon size={size} />
  if ([1, 2].includes(code)) return isDay ? <CloudSun size={size} /> : <CloudMoon size={size} />
  if (code === 3) return isDay ? <CloudSun size={size} /> : <CloudMoon size={size} />
  if (code === 45 || code === 48) return <CloudFog size={size} />
  if ([51, 53, 55, 56, 57].includes(code)) return <CloudDrizzle size={size} />
  if ([61, 63, 65, 66, 67, 80, 81, 82].includes(code)) return <CloudRain size={size} />
  if ([71, 73, 75, 77].includes(code)) return <Snowflake size={size} />
  if ([95, 96, 99].includes(code)) return <CloudLightning size={size} />
  return isDay ? <CloudSun size={size} /> : <CloudMoon size={size} />
}

function forecastDayLabel(value: string, index: number) {
  if (index === 0) return 'Hoy'
  if (index === 1) return 'Mañana'
  return new Date(`${value}T12:00:00`).toLocaleDateString('es-CL', { weekday: 'short', day: '2-digit' }).replace(/\./g, '')
}

function buildForecast(data: OpenMeteoResponse): ForecastDay[] {
  const daily = data.daily
  const dates = daily?.time ?? []
  return dates.slice(0, 7).map((date, index) => ({
    date,
    code: daily?.weather_code?.[index] ?? 0,
    temperatureMax: daily?.temperature_2m_max?.[index] ?? 0,
    temperatureMin: daily?.temperature_2m_min?.[index] ?? 0,
    precipitationProbability: daily?.precipitation_probability_max?.[index] ?? 0,
  }))
}

async function resolveLocation(latitude: number, longitude: number) {
  try {
    const params = new URLSearchParams({
      latitude: latitude.toFixed(5),
      longitude: longitude.toFixed(5),
      localityLanguage: 'es',
    })
    const response = await fetch(`https://api.bigdatacloud.net/data/reverse-geocode-client?${params.toString()}`)
    if (!response.ok) return ''
    const data = await response.json() as ReverseGeocodeResponse
    return data.city?.trim() || data.locality?.trim() || data.principalSubdivision?.trim() || ''
  } catch {
    return ''
  }
}

export function WeatherWidget() {
  const [weather, setWeather] = useState<WeatherState>({ status: 'loading', location: 'Tu ubicación' })
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let active = true

    async function loadWeather(latitude: number, longitude: number, fallbackLocation: string, resolveCity: boolean) {
      try {
        const params = new URLSearchParams({
          latitude: latitude.toFixed(4),
          longitude: longitude.toFixed(4),
          current: 'temperature_2m,apparent_temperature,weather_code,is_day',
          daily: 'weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max',
          timezone: 'auto',
          forecast_days: '7',
        })
        const [response, resolvedLocation] = await Promise.all([
          fetch(`https://api.open-meteo.com/v1/forecast?${params.toString()}`),
          resolveCity ? resolveLocation(latitude, longitude) : Promise.resolve(fallbackLocation),
        ])
        if (!response.ok) throw new Error('Weather request failed')
        const data = await response.json() as OpenMeteoResponse
        const current = data.current
        if (!current || typeof current.temperature_2m !== 'number' || typeof current.weather_code !== 'number') throw new Error('Weather data unavailable')
        if (!active) return
        const apparent = typeof current.apparent_temperature === 'number' ? current.apparent_temperature : current.temperature_2m
        setWeather({
          status: 'ready',
          temperature: current.temperature_2m,
          apparentTemperature: apparent,
          code: current.weather_code,
          isDay: current.is_day !== 0,
          description: weatherDescription(current.weather_code),
          location: resolvedLocation || fallbackLocation,
          forecast: buildForecast(data),
        })
      } catch {
        if (!active) return
        if (resolveCity) {
          void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)
          return
        }
        setWeather({ status: 'error', location: fallbackLocation })
      }
    }

    function fallbackToSantiago() {
      if (!active) return
      void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)
    }

    function requestActualLocation() {
      if (!navigator.geolocation) {
        fallbackToSantiago()
        return
      }
      navigator.geolocation.getCurrentPosition(
        position => {
          if (!active) return
          void loadWeather(position.coords.latitude, position.coords.longitude, 'Tu ubicación', true)
        },
        fallbackToSantiago,
        { enableHighAccuracy: true, timeout: 10_000, maximumAge: 0 },
      )
    }

    requestActualLocation()
    const refresh = window.setInterval(requestActualLocation, 10 * 60_000)

    return () => {
      active = false
      window.clearInterval(refresh)
    }
  }, [])

  useEffect(() => {
    if (!open) return
    function closeOnOutsideClick(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    function closeOnEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('mousedown', closeOnOutsideClick)
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [open])

  if (weather.status === 'loading') {
    return <span className="weather-widget weather-loading" aria-label={`Consultando clima en ${weather.location}`} title={`Consultando clima en ${weather.location}`}><LoaderCircle size={16} className="spin" /><span className="weather-location">{weather.location}</span></span>
  }

  if (weather.status === 'error') {
    return <span className="weather-widget weather-error" aria-label={`Clima no disponible en ${weather.location}`} title={`Clima no disponible en ${weather.location}`}><CloudSun size={17} /><strong>--°</strong><span className="weather-location">{weather.location}</span></span>
  }

  const temperature = Math.round(weather.temperature)
  const apparent = Math.round(weather.apparentTemperature)

  return <div className={`weather-control ${open ? 'open' : ''}`} ref={rootRef}>
    <button
      type="button"
      className="weather-widget weather-ready weather-trigger"
      aria-label={`${weather.description}, ${temperature} grados en ${weather.location}. Ver pronóstico de siete días`}
      aria-expanded={open}
      aria-controls="weather-seven-day-forecast"
      title={`${weather.description} · Sensación térmica ${apparent} °C · ${weather.location}`}
      onClick={() => setOpen(current => !current)}
    >
      <WeatherIcon code={weather.code} isDay={weather.isDay} />
      <strong>{temperature}°</strong>
      <span className="weather-location">{weather.location}</span>
      <ChevronDown size={13} className="weather-chevron" />
    </button>

    {open && <section id="weather-seven-day-forecast" className="weather-dropdown" aria-label={`Pronóstico de siete días para ${weather.location}`}>
      <header>
        <div><strong>{weather.location}</strong><span>{weather.description} · Sensación {apparent}°</span></div>
        <span>7 días</span>
      </header>
      <div className="weather-forecast-list">
        {weather.forecast.map((day, index) => <div className="weather-forecast-row" key={day.date}>
          <span className="weather-forecast-day">{forecastDayLabel(day.date, index)}</span>
          <span className="weather-forecast-condition" title={weatherDescription(day.code)}><WeatherIcon code={day.code} isDay size={17} /><span>{weatherDescription(day.code)}</span></span>
          <span className="weather-forecast-rain" title="Probabilidad de precipitación"><Droplets size={13} />{Math.round(day.precipitationProbability)}%</span>
          <span className="weather-forecast-temp"><strong>{Math.round(day.temperatureMax)}°</strong><span>{Math.round(day.temperatureMin)}°</span></span>
        </div>)}
      </div>
    </section>}
  </div>
}
