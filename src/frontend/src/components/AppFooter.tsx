import { Link } from 'react-router-dom'
import { APP_VERSION, PRODUCT_DEVELOPER, PRODUCT_NAME } from '../appMeta'

export function AppFooter({ compact = false }: { compact?: boolean }) {
  return <footer className={`app-footer ${compact ? 'compact' : ''}`}>
    <div className="app-footer-brand">
      <strong>{PRODUCT_NAME}</strong>
      <span>v{APP_VERSION}</span>
      <span>Desarrollado por {PRODUCT_DEVELOPER}</span>
    </div>
    <nav aria-label="Información legal">
      <Link to="/legal/terms">Términos</Link>
      <Link to="/legal/privacy">Privacidad</Link>
      <Link to="/legal/security">Seguridad</Link>
    </nav>
  </footer>
}
