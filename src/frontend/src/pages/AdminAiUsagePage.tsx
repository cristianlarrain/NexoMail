import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { aiUsageApi, AiUsageApiError, type AiUsagePeriod, type AiUsageSort } from '../api/aiUsageApi'

const clp = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 })

export function AdminAiUsagePage() {
  const [period, setPeriod] = useState<AiUsagePeriod>('week')
  const [sort, setSort] = useState<AiUsageSort>('projected')
  const summary = useQuery({ queryKey: ['ai-usage-summary', period], queryFn: () => aiUsageApi.summary(period) })
  const users = useQuery({ queryKey: ['ai-usage-users', sort], queryFn: () => aiUsageApi.users(sort) })

  const forbidden = [summary.error, users.error].some(error => error instanceof AiUsageApiError && error.status === 403)
  if (forbidden) {
    return <section className="settings-page"><h1>Consumo Nexi</h1><p>Este panel está disponible exclusivamente para el Owner.</p></section>
  }

  if (summary.isLoading || users.isLoading) {
    return <section className="settings-page"><h1>Consumo Nexi</h1><p>Cargando consumo…</p></section>
  }

  if (summary.isError || users.isError) {
    return <section className="settings-page"><h1>Consumo Nexi</h1><p>No fue posible cargar el consumo de Nexi.</p></section>
  }

  return <section className="settings-page">
    <div className="settings-header">
      <div>
        <p className="eyebrow">Administración Owner</p>
        <h1>Consumo Nexi</h1>
        <p>Costo estimado y Proyección de uso de inteligencia artificial.</p>
      </div>
      <div>
        <button type="button" onClick={() => setPeriod('week')} aria-pressed={period === 'week'}>Semana</button>
        <button type="button" onClick={() => setPeriod('month')} aria-pressed={period === 'month'}>Mes</button>
      </div>
    </div>

    <div className="settings-card">
      <strong>Costo estimado</strong>
      <p>{clp.format(summary.data?.currentCostClp ?? 0)}</p>
      <small>{summary.data?.operations ?? 0} operaciones · {summary.data?.activeUsers ?? 0} usuarios activos</small>
    </div>

    <div className="settings-card">
      <div className="settings-header">
        <h2>Usuarios</h2>
        <select value={sort} onChange={event => setSort(event.target.value as AiUsageSort)} aria-label="Ordenar consumo">
          <option value="projected">Proyección</option>
          <option value="accumulated">Costo acumulado</option>
        </select>
      </div>
      {(users.data?.length ?? 0) === 0 ? <p>Aún no hay consumo registrado.</p> : <div className="table-wrap">
        <table>
          <thead><tr><th>Usuario</th><th>Costo acumulado</th><th>Proyección</th><th>Estado</th></tr></thead>
          <tbody>{users.data?.map(user => <tr key={user.userId}>
            <td><strong>{user.displayName}</strong><br /><small>{user.email}</small></td>
            <td>{clp.format(user.accumulatedCostClp)}</td>
            <td>{clp.format(user.projectedCostClp)}</td>
            <td>{user.costStatus}</td>
          </tr>)}</tbody>
        </table>
      </div>}
    </div>
  </section>
}
