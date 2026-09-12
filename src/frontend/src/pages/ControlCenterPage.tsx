import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { BarChart3, Files, Gauge, LockKeyhole, Sparkles, Users } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { commercialApi } from '../api/commercialApi'
import { ControlCenter } from '../components/ControlCenter'
import { ControlCenterContacts } from '../components/ControlCenterContacts'
import { ControlCenterDocuments } from '../components/ControlCenterDocuments'
import { ControlCenterStatistics } from '../components/ControlCenterStatistics'
import { NexiContextWorkspace } from '../components/NexiContextWorkspace'
import { NexiMailReport } from '../components/NexiMailReport'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { commercialEntitlements } from '../utils/commercialEntitlements'

type ControlTab = 'summary' | 'report' | 'statistics' | 'contacts' | 'documents' | 'context'

function normalizedTab(value: string | null): ControlTab {
  if (value === 'nexi' || value === 'context') return 'context'
  return value === 'report' || value === 'statistics' || value === 'contacts' || value === 'documents' ? value : 'summary'
}

export function ControlCenterPage() {
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const subscription = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const tab = normalizedTab(params.get('tab'))
  const hasNexi = subscription.data?.entitlements.includes(commercialEntitlements.nexiAi) === true
  const hasAdvancedAnalytics = subscription.data?.entitlements.includes(commercialEntitlements.advancedAnalytics) === true
  const hasFullControlCenter = subscription.data?.entitlements.includes(commercialEntitlements.controlCenterFull) === true

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'active' })
    if (hasAdvancedAnalytics) void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'active' })
  }, [hasAdvancedAnalytics, queryClient])

  function selectTab(next: Exclude<ControlTab, 'context'>) {
    if (next === 'report' && !hasNexi) return
    if (next === 'statistics' && !hasAdvancedAnalytics) return
    if ((next === 'contacts' || next === 'documents') && !hasFullControlCenter) return

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

  const nexiLocked = (tab === 'report' || tab === 'context') && !hasNexi
  const analyticsLocked = tab === 'statistics' && !hasAdvancedAnalytics
  const fullControlLocked = (tab === 'contacts' || tab === 'documents') && !hasFullControlCenter
  const featureLocked = nexiLocked || analyticsLocked || fullControlLocked
  const lockTitle = nexiLocked
    ? 'Nexi e IA no está incluido en su plan'
    : analyticsLocked
      ? 'Estadísticas avanzadas no está incluido en su plan'
      : 'Centro de Control completo no está incluido en su plan'
  const lockDetail = nexiLocked
    ? 'Los informes y análisis con IA requieren un plan con Nexi habilitado.'
    : analyticsLocked
      ? 'La vista de estadísticas requiere la capacidad Estadísticas avanzadas.'
      : 'Contactos y Documentos requieren la capacidad Centro de Control completo.'

  return <section className="mail-view control-center-page nexi-control-center">
    <div className="view-header control-page-header nexi-control-header">
      <div className="control-page-title nexi-control-title">
        <span className="nexi-control-brandmark" title="Nexi" aria-hidden="true"><NexiVisual size="small" /></span>
        <h1>Nexi Control Center</h1>
      </div>

      <nav className="control-tabs control-tabs-inline" aria-label="Secciones de Nexi Control Center">
        <button type="button" className={tab === 'summary' ? 'active' : ''} onClick={() => selectTab('summary')}><Gauge size={16} /> Prioridades</button>
        <button type="button" disabled={!hasNexi} title={!hasNexi ? 'No incluido en su plan' : undefined} className={tab === 'report' ? 'active nexi-tab' : 'nexi-tab'} onClick={() => selectTab('report')}><Sparkles size={16} /> Informes {!hasNexi && <LockKeyhole size={12} />}</button>
        <button type="button" disabled={!hasAdvancedAnalytics} title={!hasAdvancedAnalytics ? 'No incluido en su plan' : undefined} className={tab === 'statistics' ? 'active' : ''} onClick={() => selectTab('statistics')}><BarChart3 size={16} /> Estadísticas {!hasAdvancedAnalytics && <LockKeyhole size={12} />}</button>
        <button type="button" disabled={!hasFullControlCenter} title={!hasFullControlCenter ? 'No incluido en su plan' : undefined} className={tab === 'contacts' ? 'active' : ''} onClick={() => selectTab('contacts')}><Users size={16} /> Contactos {!hasFullControlCenter && <LockKeyhole size={12} />}</button>
        <button type="button" disabled={!hasFullControlCenter} title={!hasFullControlCenter ? 'No incluido en su plan' : undefined} className={tab === 'documents' ? 'active' : ''} onClick={() => selectTab('documents')}><Files size={16} /> Documentos {!hasFullControlCenter && <LockKeyhole size={12} />}</button>
      </nav>
    </div>

    {featureLocked
      ? <div className="commercial-feature-lock"><LockKeyhole size={22} /><div><strong>{lockTitle}</strong><span>{lockDetail}</span></div><Link to="/settings/plan" className="primary-button">Ver planes</Link></div>
      : tab === 'summary'
        ? <ControlCenter />
        : tab === 'report'
          ? <NexiMailReport />
          : tab === 'statistics'
            ? <ControlCenterStatistics />
            : tab === 'contacts'
              ? <ControlCenterContacts />
              : tab === 'documents'
                ? <ControlCenterDocuments />
                : <NexiContextWorkspace />}
  </section>
}