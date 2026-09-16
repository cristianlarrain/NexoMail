import { readFile } from 'node:fs/promises'

async function source(path) { return readFile(new URL(`../${path}`, import.meta.url), 'utf8') }
function ensure(condition, message) { if (!condition) throw new Error(message) }
const [mailApi, inbox] = await Promise.all([source('src/api/mailApi.ts'), source('src/pages/InboxPage.tsx')])
ensure(mailApi.includes('export class MailApiError') && mailApi.includes('readonly status') && mailApi.includes('readonly problem'), 'mailApi debe conservar status y payload estructurado de errores HTTP.')
ensure(mailApi.includes('export function unifiedInboxReadiness') && mailApi.includes('gmailAccountStates'), 'mailApi debe reconocer explícitamente el 409 de readiness de la Bandeja Unificada.')
ensure(inbox.includes('retry: (failureCount, error) => !unifiedInboxReadiness(error)') && inbox.includes('refetchInterval: query => unifiedInboxReadiness(query.state.error)'), 'Inbox no debe reintentar rápidamente un 409 de readiness y debe comprobar el progreso de forma periódica.')
ensure(inbox.includes('Sincronizando historial') && inbox.includes('Comprobar estado') && inbox.includes('indexedMessageCount'), 'Inbox debe mostrar el progreso de sincronización por cuenta en lugar del error genérico.')
ensure(inbox.includes('!readinessProblem && displayItems.length === 0'), 'Inbox no debe mostrar un estado vacío mientras el índice todavía se está preparando.')
console.log('PASS: Unified Inbox trata readiness como sincronización en progreso')
