import { useEffect, useRef, useState } from 'react'
import { Copy, ExternalLink, MoreHorizontal, Send, Share2 } from 'lucide-react'
import { perspectiveShareText, type SavedPerspective } from '../utils/perspectiveCollection'

type ShareablePerspective = Pick<SavedPerspective, 'text' | 'source' | 'area'>
type OptionalShareNavigator = Navigator & { share?: (data?: ShareData) => Promise<void> }

function publicUrl() {
  if (['localhost', '127.0.0.1'].includes(window.location.hostname)) return ''
  return `${window.location.origin}/?utm_source=perspective_share&utm_medium=referral&utm_campaign=nexomail_perspectives`
}

function openShareWindow(url: string) {
  window.open(url, '_blank', 'noopener,noreferrer,width=720,height=620')
}

export function PerspectiveShareMenu({ perspective, compact = false }: { perspective: ShareablePerspective; compact?: boolean }) {
  const [open, setOpen] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)
  const rootRef = useRef<HTMLDivElement>(null)
  const baseText = perspectiveShareText(perspective)
  const text = `${baseText}\n\nConoce NexoMail: una forma más inteligente de organizar, entender y trabajar con tu correo.`
  const url = publicUrl()
  const encodedText = encodeURIComponent(text)
  const encodedUrl = encodeURIComponent(url)
  const title = encodeURIComponent(`Perspectiva · ${perspective.area} | NexoMail`)
  const nativeShare = (navigator as OptionalShareNavigator).share
  const canNativeShare = typeof nativeShare === 'function'

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [open])

  function notify(message: string) {
    setFeedback(message)
    window.setTimeout(() => setFeedback(null), 2200)
  }

  async function copy() {
    await navigator.clipboard.writeText(`${text}${url ? `\n${url}` : ''}`)
    notify('Copiado para compartir')
  }

  async function moreApps() {
    if (canNativeShare && nativeShare) {
      try {
        await nativeShare.call(navigator, {
          title: `Perspectiva · ${perspective.area} | NexoMail`,
          text,
          ...(url ? { url } : {}),
        })
        notify('Compartido')
      } catch (error) {
        if ((error as DOMException)?.name !== 'AbortError') notify('No fue posible compartir')
      }
      return
    }
    await copy()
  }

  const options = [
    {
      label: 'WhatsApp',
      action: () => openShareWindow(`https://wa.me/?text=${encodeURIComponent(`${text}${url ? `\n${url}` : ''}`)}`),
    },
    {
      label: 'Facebook',
      action: () => url ? openShareWindow(`https://www.facebook.com/sharer/sharer.php?u=${encodedUrl}`) : void copy(),
    },
    {
      label: 'X',
      action: () => openShareWindow(`https://twitter.com/intent/tweet?text=${encodedText}${url ? `&url=${encodedUrl}` : ''}`),
    },
    {
      label: 'LinkedIn',
      action: () => url ? openShareWindow(`https://www.linkedin.com/sharing/share-offsite/?url=${encodedUrl}`) : void copy(),
    },
    {
      label: 'Telegram',
      action: () => openShareWindow(`https://t.me/share/url?url=${encodedUrl}&text=${encodedText}`),
    },
    {
      label: 'Correo',
      action: () => { window.location.href = `mailto:?subject=${title}&body=${encodeURIComponent(`${text}${url ? `\n\n${url}` : ''}`)}` },
    },
  ]

  return <div className={`perspective-share-menu ${compact ? 'compact' : ''}`} ref={rootRef}>
    <button
      type="button"
      className={compact ? 'nexo-perspective-action' : ''}
      onClick={() => setOpen(current => !current)}
      aria-haspopup="menu"
      aria-expanded={open}
      title="Compartir perspectiva"
    >
      <Share2 size={compact ? 13 : 14} />
      <span>Compartir</span>
    </button>

    {open && <div className="perspective-share-popover" role="menu" aria-label="Compartir perspectiva">
      <div className="perspective-share-heading"><Share2 size={14} /><strong>Compartir pensamiento</strong></div>
      <div className="perspective-share-options">
        {options.map(option => <button key={option.label} type="button" role="menuitem" onClick={() => { option.action(); setOpen(false) }}>
          <span>{option.label}</span><ExternalLink size={13} />
        </button>)}
        <button type="button" role="menuitem" onClick={() => { void copy(); setOpen(false) }}>
          <Copy size={14} /><span>Copiar</span>
        </button>
        <button type="button" role="menuitem" onClick={() => { void moreApps(); setOpen(false) }}>
          {canNativeShare ? <Send size={14} /> : <MoreHorizontal size={14} />}
          <span>Más apps</span>
        </button>
      </div>
      {feedback && <div className="perspective-share-feedback" role="status">{feedback}</div>}
    </div>}
  </div>
}
