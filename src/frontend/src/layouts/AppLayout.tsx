import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, Clock3, FileText, Inbox, LogOut, Mail, Menu, Moon, PenLine, Search, Send, Settings, Sun, Trash2, UserRound } from 'lucide-react'
import { authApi } from '../api/authApi'
import { mailApi } from '../api/mailApi'
import { BackToTopButton } from '../components/BackToTopButton'

const navClass = ({ isActive }: { isActive: boolean }) => `nav-item ${isActive ? 'active' : ''}`
function initials(value: string) { return value.split(/\s+/).filter(Boolean).slice(0, 2).map(part => part[0]?.toUpperCase()).join('') || 'NM' }
function capitalize(value: string) { return value.charAt(0).toUpperCase() + value.slice(1) }

export function AppLayout() {
  const [open, setOpen] = useState(false); const [collapsed, setCollapsed] = useState(false); const [theme, setTheme] = useState(() => localStorage.getItem('nexomail-theme') ?? 'light'); const [profileOpen, setProfileOpen] = useState(false); const [search, setSearch] = useState(''); const [now, setNow] = useState(() => new Date())
  const queryClient = useQueryClient()
  const { data: session } = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false, staleTime: 60_000 })
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const navigate = useNavigate(); const location = useLocation()
  const logout = useMutation({ mutationFn: authApi.logout, onSuccess: () => { queryClient.clear(); navigate('/login', { replace: true }) } })
  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem('nexomail-theme', theme) }, [theme])
  useEffect(() => { setSearch(new URLSearchParams(location.search).get('q') ?? '') }, [location.search])
  useEffect(() => { const timer = window.setInterval(() => setNow(new Date()), 1000); return () => window.clearInterval(timer) }, [])
  const dateLabel = capitalize(now.toLocaleDateString('es-CL', { weekday: 'short', day: '2-digit', month: 'short', year: 'numeric' }).replace(/\./g, ''))
  const timeLabel = now.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
  return <div className="app-shell">
    <aside className={`sidebar ${open ? 'open' : ''} ${collapsed ? 'collapsed' : ''}`}>
      <div className="brand-row"><button className="brand-home" onClick={() => { setOpen(false); navigate('/inbox') }} aria-label="Ir a Bandeja de entrada"><span className="brand-mark"><Mail size={18} /></span><span className="brand-name">NexoMail</span></button><button className="icon-button collapse-button" onClick={() => setCollapsed(!collapsed)} aria-label="Contraer barra lateral"><ChevronLeft size={18} /></button></div>
      <button className="compose-button" onClick={() => navigate('/compose')}><PenLine size={17} /><span>Redactar</span></button>
      <nav aria-label="Navegación principal">
        <NavLink to="/inbox" end className={navClass}><Inbox size={17} /><span>Todas</span></NavLink>
        <p className="nav-heading">Cuentas</p>
        {accounts.map(account => <NavLink key={account.id} to={`/account/${account.id}`} className={navClass}><i className="account-dot" style={{ background: account.color }} /><span>{account.displayName}</span></NavLink>)}
        <p className="nav-heading">Carpetas</p>
        <NavLink to="/inbox" className={navClass}><Inbox size={17} /><span>Bandeja</span></NavLink>
        <NavLink to="/sent" className={navClass}><Send size={17} /><span>Enviados</span></NavLink>
        <NavLink to="/drafts" className={navClass}><FileText size={17} /><span>Borradores</span></NavLink>
        <NavLink to="/trash" className={navClass}><Trash2 size={17} /><span>Papelera</span></NavLink>
        <NavLink to="/settings/accounts" className={navClass}><Settings size={17} /><span>Configurar</span></NavLink>
        <button className="theme-switch" onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}><span>{theme === 'dark' ? 'Tema claro' : 'Tema oscuro'}</span><span className="switch" data-on={theme === 'dark'} /></button>
      </nav>
    </aside>
    {open && <button className="backdrop" aria-label="Cerrar menú" onClick={() => setOpen(false)} />}
    <main className="main-content"><header className="topbar"><button className="icon-button menu-button" onClick={() => setOpen(true)} aria-label="Abrir menú"><Menu size={20} /></button><label className="search"><Search size={17} /><input value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') navigate(search.trim() ? `/inbox?q=${encodeURIComponent(search.trim())}` : '/inbox') }} placeholder="Buscar correos y presionar Enter" aria-label="Buscar correos" /></label><div className="operations-clock" aria-label={`${dateLabel}, ${timeLabel}`} title="Hora local"><Clock3 size={16} /><span className="operations-date">{dateLabel}</span><strong>{timeLabel}</strong></div><button className={`avatar ${session?.avatarDataUrl ? 'has-image' : ''}`} aria-label="Menú de perfil" aria-expanded={profileOpen} onClick={() => setProfileOpen(!profileOpen)}>{session?.avatarDataUrl ? <img src={session.avatarDataUrl} alt="" /> : initials(session?.displayName ?? session?.email ?? '')}</button>{profileOpen && <div className="profile-menu"><p><strong>{session?.displayName}</strong><br />{session?.email}</p><button onClick={() => { setProfileOpen(false); navigate('/settings/profile') }}><UserRound size={16} /> Mi perfil</button><button onClick={() => { setProfileOpen(false); navigate('/settings/accounts') }}><Settings size={16} /> Configurar cuentas</button><button onClick={() => { setTheme(theme === 'dark' ? 'light' : 'dark'); setProfileOpen(false) }}>{theme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}{theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}</button><button disabled={logout.isPending} onClick={() => logout.mutate()}><LogOut size={16} /> {logout.isPending ? 'Saliendo…' : 'Cerrar sesión'}</button></div>}</header><Outlet /><BackToTopButton /></main>
  </div>
}
