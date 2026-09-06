import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { CheckCircle2, MailPlus, Pencil, PlugZap, Trash2, X } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import type { MailAccount } from '../types/mail'

export function AccountsPage() {
  const [params] = useSearchParams()
  const queryClient = useQueryClient()
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const [editing, setEditing] = useState<MailAccount | null>(null)
  const [displayName, setDisplayName] = useState('')
  const [color, setColor] = useState('#c6524b')
  const save = useMutation({ mutationFn: () => mailApi.updateAccount(editing!.id, { displayName, color }), onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['accounts'] }); setEditing(null) } })
  const remove = useMutation({
    mutationFn: (account: MailAccount) => mailApi.removeAccount(account.id),
    onSuccess: (_, account) => {
      if (editing?.id === account.id) setEditing(null)
      void queryClient.invalidateQueries({ queryKey: ['accounts'] })
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
    },
  })
  const openEdit = (account: MailAccount) => { setEditing(account); setDisplayName(account.displayName); setColor(account.color) }
  const error = params.get('error')

  return <section className="settings-page">
    <p className="eyebrow">Configuración</p><h1>Cuentas de correo</h1><p className="page-description">Administra las cuentas que aparecen en tu bandeja unificada.</p>
    {params.get('connected') === 'google' && <div className="success-notice">La cuenta Gmail fue conectada correctamente.</div>}{error && <div className="notice">{error}</div>}
    {remove.isSuccess && <div className="success-notice">La cuenta fue quitada de NexoMail. Sus correos permanecen en el proveedor.</div>}
    {remove.isError && <div className="notice">{remove.error instanceof Error ? remove.error.message : 'No se pudo quitar la cuenta.'}</div>}
    <div className="settings-card"><div className="settings-card-header"><div><h2>Cuentas conectadas</h2><p>Las credenciales se conservan protegidas; los mensajes permanecen con cada proveedor.</p></div><button className="primary-button" onClick={() => window.location.assign('/api/oauth/google/start')}><MailPlus size={16} /> Agregar Gmail</button></div>
      {accounts.map(account => <div className="account-row" key={account.id}><i className="account-dot large" style={{ background: account.color }} /><div><strong>{account.displayName}</strong><span>{account.emailAddress}</span></div><span className="provider-label">{account.provider === 'Gmail' ? 'Gmail' : account.provider === 'MicrosoftGraph' ? 'Microsoft 365' : account.provider}</span><span className="connected"><CheckCircle2 size={16} /> Conectada</span><button className="icon-button" aria-label={`Editar ${account.displayName}`} title="Editar cuenta" onClick={() => openEdit(account)}><Pencil size={17} /></button><button className="icon-button danger-icon" aria-label={`Quitar ${account.displayName}`} title="Quitar cuenta" disabled={remove.isPending} onClick={() => { if (window.confirm(`¿Quitar ${account.emailAddress} de NexoMail? Los correos permanecerán en Gmail.`)) remove.mutate(account) }}><Trash2 size={17} /></button></div>)}</div>
    <div className="provider-preview"><PlugZap size={19} /><div><strong>IMAP / SMTP</strong><p>Próximamente podrás conectar otros proveedores de correo.</p></div><span>Próximamente</span></div>
    {editing && <div className="modal-backdrop" role="presentation"><form className="account-modal" onSubmit={event => { event.preventDefault(); save.mutate() }}><header><div><p className="eyebrow">Cuenta conectada</p><h2>Editar cuenta</h2></div><button type="button" className="icon-button" onClick={() => setEditing(null)} aria-label="Cerrar"><X size={19} /></button></header><p className="modal-email">{editing.emailAddress} · {editing.provider === 'Gmail' ? 'Gmail' : editing.provider}</p><label>Nombre visible<input value={displayName} onChange={event => setDisplayName(event.target.value)} maxLength={80} required autoFocus /></label><label>Color identificador<span className="color-input"><input type="color" value={color} onChange={event => setColor(event.target.value)} aria-label="Seleccionar color" /><input value={color} onChange={event => setColor(event.target.value)} pattern="#[0-9a-fA-F]{6}" required /></span></label>{save.isError && <p className="form-error">No se pudo guardar. Revisa el nombre y el color.</p>}<footer><button type="button" className="secondary-button" onClick={() => setEditing(null)}>Cancelar</button><button className="primary-button" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar cambios'}</button></footer></form></div>}
  </section>
}
