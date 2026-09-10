import { useEffect, useMemo, useState } from 'react'
import { createPortal } from 'react-dom'
import { Check, ImagePlus, RefreshCw, Sparkles, X } from 'lucide-react'
import { nexiApi, type NexiGeneratedImage } from '../api/nexiApi'
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

export function NexiGreetingImageAssistant() {
  const [editor, setEditor] = useState<HTMLElement | null>(null)
  const [host, setHost] = useState<HTMLElement | null>(null)
  const [currentHtml, setCurrentHtml] = useState('')
  const [expanded, setExpanded] = useState(false)
  const [style, setStyle] = useState<GreetingStyle>('calido')
  const [image, setImage] = useState<NexiGeneratedImage | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [inserted, setInserted] = useState(false)

  useEffect(() => {
    function locateEditor() {
      const nextEditor = document.querySelector<HTMLElement>('.compose-page .ai-compose-editor .rich-editor[contenteditable="true"]')
      const nextHost = nextEditor?.closest<HTMLElement>('.ai-compose-editor') ?? null
      setEditor(current => current === nextEditor ? current : nextEditor)
      setHost(current => current === nextHost ? current : nextHost)
      setCurrentHtml(nextEditor?.innerHTML ?? '')
      if (!nextEditor) {
        setExpanded(false)
        setImage(null)
        setInserted(false)
        setError('')
      }
    }

    locateEditor()
    const observer = new MutationObserver(locateEditor)
    observer.observe(document.body, { childList: true, subtree: true })
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    if (!editor) return
    const update = () => setCurrentHtml(editor.innerHTML)
    editor.addEventListener('input', update)
    update()
    return () => editor.removeEventListener('input', update)
  }, [editor])

  const messageText = useMemo(() => plainText(currentHtml), [currentHtml])
  const suggested = useMemo(() => looksLikeGreeting(messageText), [messageText])

  useEffect(() => {
    if (!suggested && !image) setExpanded(false)
  }, [suggested, image])

  if (!host || (!suggested && !expanded && !image)) return null

  async function generate() {
    if (!messageText || loading) return
    setLoading(true)
    setError('')
    setInserted(false)
    try {
      setImage(await nexiApi.generateGreetingImage(messageText, style))
      setExpanded(true)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Nexi no pudo generar la imagen.')
    } finally {
      setLoading(false)
    }
  }

  function insertIntoMessage() {
    if (!image || !editor) return
    const paragraph = document.createElement('p')
    const element = document.createElement('img')
    element.src = image.dataUrl
    element.alt = 'Imagen creada con Nexi para acompañar este saludo'
    element.style.maxWidth = '520px'
    element.style.width = '100%'
    element.style.height = 'auto'
    element.style.borderRadius = '12px'
    element.setAttribute('data-nexi-generated-image', 'true')
    paragraph.appendChild(element)
    editor.appendChild(paragraph)
    editor.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertFromPaste' }))
    setInserted(true)
  }

  const content = <section className="nexi-greeting-image" aria-label="Imagen sugerida por Nexi">
    <header>
      <div className="nexi-greeting-image-title">
        <NexiVisual size="small" />
        <div><strong>Saludo visual con Nexi</strong><span>Este mensaje puede acompañarse con una imagen creada para la ocasión.</span></div>
      </div>
      {(expanded || image) && <button type="button" className="icon-button" onClick={() => { setExpanded(false); setImage(null); setError(''); setInserted(false) }} aria-label="Cerrar sugerencia" title="Cerrar sugerencia"><X size={15} /></button>}
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
              <button type="button" onClick={insertIntoMessage} disabled={inserted}>{inserted ? <Check size={15} /> : <ImagePlus size={15} />}{inserted ? 'Insertada en el mensaje' : 'Insertar en el mensaje'}</button>
              <button type="button" onClick={() => void generate()} disabled={loading}><RefreshCw size={14} /> Crear otra</button>
            </div>
          </div>}
        </>}

    {error && <p className="nexi-greeting-error" role="alert">{error}</p>}
  </section>

  return createPortal(content, host)
}
