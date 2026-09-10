import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Filter, Plus, ShieldCheck, Trash2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import { ruleApi } from '../api/ruleApi'

export function RulesPage() {
  const navigate = useNavigate()
  const { data: accounts = [], isLoading: accountsLoading } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const gmailAccounts = useMemo(() => accounts.filter(account => account.provider === 'Gmail'), [accounts])
  const [accountId, setAccountId] = useState('')

  useEffect(() => {
    if (!accountId && gmailAccounts.length > 0) setAccountId(gmailAccounts[0].id)
    if (accountId && !gmailAccounts.some(account => account.id === accountId)) setAccountId(gmailAccounts[0]?.id ?? '')
  }, [accountId, gmailAccounts])

  const rules = useQuery({
    queryKey: ['mail-rules', accountId],
    queryFn: () => ruleApi.listTrash(accountId),
    enabled: Boolean(accountId),
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  })

  const selectedAccount = gmailAccounts.find(account => account.id === accountId)

  return <section className="settings-page rules-settings-page">
    <div className="rules-page-header">
      <div>
        <p className="eyebrow">Configuración</p>
        <h1>Reglas automáticas</h1>
        <p className="page-description">Consulta las reglas que Nexi utiliza para automatizar tu correo.</p>
      </div>
      <button type="button" className="primary-button" disabled={!accountId} onClick={() => navigate(accountId ? `/rules/new?account=${encodeURIComponent(accountId)}` : '/rules/new')}>
        <Plus size={16} /> Nueva regla
      </button>
    </div>

    {gmailAccounts.length > 0 && <div className="rules-account-selector">
      <label htmlFor="rules-account">Cuenta</label>
      <select id="rules-account" value={accountId} onChange={event => setAccountId(event.target.value)}>
        {gmailAccounts.map(account => <option key={account.id} value={account.id}>{account.displayName} · {account.emailAddress}</option>)}
      </select>
    </div>}

    {accountsLoading && <div className="rules-loading">Cargando cuentas…</div>}

    {!accountsLoading && gmailAccounts.length === 0 && <div className="notice">Las reglas automáticas están disponibles actualmente para cuentas Gmail. Conecta una cuenta Gmail para comenzar.</div>}

    {rules.isError && <div className="notice rules-error">
      <span>{rules.error instanceof Error ? rules.error.message : 'No fue posible consultar las reglas.'}</span>
      <button type="button" className="text-button" onClick={() => navigate('/settings/accounts')}>Configurar cuentas</button>
    </div>}

    {accountId && rules.isLoading && <div className="rules-loading"><span className="reading-skeleton" /><span className="reading-skeleton" /></div>}

    {accountId && rules.data && <div className="rules-panel">
      <header>
        <div><Filter size={18} /><span><strong>Reglas activas</strong><small>{selectedAccount?.emailAddress}</small></span></div>
        <span className="rules-count">{rules.data.length}</span>
      </header>

      {rules.data.length === 0 ? <div className="rules-empty">
        <ShieldCheck size={28} />
        <strong>No hay reglas de Papelera</strong>
        <span>Puedes pedirle a Nexi, por ejemplo: “Crea una regla para enviar a Papelera los correos del banco Santander”.</span>
      </div> : <div className="rules-list">
        {rules.data.map(rule => <article className="rule-row" key={rule.filterId}>
          <div className="rule-row-icon"><Filter size={17} /></div>
          <div className="rule-row-copy">
            <span className="rule-status">Activa</span>
            <strong>{rule.query || 'Criterio configurado en Gmail'}</strong>
            <small><Trash2 size={13} /> Si coincide, mover a Papelera</small>
          </div>
        </article>)}
      </div>}
    </div>}

    <div className="rules-footnote">
      <ShieldCheck size={15} />
      <span>Estas reglas viven en Gmail y siguen funcionando aunque NexoMail esté cerrado.</span>
    </div>
  </section>
}
