import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Check, Pencil, Plus, ShieldCheck, Trash2, X } from 'lucide-react'
import { Link } from 'react-router-dom'
import { commercialApi, type CommercialAdminPlan, type CommercialPlanWriteRequest } from '../api/commercialApi'
import { ConfirmDialog } from '../components/ConfirmDialog'

type PlanDraft = {
  code: string
  name: string
  price: string
  cadence: string
  maxAccounts: string
  description: string
  features: string
  entitlements: string[]
  isFeatured: boolean
  isCorporate: boolean
  isWhiteLabel: boolean
  isActive: boolean
  sortOrder: string
}

const baseEntitlements = ['unified_mail', 'mail_actions', 'control_center_basic', 'tracking_basic']

const emptyDraft: PlanDraft = {
  code: '',
  name: '',
  price: '',
  cadence: 'CLP / mes',
  maxAccounts: '',
  description: '',
  features: '',
  entitlements: [...baseEntitlements],
  isFeatured: false,
  isCorporate: false,
  isWhiteLabel: false,
  isActive: true,
  sortOrder: '50',
}

function fromPlan(plan: CommercialAdminPlan): PlanDraft {
  return {
    code: plan.code,
    name: plan.name,
    price: plan.price,
    cadence: plan.cadence,
    maxAccounts: plan.maxAccounts?.toString() ?? '',
    description: plan.description,
    features: plan.features.join('\n'),
    entitlements: [...plan.entitlements],
    isFeatured: plan.isFeatured,
    isCorporate: plan.isCorporate,
    isWhiteLabel: plan.isWhiteLabel,
    isActive: plan.isActive,
    sortOrder: plan.sortOrder.toString(),
  }
}

function toRequest(draft: PlanDraft, includeCode: boolean): CommercialPlanWriteRequest {
  const maxAccounts = draft.maxAccounts.trim() === '' ? null : Number(draft.maxAccounts)
  return {
    ...(includeCode ? { code: draft.code.trim() } : {}),
    name: draft.name.trim(),
    price: draft.price.trim(),
    cadence: draft.cadence.trim(),
    maxAccounts,
    description: draft.description.trim(),
    features: draft.features.split(/\r?\n/).map(value => value.trim()).filter(Boolean),
    entitlements: draft.entitlements,
    isFeatured: draft.isFeatured,
    isCorporate: draft.isCorporate,
    isWhiteLabel: draft.isWhiteLabel,
    isActive: draft.isActive,
    sortOrder: Number(draft.sortOrder || 0),
  }
}

export function AdminPlansPage() {
  const queryClient = useQueryClient()
  const status = useQuery({ queryKey: ['commercial-admin-status'], queryFn: commercialApi.adminStatus, staleTime: 5 * 60_000, retry: false })
  const plans = useQuery({ queryKey: ['commercial-admin-plans'], queryFn: commercialApi.adminPlans, enabled: status.data?.isAdministrator === true, staleTime: 0, retry: false })
  const entitlementDefinitions = useQuery({ queryKey: ['commercial-entitlements'], queryFn: commercialApi.entitlements, enabled: status.data?.isAdministrator === true, staleTime: 5 * 60_000, retry: false })
  const [editing, setEditing] = useState<CommercialAdminPlan | 'new' | null>(null)
  const [draft, setDraft] = useState<PlanDraft>(emptyDraft)
  const [deleteCandidate, setDeleteCandidate] = useState<CommercialAdminPlan | null>(null)

  const save = useMutation({
    mutationFn: async () => editing === 'new'
      ? commercialApi.createPlan(toRequest(draft, true))
      : commercialApi.updatePlan(editing!.code, toRequest(draft, false)),
    onSuccess: async () => {
      setEditing(null)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['commercial-admin-plans'] }),
        queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] }),
      ])
    },
  })

  const remove = useMutation({
    mutationFn: (plan: CommercialAdminPlan) => commercialApi.deletePlan(plan.code),
    onSuccess: async () => {
      setDeleteCandidate(null)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['commercial-admin-plans'] }),
        queryClient.invalidateQueries({ queryKey: ['commercial-subscription'] }),
      ])
    },
  })

  function openNew() {
    setDraft({ ...emptyDraft, entitlements: [...baseEntitlements] })
    setEditing('new')
  }

  function openEdit(plan: CommercialAdminPlan) {
    setDraft(fromPlan(plan))
    setEditing(plan)
  }

  function toggleEntitlement(code: string) {
    setDraft(current => ({
      ...current,
      entitlements: current.entitlements.includes(code)
        ? current.entitlements.filter(value => value !== code)
        : [...current.entitlements, code],
    }))
  }

  if (status.isLoading) return <section className="settings-page commercial-admin-page"><p className="eyebrow">Administración</p><h1>Tipos de cuenta</h1><div className="commercial-plan-loading">Comprobando permisos…</div></section>
  if (!status.data?.isAdministrator) return <section className="settings-page commercial-admin-page"><p className="eyebrow">Administración</p><h1>Tipos de cuenta</h1><div className="notice">Esta sección está disponible sólo para administradores de NexoMail.</div><Link to="/settings/plan" className="secondary-button commercial-back-link"><ArrowLeft size={16} /> Volver a Plan y uso</Link></section>

  return <section className="settings-page commercial-admin-page">
    <div className="commercial-admin-header">
      <div><p className="eyebrow">Administración</p><h1>Tipos de cuenta</h1><p className="page-description">Cree, modifique, desactive o elimine los planes comerciales de NexoMail.</p></div>
      <div className="commercial-admin-header-actions"><Link to="/settings/plan" className="secondary-button"><ArrowLeft size={16} /> Plan y uso</Link><button type="button" className="primary-button" onClick={openNew}><Plus size={16} /> Nuevo tipo</button></div>
    </div>

    <div className="commercial-admin-guidance"><ShieldCheck size={18} /><span>Freemium se conserva como plan base. El límite de cuentas y las funciones habilitadas pueden configurarse por plan. Un tipo de cuenta con usuarios asignados no puede eliminarse; primero debe reasignarlos o dejar el plan inactivo.</span></div>
    {plans.isError && <div className="notice">{plans.error instanceof Error ? plans.error.message : 'No fue posible cargar los tipos de cuenta.'}</div>}
    {remove.isError && <div className="notice">{remove.error instanceof Error ? remove.error.message : 'No fue posible eliminar el tipo de cuenta.'}</div>}

    <div className="commercial-admin-table-wrap">
      <table className="commercial-admin-table">
        <thead><tr><th>Tipo</th><th>Precio</th><th>Límite</th><th>Funciones</th><th>Usuarios</th><th>Estado</th><th aria-label="Acciones" /></tr></thead>
        <tbody>
          {(plans.data ?? []).map(plan => <tr key={plan.code} className={plan.isActive ? '' : 'inactive'}>
            <td><strong>{plan.name}</strong><small>{plan.code}</small></td>
            <td><strong>{plan.price}</strong><small>{plan.cadence}</small></td>
            <td>{plan.maxAccounts ?? 'Sin límite'}</td>
            <td><span className="commercial-entitlement-count">{plan.entitlements.length}</span></td>
            <td>{plan.assignedUsers}</td>
            <td><span className={`commercial-admin-status ${plan.isActive ? 'active' : 'inactive'}`}>{plan.isActive ? 'Activo' : 'Inactivo'}</span></td>
            <td><div className="commercial-admin-row-actions"><button type="button" className="icon-button" title={`Editar ${plan.name}`} aria-label={`Editar ${plan.name}`} onClick={() => openEdit(plan)}><Pencil size={16} /></button><button type="button" className="icon-button danger-icon" title={plan.canDelete ? `Eliminar ${plan.name}` : 'No puede eliminarse mientras tenga usuarios asignados o sea el plan base'} aria-label={`Eliminar ${plan.name}`} disabled={!plan.canDelete} onClick={() => setDeleteCandidate(plan)}><Trash2 size={16} /></button></div></td>
          </tr>)}
        </tbody>
      </table>
    </div>

    {editing && <div className="modal-backdrop" role="presentation"><form className="account-modal commercial-plan-editor" onSubmit={event => { event.preventDefault(); save.mutate() }}>
      <header><div><p className="eyebrow">Administración</p><h2>{editing === 'new' ? 'Nuevo tipo de cuenta' : `Editar ${editing.name}`}</h2></div><button type="button" className="icon-button" onClick={() => setEditing(null)} aria-label="Cerrar"><X size={19} /></button></header>
      {editing === 'new' && <label>Código interno<input value={draft.code} onChange={event => setDraft(current => ({ ...current, code: event.target.value }))} placeholder="ej. profesional_plus" maxLength={32} required /></label>}
      <div className="commercial-plan-editor-grid">
        <label>Nombre<input value={draft.name} onChange={event => setDraft(current => ({ ...current, name: event.target.value }))} maxLength={80} required /></label>
        <label>Precio<input value={draft.price} onChange={event => setDraft(current => ({ ...current, price: event.target.value }))} placeholder="$4.990" maxLength={40} required /></label>
        <label>Periodicidad<input value={draft.cadence} onChange={event => setDraft(current => ({ ...current, cadence: event.target.value }))} placeholder="CLP / mes" maxLength={80} required /></label>
        <label>Máximo de cuentas<input type="number" min="1" value={draft.maxAccounts} onChange={event => setDraft(current => ({ ...current, maxAccounts: event.target.value }))} placeholder="Vacío = sin límite" /></label>
        <label>Orden<input type="number" min="0" max="10000" value={draft.sortOrder} onChange={event => setDraft(current => ({ ...current, sortOrder: event.target.value }))} /></label>
      </div>
      <label>Descripción<textarea rows={3} value={draft.description} onChange={event => setDraft(current => ({ ...current, description: event.target.value }))} maxLength={600} required /></label>
      <label>Características comerciales <small>Una por línea. Se muestran al usuario en la tarjeta del plan.</small><textarea rows={6} value={draft.features} onChange={event => setDraft(current => ({ ...current, features: event.target.value }))} required /></label>

      <fieldset className="commercial-entitlement-editor">
        <legend>Funciones habilitadas</legend>
        <p>Estas opciones definen las capacidades técnicas asociadas al plan.</p>
        {entitlementDefinitions.isLoading && <div className="commercial-entitlement-loading">Cargando funciones…</div>}
        {entitlementDefinitions.isError && <div className="notice">No fue posible cargar el catálogo de funciones.</div>}
        <div className="commercial-entitlement-grid">
          {(entitlementDefinitions.data ?? []).map(definition => {
            const checked = draft.entitlements.includes(definition.code)
            return <label className={`commercial-entitlement-option ${checked ? 'selected' : ''}`} key={definition.code}>
              <input type="checkbox" checked={checked} onChange={() => toggleEntitlement(definition.code)} />
              <span className="commercial-entitlement-check"><Check size={14} /></span>
              <span><strong>{definition.name}</strong><small>{definition.description}</small></span>
            </label>
          })}
        </div>
        {draft.entitlements.length === 0 && <p className="form-error">Seleccione al menos una función habilitada.</p>}
      </fieldset>

      <div className="commercial-plan-toggles">
        <label><input type="checkbox" checked={draft.isActive} disabled={editing !== 'new' && editing.code === 'freemium'} onChange={event => setDraft(current => ({ ...current, isActive: event.target.checked }))} /> Activo</label>
        <label><input type="checkbox" checked={draft.isFeatured} onChange={event => setDraft(current => ({ ...current, isFeatured: event.target.checked }))} /> Destacado</label>
        <label><input type="checkbox" checked={draft.isCorporate} onChange={event => setDraft(current => ({ ...current, isCorporate: event.target.checked }))} /> Corporativo</label>
        <label><input type="checkbox" checked={draft.isWhiteLabel} onChange={event => setDraft(current => ({ ...current, isWhiteLabel: event.target.checked }))} /> White Label</label>
      </div>
      {save.isError && <p className="form-error">{save.error instanceof Error ? save.error.message : 'No fue posible guardar el tipo de cuenta.'}</p>}
      <footer><button type="button" className="secondary-button" onClick={() => setEditing(null)}>Cancelar</button><button className="primary-button" disabled={save.isPending || draft.entitlements.length === 0}>{save.isPending ? 'Guardando…' : 'Guardar cambios'}</button></footer>
    </form></div>}

    <ConfirmDialog open={Boolean(deleteCandidate)} title="Eliminar tipo de cuenta" message={deleteCandidate ? `Se eliminará “${deleteCandidate.name}” de la configuración comercial. Esta acción no se puede deshacer.` : ''} confirmLabel="Eliminar" pending={remove.isPending} onCancel={() => setDeleteCandidate(null)} onConfirm={() => { if (deleteCandidate) remove.mutate(deleteCandidate) }} />
  </section>
}
