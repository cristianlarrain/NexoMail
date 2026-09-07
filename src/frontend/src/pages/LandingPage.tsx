import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, Bot, Check, Clock3, Gauge, Inbox, Layers3, LockKeyhole, Mail, Moon, ShieldCheck, Sparkles, Sun, Users } from 'lucide-react'
import { NexoMailLogo } from '../components/brand/NexoMailLogo'
import { NexiVisual } from '../components/nexi/NexiVisual'

const features = [
  { icon: <Inbox size={21} />, title: 'Todas sus cuentas, en un solo lugar', description: 'Revise, responda y gestione varias cuentas de correo desde una interfaz única, sin saltar entre proveedores.' },
  { icon: <Gauge size={21} />, title: 'Centro de Control', description: 'Detecte correos sin responder, mensajes enviados sin respuesta, pendientes de más de 48 horas y actividad por cuenta.' },
  { icon: <Clock3 size={21} />, title: 'Seguimiento inteligente', description: 'Marque conversaciones, identifique pendientes y vuelva rápidamente a los correos que requieren una acción.' },
  { icon: <Layers3 size={21} />, title: 'Gestión operativa', description: 'Archive, ignore, marque spam, mueva a papelera, gestione no leídos y trabaje con adjuntos desde un mismo flujo.' },
  { icon: <Bot size={21} />, title: 'Nexi, su asistente', description: 'Nexi ya interpreta el contexto del correo y el Centro de Control. La siguiente etapa incorporará resúmenes y redacción asistida por IA.' },
  { icon: <ShieldCheck size={21} />, title: 'Privacidad desde el diseño', description: 'NexoMail está diseñado para consultar el correo desde los proveedores y evitar almacenar de forma permanente el contenido de los mensajes.' },
]

const plans = [
  {
    name: 'Personal',
    description: 'Para organizar sus cuentas personales y profesionales desde un solo espacio.',
    items: ['Bandeja unificada', 'Centro de Control', 'Seguimiento de conversaciones', 'Nexi asistente'],
    note: 'Modalidad prevista',
  },
  {
    name: 'Profesional',
    description: 'Para usuarios que manejan un volumen mayor de correo y necesitan más automatización.',
    items: ['Todo lo de Personal', 'Funciones avanzadas de IA', 'Redacción y respuestas asistidas', 'Más cuentas conectadas'],
    note: 'Próximamente',
    featured: true,
  },
  {
    name: 'Equipos',
    description: 'Para organizaciones que necesiten una experiencia administrada y escalable.',
    items: ['Gestión de usuarios', 'Políticas y administración', 'Configuración para organizaciones', 'Funciones colaborativas futuras'],
    note: 'En hoja de ruta',
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
        <a href="#servicios">Servicios</a>
        <a href="#modalidades">Modalidades</a>
      </nav>
      <div className="landing-actions">
        <button type="button" className="landing-theme-button" onClick={() => setTheme(current => current === 'dark' ? 'light' : 'dark')} aria-label={theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}>{theme === 'dark' ? <Sun size={17} /> : <Moon size={17} />}</button>
        <Link to="/login" className="landing-login">Iniciar sesión</Link>
        <Link to="/login" className="landing-primary">Crear cuenta <ArrowRight size={16} /></Link>
      </div>
    </header>

    <section className="landing-hero">
      <div className="landing-hero-copy">
        <span className="landing-pill"><Sparkles size={14} /> Una nueva forma de gestionar su correo</span>
        <h1>Todas sus cuentas.<br /><span>Una sola bandeja.</span></h1>
        <p>NexoMail reúne su correo, sus pendientes y su seguimiento en una sola interfaz. Menos tiempo buscando mensajes. Más claridad sobre lo que requiere atención.</p>
        <div className="landing-hero-actions">
          <Link to="/login" className="landing-primary large">Comenzar con NexoMail <ArrowRight size={17} /></Link>
          <a href="#caracteristicas" className="landing-secondary">Ver características</a>
        </div>
        <div className="landing-trust-row"><span><Check size={14} /> Gmail disponible</span><span><LockKeyhole size={14} /> Privacidad por diseño</span><span><Gauge size={14} /> Control operativo real</span></div>
      </div>

      <div className="landing-product-preview" aria-label="Vista conceptual de NexoMail">
        <div className="landing-preview-top"><NexoMailLogo compact /><span>Centro de Control</span><i /></div>
        <div className="landing-preview-body">
          <aside><span className="active" /><span /><span /><span /><span /></aside>
          <div className="landing-preview-main">
            <div className="landing-preview-heading"><span /><small /></div>
            <div className="landing-preview-metrics"><article><b>12</b><span>Recibidos sin responder</span></article><article><b>7</b><span>Enviados sin respuesta</span></article><article><b>4</b><span>Sin leer</span></article><article><b>3</b><span>Más de 48 horas</span></article></div>
            <div className="landing-preview-lower"><article><div className="landing-preview-chart"><i /><i /><i /><i /><i /><i /><i /></div></article><article className="landing-preview-list"><span /><span /><span /><span /></article></div>
          </div>
        </div>
      </div>
    </section>

    <section className="landing-section landing-value-strip" aria-label="Propuesta de valor">
      <div><strong>Unificar</strong><span>Cuentas y bandejas</span></div>
      <div><strong>Priorizar</strong><span>Lo que requiere atención</span></div>
      <div><strong>Responder</strong><span>Con menos fricción</span></div>
      <div><strong>Controlar</strong><span>Pendientes y seguimiento</span></div>
    </section>

    <section className="landing-section" id="caracteristicas">
      <div className="landing-section-heading"><span>Producto</span><h2>Correo pensado como un centro de trabajo</h2><p>NexoMail no busca ser otra bandeja de entrada. El objetivo es convertir el correo en una herramienta más clara, medible y fácil de gestionar.</p></div>
      <div className="landing-feature-grid">{features.map(feature => <article key={feature.title}><i>{feature.icon}</i><h3>{feature.title}</h3><p>{feature.description}</p></article>)}</div>
    </section>

    <section className="landing-section landing-nexi-section" id="nexi">
      <div className="landing-nexi-visual"><NexiVisual size="large" /><span>Nexi</span></div>
      <div className="landing-nexi-copy">
        <span className="landing-section-label">Asistente inteligente</span>
        <h2>Nexi entiende dónde está el trabajo pendiente</h2>
        <p>Actualmente Nexi utiliza información real de NexoMail para identificar conversaciones que requieren atención y facilitar acciones. La integración de IA ampliará esa capacidad sin reemplazar el control del usuario.</p>
        <div className="landing-nexi-capabilities">
          <span><Check size={14} /> Contexto del correo abierto</span>
          <span><Check size={14} /> Pendientes y seguimiento</span>
          <span><Sparkles size={14} /> Resumen de correos — próxima etapa</span>
          <span><Sparkles size={14} /> Redacción asistida — próxima etapa</span>
        </div>
      </div>
    </section>

    <section className="landing-section" id="servicios">
      <div className="landing-section-heading"><span>Integraciones</span><h2>Una plataforma para sus distintas cuentas</h2><p>La arquitectura está preparada para ampliar proveedores sin cambiar la experiencia central de NexoMail.</p></div>
      <div className="landing-provider-grid">
        <article className="available"><div className="provider-symbol"><Mail size={22} /></div><div><strong>Google / Gmail</strong><span>Disponible actualmente</span></div><b>Disponible</b></article>
        <article><div className="provider-symbol microsoft"><span /><span /><span /><span /></div><div><strong>Microsoft / Outlook</strong><span>Microsoft 365 y Outlook</span></div><b>Próximamente</b></article>
        <article><div className="provider-symbol"><Layers3 size={22} /></div><div><strong>IMAP / SMTP</strong><span>Otros proveedores compatibles</span></div><b>Próximamente</b></article>
      </div>
    </section>

    <section className="landing-section landing-how">
      <div className="landing-section-heading"><span>Cómo funciona</span><h2>Conectar, organizar y actuar</h2></div>
      <div className="landing-steps"><article><b>01</b><h3>Cree su cuenta</h3><p>Su usuario NexoMail mantiene separada su configuración y sus cuentas conectadas.</p></article><article><b>02</b><h3>Conecte sus correos</h3><p>Autorice las cuentas que quiera gestionar desde una sola interfaz.</p></article><article><b>03</b><h3>Trabaje desde NexoMail</h3><p>Lea, responda, haga seguimiento y consulte el Centro de Control.</p></article></div>
    </section>

    <section className="landing-section" id="modalidades">
      <div className="landing-section-heading"><span>Modalidades</span><h2>Preparado para crecer con cada tipo de usuario</h2><p>Estas modalidades definen la dirección comercial del producto. Los precios y límites se establecerán antes de la publicación comercial.</p></div>
      <div className="landing-plan-grid">{plans.map(plan => <article key={plan.name} className={plan.featured ? 'featured' : ''}>{plan.featured && <span className="landing-plan-badge">Opción principal</span>}<small>{plan.note}</small><h3>{plan.name}</h3><p>{plan.description}</p><ul>{plan.items.map(item => <li key={item}><Check size={14} />{item}</li>)}</ul><Link to="/login">Crear cuenta <ArrowRight size={15} /></Link></article>)}</div>
    </section>

    <section className="landing-section landing-security">
      <div><span className="landing-section-label">Seguridad y privacidad</span><h2>Sus correos siguen perteneciendo a sus proveedores.</h2><p>La arquitectura de NexoMail está orientada a consultar y gestionar el correo mediante las APIs de cada proveedor, manteniendo en la plataforma sólo la información necesaria para la operación y el seguimiento.</p></div>
      <div className="landing-security-points"><span><ShieldCheck size={18} /> Sesiones y aislamiento por usuario</span><span><LockKeyhole size={18} /> Protección de credenciales y tokens</span><span><Users size={18} /> Arquitectura preparada para múltiples usuarios</span></div>
    </section>

    <section className="landing-cta">
      <NexoMailLogo />
      <h2>Su correo puede ser más simple de gestionar.</h2>
      <p>Centralice cuentas, pendientes y seguimiento en un solo espacio.</p>
      <Link to="/login" className="landing-primary large">Probar NexoMail <ArrowRight size={17} /></Link>
    </section>

    <footer className="landing-footer"><NexoMailLogo /><span>Correo unificado · Control · Seguimiento · IA</span><div><a href="#caracteristicas">Características</a><a href="#servicios">Integraciones</a><Link to="/login">Acceso</Link></div></footer>
  </main>
}
