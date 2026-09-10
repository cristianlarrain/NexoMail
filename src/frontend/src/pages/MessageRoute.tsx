import { useQuery } from '@tanstack/react-query'
import { Navigate, useLocation, useParams } from 'react-router-dom'
import { mailApi } from '../api/mailApi'
import { MessageNexiReaderTools } from '../components/MessageNexiReaderTools'
import { MessagePage } from './MessagePage'

type MessageRouteState = { returnTo?: string }

export function MessageRoute() {
  const { accountId = '', messageId = '' } = useParams()
  const location = useLocation()
  const { data: message, isLoading } = useQuery({
    queryKey: ['message', accountId, messageId],
    queryFn: () => mailApi.message(accountId, messageId),
    enabled: Boolean(accountId && messageId),
    staleTime: 10 * 60_000,
    gcTime: 30 * 60_000,
    refetchOnMount: false,
    refetchOnWindowFocus: false,
  })

  if (isLoading || !message) return <section className="mail-view"><div className="reading-skeleton" /></section>

  if (message.folderId === 'drafts') {
    const routeState = location.state as MessageRouteState | null
    return <Navigate
      to="/compose"
      replace
      state={{
        mode: 'editDraft',
        message,
        returnTo: routeState?.returnTo ?? '/drafts',
      }}
    />
  }

  return <>
    <MessageNexiReaderTools accountId={accountId} messageId={messageId} />
    <MessagePage />
  </>
}
