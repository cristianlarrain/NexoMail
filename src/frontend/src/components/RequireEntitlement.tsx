import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { LockKeyhole } from 'lucide-react'
import { commercialApi } from '../api/commercialApi'

interface RequireEntitlementProps {
  entitlement: string
  label: string
  children: ReactNode
}

export function RequireEntitlement({ entitlement, label, children }: RequireEntitlementProps) {
  const subscription = useQuery({
    queryKey: ['commercial-subscription'],
    queryFn: commercialApi.subscription,
    staleTime: 30_000,
  })

  if (subscription.isLoading) {
    return <section className="settings-page"><div className="settings-card">Comprobando funciones del plan…</div></section>
  }

  if (!subscription.data?.entitlements.includes(entitlement)) {
    return <section className="settings-page">
      <div className="settings-card commercial-feature-lock">
        <LockKeyhole size={22} aria-hidden="true" />
        <div>
          <h2>{label} no está incluido en su plan</h2>
          <p>Puede revisar las funciones disponibles o cambiar de plan desde Plan y uso.</p>
          <Link className="button primary" to="/settings/plan">Ver Plan y uso</Link>
        </div>
      </div>
    </section>
  }

  return <>{children}</>
}
