import { Link } from 'react-router-dom'
import { LEGAL_VERSION, PRODUCT_DEVELOPER, PRODUCT_NAME } from '../appMeta'
import { legalDocuments, type LegalDocument } from '../legal/legalDocuments'

export function LegalPage({ document }: { document: LegalDocument }) {
  const current = legalDocuments[document]
  return <main className="legal-page">
    <article className="legal-card">
      <div className="legal-topline"><Link to="/">← {PRODUCT_NAME}</Link><span>Versión legal {LEGAL_VERSION}</span></div>
      <header><p className="eyebrow">{PRODUCT_DEVELOPER}</p><h1>{current.title}</h1><p>{current.intro}</p></header>
      <nav className="legal-tabs" aria-label="Documentos legales">
        <Link className={document === 'terms' ? 'active' : ''} to="/legal/terms">Términos</Link>
        <Link className={document === 'privacy' ? 'active' : ''} to="/legal/privacy">Privacidad</Link>
        <Link className={document === 'security' ? 'active' : ''} to="/legal/security">Seguridad</Link>
      </nav>
      <div className="legal-sections">
        {current.sections.map(section => <section key={section.title}><h2>{section.title}</h2>{section.paragraphs.map(paragraph => <p key={paragraph}>{paragraph}</p>)}</section>)}
      </div>
      <aside className="legal-review-note">Estos documentos forman parte de la base operativa del producto y deben someterse a revisión jurídica antes del lanzamiento comercial definitivo.</aside>
    </article>
  </main>
}
