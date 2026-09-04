import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Navigate, useLocation } from 'react-router-dom'
import { authApi } from '../api/authApi'

export function RequireAuth({ children }: { children: ReactNode }) {
  const location = useLocation()
  const session = useQuery({ queryKey: ['session'], queryFn: authApi.me, retry: false, staleTime: 60_000 })

  if (session.isLoading) return <main className="auth-page"><div className="reading-skeleton auth-loading" /></main>
  if (!session.data) return <Navigate to="/login" replace state={{ from: `${location.pathname}${location.search}` }} />
  return children
}
