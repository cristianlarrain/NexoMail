import { createBrowserRouter, Navigate } from 'react-router-dom'
import { RequireAuth } from './components/RequireAuth'
import { AppLayout } from './layouts/AppLayout'
import { AccountsPage } from './pages/AccountsPage'
import { AppearancePage } from './pages/AppearancePage'
import { AuthPage } from './pages/AuthPage'
import { ComposePage } from './pages/ComposePage'
import { InboxPage } from './pages/InboxPage'
import { MessagePage } from './pages/MessagePage'

export const router = createBrowserRouter([
  { path: '/login', element: <AuthPage /> },
  {
    path: '/',
    element: <RequireAuth><AppLayout /></RequireAuth>,
    children: [
      { index: true, element: <Navigate to="/inbox" replace /> },
      { path: 'inbox', element: <InboxPage /> },
      { path: 'account/:accountId', element: <InboxPage /> },
      { path: 'sent', element: <InboxPage folder="sent" /> },
      { path: 'drafts', element: <InboxPage folder="drafts" /> },
      { path: 'trash', element: <InboxPage folder="trash" /> },
      { path: 'message/:accountId/:messageId', element: <MessagePage /> },
      { path: 'compose', element: <ComposePage /> },
      { path: 'settings', element: <Navigate to="/settings/accounts" replace /> },
      { path: 'settings/accounts', element: <AccountsPage /> },
      { path: 'settings/appearance', element: <AppearancePage /> },
    ],
  },
])
