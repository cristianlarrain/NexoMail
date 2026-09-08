import { useEffect, useState } from 'react'
import { createPortal } from 'react-dom'
import { MessageSquareText, Sparkles } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router-dom'

export function SearchContextLauncher() {
  const location = useLocation()
  const navigate = useNavigate()
  const [host, setHost] = useState<HTMLElement | null>(null)

  const params = new URLSearchParams(location.search)
  const query = params.get('q')?.trim() ?? ''
  const accountId = params.get('account') ?? ''
  const active = location.pathname === '/search' && Boolean(query)

  useEffect(() => {
    if (!active) {
      setHost(null)
      return
    }

    let cancelled = false
    let attempts = 0
    const findHost = () => {
      if (cancelled) return
      const next = document.querySelector<HTMLElement>('.universal-search-heading')
      if (next) {
        setHost(next)
        return
      }
      attempts += 1
      if (attempts < 20) window.setTimeout(findHost, 50)
    }
    findHost()
    return () => { cancelled = true }
  }, [active, location.search])

  if (!active || !host) return null

  function openContext() {
    const next = new URLSearchParams({ tab: 'nexi', q: query, auto: '1' })
    if (accountId) next.set('account', accountId)
    navigate(`/control-center?${next.toString()}`)
  }

  return createPortal(
    <button type="button" className="secondary-button universal-context-button" onClick={openContext} title="Continuar preguntando a Nexi sobre estos mismos resultados">
      <Sparkles size={15} /><MessageSquareText size={14} /> Analizar con Nexi
    </button>,
    host,
  )
}
