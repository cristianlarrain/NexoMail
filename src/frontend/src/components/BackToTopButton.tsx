import { useEffect, useState } from 'react'
import { ArrowUp } from 'lucide-react'
import { useLocation } from 'react-router-dom'

function isMailRoute(pathname: string) {
  return pathname === '/inbox' ||
    pathname.startsWith('/account/') ||
    pathname === '/sent' ||
    pathname === '/drafts' ||
    pathname === '/trash' ||
    pathname.startsWith('/message/')
}

export function BackToTopButton() {
  const location = useLocation()
  const [visible, setVisible] = useState(false)
  const enabled = isMailRoute(location.pathname)

  useEffect(() => {
    if (!enabled) {
      setVisible(false)
      return
    }

    const update = () => setVisible(window.scrollY > 520)
    update()
    window.addEventListener('scroll', update, { passive: true })
    return () => window.removeEventListener('scroll', update)
  }, [enabled, location.pathname])

  if (!enabled || !visible) return null

  return <button
    type="button"
    className="back-to-top-button"
    onClick={() => window.scrollTo({ top: 0, behavior: 'smooth' })}
    title="Volver arriba"
    aria-label="Volver arriba"
  >
    <ArrowUp size={18} />
    <span>Subir</span>
  </button>
}
