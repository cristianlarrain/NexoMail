import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Building2, ChevronLeft, Info, X } from 'lucide-react'
import { accountProviderApi, type ImapConnectionRequest } from '../api/accountProviderApi'
import { MailProviderLogo } from './MailProviderLogo'

type Props = {
  open: boolean
  onClose: () => void
}

const initialImap: ImapConnectionRequest = {
  emailAddress: '', displayName: '', username: '', password: '',
  imapHost: '', imapPort: 993, imapSecurity: 'ssl',
  smtpHost: '', smtpPort: 587, smtpSecurity: 'starttls',
}

export function AddAccountModal({ open, onClose }: Props) {
  const queryClient = useQueryClient()
  const [step, setStep] = useState<'providers' | 'imap'>('providers')
  const [form, setForm] = useState<ImapConnectionRequest>(initialImap)
  const connectImap = useMutation({
    mutationFn: accountProviderApi.connectImap,
    onSuccess: () => {
      setForm(initialImap)
      setStep('providers')
      void queryClient.invalidateQueries({ queryKey: ['accounts'] })
      void queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] })
      onClose()
    },
  })
  if (!open) return null

  const close = () => {
    if (connectImap.isPending) return
    setStep('providers')
    setForm(initialImap)
    onClose()
  }
  const set = <K extends keyof ImapConnectionRequest>(key: K, value: ImapConnectionRequest[K]) => setForm(current => ({ ...current, [key]: value }))

  if (step === 'imap') {
    return <div className="modal-backdrop" role="presentation">
      <form className="account-modal imap-connect-modal" onSubmit={event => { event.preventDefault(); connectImap.mutate(form) }}>
        <header>
          <div><p className="eyebrow">IMAP / SMTP · Beta</p><h2>Configurar otro correo</h2></div>
          <button type="button" className="icon-button" onClick={close} aria-label="Cerrar"><X size={19} /></button>
        </header>
        <p className="imap-helper">Usa los datos que entrega tu proveedor de correo. NexoMail probará primero IMAP y SMTP y sólo guardará la cuenta si ambas conexiones funcionan.</p>
        <div className="imap-form-grid">
          <label>Correo electrónico<input type="email" value={form.emailAddress} onChange={event => { set('emailAddress', event.target.value); if (!form.username) set('username', event.target.value) }} required autoFocus /></label>
          <label>Nombre visible<input value={form.displayName} onChange={event => set('displayName', event.target.value)} maxLength={80} placeholder="Ej. Trabajo" /></label>
          <label>Usuario<input value={form.username} onChange={event => set('username', event.target.value)} required autoComplete="username" /></label>
          <label>Contraseña / contraseña de aplicación<input type="password" value={form.password} onChange={event => set('password', event.target.value)} required autoComplete="new-password" /></label>
          <label className="full">Servidor IMAP<span className="imap-server-row"><input value={form.imapHost} onChange={event => set('imapHost', event.target.value)} placeholder="imap.ejemplo.cl" required /><input type="number" value={form.imapPort} onChange={event => set('imapPort', Number(event.target.value))} min={1} max={65535} required /><select value={form.imapSecurity} onChange={event => set('imapSecurity', event.target.value as 'ssl' | 'starttls')}><option value="ssl">SSL/TLS</option><option value="starttls">STARTTLS</option></select></span></label>
          <label className="full">Servidor SMTP<span className="imap-server-row"><input value={form.smtpHost} onChange={event => set('smtpHost', event.target.value)} placeholder="smtp.ejemplo.cl" required /><input type="number" value={form.smtpPort} onChange={event => set('smtpPort', Number(event.target.value))} min={1} max={65535} required /><select value={form.smtpSecurity} onChange={event => set('smtpSecurity', event.target.value as 'ssl' | 'starttls')}><option value="starttls">STARTTLS</option><option value="ssl">SSL/TLS</option></select></span></label>
        </div>
        {connectImap.isError && <p className="form-error">{connectImap.error instanceof Error ? connectImap.error.message : 'No fue posible conectar la cuenta.'}</p>}
        <footer><button type="button" className="secondary-button" onClick={() => setStep('providers')} disabled={connectImap.isPending}><ChevronLeft size={16} /> Volver</button><button className="primary-button" disabled={connectImap.isPending}>{connectImap.isPending ? 'Probando servidores…' : 'Probar y conectar'}</button></footer>
      </form>
    </div>
  }

  return <div className="modal-backdrop" role="presentation">
    <section className="account-modal mail-provider-modal" role="dialog" aria-modal="true" aria-labelledby="add-account-title">
      <header><div><p className="eyebrow">Cuentas de correo</p><h2 id="add-account-title">Agregar cuenta de correo</h2><p>Elige cómo quieres conectar tu correo a NexoMail.</p></div><button type="button" className="icon-button" onClick={close} aria-label="Cerrar"><X size={19} /></button></header>
      <div className="mail-provider-grid">
        <button type="button" className="mail-provider-card" onClick={() => window.location.assign('/api/oauth/google/start')}><MailProviderLogo provider="gmail" /><strong>Gmail / Google Workspace</strong><p>Conexión segura con Google mediante autorización OAuth.</p><span className="provider-action">Conectar con Google</span></button>
        <button type="button" className="mail-provider-card" onClick={() => window.location.assign('/api/oauth/microsoft/start')}><MailProviderLogo provider="microsoft" /><strong>Microsoft 365</strong><p>Cuenta profesional, educativa o institucional mediante Microsoft Graph.</p><span className="provider-action">Conectar con Microsoft</span></button>
        <button type="button" className="mail-provider-card" onClick={() => setStep('imap')}><MailProviderLogo provider="imap" /><span className="beta-badge">Beta</span><strong>IMAP / SMTP</strong><p>Dominio propio u otro proveedor compatible con configuración manual.</p><span className="provider-action">Configurar manualmente</span></button>
      </div>
      <div className="provider-admin-note"><Building2 size={17} /><span><strong>Microsoft 365 institucional:</strong> algunas organizaciones requieren autorización previa de su administrador para permitir aplicaciones externas como NexoMail.</span></div>
      <div className="provider-coming-soon"><span>Próximamente</span><div className="provider-future-list"><div className="provider-future-item"><MailProviderLogo provider="outlook" size={28} /><div>Outlook / Hotmail<small>Cuentas personales Microsoft</small></div></div><div className="provider-future-item"><MailProviderLogo provider="yahoo" size={28} /><div>Yahoo Mail<small>Integración dedicada</small></div></div><div className="provider-future-item"><MailProviderLogo provider="exchange" size={28} /><div>Exchange Server<small>Entornos locales / híbridos</small></div></div></div></div>
      <div className="settings-marcha-blanca"><Info size={14} /> Marcha blanca · 30 días</div>
    </section>
  </div>
}
