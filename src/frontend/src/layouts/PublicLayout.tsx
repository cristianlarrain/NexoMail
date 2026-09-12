import { Outlet } from 'react-router-dom'
import { AppFooter } from '../components/AppFooter'

export function PublicLayout() {
  return <div className="public-shell">
    <Outlet />
    <AppFooter compact />
  </div>
}
