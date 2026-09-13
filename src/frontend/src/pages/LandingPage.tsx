import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, Building2, Check, Clock3, Gauge, Inbox, Layers3, LockKeyhole, Mail, Moon, Palette, ShieldCheck, Sparkles, Sun, Users } from 'lucide-react'
import { NexoMailLogo } from '../components/brand/NexoMailLogo'
import { NexiVisual } from '../components/nexi/NexiVisual'

const demoVideoUrl = 'https://cdn.creativeclaw.co/u/372f7075/videos/d1cab7f4-113a-43c9-bc48-ea80643f0088.mp4'
const demoPosterUrl = 'https://cdn.creativeclaw.co/u/372f7075/images/8b998e07-5e31-4275-bf93-836a2e3723ae.png'

const features = [
  { icon: <Inbox size={21} />, title: 'Todas sus cuentas, en un solo lugar', description: 'Revise, responda y gestione varias cuentas de correo desde una interfaz única, sin saltar entre proveedores.' },
  { icon: <Gauge size={21} />, title: 'Centro de Control', description: 'Detecte correos sin responder, mensajes enviados sin respuesta, pendientes de más de 48 horas y actividad por cuenta.' },
  { icon: <Clock3 size={21} />, title: 'Seguimiento inteligente', description: 'Identifique pendientes y vuelva rápidamente a las conversaciones que requieren una acción.' },
  { icon: <Layers3 size={21} />, title: 'Gestión operativa', description: 'Archive, ignore, gestione no leídos, adjuntos y otras acciones habituales desde un mismo flujo.' },
]

const plans = [
  {
    name: 'Freemium',
    price: '$0',
    cadence: 'para siempre',
    description: 'Para comenzar a centralizar sus cuentas personales sin costo.',
    items: ['Hasta 2 cuentas de correo', 'Bandeja unificada', 'Enviar, responder y organizar', 'Centro de Control básico', 'Seguimiento esencial'],
    cta: 'Comenzar gratis',
    href: '/login?mode=register',
  },
  {
    name: 'Premium',
    price: '$4.990',
    cadence: 'CLP / mes',
    description: 'Para profesionales que necesitan más control, productividad y seguimiento.',
    items: ['Hasta 10 cuentas de correo', 'Centro de Control completo', 'Estadísticas y seguimientos avanzados', 'Firmas y plantillas', 'Funciones de Nexi e IA'],
    cta: 'Elegir Premium',
    href: '/login?mode=register',
    featured: true,
    badge: 'Más elegido',
  },
  {
    name: 'Corporativo',
    price: '$7.990',
    cadence: 'CLP / usuario / mes',
    description: 'Para equipos y organizaciones que requieren administración centralizada.',
    items: ['Todo lo de Premium', 'Gestión de usuarios', 'Roles y políticas', 'Estadísticas de organización', 'Soporte prioritario'],
    cta: 'Conocer Corporativo',
    href: '/login?mode=register',
    note: 'Desde 5 usuarios',
  },
]

export function LandingPage() {
  const [theme, setTheme] = useState(() => localStorage.getItem('nexomail-theme') ?? 'light')

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem('nexomail-theme', theme)
  }, [theme])

  return <main className="landing-page">
    <header className="landing-nav">
      <Link to="/" className="landing-brand" aria-label="NexoMail, inicio"><NexoMailLogo /></Link>
      <nav className="landing-links" aria-label="Navegación del sitio">
        <a href="#caracteristicas">Características</a>
        <a href="#nexi">Nexi</a>
        <a href="#servicios">Integraciones</a>
        <a href="#planes">Planes</a>
      </nav>
      <div className="landing-actions">
        <button type="button" className="landing-theme-button" onClick={() => setTheme(current => current === 'dark' ? 'light' : 'dark')} aria-label={theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}>{theme === 'dark' ? <Sun size={17} /> : <Moon size={17} />}</button>
        <Link to="/login" className="landing-login">Iniciar sesión</Link>
        <Link to="/login?mode=register" className="landing-primary">Crear cuenta gratis <ArrowRight size={16} /></Link>
      </div>
    </header>

    <section className="landing-hero">
      <div className="landing-hero-copy">
        <span className="landing-pill"><Sparkles size={14} /> Correo unificado con control y seguimiento</span>
        <h1>Todos sus correos.<br /><span>Un solo lugar.</span></h1>
        <p>NexoMail reúne sus cuentas, sus pendientes y su seguimiento en una sola interfaz. Menos tiempo buscando mensajes. Más claridad sobre lo que requiere atención.</p>
        <div className="landing-hero-actions">
          <Link to="/login?mode=register" className="landing-primary large">Comenzar gratis <ArrowRight size={17} /></Link>
          <a href="#planes" className="landing-secondary">Ver planes</a>
        </div>
        <div className="landing-trust-row">
          <span><Check size={14} /> Gmail, Microsoft 365 e IMAP / SMTP Beta</span>
          <span><Clock3 size={14} /> Marcha blanca · 30 días</span>
        </div>
      </div>

      <div className="landing-commercial-preview" aria-label="NexoMail en acción">
        <div className="landing-commercial-preview-head">
          <span className="landing-commercial-kicker">Centro de Control</span>
          <h3>NexoMail le muestra lo que su bandeja no le dice.</h3>
          <p>Pendientes, respuestas y atrasos visibles antes de que algo importante quede olvidado.</p>
        </div>

        <div className="landing-commercial-metrics">
          <article className="metric-cyan"><b>46</b><span>Recibidos sin responder</span></article>
          <article className="metric-amber"><b>10</b><span>Esperando respuesta</span></article>
          <article className="metric-orange"><b>39</b><span>Más de 48 horas</span></article>
        </div>

        <div className="landing-video-stage" id="demo-video">
          <video
            className="landing-video-player"
            controls
            playsInline
            preload="metadata"
            poster={demoPosterUrl}
            aria-label="Vea NexoMail en acción"
          >
            <source src={demoVideoUrl} type="video/mp4" />
            Su navegador no admite reproducción de video HTML5.
          </video>
          <div className="landing-video-caption">
            <strong>Vea NexoMail en acción</strong>
            <span>Demo de producto · 55 segundos</span>
          </div>
        </div>

        <div className="landing-commercial-preview-foot">
          <span><Sparkles size={14} /> Priorización asistida por Nexi</span>
          <span><Check size={14} /> Lo importante, visible primero</span>
        </div>
      </div>
    </section>

    <section className="landing-section" id="caracteristicas">
      <div className="landing-section-heading"><span>Producto</span><h2>Correo pensado como un centro de trabajo</h2><p>NexoMail convierte el correo en una herramienta más clara, medible y fácil de gestionar, sin obligarlo a cambiar constantemente entre proveedores.</p></div>
      <div className="landing-feature-grid">{features.map(feature => <article key={feature.title}><i>{feature.icon}</i><h3>{feature.title}</h3><p>{feature.description}</p></article>)}</div>
    </section>

    <section className="landing-section landing-nexi-section" id="nexi">
      <div className="landing-nexi-visual"><NexiVisual size="large" /><span>Nexi</span></div>
      <div className="landing-nexi-copy">
        <span className="landing-section-label">Asistente inteligente</span>
        <h2>Nexi entiende dónde está el trabajo pendiente</h2>
        <p>Nexi utiliza información real de NexoMail para identificar conversaciones que requieren atención y facilitar acciones. Las funciones de IA ampliarán progresivamente esta capacidad sin reemplazar el control del usuario.</p>
        <div className="landing-nexi-capabilities">
          <span><Check size={14} /> Contexto del correo abierto</span>
          <span><Check size={14} /> Pendientes y seguimiento</span>
          <span><Sparkles size={14} /> Resúmenes inteligentes</span>
          <span><Sparkles size={14} /> Redacción asistida</span>
        </div>
      </div>
    </section>

    <section className="landing-section" id="servicios">
      <div className="landing-section-heading"><span>Integraciones</span><h2>Conecte las cuentas que ya utiliza</h2><p>Conecte sus cuentas actuales y trabaje desde una sola interfaz, sin cambiar su proveedor de correo.</p></div>
      <div className="landing-provider-grid">
        <article className="available"><div className="provider-symbol"><Mail size={22} /></div><div><strong>Google / Gmail</strong><span>Gmail y Google Workspace</span></div><b>Disponible</b></article>
        <article className="available"><div className="provider-symbol microsoft"><span /><span /><span /><span /></div><div><strong>Microsoft 365</strong><span>Profesional, educativa o institucional</span></div><b>Disponible</b></article>
        <article className="available"><div className="provider-symbol"><Layers3 size={22} /></div><div><strong>IMAP / SMTP</strong><span>Dominio propio y otros proveedores compatibles</span></div><b>Beta</b></article>
      </div>
      <p className="landing-pricing-note">Las cuentas Microsoft 365 institucionales pueden requerir autorización previa del administrador de su organización para permitir aplicaciones externas como NexoMail.</p>
    </section>

    <section className="landing-section landing-how">
      <div className="landing-section-heading"><span>Cómo funciona</span><h2>Conectar, organizar y actuar</h2></div>
      <div className="landing-steps"><article><b>01</b><h3>Cree su cuenta</h3><p>Su usuario NexoMail mantiene separada su configuración y sus cuentas conectadas.</p></article><article><b>02</b><h3>Conecte sus correos</h3><p>Autorice las cuentas que quiera gestionar desde una sola interfaz.</p></article><article><b>03</b><h3>Trabaje desde NexoMail</h3><p>Lea, responda, haga seguimiento y consulte el Centro de Control.</p></article></div>
    </section>

    <section className="landing-section" id="planes">
      <div className="landing-section-heading"><span>Planes</span><h2>Una modalidad para cada forma de trabajar</h2><p>Comience sin costo, avance a Premium cuando necesite más capacidad o lleve NexoMail a toda su organización.</p></div>
      <div className="landing-plan-grid landing-commercial-plans">{plans.map(plan => <article key={plan.name} className={plan.featured ? 'featured' : ''}>
        {plan.badge && <span className="landing-plan-badge">{plan.badge}</span>}
        {plan.note && <small>{plan.note}</small>}
        <h3>{plan.name}</h3>
        <div className="landing-plan-price"><strong>{plan.price}</strong><span>{plan.cadence}</span></div>
        <p>{plan.description}</p>
        <ul>{plan.items.map(item => <li key={item}><Check size={14} />{item}</li>)}</ul>
        <Link to={plan.href}>{plan.cta} <ArrowRight size={15} /></Link>
      </article>)}</div>
      <p className="landing-pricing-note">Los valores corresponden a la propuesta comercial inicial y pueden ajustarse antes del lanzamiento definitivo.</p>
    </section>

    <section className="landing-section landing-white-label" id="white-label">
      <div className="landing-white-label-icon"><Palette size={28} /></div>
      <div>
        <span className="landing-section-label">White Label</span>
        <h2>NexoMail, con la identidad de su organización</h2>
        <p>EIDOS Digital puede implementar una versión personalizada de NexoMail con marca, logotipo, colores, dominio y configuración propios. Está orientada a empresas, proveedores de servicios y organizaciones que quieran ofrecer la plataforma como parte de su propia solución digital.</p>
      </div>
      <div className="landing-white-label-points"><span><Building2 size={17} /> Dominio y marca propios</span><span><Palette size={17} /> Identidad visual personalizada</span><span><Users size={17} /> Usuarios y límites configurables</span><span><ShieldCheck size={17} /> Implementación y soporte EIDOS Digital</span></div>
    </section>

    <section className="landing-section landing-security">
      <div><span className="landing-section-label">Seguridad y privacidad</span><h2>Sus correos siguen perteneciendo a sus proveedores.</h2><p>La arquitectura de NexoMail está orientada a consultar y gestionar el correo mediante las APIs de cada proveedor, manteniendo en la plataforma sólo la información necesaria para la operación y el seguimiento.</p></div>
      <div className="landing-security-points"><span><ShieldCheck size={18} /> Sesiones y aislamiento por usuario</span><span><LockKeyhole size={18} /> Protección de credenciales y tokens</span><span><Users size={18} /> Arquitectura preparada para múltiples usuarios</span></div>
    </section>

    <section className="landing-cta">
      <NexoMailLogo />
      <h2>Recupere el control de su correo.</h2>
      <p>Centralice cuentas, pendientes y seguimiento en un solo espacio.</p>
      <Link to="/login?mode=register" className="landing-primary large">Crear cuenta gratis <ArrowRight size={17} /></Link>
    </section>

    <footer className="landing-footer"><NexoMailLogo /><span>Correo unificado · Control · Seguimiento · IA</span><div><a href="#caracteristicas">Características</a><a href="#planes">Planes</a><a href="#white-label">White Label</a><Link to="/login">Acceso</Link></div></footer>
  </main>
}
