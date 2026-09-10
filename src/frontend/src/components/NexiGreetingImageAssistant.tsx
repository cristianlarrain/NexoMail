import { useMemo, useState } from 'react'
import { Check, ImagePlus, RefreshCw, Sparkles, X } from 'lucide-react'
import { nexiApi, type NexiGeneratedImage } from '../api/nexiApi'
import type { OutgoingAttachment } from '../types/mail'
import { NexiVisual } from './nexi/NexiVisual'

type GreetingStyle = 'formal' | 'calido' | 'corporativo' | 'festivo'

function plainText(html: string) {
  return html
    .replace(/<br\s*\/?\s*>/gi, '\n')
    .replace(/<\/p>|<\/div>|<\/li>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/\s+/g, ' ')
    .trim()
}

function looksLikeGreeting(value: string) {
  const normalized = value.toLocaleLowerCase('es').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
  if (normalized.length < 8) return false
  return /(feliz cumple|cumpleanos|felicit|enhorabuena|bienvenid|aniversario|feliz navidad|navidad|ano nuevo|feliz ano|buenos dias|buenas tardes|buenas noches|muchas felicidades|mis mejores deseos|te deseo|les deseo|celebr|saludos especiales)/.test(normalized)
}

function toAttachment(image: NexiGeneratedImage): OutgoingAttachment {
  const encoded = image.dataUrl.includes(',') ? image.dataUrl.slice(image.dataUrl.indexOf(',') + 1) : image.dataUrl
  return { name: image.fileName, contentType: image.contentType || 'image/png', base64Content: encoded }
}

export function NexiGreetingImageAssistant({
  currentHtml,
  onAttach,
}: {
  currentHtml: string
  onAttach: (attachment: OutgoingAttachment) => void
}) {
  const messageText = useMemo(() => plainText(currentHtml), [currentHtml])
  const suggested = useMemo(() => looksLikeGreeting(messageText), [messageText])
  const [expanded, setExpanded] = useState(false)
  const [style, setStyle] = useState<GreetingStyle>('calido')
  const [image, setImage] = useState<NexiGeneratedImage | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [attached, setAttached] = useState(false)

  if (!suggested && !expanded && !image) return null

  async function generate() {
    if (!messageText || loading) return
    setLoading(true)
    setError('')
    setAttached(false)
    try {
      setImage(await nexiApi.generateGreetingImage(messageText, style))
      setExpanded(true)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Nexi no pudo generar la imagen.')
    } finally {
      setLoading(false)
    }
  }

  function attach() {
    if (!image) return
    const attachment = toAttachment(image)
    const estimatedBytes = Math.ceil(attachment.base64Content.length * .75)
    if (estimatedBytes > 8 * 1024 * 1024) {
      setError('La imagen generada supera el límite de 8 MB por archivo.')
      return
    }
    onAttach(attachment)
    setAttached(true)
  }

  return <section className="nexi-greeting-image" aria-label="Imagen sugerida por Nexi">
    <header>
      <div className="nexi-greeting-image-title">
        <NexiVisual size="small" />
        <div><strong>Saludo visual con Nexi</strong><span>Este mensaje puede acompañarse con una imagen creada para la ocasión.</span></div>
      </div>
      {(expanded || image) && <button type="button" className="icon-button" onClick={() => { setExpanded(false); setImage(null); setError(''); setAttached(false) }} aria-label="Cerrar sugerencia" title="Cerrar sugerencia"><X size={15} /></button>}
    </header>

    {!expanded && !image
      ? <button type="button" className="nexi-greeting-open" onClick={() => setExpanded(true)}><ImagePlus size={15} /> Generar imagen con Nexi</button>
      : <>
          <div className="nexi-greeting-styles" aria-label="Estilo de imagen">
            {([
              ['calido', 'Cálido'],
              ['formal', 'Formal'],
              ['corporativo', 'Corporativo'],
              ['festivo', 'Festivo'],
            ] as const).map(([value, label]) => <button type="button" key={value} className={style === value ? 'active' : ''} onClick={() => setStyle(value)} disabled={loading}>{label}</button>)}
          </div>

          {!image && <button type="button" className="nexi-greeting-generate" onClick={() => void generate()} disabled={loading || !messageText}>
            {loading ? <NexiVisual size="small" /> : <Sparkles size={15} />}{loading ? 'Nexi está creando…' : 'Crear imagen'}
          </button>}

          {image && <div className="nexi-greeting-result">
            <img src={image.dataUrl} alt="Imagen generada por Nexi para acompañar el saludo" />
            <div className="nexi-greeting-result-actions">
              <button type="button" onClick={attach} disabled={attached}>{attached ? <Check size={15} /> : <ImagePlus size={15} />}{attached ? 'Adjuntada al correo' : 'Adjuntar al correo'}</button>
              <button type="button" onClick={() => void generate()} disabled={loading}><RefreshCw size={14} /> Crear otra</button>
            </div>
          </div>}
        </>}

    {error && <p className="nexi-greeting-error" role="alert">{error}</p>}
  </section>
}
