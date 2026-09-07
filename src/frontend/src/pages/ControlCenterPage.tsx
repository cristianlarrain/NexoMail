import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { BarChart3, Files, Users } from 'lucide-react'
import { ControlCenter } from '../components/ControlCenter'
import { ControlCenterContacts } from '../components/ControlCenterContacts'
import { ControlCenterDocuments } from '../components/ControlCenterDocuments'

type ControlTab = 'summary' | 'contacts' | 'documents'

export function ControlCenterPage() {
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<ControlTab>('summary')

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'active' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'active' })
  }, [queryClient])

  return <section className="mail-view control-center-page">
    <div className="view-header control-page-header">
      <div>
        <h1>Centro de control</h1>
        <p className="view-context">Todas las cuentas</p>
      </div>
    </div>

    <nav className="control-tabs" aria-label="Secciones del Centro de control">
      <button type="button" className={tab === 'summary' ? 'active' : ''} onClick={() => setTab('summary')}><BarChart3 size={16} /> Resumen</button>
      <button type="button" className={tab === 'contacts' ? 'active' : ''} onClick={() => setTab('contacts')}><Users size={16} /> Contactos</button>
      <button type="button" className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}><Files size={16} /> Documentos</button>
    </nav>

    {tab === 'summary' ? <ControlCenter /> : tab === 'contacts' ? <ControlCenterContacts /> : <ControlCenterDocuments />}
  </section>
}
