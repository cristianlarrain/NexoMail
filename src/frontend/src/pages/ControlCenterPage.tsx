import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { BarChart3, Files, MessageSquareText, Sparkles, Users } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { ControlCenter } from '../components/ControlCenter'
import { ControlCenterContacts } from '../components/ControlCenterContacts'
import { ControlCenterDocuments } from '../components/ControlCenterDocuments'
import { NexiContextWorkspace } from '../components/NexiContextWorkspace'
import { NexiDailyBrief } from '../components/NexiDailyBrief'
import { NexiMailReport } from '../components/NexiMailReport'
import { NexiTrendReport } from '../components/NexiTrendReport'
import { NexoPerspective } from '../components/NexoPerspective'

type ControlTab = 'summary' | 'nexi' | 'report' | 'contacts' | 'documents'

function normalizedTab(value: string | null): ControlTab {
  return value === 'nexi' || value === 'report' || value === 'contacts' || value === 'documents' ? value : 'summary'
}

export function ControlCenterPage() {
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const tab = normalizedTab(params.get('tab'))

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'active' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'active' })
  }, [queryClient])

  function selectTab(next: ControlTab) {
    const updated = new URLSearchParams(params)
    if (next === 'summary') updated.delete('tab')
    else updated.set('tab', next)

    if (next !== 'report') updated.delete('period')
    if (next !== 'nexi') {
      updated.delete('q')
      updated.delete('auto')
    }
    if (next !== 'nexi' && next !== 'report') updated.delete('account')

    setParams(updated, { replace: true })
  }

  return <section className="mail-view control-center-page nexi-control-center">
    <div className="view-header control-page-header nexi-control-header">
      <div className="control-page-title nexi-control-title">
        <div><h1>Centro de Control</h1><p>Supervisa el correo, prioriza pendientes y consulta análisis, contactos y documentos desde un solo espacio.</p></div>
      </div>

      <nav className="control-tabs control-tabs-inline" aria-label="Secciones del Centro de Control">
        <button type="button" className={tab === 'summary' ? 'active' : ''} onClick={() => selectTab('summary')}><BarChart3 size={16} /> Operación</button>
        <button type="button" className={tab === 'nexi' ? 'active nexi-tab' : 'nexi-tab'} onClick={() => selectTab('nexi')}><MessageSquareText size={16} /> Nexi</button>
        <button type="button" className={tab === 'report' ? 'active nexi-tab' : 'nexi-tab'} onClick={() => selectTab('report')}><Sparkles size={16} /> Informes</button>
        <button type="button" className={tab === 'contacts' ? 'active' : ''} onClick={() => selectTab('contacts')}><Users size={16} /> Contactos</button>
        <button type="button" className={tab === 'documents' ? 'active' : ''} onClick={() => selectTab('documents')}><Files size={16} /> Documentos</button>
      </nav>
    </div>

    <NexoPerspective />

    {tab === 'summary'
      ? <><NexiDailyBrief /><ControlCenter /><NexiTrendReport /></>
      : tab === 'nexi'
        ? <NexiContextWorkspace />
        : tab === 'report'
          ? <NexiMailReport />
          : tab === 'contacts'
            ? <ControlCenterContacts />
            : <ControlCenterDocuments />}
  </section>
}
