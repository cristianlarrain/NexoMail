import { readFile } from 'node:fs/promises'

async function source(path) { return readFile(new URL(`../${path}`, import.meta.url), 'utf8') }
function ensure(condition, message) { if (!condition) throw new Error(message) }

const [mailApi, inbox] = await Promise.all([
  source('src/api/mailApi.ts'),
  source('src/pages/InboxPage.tsx'),
])

ensure(
  mailApi.includes("resolveMessages: (references: Array<{ accountId: string; providerMessageId: string }>)") &&
  mailApi.includes("api<MailSummary[]>('/mail/messages/resolve'") &&
  mailApi.includes("method: 'POST'") &&
  mailApi.includes('body: JSON.stringify(references)'),
  'mailApi debe resolver referencias prioritarias contra /mail/messages/resolve.',
)

ensure(
  inbox.includes('mailApi.resolveMessages'),
  'La vista prioritaria debe resolver mensajes reales desde el índice.',
)

ensure(
  !inbox.includes('senderName: reference.counterpart') &&
  !inbox.includes("preview: reference.conversationId.startsWith('manual:')"),
  'La vista prioritaria todavía fabrica MailSummary fuera del índice.',
)

ensure(
  inbox.includes('mailApi.messages(accountId, folder, search'),
  'Las vistas ordinarias deben seguir leyendo desde mailApi.messages.',
)

ensure(
  inbox.includes('unifiedInboxReadiness') && inbox.includes('Sincronizando historial'),
  'Task 8 no debe eliminar la UX de readiness de la Bandeja Unificada.',
)

console.log('PASS: prioridad resuelve MailSummary reales desde la Bandeja Unificada')
