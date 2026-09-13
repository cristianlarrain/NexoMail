import { useQuery } from '@tanstack/react-query'
import { BarChart3, RefreshCw } from 'lucide-react'
import { mailApi } from '../api/mailApi'
import { ControlCenterActivity } from './ControlCenterActivity'

export function ControlCenterStatistics() {
  const snapshot = useQuery({
    queryKey: ['control-center', 'all'],
    queryFn: () => mailApi.controlCenter(),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    retry: 1,
    refetchOnWindowFocus: false,
  })

  if (snapshot.isLoading) return <section className="control-panel control-statistics-loading" role="status"><RefreshCw size={16} className="spin" /><span>Actualizando estadísticas…</span></section>
  if (snapshot.isError || !snapshot.data) return <section className="control-panel"><div className="control-center-header"><div><h2>Estadísticas</h2><p>No fue posible cargar los datos.</p></div><button type="button" className="icon-button" onClick={() => snapshot.refetch()} aria-label="Reintentar estadísticas"><RefreshCw size={16} /></button></div></section>

  return <section className="control-statistics-view">
    <header className="control-module-heading"><div><span><BarChart3 size={15} /> Estadísticas</span><strong>Actividad del correo</strong><p>Volumen recibido y enviado por cuenta y período.</p></div></header>
    <ControlCenterActivity accounts={snapshot.data.accounts} />
  </section>
}
