import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { BarChart3, Files, Users } from 'lucide-react'
import { useSearchParams } from 'react-router-dom'
import { ControlCenter } from '../components/ControlCenter'
import { ControlCenterContacts } from '../components/ControlCenterContacts'
import { ControlCenterDocuments } from '../components/ControlCenterDocuments'

type ControlTab = 'summary' | 'contacts' | 'documents'

function normalizedTab(value: string | null): ControlTab {
  return value === 'contacts' || value === 'documents' ? value : 'summary'
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
    setParams(updated, { replace: true })
  }

  return <section className="mail-view control-center-page">
    <div className="view-header control-page-header">
      <div className="control-page-title">
        <h1>Centro de control</h1>
      </div>

      <nav className="control-tabs control-tabs-inline" aria-label="Secciones del Centro de control">
        <button type="button" className={tab === 'summary' ? 'active' : ''} onClick={() => selectTab('summary')}><BarChart3 size={16} /> Resumen</button>
        <button type="button" className={tab === 'contacts' ? 'active' : ''} onClick={() => selectTab('contacts')}><Users size={16} /> Contactos</button>
        <button type="button" className={tab === 'documents' ? 'active' : ''} onClick={() => selectTab('documents')}><Files size={16} /> Documentos</button>
      </nav>
    </div>

    {tab === 'summary' ? <ControlCenter /> : tab === 'contacts' ? <ControlCenterContacts /> : <ControlCenterDocuments />}
  </section>
}
