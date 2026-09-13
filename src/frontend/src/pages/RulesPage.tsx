import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, CheckCheck, Filter, FolderInput, Plus, ShieldCheck, Trash2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { mailApi } from '../api/mailApi'
import { ruleApi, type MailRule } from '../api/ruleApi'

function actionDescription(rule: MailRule) {
  switch (rule.action) {
    case 'trash': return 'Mover a Papelera'
    case 'archive': return 'Archivar'
    case 'markRead': return 'Marcar como leído'
    case 'moveToFolder': return `Mover a ${rule.destinationName || 'carpeta'}`
  }
}

function ActionIcon({ rule }: { rule: MailRule }) {
  switch (rule.action) {
    case 'trash': return <Trash2 size={13} />
    case 'archive': return <Archive size={13} />
    case 'markRead': return <CheckCheck size={13} />
    case 'moveToFolder': return <FolderInput size={13} />
  }
}

export function RulesPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: accounts = [], isLoading: accountsLoading } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const ruleAccounts = useMemo(() => accounts.filter(account => account.provider === 'Gmail' || account.provider === 'MicrosoftGraph'), [accounts])
  const [accountId, setAccountId] = useState('')
  const [removeCandidate, setRemoveCandidate] = useState<MailRule | null>(null)

  useEffect(() => {
    if (!accountId && ruleAccounts.length > 0) setAccountId(ruleAccounts[0]?.id ?? '')
    if (accountId && !ruleAccounts.some(account => account.id === accountId)) setAccountId(ruleAccounts[0]?.id ?? '')
  }, [accountId, ruleAccounts])

  const rules = useQuery({
    queryKey: ['mail-rules', accountId],
    queryFn: () => ruleApi.list(accountId),
    enabled: Boolean(accountId),
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  })

  const removeRule = useMutation({
    mutationFn: (rule: MailRule) => ruleApi.remove(rule.accountId, rule.ruleId),
    onSuccess: () => {
      setRemoveCandidate(null)
      void queryClient.invalidateQueries({ queryKey: ['mail-rules', accountId] })
    },
  })

  const selectedAccount = ruleAccounts.find(account => account.id === accountId)

  return <section className="settings-page rules-settings-page">
    <div className="rules-page-header">
      <div>
        <p className="eyebrow">Configuración</p>
        <h1>Reglas automáticas</h1>
        <p className="page-description">Consulta y administra las reglas que Nexi utiliza para automatizar tu correo.</p>
      </div>
      <button type="button" className="primary-button" disabled={!accountId} onClick={() => navigate(accountId ? `/rules/new?account=${encodeURIComponent(accountId)}` : '/rules/new')}>
        <Plus size={16} /> Nueva regla
      </button>
    </div>

    {ruleAccounts.length > 0 && <div className="rules-account-selector">
      <label htmlFor="rules-account">Cuenta</label>
      <select id="rules-account" value={accountId} onChange={event => setAccountId(event.target.value)}>
        {ruleAccounts.map(account => <option key={account.id} value={account.id}>{account.displayName} · {account.emailAddress}</option>)}
      </select>
    </div>}

    {accountsLoading && <div className="rules-loading">Cargando cuentas…</div>}

    {!accountsLoading && ruleAccounts.length === 0 && <div className="notice">Conecta una cuenta Gmail o Outlook/Microsoft 365 compatible para administrar reglas automáticas.</div>}

    {rules.isError && <div className="notice rules-error">
      <span>{rules.error instanceof Error ? rules.error.message : 'No fue posible consultar las reglas.'}</span>
      <button type="button" className="text-button" onClick={() => navigate('/settings/accounts')}>Configurar cuentas</button>
    </div>}

    {removeRule.isError && <div className="notice rules-error">{removeRule.error instanceof Error ? removeRule.error.message : 'No fue posible quitar la regla.'}</div>}

    {accountId && rules.isLoading && <div className="rules-loading"><span className="reading-skeleton" /><span className="reading-skeleton" /></div>}

    {accountId && rules.data && <div className="rules-panel">
      <header>
        <div><Filter size={18} /><span><strong>Reglas activas</strong><small>{selectedAccount?.emailAddress}</small></span></div>
        <span className="rules-count">{rules.data.length}</span>
      </header>

      {rules.data.length === 0 ? <div className="rules-empty">
        <ShieldCheck size={28} />
        <strong>No hay reglas automáticas compatibles</strong>
        <span>Puedes pedirle a Nexi que archive, marque como leído, mueva a una carpeta o envíe a Papelera los correos que coincidan.</span>
      </div> : <div className="rules-list">
        {rules.data.map(rule => <article className="rule-row" key={rule.ruleId}>
          <div className="rule-row-icon"><Filter size={17} /></div>
          <div className="rule-row-copy">
            <span className="rule-status">Activa</span>
            <strong>{rule.query || 'Criterio configurado en el proveedor'}</strong>
            <small><ActionIcon rule={rule} /> Si coincide, {actionDescription(rule).toLocaleLowerCase('es')}</small>
          </div>
          <button type="button" className="icon-button danger-icon rule-remove" title="Quitar regla" aria-label={`Quitar regla ${rule.query || ''}`} onClick={() => setRemoveCandidate(rule)} disabled={removeRule.isPending}>
            <Trash2 size={16} />
          </button>
        </article>)}
      </div>}
    </div>}

    <div className="rules-footnote">
      <ShieldCheck size={15} />
      <span>Las reglas se guardan en el proveedor de correo y continúan funcionando aunque NexoMail esté cerrado. El mismo modelo está preparado para Gmail y Outlook/Microsoft 365.</span>
    </div>

    <ConfirmDialog
      open={Boolean(removeCandidate)}
      title="Quitar regla automática"
      message={removeCandidate ? `La regla “${removeCandidate.query || 'Regla de correo'}” dejará de ejecutarse automáticamente.` : ''}
      confirmLabel="Quitar regla"
      pending={removeRule.isPending}
      onCancel={() => setRemoveCandidate(null)}
      onConfirm={() => { if (removeCandidate) removeRule.mutate(removeCandidate) }}
    />
  </section>
}
