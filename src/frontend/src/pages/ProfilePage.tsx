import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Camera, Move, Trash2, UserRound, X, ZoomIn } from 'lucide-react'
import { authApi } from '../api/authApi'

function initials(value: string) {
  return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || 'NM'
}

function drawCrop(canvas: HTMLCanvasElement, image: HTMLImageElement, zoom: number, positionX: number, positionY: number) {
  const context = canvas.getContext('2d')
  if (!context) throw new Error('No fue posible procesar la imagen.')

  const size = canvas.width
  const baseScale = Math.max(size / image.naturalWidth, size / image.naturalHeight)
  const scale = baseScale * zoom
  const width = image.naturalWidth * scale
  const height = image.naturalHeight * scale
  const overflowX = Math.max(0, (width - size) / 2)
  const overflowY = Math.max(0, (height - size) / 2)
  const x = (size - width) / 2 + (positionX / 100) * overflowX
  const y = (size - height) / 2 + (positionY / 100) * overflowY

  context.clearRect(0, 0, size, size)
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, size, size)
  context.drawImage(image, x, y, width, height)
}

export function ProfilePage() {
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false })
  const [displayName, setDisplayName] = useState('')
  const [avatarDataUrl, setAvatarDataUrl] = useState<string | null>(null)
  const [imageError, setImageError] = useState('')
  const [cropImageSrc, setCropImageSrc] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  const [positionX, setPositionX] = useState(0)
  const [positionY, setPositionY] = useState(0)
  const cropImageRef = useRef<HTMLImageElement | null>(null)
  const previewCanvasRef = useRef<HTMLCanvasElement | null>(null)

  useEffect(() => {
    if (!session) return
    setDisplayName(session.displayName)
    setAvatarDataUrl(session.avatarDataUrl)
  }, [session])

  useEffect(() => {
    if (!cropImageSrc || !cropImageRef.current || !previewCanvasRef.current) return
    drawCrop(previewCanvasRef.current, cropImageRef.current, zoom, positionX, positionY)
  }, [cropImageSrc, zoom, positionX, positionY])

  useEffect(() => () => {
    if (cropImageSrc) URL.revokeObjectURL(cropImageSrc)
  }, [cropImageSrc])

  const save = useMutation({
    mutationFn: () => authApi.updateProfile({ displayName, avatarDataUrl }),
    onSuccess: updated => {
      queryClient.setQueryData(['session'], updated)
      setImageError('')
    },
  })

  function selectAvatar(file?: File) {
    if (!file) return
    if (!file.type.startsWith('image/')) {
      setImageError('Selecciona una imagen válida.')
      return
    }
    if (file.size > 8 * 1024 * 1024) {
      setImageError('La imagen original no puede superar 8 MB.')
      return
    }

    setImageError('')
    const objectUrl = URL.createObjectURL(file)
    const image = new Image()
    image.onload = () => {
      cropImageRef.current = image
      setZoom(1)
      setPositionX(0)
      setPositionY(0)
      setCropImageSrc(objectUrl)
    }
    image.onerror = () => {
      URL.revokeObjectURL(objectUrl)
      setImageError('No fue posible abrir la imagen seleccionada.')
    }
    image.src = objectUrl
  }

  function cancelCrop() {
    setCropImageSrc(null)
    cropImageRef.current = null
  }

  function applyCrop() {
    const image = cropImageRef.current
    if (!image) return
    try {
      const canvas = document.createElement('canvas')
      canvas.width = 256
      canvas.height = 256
      drawCrop(canvas, image, zoom, positionX, positionY)
      const dataUrl = canvas.toDataURL('image/jpeg', 0.82)
      if (dataUrl.length > 200_000) throw new Error('La imagen procesada sigue siendo demasiado grande.')
      setAvatarDataUrl(dataUrl)
      setImageError('')
      cancelCrop()
    } catch (error) {
      setImageError(error instanceof Error ? error.message : 'No fue posible procesar la imagen.')
    }
  }

  if (!session) return <section className="settings-page"><div className="reading-skeleton" /></section>

  return <section className="settings-page profile-page">
    <p className="eyebrow">Configuración</p>
    <h1>Mi perfil</h1>
    <p className="page-description">Actualiza cómo aparece tu usuario dentro de NexoMail.</p>

    {save.isSuccess && <div className="success-notice">Perfil actualizado correctamente.</div>}
    {save.isError && <div className="notice">{save.error instanceof Error ? save.error.message : 'No fue posible guardar el perfil.'}</div>}
    {imageError && <div className="notice">{imageError}</div>}

    <form className="settings-card profile-card" onSubmit={event => { event.preventDefault(); save.mutate() }}>
      <div className="profile-avatar-section">
        <div className="profile-avatar" aria-label="Avatar actual">
          {avatarDataUrl ? <img src={avatarDataUrl} alt="Avatar del usuario" /> : <span>{initials(displayName || session.email)}</span>}
        </div>
        <div className="profile-avatar-actions">
          <div><strong>Foto de perfil</strong><p>Elige una foto y ajusta el encuadre antes de guardarla.</p></div>
          <div className="profile-avatar-buttons">
            <label className="secondary-button profile-file-button"><Camera size={16} /> Elegir foto<input type="file" accept="image/jpeg,image/png,image/webp" onChange={event => selectAvatar(event.target.files?.[0])} /></label>
            {avatarDataUrl && <button type="button" className="secondary-button danger-button" onClick={() => setAvatarDataUrl(null)}><Trash2 size={15} /> Quitar</button>}
          </div>
        </div>
      </div>

      <div className="profile-fields">
        <label>Nombre visible<input value={displayName} onChange={event => setDisplayName(event.target.value)} minLength={2} maxLength={120} required /></label>
        <label>Correo de la cuenta<input value={session.email} readOnly aria-readonly="true" /></label>
        <p className="profile-email-note"><UserRound size={15} /> El correo no se modifica desde aquí porque requiere una verificación de seguridad independiente.</p>
      </div>

      <footer className="profile-footer"><button className="primary-button" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar perfil'}</button></footer>
    </form>

    {cropImageSrc && <div className="modal-backdrop" role="presentation">
      <section className="avatar-editor" role="dialog" aria-modal="true" aria-labelledby="avatar-editor-title">
        <header><div><p className="eyebrow">Foto de perfil</p><h2 id="avatar-editor-title">Ajustar foto</h2></div><button type="button" className="icon-button" onClick={cancelCrop} aria-label="Cerrar"><X size={19} /></button></header>
        <div className="avatar-editor-body">
          <div className="avatar-crop-preview"><canvas ref={previewCanvasRef} width={320} height={320} aria-label="Vista previa del recorte" /></div>
          <div className="avatar-editor-controls">
            <label><span><ZoomIn size={15} /> Zoom</span><input type="range" min="1" max="3" step="0.01" value={zoom} onChange={event => setZoom(Number(event.target.value))} /></label>
            <label><span><Move size={15} /> Posición horizontal</span><input type="range" min="-100" max="100" step="1" value={positionX} onChange={event => setPositionX(Number(event.target.value))} /></label>
            <label><span><Move size={15} /> Posición vertical</span><input type="range" min="-100" max="100" step="1" value={positionY} onChange={event => setPositionY(Number(event.target.value))} /></label>
          </div>
        </div>
        <footer><button type="button" className="secondary-button" onClick={cancelCrop}>Cancelar</button><button type="button" className="primary-button" onClick={applyCrop}>Usar esta foto</button></footer>
      </section>
    </div>}
  </section>
}
