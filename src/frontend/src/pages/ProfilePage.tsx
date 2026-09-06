import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Camera, Clock3, LogOut, Monitor, Move, ShieldCheck, Trash2, UserRound, X, ZoomIn } from 'lucide-react'
import { authApi, type ActiveSession } from '../api/authApi'
import { ConfirmDialog } from '../components/ConfirmDialog'

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

function browserName(userAgent: string | null) {
  if (!userAgent) return 'Navegador desconocido'
  if (/Edg\//.test(userAgent)) return 'Microsoft Edge'
  if (/OPR\//.test(userAgent)) return 'Opera'
  if (/Firefox\//.test(userAgent)) return 'Firefox'
  if (/Chrome\//.test(userAgent)) return 'Google Chrome'
  if (/Safari\//.test(userAgent)) return 'Safari'
  return 'Navegador desconocido'
}

function deviceName(userAgent: string | null) {
  if (!userAgent) return 'Dispositivo desconocido'
  if (/iPhone/.test(userAgent)) return 'iPhone'
  if (/iPad/.test(userAgent)) return 'iPad'
  if (/Android/.test(userAgent)) return 'Android'
  if (/Windows/.test(userAgent)) return 'PC con Windows'
  if (/Macintosh|Mac OS/.test(userAgent)) return 'Mac'
  if (/Linux/.test(userAgent)) return 'Equipo Linux'
  return 'Dispositivo desconocido'
}

function formatDate(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return 'Fecha no disponible'
  return new Intl.DateTimeFormat('es-CL', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

type SessionAction =
  | { kind: 'one'; session: ActiveSession }
  | { kind: 'others' }
  | null

export function ProfilePage() {
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false })
  const { data: sessions = [], isLoading: sessionsLoading, error: sessionsError } = useQuery({
    queryKey: ['active-sessions'],
    queryFn: authApi.getSessions,
    retry: false,
  })
  const [displayName, setDisplayName] = useState('')
  const [avatarDataUrl, setAvatarDataUrl] = useState<string | null>(null)
  const [imageError, setImageError] = useState('')
  const [cropImageSrc, setCropImageSrc] = useState<string | null>(null)
  const [zoom, setZoom] = useState(1)
  const [positionX, setPositionX] = useState(0)
  const [positionY, setPositionY] = useState(0)
  const [sessionAction, setSessionAction] = useState<SessionAction>(null)
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

  const revokeSession = useMutation({
    mutationFn: (sessionId: string) => authApi.revokeSession(sessionId),
    onSuccess: () => {
      setSessionAction(null)
      void queryClient.invalidateQueries({ queryKey: ['active-sessions'] })
    },
  })

  const revokeOthers = useMutation({
    mutationFn: authApi.revokeOtherSessions,
    onSuccess: () => {
      setSessionAction(null)
      void queryClient.invalidateQueries({ queryKey: ['active-sessions'] })
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

  function confirmSessionAction() {
    if (!sessionAction) return
    if (sessionAction.kind === 'one') revokeSession.mutate(sessionAction.session.id)
    else revokeOthers.mutate()
  }

  if (!session) return <section className="settings-page"><div className="reading-skeleton" /></section>

  const otherSessionCount = sessions.filter(item => !item.isCurrent).length
  const sessionMutationError = revokeSession.error ?? revokeOthers.error
  const sessionMutationPending = revokeSession.isPending || revokeOthers.isPending

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

    <div className="profile-section-heading">
      <div><p className="eyebrow">Seguridad</p><h2>Sesiones activas</h2><p>Revisa dónde está abierta tu cuenta NexoMail y cierra accesos que ya no uses.</p></div>
      {otherSessionCount > 0 && <button type="button" className="secondary-button danger-button" onClick={() => setSessionAction({ kind: 'others' })}><LogOut size={15} /> Cerrar las demás</button>}
    </div>

    <section className="settings-card sessions-card" aria-label="Sesiones activas">
      {sessionsLoading ? <div className="sessions-loading"><div className="reading-skeleton" /></div> : sessionsError ? <div className="sessions-message notice">{sessionsError instanceof Error ? sessionsError.message : 'No fue posible consultar las sesiones activas.'}</div> : sessions.length === 0 ? <div className="sessions-message">No hay sesiones activas para mostrar.</div> : <div className="session-list">
        {sessions.map(item => <article className={`session-row ${item.isCurrent ? 'current' : ''}`} key={item.id}>
          <div className="session-device-icon" aria-hidden="true"><Monitor size={20} /></div>
          <div className="session-details">
            <div className="session-title-line"><strong>{browserName(item.userAgent)} · {deviceName(item.userAgent)}</strong>{item.isCurrent && <span className="current-session-badge"><ShieldCheck size={13} /> Esta sesión</span>}</div>
            <div className="session-meta"><span><Clock3 size={13} /> {item.isCurrent ? 'Activa ahora' : `Última actividad: ${formatDate(item.lastSeenAt)}`}</span><span>Inicio: {formatDate(item.createdAt)}</span>{item.ipAddress && <span>IP: {item.ipAddress}</span>}</div>
          </div>
          {!item.isCurrent && <button type="button" className="secondary-button session-revoke-button" onClick={() => setSessionAction({ kind: 'one', session: item })}><LogOut size={15} /> Cerrar sesión</button>}
        </article>)}
      </div>}
    </section>

    {revokeSession.isSuccess && <div className="success-notice session-status">La sesión seleccionada fue cerrada.</div>}
    {revokeOthers.isSuccess && <div className="success-notice session-status">Se cerraron {revokeOthers.data.revoked} sesión{revokeOthers.data.revoked === 1 ? '' : 'es'} adicional{revokeOthers.data.revoked === 1 ? '' : 'es'}.</div>}
    {sessionMutationError && <div className="notice session-status">{sessionMutationError instanceof Error ? sessionMutationError.message : 'No fue posible cerrar la sesión.'}</div>}

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

    <ConfirmDialog
      open={sessionAction !== null}
      title={sessionAction?.kind === 'others' ? 'Cerrar las demás sesiones' : 'Cerrar esta sesión'}
      message={sessionAction?.kind === 'others'
        ? `Se cerrarán ${otherSessionCount} sesión${otherSessionCount === 1 ? '' : 'es'} en otros navegadores o dispositivos. Esta sesión permanecerá abierta.`
        : sessionAction?.kind === 'one'
          ? `Se cerrará la sesión de ${browserName(sessionAction.session.userAgent)} en ${deviceName(sessionAction.session.userAgent)}.`
          : ''}
      confirmLabel={sessionAction?.kind === 'others' ? 'Cerrar las demás' : 'Cerrar sesión'}
      pending={sessionMutationPending}
      onConfirm={confirmSessionAction}
      onCancel={() => { if (!sessionMutationPending) setSessionAction(null) }}
    />
  </section>
}
