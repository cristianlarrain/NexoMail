import { useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Filter, ShieldCheck, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { mailApi } from '../api/mailApi'
import { ruleApi } from '../api/ruleApi'
import { searchApi } from '../api/searchApi'
import { extractTrashRuleQuery } from '../utils/nexiRuleIntent'
import type { MailSummary } from '../types/mail'

const MAX_EXISTING_MATCHES = 250

async function existingMatches(accountId: string, query: string) {
  const unique = new Map<string, MailSummary>()
  for (const folder of ['inbox', 'archive'] as const) {
    let cursor: string | undefined
    do {
      const page = await searchApi.messages(accountId, folder, query, cursor)
      for (const item of page.items) unique.set(`${item.accountId}:${item.providerMessageId}`, item)
      cursor = page.nextCursor
    } while (cursor && unique.size < MAX_EXISTING_MATCHES)
  }
  return [...unique.values()].slice(0, MAX_EXISTING_MATCHES)
}

async function trashExisting(items: MailSummary[]) {
  let completed = 0
  let failed = 0
  for (let offset = 0; offset < items.length; offset += 5) {
    const batch = items.slice(offset, offset + 5)
    const results = await Promise.allSettled(batch.map(item => mailApi.trash(item.accountId, item.providerMessageId)))
    for (const result of results) {
      if (result.status === 'fulfilled') completed += 1
      else failed += 1
    }
  }
  return { completed, failed }
}

export function MailRulePage() {
  const [params] = useSearchParams()
  const instruction = params.get('q')?.trim() ?? ''
  const requestedAccount = params.get('account') ?? ''
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const suggestedQuery = useMemo(() => extractTrashRuleQuery(instruction), [instruction])
  const [accountId, setAccountId] = useState(requestedAccount)
  const [ruleQuery, setRuleQuery] = useState(suggestedQuery)
  const [applyExisting, setApplyExisting] = useState(true)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const effectiveAccountId = accountId || (accounts.data?.length === 1 ? accounts.data[0].id : '')
  const selectedAccount = accounts.data?.find(account => account.id === effectiveAccountId)

  const createRule = useMutation({
    mutationFn: async () => {
      const result = await ruleApi.createTrash(effectiveAccountId, ruleQuery.trim())
      if (!applyExisting) return { result, existing: { completed: 0, failed: 0 } }
      const matches = await existingMatches(effectiveAccountId, ruleQuery.trim())
      const existing = await trashExisting(matches)
      return { result, existing }
    },
    onSuccess: () => {
      setConfirmOpen(false)
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
    },
  })

  const canCreate = Boolean(effectiveAccountId && ruleQuery.trim() && !createRule.isPending)
  const permissionIssue = createRule.error instanceof Error && /autorizar el permiso|vuelve a conectar/i.test(createRule.error.message)

  return <section className="mail-view mail-rule-page">
    <div className="mail-rule-heading">
      <div className="mail-rule-nexi"><NexiVisual size="small" /></div>
      <div>
        <p className="eyebrow">Nexi · Regla automática</p>
        <h1>Enviar correos coincidentes a Papelera</h1>
        <p>Nexi interpretó tu instrucción como una regla permanente de correo.</p>
      </div>
    </div>

    <div className="mail-rule-card">
      <label>
        <span>Cuenta</span>
        <select value={effectiveAccountId} onChange={event => setAccountId(event.target.value)} disabled={createRule.isPending}>
          <option value="">Selecciona una cuenta</option>
          {(accounts.data ?? []).map(account => <option key={account.id} value={account.id}>{account.displayName} · {account.emailAddress}</option>)}
        </select>
      </label>

      <label>
        <span>Qué debe detectar</span>
        <input
          value={ruleQuery}
          onChange={event => setRuleQuery(event.target.value)}
          maxLength={500}
          disabled={createRule.isPending}
          placeholder="Ej.: cristianlarrain/NexoMail"
        />
        <small>Se usa como criterio de búsqueda de Gmail. Puedes escribir un remitente, asunto, frase o consulta de Gmail.</small>
      </label>

      <div className="mail-rule-action-preview">
        <Filter size={18} />
        <div><strong>Si un mensaje coincide con “{ruleQuery.trim() || '…'}”</strong><span><Trash2 size={14} /> mover automáticamente a Papelera</span></div>
      </div>

      <label className="mail-rule-checkbox">
        <input type="checkbox" checked={applyExisting} onChange={event => setApplyExisting(event.target.checked)} disabled={createRule.isPending} />
        <span><strong>Aplicar también a correos actuales</strong><small>Moverá a Papelera las coincidencias que NexoMail encuentre ahora en Bandeja y Archivados, hasta {MAX_EXISTING_MATCHES} mensajes.</small></span>
      </label>

      <div className="mail-rule-security-note"><ShieldCheck size={17} /><span>La regla se crea en Gmail y seguirá funcionando aunque NexoMail esté cerrado. Antes de crearla, NexoMail pide confirmación.</span></div>

      {createRule.isError && <div className="notice mail-rule-error">
        {createRule.error instanceof Error ? createRule.error.message : 'No fue posible crear la regla.'}
        {permissionIssue && <button type="button" className="text-button" onClick={() => navigate('/settings/accounts')}>Ir a Configurar cuentas</button>}
      </div>}

      {createRule.data && <div className="mail-rule-success">
        <CheckCircle2 size={20} />
        <div>
          <strong>{createRule.data.result.created ? 'Regla creada' : 'La regla ya existía'}</strong>
          <span>Los nuevos mensajes que coincidan se enviarán a Papelera automáticamente.</span>
          {applyExisting && <span>{createRule.data.existing.completed} correo(s) actual(es) movido(s) a Papelera{createRule.data.existing.failed ? `; ${createRule.data.existing.failed} no pudieron procesarse` : ''}.</span>}
        </div>
      </div>}

      <div className="mail-rule-actions">
        <button type="button" className="secondary-button" onClick={() => navigate(-1)} disabled={createRule.isPending}>Cancelar</button>
        <button type="button" className="primary-button" disabled={!canCreate} onClick={() => setConfirmOpen(true)}>Crear regla</button>
      </div>
    </div>

    <ConfirmDialog
      open={confirmOpen}
      title="Crear regla automática"
      message={`En ${selectedAccount?.emailAddress ?? 'esta cuenta'}, los nuevos correos que coincidan con “${ruleQuery.trim()}” se moverán automáticamente a Papelera.${applyExisting ? ' También se procesarán las coincidencias actuales.' : ''}`}
      confirmLabel="Crear regla"
      pending={createRule.isPending}
      onConfirm={() => createRule.mutate()}
      onCancel={() => setConfirmOpen(false)}
    />
  </section>
}
