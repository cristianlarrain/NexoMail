import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { Archive, BookMarked, ChevronDown, ChevronLeft, Clock3, CreditCard, EyeOff, FileText, Inbox, LogOut, Menu, Moon, PenLine, Send, Settings, ShieldAlert, Sun, Trash2, UserRound } from 'lucide-react'
import { authApi } from '../api/authApi'
import { commercialApi } from '../api/commercialApi'
import { mailApi } from '../api/mailApi'
import type { MailSummary, PagedResult } from '../types/mail'
import { AppFooter } from '../components/AppFooter'
import { BackToTopButton } from '../components/BackToTopButton'
import { NexoPerspective } from '../components/NexoPerspective'
import { TopSearchBox } from '../components/TopSearchBox'
import { NexoMailLogo } from '../components/brand/NexoMailLogo'
import { NexiVisual } from '../components/nexi/NexiVisual'
import { WeatherWidget } from '../components/WeatherWidget'
import { commercialEntitlements } from '../utils/commercialEntitlements'
import { detectNexiMailAction } from '../utils/nexiSearchIntent'
import { detectNexiTrashRuleIntent } from '../utils/nexiRuleIntent'

const FOLDERS_COLLAPSED_KEY = 'nexomail-sidebar-folders-collapsed'
const navClass = ({ isActive }: { isActive: boolean }) => `nav-item ${isActive ? 'active' : ''}`
const controlCenterNavClass = ({ isActive }: { isActive: boolean }) => `nav-item primary-nav-action control-center-nav ${isActive ? 'active' : ''}`
const inboxNavClass = ({ isActive }: { isActive: boolean }) => `nav-item primary-nav-action inbox-primary-nav ${isActive ? 'active' : ''}`
function initials(value: string) { return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || 'NM' }
function capitalize(value: string) { return value.charAt(0).toUpperCase() + value.slice(1) }
function accountIdFromPath(pathname: string) {
  const accountMatch = pathname.match(/^\/account\/([^/]+)/)
  if (accountMatch) return decodeURIComponent(accountMatch[1])
  const messageMatch = pathname.match(/^\/message\/([^/]+)\/[^/]+$/)
  return messageMatch ? decodeURIComponent(messageMatch[1]) : undefined
}
function reportPeriodFromQuery(value: string): 'today' | 'this_week' | 'last_week' | null {
  const normalized = value.toLocaleLowerCase('es').normalize('NFD').replace(/[\u0300-\u036f]/g, '')
  const asksForReport = /(resumen|resume|reporte|informe)/.test(normalized) && /(correo|correos|mail|mails|mensaje|mensajes)/.test(normalized)
  if (!asksForReport) return null
  if (/semana pasada|semana anterior/.test(normalized)) return 'last_week'
  if (/esta semana|semana actual|de la semana/.test(normalized)) return 'this_week'
  return 'today'
}

export function AppLayout() {
  const [open, setOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [foldersCollapsed, setFoldersCollapsed] = useState(() => localStorage.getItem(FOLDERS_COLLAPSED_KEY) === '1')
  const [theme, setTheme] = useState(() => localStorage.getItem('nexomail-theme') ?? 'light')
  const [profileOpen, setProfileOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [now, setNow] = useState(() => new Date())
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false, staleTime: 60_000 })
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const { data: commercialSubscription } = useQuery({ queryKey: ['commercial-subscription'], queryFn: commercialApi.subscription, staleTime: 30_000 })
  const navigate = useNavigate()
  const location = useLocation()
  const logout = useMutation({ mutationFn: authApi.logout, onSuccess: () => { queryClient.clear(); navigate('/login', { replace: true }) } })

  const hasMailActions = !commercialSubscription || commercialSubscription.entitlements.includes(commercialEntitlements.mailActions)
  const hasControlCenter = !commercialSubscription || commercialSubscription.entitlements.includes(commercialEntitlements.controlCenterBasic)
  const isOwner = commercialSubscription?.effectivePlanCode === 'owner'

  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem('nexomail-theme', theme) }, [theme])
  useEffect(() => { setSearch(new URLSearchParams(location.search).get('q') ?? '') }, [location.search])
  useEffect(() => { setOpen(false); setProfileOpen(false) }, [location.pathname, location.search])
  useEffect(() => { const timer = window.setInterval(() => setNow(new Date()), 1000); return () => window.clearInterval(timer) }, [])
  useEffect(() => {
    if (!accounts.length || !(location.pathname === '/inbox' || location.pathname.startsWith('/account/'))) return
    let cancelled = false
    const timer = window.setTimeout(() => {
      const scopes: Array<string | undefined> = [undefined, ...accounts.map(account => account.id)]
      const activeScope = accountIdFromPath(location.pathname)
      void Promise.all(scopes.map(scope => queryClient.prefetchInfiniteQuery({
        queryKey: ['messages', scope, 'inbox', ''],
        queryFn: ({ pageParam }) => mailApi.messages(scope, 'inbox', '', pageParam || undefined),
        initialPageParam: '',
        getNextPageParam: lastPage => lastPage.nextCursor ?? undefined,
        pages: 1,
        staleTime: 5 * 60_000,
      }))).then(() => {
        if (cancelled) return
        const activeMessages = queryClient.getQueryData<InfiniteData<PagedResult<MailSummary>>>(['messages', activeScope, 'inbox', ''])
        const firstVisible = activeMessages?.pages[0]?.items.slice(0, 3) ?? []
        for (const item of firstVisible) {
          void queryClient.prefetchQuery({
            queryKey: ['message', item.accountId, item.providerMessageId],
            queryFn: () => mailApi.message(item.accountId, item.providerMessageId),
            staleTime: 10 * 60_000,
          })
        }
      })
    }, 650)
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [accounts, location.pathname, queryClient])

  const dateLabel = capitalize(now.toLocaleDateString('es-CL', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' }).replace(/\./g, ''))
  const timeLabel = now.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  const activeAccountId = accountIdFromPath(location.pathname)
  const contextualQueryAccount = ['/search', '/search-action', '/rules/new', '/control-center'].includes(location.pathname)
    ? new URLSearchParams(location.search).get('account') ?? undefined
    : undefined
  const contextualAccountId = activeAccountId ?? contextualQueryAccount

  function runSearch() {
    const query = search.trim()
    if (!query) {
      navigate(contextualAccountId ? `/search?account=${encodeURIComponent(contextualAccountId)}` : '/search')
      return
    }
    if (detectNexiTrashRuleIntent(query)) {
      const params = new URLSearchParams({ q: query })
      if (contextualAccountId) params.set('account', contextualAccountId)
      navigate(`/rules/new?${params.toString()}`)
      return
    }
    const reportPeriod = reportPeriodFromQuery(query)
    if (reportPeriod) {
      const params = new URLSearchParams({ tab: 'report', period: reportPeriod })
      if (contextualAccountId) params.set('account', contextualAccountId)
      navigate(`/control-center?${params.toString()}`)
      return
    }
    const params = new URLSearchParams()
    params.set('q', query)
    if (contextualAccountId) params.set('account', contextualAccountId)
    const action = detectNexiMailAction(query)
    navigate(`${action ? '/search-action' : '/search'}?${params.toString()}`)
  }

  function toggleFolders() {
    setFoldersCollapsed(current => {
      const next = !current
      localStorage.setItem(FOLDERS_COLLAPSED_KEY, next ? '1' : '0')
      return next
    })
  }

  return <div className="app-shell">
    <aside className={`sidebar ${open ? 'open' : ''} ${collapsed ? 'collapsed' : ''}`}>
      <div className="brand-row"><button className="brand-home" data-sidebar-tooltip="Inicio" onClick={() => { setOpen(false); navigate('/inbox') }} aria-label="Ir a Bandeja de entrada"><NexoMailLogo compact={collapsed} /></button><button className="icon-button collapse-button" data-sidebar-tooltip={collapsed ? 'Expandir menú' : 'Contraer menú'} onClick={() => setCollapsed(!collapsed)} aria-label={collapsed ? 'Expandir barra lateral' : 'Contraer barra lateral'}><ChevronLeft size={18} /></button></div>
      <nav aria-label="Navegación principal">
        <NavLink to="/inbox" end className={inboxNavClass} data-sidebar-tooltip="Bandeja de entrada"><span>Bandeja de Entrada</span><Inbox className="primary-nav-icon" size={15} /></NavLink>
        {hasMailActions && <button type="button" className={`compose-button primary-nav-action ${location.pathname === '/compose' ? 'active' : ''}`} data-sidebar-tooltip="Redactar" onClick={() => { setOpen(false); navigate('/compose', { state: contextualAccountId ? { fromAccountId: contextualAccountId } : undefined }) }}><span>Redactar</span><PenLine className="primary-nav-icon" size={15} /></button>}
        {hasControlCenter && <NavLink to="/control-center" className={controlCenterNavClass} data-sidebar-tooltip="Nexi Control Center"><span>Nexi Control Center</span><span className="primary-nav-icon nexi-sidebar-icon" aria-hidden="true"><NexiVisual size="small" /></span></NavLink>}
        <p className="nav-heading">Cuentas</p>
        {accounts.map(account => <NavLink key={account.id} to={`/account/${account.id}`} className={navClass} data-sidebar-tooltip={account.displayName} aria-label={`Cuenta ${account.displayName}`}><i className="account-dot" style={{ background: account.color }} /><span>{account.displayName}</span></NavLink>)}
        <button type="button" className="nav-section-toggle" data-sidebar-tooltip={foldersCollapsed ? 'Mostrar carpetas' : 'Ocultar carpetas'} onClick={toggleFolders} aria-expanded={!foldersCollapsed} aria-controls="sidebar-folders" title={foldersCollapsed ? 'Mostrar carpetas' : 'Ocultar carpetas'}>
          <span>Carpetas</span><ChevronDown size={14} className={foldersCollapsed ? 'collapsed' : ''} />
        </button>
        <div id="sidebar-folders" className={`nav-folder-group ${foldersCollapsed ? 'collapsed' : ''}`}>
          {!foldersCollapsed && <>
            <NavLink to="/archive" className={navClass} data-sidebar-tooltip="Archivados"><Archive size={17} /><span>Archivados</span></NavLink>
            <NavLink to="/ignored" className={navClass} data-sidebar-tooltip="Ignorados"><EyeOff size={17} /><span>Ignorados</span></NavLink>
            <NavLink to="/sent" className={navClass} data-sidebar-tooltip="Enviados"><Send size={17} /><span>Enviados</span></NavLink>
            <NavLink to="/drafts" className={navClass} data-sidebar-tooltip="Borradores"><FileText size={17} /><span>Borradores</span></NavLink>
            <NavLink to="/spam" className={navClass} data-sidebar-tooltip="Spam"><ShieldAlert size={17} /><span>Spam</span></NavLink>
            <NavLink to="/trash" className={navClass} data-sidebar-tooltip="Papelera"><Trash2 size={17} /><span>Papelera</span></NavLink>
          </>}
        </div>
        <NavLink to="/perspectives" className={navClass} data-sidebar-tooltip="Perspectivas"><BookMarked size={17} /><span>Perspectivas</span></NavLink>
        <NavLink to="/settings/plan" className={navClass} data-sidebar-tooltip="Plan y uso"><CreditCard size={17} /><span>Plan y uso</span></NavLink>
        {isOwner && <NavLink to="/admin/ai-usage" className={navClass} data-sidebar-tooltip="Consumo Nexi"><ShieldAlert size={17} /><span>Consumo Nexi</span></NavLink>}
        <NavLink to="/settings/accounts" className={navClass} data-sidebar-tooltip="Configuración"><Settings size={17} /><span>Configurar</span></NavLink>
        <button className="theme-switch" data-sidebar-tooltip={theme === 'dark' ? 'Tema claro' : 'Tema oscuro'} aria-label={theme === 'dark' ? 'Cambiar a tema claro' : 'Cambiar a tema oscuro'} onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}><span>{theme === 'dark' ? 'Tema claro' : 'Tema oscuro'}</span><span className="switch" data-on={theme === 'dark'} /></button>
      </nav>
    </aside>

    {open && <button className="backdrop" aria-label="Cerrar menú" onClick={() => setOpen(false)} />}

    <main className="main-content">
      <header className="topbar">
        <button className="icon-button menu-button" onClick={() => setOpen(true)} aria-label="Abrir menú"><Menu size={20} /></button>
        <TopSearchBox value={search} onChange={setSearch} onSubmit={runSearch} />
        <div className="operations-clock" aria-label={`${dateLabel}, ${timeLabel}`} title="Hora local"><Clock3 size={16} /><span className="operations-date">{dateLabel}</span><strong>{timeLabel}</strong></div>
        <WeatherWidget />
        <button className={`avatar ${session?.avatarDataUrl ? 'has-image' : ''}`} aria-label="Menú de perfil" aria-expanded={profileOpen} onClick={() => setProfileOpen(!profileOpen)}>{session?.avatarDataUrl ? <img src={session.avatarDataUrl} alt="" /> : initials(session?.displayName ?? session?.email ?? '')}</button>
        {profileOpen && <div className="profile-menu"><p><strong>{session?.displayName}</strong><br />{session?.email}</p><button onClick={() => { setProfileOpen(false); navigate('/settings/profile') }}><UserRound size={16} /> Mi perfil</button><button onClick={() => { setProfileOpen(false); navigate('/settings/plan') }}><CreditCard size={16} /> Plan y uso</button><button onClick={() => { setProfileOpen(false); navigate('/settings/accounts') }}><Settings size={16} /> Configurar cuentas</button><button onClick={() => { setTheme(theme === 'dark' ? 'light' : 'dark'); setProfileOpen(false) }}>{theme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}{theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}</button><button disabled={logout.isPending} onClick={() => logout.mutate()}><LogOut size={16} /> {logout.isPending ? 'Saliendo…' : 'Cerrar sesión'}</button></div>}
      </header>
      <div className="global-nexo-perspective"><NexoPerspective contextKey={location.pathname} /></div>
      <Outlet />
      <AppFooter />
      <BackToTopButton />
    </main>
  </div>
}
