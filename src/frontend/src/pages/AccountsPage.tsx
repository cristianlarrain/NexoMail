import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { CheckCircle2, ListFilter, MailPlus, Pencil, Trash2, X } from 'lucide-react'
import { AddAccountModal } from '../components/AddAccountModal'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { commercialApi } from '../api/commercialApi'
import { mailApi } from '../api/mailApi'
import type { MailAccount } from '../types/mail'

function providerName(provider: MailAccount['provider']) {
  if (provider === 'Gmail') return 'Gmail'
  if (provider === 'MicrosoftGraph') return 'Microsoft 365'
  if (provider === 'Imap') return 'IMAP / SMTP · Beta'
  return provider
}

export function AccountsPage() {
  const [params] = useSearchParams()
  const queryClient = useQueryClient()
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const { data: subscription } = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const [addingAccount, setAddingAccount] = useState(false)
  const [editing, setEditing] = useState<MailAccount | null>(null)
  const [removeCandidate, setRemoveCandidate] = useState<MailAccount | null>(null)
  const [displayName, setDisplayName] = useState('')
  const [color, setColor] = useState('#c6524b')
  const save = useMutation({ mutationFn: () => mailApi.updateAccount(editing!.id, { displayName, color }), onSuccess: () => { queryClient.invalidateQueries({ queryKey: ['accounts'] }); setEditing(null) } })
  const remove = useMutation({
    mutationFn: (account: MailAccount) => mailApi.removeAccount(account.id),
    onSuccess: (_, account) => {
      if (editing?.id === account.id) setEditing(null)
      setRemoveCandidate(null)
      void queryClient.invalidateQueries({ queryKey: ['accounts'] })
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] })
    },
  })
  const openEdit = (account: MailAccount) => { setEditing(account); setDisplayName(account.displayName); setColor(account.color) }
  const error = params.get('error')
  const connected = params.get('connected')
  const accountLimitReached = subscription ? !subscription.canAddAccount : false
  const accountUsage = subscription?.currentPlan.maxAccounts
    ? `${subscription.connectedAccounts} de ${subscription.currentPlan.maxAccounts}`
    : subscription ? `${subscription.connectedAccounts}` : null

  return <section className="settings-page">
    <div className="settings-heading-row">
      <div><p className="eyebrow">Configuración</p><h1>Cuentas de correo</h1><p className="page-description">Administra las cuentas que aparecen en tu bandeja unificada.</p><span className="settings-marcha-blanca">Marcha blanca · 30 días</span></div>
      <Link className="secondary-button settings-rules-link" to="/settings/rules"><ListFilter size={16} /> Reglas automáticas</Link>
    </div>
    {connected === 'google' && <div className="success-notice">La cuenta Gmail fue conectada correctamente.</div>}
    {connected === 'microsoft' && <div className="success-notice">La cuenta Microsoft 365 fue conectada correctamente.</div>}
    {error && <div className="notice">{error}</div>}
    {accountLimitReached && <div className="notice">Su plan {subscription?.currentPlan.name} alcanzó el límite de cuentas conectadas. <Link to="/settings/plan">Ver plan y alternativas</Link>.</div>}
    {remove.isSuccess && <div className="success-notice">La cuenta fue quitada de NexoMail. Sus correos permanecen con su proveedor.</div>}
    {remove.isError && <div className="notice">{remove.error instanceof Error ? remove.error.message : 'No se pudo quitar la cuenta.'}</div>}
    <div className="settings-card">
      <div className="settings-card-header">
        <div><h2>Cuentas conectadas</h2><p>Las credenciales se conservan protegidas; los mensajes permanecen con cada proveedor.{subscription && <> Plan {subscription.currentPlan.name}{accountUsage ? ` · ${accountUsage} cuentas en uso` : ''}.</>}</p></div>
        <button className="primary-button" disabled={accountLimitReached} title={accountLimitReached ? 'Ha alcanzado el límite de cuentas de su plan.' : 'Agregar una cuenta de correo'} onClick={() => setAddingAccount(true)}><MailPlus size={16} /> Agregar cuenta</button>
      </div>
      {accounts.map(account => <div className="account-row" key={account.id}><i className="account-dot large" style={{ background: account.color }} /><div><strong>{account.displayName}</strong><span>{account.emailAddress}</span></div><span className="provider-label">{providerName(account.provider)}</span><span className="connected"><CheckCircle2 size={16} /> Conectada</span><button className="icon-button" aria-label={`Editar ${account.displayName}`} title="Editar cuenta" onClick={() => openEdit(account)}><Pencil size={17} /></button><button className="icon-button danger-icon" aria-label={`Quitar ${account.displayName}`} title="Quitar cuenta" disabled={remove.isPending} onClick={() => setRemoveCandidate(account)}><Trash2 size={17} /></button></div>)}
    </div>
    <AddAccountModal open={addingAccount} onClose={() => setAddingAccount(false)} />
    {editing && <div className="modal-backdrop" role="presentation"><form className="account-modal" onSubmit={event => { event.preventDefault(); save.mutate() }}><header><div><p className="eyebrow">Cuenta conectada</p><h2>Editar cuenta</h2></div><button type="button" className="icon-button" onClick={() => setEditing(null)} aria-label="Cerrar"><X size={19} /></button></header><p className="modal-email">{editing.emailAddress} · {providerName(editing.provider)}</p><label>Nombre visible<input value={displayName} onChange={event => setDisplayName(event.target.value)} maxLength={80} required autoFocus /></label><label>Color identificador<span className="color-input"><input type="color" value={color} onChange={event => setColor(event.target.value)} aria-label="Seleccionar color" /><input value={color} onChange={event => setColor(event.target.value)} pattern="#[0-9a-fA-F]{6}" required /></span></label>{save.isError && <p className="form-error">No se pudo guardar. Revisa el nombre y el color.</p>}<footer><button type="button" className="secondary-button" onClick={() => setEditing(null)}>Cancelar</button><button className="primary-button" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar cambios'}</button></footer></form></div>}
    <ConfirmDialog open={Boolean(removeCandidate)} title="Quitar cuenta de NexoMail" message={removeCandidate ? `${removeCandidate.emailAddress} dejará de aparecer en NexoMail. Sus correos permanecerán intactos con su proveedor.` : ''} confirmLabel="Quitar cuenta" pending={remove.isPending} onCancel={() => setRemoveCandidate(null)} onConfirm={() => { if (removeCandidate) remove.mutate(removeCandidate) }} />
  </section>
}
