import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { BarChart3, Files, Gauge, LockKeyhole, Sparkles, Users } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { commercialApi } from '../api/commercialApi'
import { ControlCenter } from '../components/ControlCenter'
import { ControlCenterContacts } from '../components/ControlCenterContacts'
import { ControlCenterDocuments } from '../components/ControlCenterDocuments'
import { ControlCenterStatistics } from '../components/ControlCenterStatistics'
import { NexiMailReport } from '../components/NexiMailReport'
import { NexiVisual } from '../components/nexi/NexiVisual'

type ControlTab = 'summary' | 'report' | 'statistics' | 'contacts' | 'documents'

function normalizedTab(value: string | null): ControlTab {
  return value === 'report' || value === 'statistics' || value === 'contacts' || value === 'documents' ? value : 'summary'
}

export function ControlCenterPage() {
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const subscription = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const tab = normalizedTab(params.get('tab'))
  const hasNexi = subscription.data?.entitlements.includes('nexi_ai') === true
  const hasAdvancedAnalytics = subscription.data?.entitlements.includes('advanced_analytics') === true

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'active' })
    if (hasAdvancedAnalytics) void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'active' })
  }, [hasAdvancedAnalytics, queryClient])

  function selectTab(next: ControlTab) {
    if (next === 'report' && !hasNexi) return
    const updated = new URLSearchParams(params)
    if (next === 'summary') updated.delete('tab')
    else updated.set('tab', next)

    if (next !== 'report') {
      updated.delete('period')
      updated.delete('account')
    }
    updated.delete('q')
    updated.delete('auto')
    setParams(updated, { replace: true })
  }

  const premiumLocked = tab === 'report' && !hasNexi

  return <section className="mail-view control-center-page nexi-control-center">
    <div className="view-header control-page-header nexi-control-header">
      <div className="control-page-title nexi-control-title">
        <span className="nexi-control-brandmark" aria-hidden="true"><NexiVisual size="small" /></span>
        <div><h1>Nexi Control Center</h1><p>Prioridades, resúmenes, informes y estadísticas para decidir y actuar más rápido.</p></div>
      </div>

      <nav className="control-tabs control-tabs-inline" aria-label="Secciones de Nexi Control Center">
        <button type="button" className={tab === 'summary' ? 'active' : ''} onClick={() => selectTab('summary')}><Gauge size={16} /> Prioridades</button>
        <button type="button" disabled={!hasNexi} title={!hasNexi ? 'Disponible desde Premium' : undefined} className={tab === 'report' ? 'active nexi-tab' : 'nexi-tab'} onClick={() => selectTab('report')}><Sparkles size={16} /> Informes {!hasNexi && <LockKeyhole size={12} />}</button>
        <button type="button" className={tab === 'statistics' ? 'active' : ''} onClick={() => selectTab('statistics')}><BarChart3 size={16} /> Estadísticas</button>
        <button type="button" className={tab === 'contacts' ? 'active' : ''} onClick={() => selectTab('contacts')}><Users size={16} /> Contactos</button>
        <button type="button" className={tab === 'documents' ? 'active' : ''} onClick={() => selectTab('documents')}><Files size={16} /> Documentos</button>
      </nav>
    </div>

    {premiumLocked
      ? <div className="commercial-feature-lock"><LockKeyhole size={22} /><div><strong>Función disponible desde Premium</strong><span>Los informes con IA forman parte de los planes con Nexi habilitado.</span></div><Link to="/settings/plan" className="primary-button">Ver planes</Link></div>
      : tab === 'summary'
        ? <ControlCenter />
        : tab === 'report'
          ? <NexiMailReport />
          : tab === 'statistics'
            ? <ControlCenterStatistics />
            : tab === 'contacts'
              ? <ControlCenterContacts />
              : <ControlCenterDocuments />}
  </section>
}
