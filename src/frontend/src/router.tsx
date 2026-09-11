import { createBrowserRouter, Navigate } from 'react-router-dom'
import { RequireAuth } from './components/RequireAuth'
import { RequireEntitlement } from './components/RequireEntitlement'
import { AppLayout } from './layouts/AppLayout'
import { PublicLayout } from './layouts/PublicLayout'
import { AccountsPage } from './pages/AccountsPage'
import { AdminPlansPage } from './pages/AdminPlansPage'
import { AdminUsersPage } from './pages/AdminUsersPage'
import { AppearancePage } from './pages/AppearancePage'
import { AuthPage } from './pages/AuthPage'
import { ComposePage } from './pages/ComposePage'
import { ControlCenterPage } from './pages/ControlCenterPage'
import { InboxPage } from './pages/InboxPage'
import { LandingPage } from './pages/LandingPage'
import { LegalPage } from './pages/LegalPage'
import { MailRulePage } from './pages/MailRulePage'
import { MessageRoute } from './pages/MessageRoute'
import { NexiSearchActionRoute } from './pages/NexiSearchActionRoute'
import { PerspectivesPage } from './pages/PerspectivesPage'
import { PlanPage } from './pages/PlanPage'
import { ProfilePage } from './pages/ProfilePage'
import { RulesPage } from './pages/RulesPage'
import { SearchPage } from './pages/SearchPage'
import { commercialEntitlements } from './utils/commercialEntitlements'

export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { path: '/', element: <LandingPage /> },
      { path: '/login', element: <AuthPage /> },
      { path: '/legal/terms', element: <LegalPage document="terms" /> },
      { path: '/legal/privacy', element: <LegalPage document="privacy" /> },
      { path: '/legal/security', element: <LegalPage document="security" /> },
    ],
  },
  {
    element: <RequireAuth><AppLayout /></RequireAuth>,
    children: [
      { path: '/inbox', element: <RequireEntitlement entitlement={commercialEntitlements.unifiedMail} label="Bandeja unificada"><InboxPage /></RequireEntitlement> },
      { path: '/search', element: <RequireEntitlement entitlement={commercialEntitlements.unifiedMail} label="Bandeja unificada"><SearchPage /></RequireEntitlement> },
      { path: '/search-action', element: <RequireEntitlement entitlement={commercialEntitlements.nexiAi} label="Nexi e IA"><NexiSearchActionRoute /></RequireEntitlement> },
      { path: '/rules/new', element: <RequireEntitlement entitlement={commercialEntitlements.mailActions} label="Acciones de correo"><MailRulePage /></RequireEntitlement> },
      { path: '/control-center', element: <RequireEntitlement entitlement={commercialEntitlements.controlCenterBasic} label="Centro de Control"><ControlCenterPage /></RequireEntitlement> },
      { path: '/perspectives', element: <PerspectivesPage /> },
      { path: '/account/:accountId', element: <InboxPage /> },
      { path: '/archive', element: <InboxPage folder="archive" /> },
      { path: '/ignored', element: <InboxPage folder="ignored" /> },
      { path: '/sent', element: <InboxPage folder="sent" /> },
      { path: '/drafts', element: <InboxPage folder="drafts" /> },
      { path: '/spam', element: <InboxPage folder="spam" /> },
      { path: '/trash', element: <InboxPage folder="trash" /> },
      { path: '/message/:accountId/:messageId', element: <MessageRoute /> },
      { path: '/compose', element: <RequireEntitlement entitlement={commercialEntitlements.mailActions} label="Redactar y enviar"><ComposePage /></RequireEntitlement> },
      { path: '/settings', element: <Navigate to="/settings/accounts" replace /> },
      { path: '/settings/accounts', element: <AccountsPage /> },
      { path: '/settings/rules', element: <RequireEntitlement entitlement={commercialEntitlements.mailActions} label="Acciones de correo"><RulesPage /></RequireEntitlement> },
      { path: '/settings/profile', element: <ProfilePage /> },
      { path: '/settings/appearance', element: <AppearancePage /> },
      { path: '/settings/plan', element: <PlanPage /> },
      { path: '/admin/plans', element: <AdminPlansPage /> },
      { path: '/admin/users', element: <AdminUsersPage /> },
    ],
  },
])
