import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { Archive, ChevronLeft, Clock3, EyeOff, FileText, Inbox, LayoutDashboard, LogOut, Menu, Moon, PenLine, Search, Send, Settings, ShieldAlert, Sun, Trash2, UserRound } from 'lucide-react'
import { authApi } from '../api/authApi'
import { mailApi } from '../api/mailApi'
import type { MailSummary, PagedResult } from '../types/mail'
import { BackToTopButton } from '../components/BackToTopButton'
import { NexoMailLogo } from '../components/brand/NexoMailLogo'
import { NexiAssistantButton } from '../components/nexi/NexiAssistantButton'
import { NexiAssistantPanel } from '../components/nexi/NexiAssistantPanel'
import { WeatherWidget } from '../components/WeatherWidget'

const navClass = ({ isActive }: { isActive: boolean }) => `nav-item ${isActive ? 'active' : ''}`
const controlCenterNavClass = ({ isActive }: { isActive: boolean }) => `nav-item control-center-nav ${isActive ? 'active' : ''}`
function initials(value: string) { return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || 'NM' }
function capitalize(value: string) { return value.charAt(0).toUpperCase() + value.slice(1) }
function accountIdFromPath(pathname: string) {
  const accountMatch = pathname.match(/^\/account\/([^/]+)/)
  if (accountMatch) return decodeURIComponent(accountMatch[1])
  const messageMatch = pathname.match(/^\/message\/([^/]+)\/[^/]+$/)
  return messageMatch ? decodeURIComponent(messageMatch[1]) : undefined
}

export function AppLayout() {
  const [open, setOpen] = useState(false)
  const [collapsed, setCollapsed] = useState(false)
  const [theme, setTheme] = useState(() => localStorage.getItem('nexomail-theme') ?? 'light')
  const [profileOpen, setProfileOpen] = useState(false)
  const [nexiOpen, setNexiOpen] = useState(false)
  const [search, setSearch] = useState('')
  const [now, setNow] = useState(() => new Date())
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false, staleTime: 60_000 })
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const navigate = useNavigate()
  const location = useLocation()
  const logout = useMutation({ mutationFn: authApi.logout, onSuccess: () => { queryClient.clear(); navigate('/login', { replace: true }) } })

  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem('nexomail-theme', theme) }, [theme])
  useEffect(() => { setSearch(new URLSearchParams(location.search).get('q') ?? '') }, [location.search])
  useEffect(() => { setNexiOpen(false) }, [location.pathname])
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
  const searchAccountId = location.pathname === '/search' ? new URLSearchParams(location.search).get('account') ?? undefined : undefined
  const contextualAccountId = activeAccountId ?? searchAccountId

  function runSearch() {
    const query = search.trim()
    if (!query) {
      navigate(contextualAccountId ? `/search?account=${encodeURIComponent(contextualAccountId)}` : '/search')
      return
    }
    const params = new URLSearchParams()
    params.set('q', query)
    if (contextualAccountId) params.set('account', contextualAccountId)
    navigate(`/search?${params.toString()}`)
  }

  return <div className="app-shell">
    <aside className={`sidebar ${open ? 'open' : ''} ${collapsed ? 'collapsed' : ''}`}>
      <div className="brand-row"><button className="brand-home" onClick={() => { setOpen(false); navigate('/inbox') }} aria-label="Ir a Bandeja de entrada"><NexoMailLogo compact={collapsed} /></button><button className="icon-button collapse-button" onClick={() => setCollapsed(!collapsed)} aria-label="Contraer barra lateral"><ChevronLeft size={18} /></button></div>
      <button className="compose-button" onClick={() => { setOpen(false); navigate('/compose', { state: contextualAccountId ? { fromAccountId: contextualAccountId } : undefined }) }}><PenLine size={17} /><span>Redactar</span></button>
      <nav aria-label="Navegación principal">
        <NavLink to="/inbox" end className={navClass}><Inbox size={17} /><span>Bandeja de entrada</span></NavLink>
        <NavLink to="/control-center" className={controlCenterNavClass}><i className="control-center-nav-icon"><LayoutDashboard size={16} /></i><span>Centro de control</span></NavLink>
        <p className="nav-heading">Cuentas</p>
        {accounts.map(account => <NavLink key={account.id} to={`/account/${account.id}`} className={navClass}><i className="account-dot" style={{ background: account.color }} /><span>{account.displayName}</span></NavLink>)}
        <p className="nav-heading">Carpetas</p>
        <NavLink to="/archive" className={navClass}><Archive size={17} /><span>Archivados</span></NavLink>
        <NavLink to="/ignored" className={navClass}><EyeOff size={17} /><span>Ignorados</span></NavLink>
        <NavLink to="/sent" className={navClass}><Send size={17} /><span>Enviados</span></NavLink>
        <NavLink to="/drafts" className={navClass}><FileText size={17} /><span>Borradores</span></NavLink>
        <NavLink to="/spam" className={navClass}><ShieldAlert size={17} /><span>Spam</span></NavLink>
        <NavLink to="/trash" className={navClass}><Trash2 size={17} /><span>Papelera</span></NavLink>
        <NavLink to="/settings/accounts" className={navClass}><Settings size={17} /><span>Configurar</span></NavLink>
        <button className="theme-switch" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}><span>{theme === 'dark' ? 'Tema claro' : 'Tema oscuro'}</span><span className="switch" data-on={theme === 'dark'} /></button>
      </nav>
    </aside>

    {open && <button className="backdrop" aria-label="Cerrar menú" onClick={() => setOpen(false)} />}

    <main className="main-content">
      <header className="topbar">
        <button className="icon-button menu-button" onClick={() => setOpen(true)} aria-label="Abrir menú"><Menu size={20} /></button>
        <label className="search" title="Busca correos, contactos y documentos con lenguaje normal"><Search size={17} /><input value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') runSearch() }} placeholder="Busca correos, contactos, documentos o pregúntale a Nexi" aria-label="Buscar en NexoMail" /></label>
        <div className="operations-clock" aria-label={`${dateLabel}, ${timeLabel}`} title="Hora local"><Clock3 size={16} /><span className="operations-date">{dateLabel}</span><strong>{timeLabel}</strong></div>
        <WeatherWidget />
        <button className={`avatar ${session?.avatarDataUrl ? 'has-image' : ''}`} aria-label="Menú de perfil" aria-expanded={profileOpen} onClick={() => setProfileOpen(!profileOpen)}>{session?.avatarDataUrl ? <img src={session.avatarDataUrl} alt="" /> : initials(session?.displayName ?? session?.email ?? '')}</button>
        {profileOpen && <div className="profile-menu"><p><strong>{session?.displayName}</strong><br />{session?.email}</p><button onClick={() => { setProfileOpen(false); navigate('/settings/profile') }}><UserRound size={16} /> Mi perfil</button><button onClick={() => { setProfileOpen(false); navigate('/settings/accounts') }}><Settings size={16} /> Configurar cuentas</button><button onClick={() => { setTheme(theme === 'dark' ? 'light' : 'dark'); setProfileOpen(false) }}>{theme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}{theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}</button><button disabled={logout.isPending} onClick={() => logout.mutate()}><LogOut size={16} /> {logout.isPending ? 'Saliendo…' : 'Cerrar sesión'}</button></div>}
      </header>
      <Outlet />
      <BackToTopButton />
    </main>

    <NexiAssistantButton open={nexiOpen} onClick={() => { setProfileOpen(false); setNexiOpen(current => !current) }} />
    <NexiAssistantPanel open={nexiOpen} onClose={() => setNexiOpen(false)} />
  </div>
}
