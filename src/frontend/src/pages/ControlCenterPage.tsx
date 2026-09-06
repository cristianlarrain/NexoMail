import { ControlCenter } from '../components/ControlCenter'

export function ControlCenterPage() {
  return <section className="mail-view control-center-page">
    <div className="view-header">
      <div>
        <h1>Centro de control</h1>
        <p className="view-context">Todas las cuentas</p>
      </div>
    </div>
    <ControlCenter />
  </section>
}
