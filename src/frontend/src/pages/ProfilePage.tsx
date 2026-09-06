import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Camera, Trash2, UserRound } from 'lucide-react'
import { authApi } from '../api/authApi'

function initials(value: string) {
  return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || 'NM'
}

function compressAvatar(file: File) {
  return new Promise<string>((resolve, reject) => {
    if (!file.type.startsWith('image/')) {
      reject(new Error('Selecciona una imagen válida.'))
      return
    }
    if (file.size > 8 * 1024 * 1024) {
      reject(new Error('La imagen original no puede superar 8 MB.'))
      return
    }

    const objectUrl = URL.createObjectURL(file)
    const image = new Image()
    image.onload = () => {
      try {
        const canvas = document.createElement('canvas')
        canvas.width = 256
        canvas.height = 256
        const context = canvas.getContext('2d')
        if (!context) throw new Error('No fue posible procesar la imagen.')

        const side = Math.min(image.naturalWidth, image.naturalHeight)
        const sourceX = (image.naturalWidth - side) / 2
        const sourceY = (image.naturalHeight - side) / 2
        context.fillStyle = '#ffffff'
        context.fillRect(0, 0, 256, 256)
        context.drawImage(image, sourceX, sourceY, side, side, 0, 0, 256, 256)
        const dataUrl = canvas.toDataURL('image/jpeg', 0.82)
        if (dataUrl.length > 200_000) throw new Error('La imagen procesada sigue siendo demasiado grande.')
        resolve(dataUrl)
      } catch (error) {
        reject(error instanceof Error ? error : new Error('No fue posible procesar la imagen.'))
      } finally {
        URL.revokeObjectURL(objectUrl)
      }
    }
    image.onerror = () => {
      URL.revokeObjectURL(objectUrl)
      reject(new Error('No fue posible abrir la imagen seleccionada.'))
    }
    image.src = objectUrl
  })
}

export function ProfilePage() {
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false })
  const [displayName, setDisplayName] = useState('')
  const [avatarDataUrl, setAvatarDataUrl] = useState<string | null>(null)
  const [imageError, setImageError] = useState('')

  useEffect(() => {
    if (!session) return
    setDisplayName(session.displayName)
    setAvatarDataUrl(session.avatarDataUrl)
  }, [session])

  const save = useMutation({
    mutationFn: () => authApi.updateProfile({ displayName, avatarDataUrl }),
    onSuccess: updated => {
      queryClient.setQueryData(['session'], updated)
      setImageError('')
    },
  })

  async function selectAvatar(file?: File) {
    if (!file) return
    try {
      setImageError('')
      setAvatarDataUrl(await compressAvatar(file))
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
          <div><strong>Foto de perfil</strong><p>Se recorta automáticamente en formato cuadrado.</p></div>
          <div className="profile-avatar-buttons">
            <label className="secondary-button profile-file-button"><Camera size={16} /> Elegir foto<input type="file" accept="image/jpeg,image/png,image/webp" onChange={event => void selectAvatar(event.target.files?.[0])} /></label>
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
  </section>
}
