import { useSearchParams } from 'react-router-dom'
import { detectNexiMailPlan } from '../utils/nexiSearchIntent'
import { NexiActionPlanPage } from './NexiActionPlanPage'
import { NexiSearchActionPage } from './NexiSearchActionPage'

export function NexiSearchActionRoute() {
  const [params] = useSearchParams()
  const query = params.get('q')?.trim() ?? ''
  const plan = detectNexiMailPlan(query)

  return plan.length > 1 ? <NexiActionPlanPage /> : <NexiSearchActionPage />
}
