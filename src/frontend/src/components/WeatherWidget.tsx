import { useEffect, useState } from 'react'
import { CloudDrizzle, CloudFog, CloudLightning, CloudMoon, CloudRain, CloudSun, LoaderCircle, Moon, Snowflake, Sun } from 'lucide-react'

type WeatherState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ready'; temperature: number; apparentTemperature: number; code: number; isDay: boolean; description: string }
  | { status: 'unavailable' }

type OpenMeteoResponse = {
  current?: {
    temperature_2m?: number
    apparent_temperature?: number
    weather_code?: number
    is_day?: number
  }
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
  if (code === 3) return <CloudSun size={17} />
  if (code === 45 || code === 48) return <CloudFog size={17} />
  if ([51, 53, 55, 56, 57].includes(code)) return <CloudDrizzle size={17} />
  if ([61, 63, 65, 66, 67, 80, 81, 82].includes(code)) return <CloudRain size={17} />
  if ([71, 73, 75, 77].includes(code)) return <Snowflake size={17} />
  if ([95, 96, 99].includes(code)) return <CloudLightning size={17} />
  return <CloudSun size={17} />
}

export function WeatherWidget() {
  const [weather, setWeather] = useState<WeatherState>({ status: 'idle' })

  async function loadWeather(latitude: number, longitude: number) {
    try {
      const params = new URLSearchParams({
        latitude: latitude.toFixed(4),
        longitude: longitude.toFixed(4),
        current: 'temperature_2m,apparent_temperature,weather_code,is_day',
        timezone: 'auto',
        forecast_days: '1',
      })
      const response = await fetch(`https://api.open-meteo.com/v1/forecast?${params.toString()}`)
      if (!response.ok) throw new Error('Weather request failed')
      const data = await response.json() as OpenMeteoResponse
      const current = data.current
      if (!current || typeof current.temperature_2m !== 'number' || typeof current.weather_code !== 'number') throw new Error('Weather data unavailable')
      const apparent = typeof current.apparent_temperature === 'number' ? current.apparent_temperature : current.temperature_2m
      setWeather({
        status: 'ready',
        temperature: current.temperature_2m,
        apparentTemperature: apparent,
        code: current.weather_code,
        isDay: current.is_day !== 0,
        description: weatherDescription(current.weather_code),
      })
    } catch {
      setWeather({ status: 'unavailable' })
    }
  }

  function requestWeather() {
    if (!navigator.geolocation) {
      setWeather({ status: 'unavailable' })
      return
    }
    setWeather({ status: 'loading' })
    navigator.geolocation.getCurrentPosition(
      position => { void loadWeather(position.coords.latitude, position.coords.longitude) },
      () => setWeather({ status: 'unavailable' }),
      { enableHighAccuracy: false, timeout: 8_000, maximumAge: 30 * 60_000 },
    )
  }

  useEffect(() => {
    let cancelled = false
    if (!navigator.geolocation || !navigator.permissions?.query) return
    void navigator.permissions.query({ name: 'geolocation' }).then(permission => {
      if (cancelled) return
      if (permission.state === 'granted') requestWeather()
      else if (permission.state === 'denied') setWeather({ status: 'unavailable' })
    }).catch(() => undefined)
    return () => { cancelled = true }
  }, [])

  if (weather.status === 'unavailable') return null
  if (weather.status === 'idle') {
    return <button type="button" className="weather-widget weather-enable" onClick={requestWeather} title="Mostrar clima local" aria-label="Mostrar clima local"><CloudSun size={17} /><span>Clima</span></button>
  }
  if (weather.status === 'loading') {
    return <span className="weather-widget weather-loading" aria-label="Consultando clima local" title="Consultando clima local"><LoaderCircle size={16} className="spin" /><span>…</span></span>
  }

  const temperature = Math.round(weather.temperature)
  const apparent = Math.round(weather.apparentTemperature)
  return <span className="weather-widget weather-ready" aria-label={`${weather.description}, ${temperature} grados`} title={`${weather.description} · Sensación térmica ${apparent} °C`}><WeatherIcon code={weather.code} isDay={weather.isDay} /><strong>{temperature}°</strong></span>
}
