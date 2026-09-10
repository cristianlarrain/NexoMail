import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Archive, CheckCheck, CheckCircle2, Filter, FolderInput, ListFilter, ShieldCheck, Trash2 } from 'lucide-react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { mailApi } from '../api/mailApi'
import { ruleApi, type RuleAction } from '../api/ruleApi'
import { searchApi } from '../api/searchApi'
import { extractRuleDestination, extractRuleQuery, inferRuleAction } from '../utils/nexiRuleIntent'
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

async function applyExistingAction(items: MailSummary[], action: RuleAction) {
  let completed = 0
  let failed = 0
  for (let offset = 0; offset < items.length; offset += 5) {
    const batch = items.slice(offset, offset + 5)
    const results = await Promise.allSettled(batch.map(item => {
      switch (action) {
        case 'trash': return mailApi.trash(item.accountId, item.providerMessageId)
        case 'archive': return mailApi.move(item.accountId, item.providerMessageId, 'archive')
        case 'markRead': return mailApi.read(item.accountId, item.providerMessageId, true)
        case 'moveToFolder': return Promise.reject(new Error('El traslado histórico a carpetas personalizadas no está disponible todavía.'))
      }
    }))
    for (const result of results) {
      if (result.status === 'fulfilled') completed += 1
      else failed += 1
    }
  }
  return { completed, failed }
}

function actionLabel(action: RuleAction, destinationName?: string) {
  switch (action) {
    case 'trash': return 'mover a Papelera'
    case 'archive': return 'archivar'
    case 'markRead': return 'marcar como leído'
    case 'moveToFolder': return `mover a ${destinationName || 'la carpeta seleccionada'}`
  }
}

function ActionIcon({ action }: { action: RuleAction }) {
  switch (action) {
    case 'trash': return <Trash2 size={14} />
    case 'archive': return <Archive size={14} />
    case 'markRead': return <CheckCheck size={14} />
    case 'moveToFolder': return <FolderInput size={14} />
  }
}

export function MailRulePage() {
  const [params] = useSearchParams()
  const instruction = params.get('q')?.trim() ?? ''
  const requestedAccount = params.get('account') ?? ''
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts, staleTime: 10 * 60_000 })
  const ruleAccounts = useMemo(() => (accounts.data ?? []).filter(account => account.provider === 'Gmail' || account.provider === 'MicrosoftGraph'), [accounts.data])
  const suggestedQuery = useMemo(() => extractRuleQuery(instruction), [instruction])
  const suggestedAction = useMemo(() => inferRuleAction(instruction), [instruction])
  const suggestedDestination = useMemo(() => extractRuleDestination(instruction), [instruction])
  const [accountId, setAccountId] = useState(requestedAccount)
  const [ruleQuery, setRuleQuery] = useState(suggestedQuery)
  const [action, setAction] = useState<RuleAction>(suggestedAction)
  const [destinationId, setDestinationId] = useState('')
  const [applyExisting, setApplyExisting] = useState(suggestedAction !== 'moveToFolder')
  const [confirmOpen, setConfirmOpen] = useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const effectiveAccountId = accountId || (ruleAccounts.length === 1 ? ruleAccounts[0].id : '')
  const selectedAccount = ruleAccounts.find(account => account.id === effectiveAccountId)
  const destinations = useQuery({
    queryKey: ['mail-rule-destinations', effectiveAccountId],
    queryFn: () => ruleApi.destinations(effectiveAccountId),
    enabled: Boolean(effectiveAccountId && action === 'moveToFolder'),
    staleTime: 5 * 60_000,
  })

  useEffect(() => {
    if (action === 'moveToFolder') setApplyExisting(false)
  }, [action])

  useEffect(() => {
    if (action !== 'moveToFolder') {
      setDestinationId('')
      return
    }
    if (destinationId || !suggestedDestination || !destinations.data?.length) return
    const normalizedSuggestion = suggestedDestination.toLocaleLowerCase('es').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    const match = destinations.data.find(item => item.displayName.toLocaleLowerCase('es').normalize('NFD').replace(/[\u0300-\u036f]/g, '') === normalizedSuggestion)
      ?? destinations.data.find(item => item.displayName.toLocaleLowerCase('es').includes(suggestedDestination.toLocaleLowerCase('es')))
    if (match) setDestinationId(match.id)
  }, [action, destinationId, destinations.data, suggestedDestination])

  const selectedDestination = destinations.data?.find(item => item.id === destinationId)

  const createRule = useMutation({
    mutationFn: async () => {
      const result = await ruleApi.create(effectiveAccountId, ruleQuery.trim(), action, destinationId || undefined)
      const effectiveQuery = result.rule.query.trim()
      if (!applyExisting) return { result, existing: { completed: 0, failed: 0 } }
      const matches = await existingMatches(effectiveAccountId, effectiveQuery)
      const existing = await applyExistingAction(matches, action)
      return { result, existing }
    },
    onSuccess: data => {
      setConfirmOpen(false)
      setRuleQuery(data.result.rule.query)
      void queryClient.invalidateQueries({ queryKey: ['messages'] })
      void queryClient.invalidateQueries({ queryKey: ['control-center'] })
      void queryClient.invalidateQueries({ queryKey: ['mail-rules'] })
    },
  })

  const canCreate = Boolean(effectiveAccountId && ruleQuery.trim() && (action !== 'moveToFolder' || destinationId) && !createRule.isPending)
  const permissionIssue = createRule.error instanceof Error && /autorizar el permiso|vuelve a conectar/i.test(createRule.error.message)
  const effectiveActionLabel = actionLabel(action, selectedDestination?.displayName)

  return <section className="mail-view mail-rule-page">
    <div className="mail-rule-heading">
      <div className="mail-rule-nexi"><NexiVisual size="small" /></div>
      <div>
        <p className="eyebrow">Nexi · Regla automática</p>
        <h1>Crear regla automática</h1>
        <p>Nexi convierte la instrucción en una regla permanente del proveedor de correo.</p>
      </div>
    </div>

    <div className="mail-rule-card">
      <label>
        <span>Cuenta</span>
        <select value={effectiveAccountId} onChange={event => setAccountId(event.target.value)} disabled={createRule.isPending}>
          <option value="">Selecciona una cuenta</option>
          {ruleAccounts.map(account => <option key={account.id} value={account.id}>{account.displayName} · {account.emailAddress}</option>)}
        </select>
      </label>

      <label>
        <span>Qué debe detectar</span>
        <input value={ruleQuery} onChange={event => setRuleQuery(event.target.value)} maxLength={500} disabled={createRule.isPending} placeholder="Ej.: from:avisos@empresa.cl" />
        <small>Puedes indicar remitente, asunto, frase u otro criterio. Nexi lo normaliza para el proveedor conectado.</small>
      </label>

      <label>
        <span>Acción automática</span>
        <select value={action} onChange={event => setAction(event.target.value as RuleAction)} disabled={createRule.isPending}>
          <option value="trash">Mover a Papelera</option>
          <option value="archive">Archivar</option>
          <option value="markRead">Marcar como leído</option>
          <option value="moveToFolder">Mover a carpeta</option>
        </select>
      </label>

      {action === 'moveToFolder' && <label>
        <span>Carpeta o etiqueta de destino</span>
        <select value={destinationId} onChange={event => setDestinationId(event.target.value)} disabled={createRule.isPending || destinations.isLoading}>
          <option value="">{destinations.isLoading ? 'Cargando carpetas…' : 'Selecciona un destino'}</option>
          {(destinations.data ?? []).map(destination => <option key={destination.id} value={destination.id}>{destination.displayName}</option>)}
        </select>
        {destinations.isError && <small className="rules-inline-error">{destinations.error instanceof Error ? destinations.error.message : 'No fue posible consultar las carpetas.'}</small>}
      </label>}

      <div className="mail-rule-action-preview">
        <Filter size={18} />
        <div><strong>Si un mensaje coincide con “{ruleQuery.trim() || '…'}”</strong><span><ActionIcon action={action} /> {effectiveActionLabel} automáticamente</span></div>
      </div>

      <label className="mail-rule-checkbox">
        <input type="checkbox" checked={applyExisting} onChange={event => setApplyExisting(event.target.checked)} disabled={createRule.isPending || action === 'moveToFolder'} />
        <span><strong>Aplicar también a correos actuales</strong><small>{action === 'moveToFolder' ? 'Para carpetas personalizadas, esta primera versión aplica la regla a los nuevos mensajes.' : `Procesará las coincidencias actuales en Bandeja y Archivados, hasta ${MAX_EXISTING_MATCHES} mensajes.`}</small></span>
      </label>

      <div className="mail-rule-security-note"><ShieldCheck size={17} /><span>La regla se guarda en el proveedor de correo y seguirá funcionando aunque NexoMail esté cerrado. El modelo de reglas es común para Gmail y Outlook/Microsoft 365.</span></div>

      {createRule.isError && <div className="notice mail-rule-error">
        {createRule.error instanceof Error ? createRule.error.message : 'No fue posible crear la regla.'}
        {permissionIssue && <button type="button" className="text-button" onClick={() => navigate('/settings/accounts')}>Ir a Configurar cuentas</button>}
      </div>}

      {createRule.data && <div className="mail-rule-success">
        <CheckCircle2 size={20} />
        <div>
          <strong>{createRule.data.result.created ? 'Regla creada' : 'La regla ya existía'}</strong>
          <span>Criterio aplicado: <strong>{createRule.data.result.rule.query}</strong>.</span>
          <span>Los nuevos mensajes que coincidan se procesarán automáticamente: {actionLabel(createRule.data.result.rule.action, createRule.data.result.rule.destinationName ?? undefined)}.</span>
          {applyExisting && <span>{createRule.data.existing.completed} correo(s) actual(es) procesado(s){createRule.data.existing.failed ? `; ${createRule.data.existing.failed} no pudieron procesarse` : ''}.</span>}
          <button type="button" className="text-button mail-rule-manage-link" onClick={() => navigate('/settings/rules')}><ListFilter size={14} /> Administrar reglas</button>
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
      message={`En ${selectedAccount?.emailAddress ?? 'esta cuenta'}, los nuevos correos que coincidan con “${ruleQuery.trim()}” se procesarán así: ${effectiveActionLabel}.${applyExisting ? ' También se procesarán las coincidencias actuales.' : ''}`}
      confirmLabel="Crear regla"
      pending={createRule.isPending}
      onConfirm={() => createRule.mutate()}
      onCancel={() => setConfirmOpen(false)}
    />
  </section>
}
