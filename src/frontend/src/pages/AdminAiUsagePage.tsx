import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { aiUsageApi, AiUsageApiError, type AiUsageSort } from '../api/aiUsageApi'

const clp = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 })
const integer = new Intl.NumberFormat('es-CL', { maximumFractionDigits: 0 })
const date = new Intl.DateTimeFormat('es-CL', { day: '2-digit', month: 'short', year: 'numeric' })

function formatDate(value: string | null) {
  return value ? date.format(new Date(value)) : '—'
}

function variationLabel(value: number | null | undefined) {
  if (value == null) return 'Sin base comparable'
  const prefix = value > 0 ? '+' : ''
  return `${prefix}${value.toLocaleString('es-CL', { maximumFractionDigits: 1 })}%`
}

function statusLabel(value: string) {
  const normalized = value.toLowerCase()
  return normalized === 'verde' ? 'Verde' : normalized === 'amarillo' ? 'Amarillo' : normalized === 'rojo' ? 'Rojo' : value
}

export function AdminAiUsagePage() {
  const queryClient = useQueryClient()
  const [sort, setSort] = useState<AiUsageSort>('projected')
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null)
  const [greenMaxClp, setGreenMaxClp] = useState('')
  const [yellowMaxClp, setYellowMaxClp] = useState('')
  const [referenceClpPerUsd, setReferenceClpPerUsd] = useState('')

  const week = useQuery({ queryKey: ['ai-usage', 'summary', 'week'], queryFn: () => aiUsageApi.summary('week') })
  const month = useQuery({ queryKey: ['ai-usage', 'summary', 'month'], queryFn: () => aiUsageApi.summary('month') })
  const users = useQuery({ queryKey: ['ai-usage', 'users', sort], queryFn: () => aiUsageApi.users(sort) })
  const settings = useQuery({ queryKey: ['ai-usage', 'settings'], queryFn: aiUsageApi.settings })
  const detail = useQuery({
    queryKey: ['ai-usage', 'user', selectedUserId],
    queryFn: () => aiUsageApi.user(selectedUserId!),
    enabled: Boolean(selectedUserId),
  })

  useEffect(() => {
    if (!settings.data) return
    setGreenMaxClp(String(settings.data.greenMaxClp))
    setYellowMaxClp(String(settings.data.yellowMaxClp))
    setReferenceClpPerUsd(String(settings.data.referenceClpPerUsd))
  }, [settings.data])

  const updateSettings = useMutation({
    mutationFn: aiUsageApi.updateSettings,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['ai-usage'] })
    },
  })

  const errors = [week.error, month.error, users.error, settings.error, detail.error].filter(Boolean)
  const forbidden = errors.some(error => error instanceof AiUsageApiError && error.status === 403)
  const maxDailyCost = useMemo(
    () => Math.max(1, ...(detail.data?.daily.map(item => item.costClp) ?? [1])),
    [detail.data],
  )

  if (forbidden) {
    return <section className="settings-page ai-usage-page"><h1>Consumo Nexi</h1><p>Este panel está disponible exclusivamente para el Owner.</p></section>
  }

  if (week.isLoading || month.isLoading || users.isLoading) {
    return <section className="settings-page ai-usage-page"><h1>Consumo Nexi</h1><p>Cargando consumo…</p></section>
  }

  if (week.isError || month.isError || users.isError) {
    return <section className="settings-page ai-usage-page"><h1>Consumo Nexi</h1><p>No fue posible cargar el consumo de Nexi.</p></section>
  }

  return <section className="settings-page ai-usage-page">
    <header className="ai-usage-header">
      <div>
        <p className="eyebrow">Administración Owner</p>
        <h1>Consumo Nexi</h1>
        <p className="ai-usage-lead">Costo estimado, Proyección y comportamiento de uso de inteligencia artificial.</p>
      </div>
      <div className="ai-usage-reference">
        <span>Referencia CLP/USD</span>
        <strong>{settings.data ? `$${settings.data.referenceClpPerUsd.toLocaleString('es-CL')}` : '—'}</strong>
      </div>
    </header>

    <div className="ai-usage-metrics" aria-label="Resumen de consumo">
      <article className="ai-usage-metric">
        <span>Esta semana</span>
        <strong>{clp.format(week.data?.currentCostClp ?? 0)}</strong>
        <small>{integer.format(week.data?.operations ?? 0)} Operaciones</small>
      </article>
      <article className="ai-usage-metric">
        <span>Semana anterior</span>
        <strong>{clp.format(week.data?.previousCostClp ?? 0)}</strong>
        <small>Base de comparación</small>
      </article>
      <article className="ai-usage-metric">
        <span>Variación semanal</span>
        <strong>{variationLabel(week.data?.variationPercent)}</strong>
        <small>Respecto de la semana anterior</small>
      </article>
      <article className="ai-usage-metric">
        <span>Este mes</span>
        <strong>{clp.format(month.data?.currentCostClp ?? 0)}</strong>
        <small>Costo estimado acumulado</small>
      </article>
      <article className="ai-usage-metric">
        <span>Usuarios activos</span>
        <strong>{integer.format(week.data?.activeUsers ?? 0)}</strong>
        <small>Con actividad esta semana</small>
      </article>
      <article className="ai-usage-metric">
        <span>Costo promedio</span>
        <strong>{clp.format(week.data?.averageCostPerActiveUserClp ?? 0)}</strong>
        <small>Por usuario activo</small>
      </article>
    </div>

    <section className="ai-usage-panel">
      <div className="ai-usage-panel-header">
        <div><h2>Consumo por usuario</h2><p>Ordena por Proyección o Costo acumulado para detectar pruebas intensivas.</p></div>
        <label className="ai-usage-sort">Ordenar
          <select value={sort} onChange={event => setSort(event.target.value as AiUsageSort)}>
            <option value="projected">Proyección</option>
            <option value="accumulated">Costo acumulado</option>
          </select>
        </label>
      </div>

      {(users.data?.length ?? 0) === 0 ? <p className="ai-usage-empty">Aún no hay consumo registrado.</p> : <div className="ai-usage-table-wrap">
        <table className="ai-usage-table">
          <thead><tr><th>Usuario</th><th>Plan / prueba</th><th>Costo acumulado</th><th>Proyección</th><th>Operaciones</th><th>Tokens</th><th>Estado</th><th /></tr></thead>
          <tbody>{users.data?.map(user => <tr key={user.userId}>
            <td><strong>{user.displayName}</strong><small>{user.email}</small></td>
            <td><span>{user.effectivePlanCode}</span>{user.isTrialActive && <small>{user.trialDaysRemaining ?? 0} días restantes</small>}</td>
            <td>{clp.format(user.accumulatedCostClp)}</td>
            <td>{clp.format(user.projectedCostClp)}{user.isInitialProjection && <small>Proyección inicial</small>}</td>
            <td>{integer.format(user.weekOperations)}</td>
            <td><span>{integer.format(user.inputTokens + user.outputTokens)}</span><small>{integer.format(user.inputTokens)} entrada · {integer.format(user.outputTokens)} salida</small></td>
            <td><span className="ai-usage-status" data-status={user.costStatus}>{statusLabel(user.costStatus)}</span></td>
            <td><button className="secondary-button" type="button" onClick={() => setSelectedUserId(user.userId)}>Ver detalle</button></td>
          </tr>)}</tbody>
        </table>
      </div>}
    </section>

    {selectedUserId && <section className="ai-usage-panel ai-usage-detail" aria-live="polite">
      <div className="ai-usage-panel-header">
        <div><p className="eyebrow">Detalle de usuario</p><h2>{detail.data?.user.displayName ?? 'Cargando…'}</h2>{detail.data && <p>{detail.data.user.email}</p>}</div>
        <button type="button" className="secondary-button" onClick={() => setSelectedUserId(null)}>Cerrar detalle</button>
      </div>
      {detail.isLoading && <p className="ai-usage-empty">Cargando detalle…</p>}
      {detail.isError && <p className="ai-usage-empty">No fue posible cargar el detalle.</p>}
      {detail.data && <>
        <div className="ai-usage-detail-metrics">
          <div><span>Costo acumulado</span><strong>{clp.format(detail.data.user.accumulatedCostClp)}</strong></div>
          <div><span>Promedio diario</span><strong>{clp.format(detail.data.user.averageDailyCostClp)}</strong></div>
          <div><span>Proyección</span><strong>{clp.format(detail.data.user.projectedCostClp)}</strong></div>
          <div><span>Estado</span><strong>{statusLabel(detail.data.user.costStatus)}</strong></div>
        </div>

        {detail.data.user.isTrialActive && <div className="ai-usage-trial-line">
          <span>Prueba {detail.data.user.trialType ?? 'Nexi'}: {formatDate(detail.data.user.trialStart)} → {formatDate(detail.data.user.trialEndsAt)}</span>
          <strong>{detail.data.user.trialDaysRemaining ?? 0} días restantes</strong>
        </div>}

        {detail.data.user.sevenDayProjectedCostClp != null && <p className="ai-usage-trend">
          Ritmo últimos 7 días: <strong>{clp.format(detail.data.user.sevenDayProjectedCostClp)}</strong> proyectados
          {detail.data.user.sevenDayTrendPercent != null && <> · tendencia {variationLabel(detail.data.user.sevenDayTrendPercent)}</>}
        </p>}

        <div className="ai-usage-detail-grid">
          <div>
            <h3>Últimos 30 días</h3>
            {detail.data.daily.length === 0 ? <p className="ai-usage-empty">Sin actividad en los últimos 30 días.</p> : <div className="ai-usage-daily-list">
              {detail.data.daily.map(item => <div className="ai-usage-daily-row" key={item.date}>
                <time>{formatDate(item.date)}</time>
                <div className="ai-usage-bar-track"><i style={{ width: `${Math.max(3, item.costClp / maxDailyCost * 100)}%` }} /></div>
                <strong>{clp.format(item.costClp)}</strong>
                <small>{item.operations} ops</small>
              </div>)}
            </div>}
          </div>
          <div>
            <h3>Operaciones</h3>
            {detail.data.operations.length === 0 ? <p className="ai-usage-empty">Sin operaciones registradas.</p> : <div className="ai-usage-operation-list">
              {detail.data.operations.map(item => <div key={item.operationType}><span>{item.operationType}</span><strong>{integer.format(item.operations)}</strong><small>{clp.format(item.costClp)}</small></div>)}
            </div>}
          </div>
        </div>
      </>}
    </section>}

    <section className="ai-usage-panel">
      <div className="ai-usage-panel-header"><div><h2>Umbrales y referencia</h2><p>Los cambios se aplican sólo a costos futuros; el histórico conserva su valor original.</p></div></div>
      {settings.isLoading ? <p className="ai-usage-empty">Cargando configuración…</p> : <form className="ai-usage-settings" onSubmit={event => {
        event.preventDefault()
        updateSettings.mutate({
          greenMaxClp: Number(greenMaxClp),
          yellowMaxClp: Number(yellowMaxClp),
          referenceClpPerUsd: Number(referenceClpPerUsd),
        })
      }}>
        <label>Umbral verde (CLP)<input type="number" min="0" step="1" value={greenMaxClp} onChange={event => setGreenMaxClp(event.target.value)} required /></label>
        <label>Umbral amarillo (CLP)<input type="number" min="1" step="1" value={yellowMaxClp} onChange={event => setYellowMaxClp(event.target.value)} required /></label>
        <label>Referencia CLP por USD<input type="number" min="0.01" step="0.01" value={referenceClpPerUsd} onChange={event => setReferenceClpPerUsd(event.target.value)} required /></label>
        <button className="primary-button" type="submit" disabled={updateSettings.isPending}>{updateSettings.isPending ? 'Guardando…' : 'Guardar configuración'}</button>
        {updateSettings.isSuccess && <span className="ai-usage-save-status">Configuración actualizada.</span>}
        {updateSettings.isError && <span className="ai-usage-save-error">{updateSettings.error.message}</span>}
      </form>}
    </section>
  </section>
}
