export interface NexiContextBreakdownItem {
  label: string
  count: number
}

export interface NexiContextDayItem {
  date: string
  count: number
}

export interface NexiContextResponse {
  query: string
  messageCount: number
  analyzedCount: number
  answer: string
  senders: NexiContextBreakdownItem[]
  days: NexiContextDayItem[]
}
