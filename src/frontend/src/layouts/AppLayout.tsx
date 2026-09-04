import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, FileText, Inbox, Mail, Menu, Moon, PenLine, Search, Send, Settings, Sun, Trash2 } from 'lucide-react'
import { mailApi } from '../api/mailApi'

const navClass = ({ isActive }: { isActive: boolean }) => `nav-item ${isActive ? 'active' : ''}`
export function AppLayout() {
  const [open, setOpen] = useState(false); const [collapsed, setCollapsed] = useState(false); const [theme, setTheme] = useState(() => localStorage.getItem('nexomail-theme') ?? 'light'); const [profileOpen, setProfileOpen] = useState(false); const [search, setSearch] = useState('')
  const { data: accounts = [] } = useQuery({ queryKey: ['accounts'], queryFn: mailApi.accounts })
  const navigate = useNavigate(); const location = useLocation()
  useEffect(() => { document.documentElement.dataset.theme = theme; localStorage.setItem('nexomail-theme', theme) }, [theme])
  useEffect(() => { setSearch(new URLSearchParams(location.search).get('q') ?? '') }, [location.search])
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
    <main className="main-content"><header className="topbar"><button className="icon-button menu-button" onClick={() => setOpen(true)} aria-label="Abrir menú"><Menu size={20} /></button><label className="search"><Search size={17} /><input value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') navigate(search.trim() ? `/inbox?q=${encodeURIComponent(search.trim())}` : '/inbox') }} placeholder="Buscar correos y presionar Enter" aria-label="Buscar correos" /></label><button className="avatar" aria-label="Menú de perfil" aria-expanded={profileOpen} onClick={() => setProfileOpen(!profileOpen)}>CR</button>{profileOpen && <div className="profile-menu"><p>Perfil local</p><button onClick={() => { setProfileOpen(false); navigate('/settings/accounts') }}><Settings size={16} /> Configurar cuentas</button><button onClick={() => { setTheme(theme === 'dark' ? 'light' : 'dark'); setProfileOpen(false) }}>{theme === 'dark' ? <Sun size={16} /> : <Moon size={16} />}{theme === 'dark' ? 'Usar tema claro' : 'Usar tema oscuro'}</button></div>}</header><Outlet /></main>
  </div>
}
