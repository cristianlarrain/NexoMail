import { useEffect, useState } from 'react'
import { CloudDrizzle, CloudFog, CloudLightning, CloudMoon, CloudRain, CloudSun, LoaderCircle, Moon, Snowflake, Sun } from 'lucide-react'

const SANTIAGO = { latitude: -33.4489, longitude: -70.6693, location: 'Santiago' }

type WeatherState =
  | { status: 'loading'; location: string }
  | { status: 'ready'; temperature: number; apparentTemperature: number; code: number; isDay: boolean; description: string; location: string }
  | { status: 'error'; location: string }

type OpenMeteoResponse = {
  current?: {
    temperature_2m?: number
    apparent_temperature?: number
    weather_code?: number
    is_day?: number
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

function WeatherIcon({ code, isDay }: { code: number; isDay: boolean }) {
  if (code === 0) return isDay ? <Sun size={17} /> : <Moon size={17} />
  if ([1, 2].includes(code)) return isDay ? <CloudSun size={17} /> : <CloudMoon size={17} />
  if (code === 3) return isDay ? <CloudSun size={17} /> : <CloudMoon size={17} />
  if (code === 45 || code === 48) return <CloudFog size={17} />
  if ([51, 53, 55, 56, 57].includes(code)) return <CloudDrizzle size={17} />
  if ([61, 63, 65, 66, 67, 80, 81, 82].includes(code)) return <CloudRain size={17} />
  if ([71, 73, 75, 77].includes(code)) return <Snowflake size={17} />
  if ([95, 96, 99].includes(code)) return <CloudLightning size={17} />
  return isDay ? <CloudSun size={17} /> : <CloudMoon size={17} />
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
  const [weather, setWeather] = useState<WeatherState>({ status: 'loading', location: SANTIAGO.location })

  useEffect(() => {
    let active = true

    async function loadWeather(latitude: number, longitude: number, fallbackLocation: string, resolveCity: boolean) {
      try {
        const params = new URLSearchParams({
          latitude: latitude.toFixed(4),
          longitude: longitude.toFixed(4),
          current: 'temperature_2m,apparent_temperature,weather_code,is_day',
          timezone: 'auto',
          forecast_days: '1',
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
        })
      } catch {
        if (active && !resolveCity) setWeather({ status: 'error', location: fallbackLocation })
      }
    }

    function requestActualLocation() {
      if (!navigator.geolocation) return
      navigator.geolocation.getCurrentPosition(
        position => {
          if (!active) return
          void loadWeather(position.coords.latitude, position.coords.longitude, 'Tu ubicación', true)
        },
        () => undefined,
        { enableHighAccuracy: false, timeout: 8_000, maximumAge: 15 * 60_000 },
      )
    }

    // Always render useful weather immediately. If browser location is available,
    // replace the Santiago fallback with the user's actual local conditions.
    void loadWeather(SANTIAGO.latitude, SANTIAGO.longitude, SANTIAGO.location, false)
    requestActualLocation()
    const refresh = window.setInterval(requestActualLocation, 15 * 60_000)

    return () => {
      active = false
      window.clearInterval(refresh)
    }
  }, [])

  if (weather.status === 'loading') {
    return <span className="weather-widget weather-loading" aria-label={`Consultando clima en ${weather.location}`} title={`Consultando clima en ${weather.location}`}><LoaderCircle size={16} className="spin" /><span className="weather-location">{weather.location}</span></span>
  }

  if (weather.status === 'error') {
    return <span className="weather-widget weather-error" aria-label={`Clima no disponible en ${weather.location}`} title={`Clima no disponible en ${weather.location}`}><CloudSun size={17} /><strong>--°</strong><span className="weather-location">{weather.location}</span></span>
  }

  const temperature = Math.round(weather.temperature)
  const apparent = Math.round(weather.apparentTemperature)
  return <span className="weather-widget weather-ready" aria-label={`${weather.description}, ${temperature} grados en ${weather.location}`} title={`${weather.description} · Sensación térmica ${apparent} °C · ${weather.location}`}><WeatherIcon code={weather.code} isDay={weather.isDay} /><strong>{temperature}°</strong><span className="weather-location">{weather.location}</span></span>
}
