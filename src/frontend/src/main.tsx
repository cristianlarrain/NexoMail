import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router-dom'
import { router } from './router'
import './styles/theme.css'
import './styles/overrides.css'
import './styles/mail-enhancements.css'
import './styles/confirm-dialog.css'
import './styles/auth.css'

const client = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000, retry: 1 } } })
createRoot(document.getElementById('root')!).render(<StrictMode><QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider></StrictMode>)
