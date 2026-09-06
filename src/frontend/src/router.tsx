import { createBrowserRouter, Navigate } from 'react-router-dom'
import { RequireAuth } from './components/RequireAuth'
import { AppLayout } from './layouts/AppLayout'
import { AccountsPage } from './pages/AccountsPage'
import { AppearancePage } from './pages/AppearancePage'
import { AuthPage } from './pages/AuthPage'
import { ComposePage } from './pages/ComposePage'
import { ControlCenterPage } from './pages/ControlCenterPage'
import { InboxPage } from './pages/InboxPage'
import { MessagePage } from './pages/MessagePage'
import { ProfilePage } from './pages/ProfilePage'

export const router = createBrowserRouter([
  { path: '/login', element: <AuthPage /> },
  {
    path: '/',
    element: <RequireAuth><AppLayout /></RequireAuth>,
    children: [
      { index: true, element: <Navigate to="/inbox" replace /> },
      { path: 'inbox', element: <InboxPage /> },
      { path: 'control-center', element: <ControlCenterPage /> },
      { path: 'account/:accountId', element: <InboxPage /> },
      { path: 'archive', element: <InboxPage folder="archive" /> },
      { path: 'ignored', element: <InboxPage folder="ignored" /> },
      { path: 'sent', element: <InboxPage folder="sent" /> },
      { path: 'drafts', element: <InboxPage folder="drafts" /> },
      { path: 'spam', element: <InboxPage folder="spam" /> },
      { path: 'trash', element: <InboxPage folder="trash" /> },
      { path: 'message/:accountId/:messageId', element: <MessagePage /> },
      { path: 'compose', element: <ComposePage /> },
      { path: 'settings', element: <Navigate to="/settings/accounts" replace /> },
      { path: 'settings/accounts', element: <AccountsPage /> },
      { path: 'settings/profile', element: <ProfilePage /> },
      { path: 'settings/appearance', element: <AppearancePage /> },
    ],
  },
])
