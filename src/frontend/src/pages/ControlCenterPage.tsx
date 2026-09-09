import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { ControlCenter } from '../components/ControlCenter'
import { NexoPerspective } from '../components/NexoPerspective'

export function ControlCenterPage() {
  const queryClient = useQueryClient()

  useEffect(() => {
    void queryClient.invalidateQueries({ queryKey: ['control-center'], refetchType: 'active' })
    void queryClient.invalidateQueries({ queryKey: ['control-center-activity'], refetchType: 'active' })
  }, [queryClient])

  return <section className="mail-view control-center-page">
    <div className="view-header">
      <div>
        <h1>Centro de control</h1>
        <p className="view-context">Todas las cuentas</p>
      </div>
    </div>
    <NexoPerspective />
    <ControlCenter />
  </section>
}
