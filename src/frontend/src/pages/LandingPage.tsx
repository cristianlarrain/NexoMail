import { ArrowRight, BarChart3, Building2, Check, Layers3, Mail, Palette, ShieldCheck, Sparkles } from 'lucide-react'
import { Link } from 'react-router-dom'

type Plan = {
  name: string
  price: string
  cadence?: string
  description: string
  features: string[]
  cta: string
  href: string
  featured?: boolean
  badge?: string
}

const plans: Plan[] = [
  {
    name: 'Freemium',
    price: '$0',
    cadence: 'para siempre',
    description: 'Para comenzar a centralizar sus cuentas personales sin costo.',
    features: [
      'Hasta 2 cuentas de correo',
      'Bandeja unificada',
      'Enviar, responder y organizar',
      'Centro de Control básico',
      'Funciones esenciales de seguimiento',
    ],
    cta: 'Comenzar gratis',
    href: '/login?mode=register',
  },
  {
    name: 'Premium',
    price: '$4.990',
    cadence: 'CLP / mes',
    description: 'Para profesionales que necesitan más control, productividad y seguimiento.',
    features: [
      'Hasta 10 cuentas de correo',
      'Centro de Control completo',
      'Estadísticas y seguimientos avanzados',
      'Firmas y plantillas múltiples',
      'Asistencia con IA',
      'Soporte prioritario',
    ],
    cta: 'Elegir Premium',
    href: '/login?mode=register',
    featured: true,
    badge: 'Más elegido',
  },
  {
    name: 'Corporativo',
    price: '$7.990',
    cadence: 'CLP / usuario / mes',
    description: 'Para equipos que requieren administración centralizada y visibilidad organizacional.',
    features: [
      'Todo lo incluido en Premium',
      'Administración de usuarios',
      'Roles y permisos',
      'Estadísticas de organización',
      'Políticas y configuración corporativa',
      'Soporte prioritario para equipos',
    ],
    cta: 'Crear cuenta corporativa',
    href: '/login?mode=register',
  },
  {
    name: 'White Label',
    price: 'A medida',
    description: 'NexoMail como plataforma de correo bajo la identidad de su propia organización o marca.',
    features: [
      'Logotipo, nombre y colores propios',
      'Dominio personalizado',
      'Experiencia sin marca NexoMail visible',
      'Configuración corporativa dedicada',
      'Límites y políticas personalizables',
      'Implementación y soporte de EIDOS Digital',
    ],
    cta: 'Solicitar propuesta',
    href: '#white-label',
    badge: 'Para organizaciones',
  },
]

function Brand() {
  return <span className="landing-brand" aria-label="NexoMail by EIDOS Digital">
    <span className="landing-brand-mark"><Mail size={21} /></span>
    <span className="landing-brand-copy">
      <strong>NexoMail</strong>
      <small>by EIDOS Digital</small>
    </span>
  </span>
}

export function LandingPage() {
  return <main className="landing-page">
    <header className="landing-header">
      <a href="#inicio" className="landing-logo-link"><Brand /></a>
      <nav className="landing-nav" aria-label="Navegación principal">
        <a href="#funciones">Funciones</a>
        <a href="#control">Centro de Control</a>
        <a href="#planes">Planes</a>
        <a href="#seguridad">Seguridad</a>
      </nav>
      <div className="landing-header-actions">
        <Link className="landing-login-link" to="/login">Iniciar sesión</Link>
        <Link className="landing-button landing-button-primary landing-header-cta" to="/login?mode=register">Crear cuenta gratis</Link>
      </div>
    </header>

    <section className="landing-hero" id="inicio">
      <div className="landing-hero-copy">
        <p className="landing-kicker">Correo unificado para personas y organizaciones</p>
        <h1>Todos sus correos.<br /><span>Un solo lugar.</span></h1>
        <p className="landing-hero-lead">Gestione múltiples cuentas desde una interfaz única, mantenga visibles sus pendientes y controle qué mensajes requieren respuesta o seguimiento.</p>
        <div className="landing-hero-actions">
          <Link className="landing-button landing-button-primary" to="/login?mode=register">Comenzar gratis <ArrowRight size={17} /></Link>
          <Link className="landing-button landing-button-secondary" to="/login">Iniciar sesión</Link>
        </div>
        <p className="landing-no-card">Sin tarjeta de crédito para comenzar.</p>
      </div>

      <div className="landing-product-preview" aria-label="Vista conceptual del Centro de Control de NexoMail">
        <div className="landing-preview-topbar">
          <Brand />
          <span className="landing-preview-status">Centro de Control</span>
        </div>
        <div className="landing-preview-grid">
          <article><span>Sin responder</span><strong>12</strong><small>Recibidos pendientes</small></article>
          <article><span>Esperando respuesta</span><strong>5</strong><small>Enviados en seguimiento</small></article>
          <article><span>Próximos a vencer</span><strong>3</strong><small>Requieren atención</small></article>
        </div>
        <div className="landing-preview-chart">
          <div className="landing-chart-heading"><span>Actividad semanal</span><small>Últimos 7 días</small></div>
          <div className="landing-bars" aria-hidden="true">
            <i style={{ height: '35%' }} /><i style={{ height: '52%' }} /><i style={{ height: '43%' }} /><i style={{ height: '74%' }} /><i style={{ height: '62%' }} /><i style={{ height: '88%' }} /><i style={{ height: '68%' }} />
          </div>
        </div>
      </div>
    </section>

    <section className="landing-trust-strip" id="funciones">
      <article><Layers3 size={22} /><div><strong>Todas sus cuentas</strong><p>Centralice cuentas personales, académicas y corporativas en una sola experiencia.</p></div></article>
      <article><BarChart3 size={22} /><div><strong>Centro de Control</strong><p>Vea pendientes, respuestas esperadas, plazos y actividad sin revisar cada buzón por separado.</p></div></article>
      <article><ShieldCheck size={22} /><div><strong>Correo bajo su control</strong><p>NexoMail funciona como gestor de sus cuentas y evita convertirse en otro buzón que deba administrar.</p></div></article>
    </section>

    <section className="landing-section landing-control-section" id="control">
      <div className="landing-section-heading">
        <p className="landing-kicker">Centro de Control</p>
        <h2>Su correo deja de ser una lista de mensajes.</h2>
        <p>Transforme la actividad de sus cuentas en información útil para responder, hacer seguimiento y priorizar.</p>
      </div>
      <div className="landing-control-features">
        <article><BarChart3 size={20} /><strong>Pendientes visibles</strong><p>Correos recibidos sin responder y mensajes enviados que siguen esperando respuesta.</p></article>
        <article><Sparkles size={20} /><strong>Asistencia inteligente</strong><p>Resúmenes, apoyo de redacción y detección de información que requiere atención.</p></article>
        <article><ShieldCheck size={20} /><strong>Visión centralizada</strong><p>Una lectura rápida del estado de sus cuentas sin perder la separación entre ellas.</p></article>
      </div>
    </section>

    <section className="landing-section landing-pricing-section" id="planes">
      <div className="landing-section-heading landing-section-heading-centered">
        <p className="landing-kicker">Planes</p>
        <h2>Un plan para cada forma de trabajar.</h2>
        <p>Comience gratis y aumente capacidades cuando su uso lo requiera.</p>
      </div>
      <div className="landing-pricing-grid">
        {plans.map(plan => <article key={plan.name} className={`landing-plan-card ${plan.featured ? 'featured' : ''}`}>
          {plan.badge && <span className="landing-plan-badge">{plan.badge}</span>}
          <h3>{plan.name}</h3>
          <p className="landing-plan-description">{plan.description}</p>
          <div className="landing-plan-price"><strong>{plan.price}</strong>{plan.cadence && <span>{plan.cadence}</span>}</div>
          <ul>{plan.features.map(feature => <li key={feature}><Check size={16} /> <span>{feature}</span></li>)}</ul>
          {plan.href.startsWith('/')
            ? <Link className={`landing-button ${plan.featured ? 'landing-button-primary' : 'landing-button-secondary'}`} to={plan.href}>{plan.cta}</Link>
            : <a className="landing-button landing-button-secondary" href={plan.href}>{plan.cta}</a>}
        </article>)}
      </div>
      <p className="landing-pricing-note">Los precios corresponden a la propuesta comercial inicial de NexoMail. Las capacidades sujetas a activación progresiva se identificarán como “Próximamente” antes de la comercialización definitiva.</p>
    </section>

    <section className="landing-section landing-white-label" id="white-label">
      <div className="landing-white-label-icon"><Palette size={28} /></div>
      <div>
        <p className="landing-kicker">White Label</p>
        <h2>Su marca. Su dominio. La plataforma NexoMail.</h2>
        <p>Para empresas, instituciones o proveedores que quieran ofrecer la experiencia de NexoMail como un servicio propio. EIDOS Digital adapta identidad visual, dominio, configuración, límites y presentación comercial manteniendo una base tecnológica común.</p>
        <div className="landing-white-label-tags"><span>Logo propio</span><span>Colores propios</span><span>Dominio personalizado</span><span>Sin marca NexoMail</span><span>Configuración dedicada</span></div>
      </div>
      <div className="landing-white-label-commercial">
        <Building2 size={24} />
        <strong>Cotización personalizada</strong>
        <p>El valor se define según usuarios, personalización, dominio, soporte e implementación requerida.</p>
      </div>
    </section>

    <section className="landing-section landing-security" id="seguridad">
      <ShieldCheck size={30} />
      <div><p className="landing-kicker">Seguridad</p><h2>Su correo sigue siendo suyo.</h2><p>NexoMail se diseña para gestionar cuentas conectadas manteniendo separación entre usuarios y evitando almacenamiento innecesario del contenido de los buzones. Las garantías técnicas específicas se publicarán de acuerdo con los mecanismos efectivamente habilitados en producción.</p></div>
    </section>

    <section className="landing-final-cta">
      <p className="landing-kicker">NexoMail</p>
      <h2>Recupere el control de su correo.</h2>
      <p>Conecte sus cuentas y trabaje desde una sola interfaz.</p>
      <Link className="landing-button landing-button-primary" to="/login?mode=register">Crear cuenta gratis <ArrowRight size={17} /></Link>
    </section>

    <footer className="landing-footer">
      <Brand />
      <div className="landing-footer-links"><a href="#funciones">Producto</a><a href="#planes">Planes</a><a href="#seguridad">Seguridad</a><a href="#white-label">White Label</a></div>
      <small>© 2026 EIDOS Digital</small>
    </footer>
  </main>
}
