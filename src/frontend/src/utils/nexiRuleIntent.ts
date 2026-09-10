import type { RuleAction } from '../api/ruleApi'

function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

const EMAIL_PATTERN = /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i
const GMAIL_OPERATOR_PATTERN = /(?:^|\s)(?:from|to|cc|bcc|subject|label|in|is|has|larger|smaller|after|before|newer|older|filename):/i

export function detectNexiRuleIntent(query: string) {
  const value = normalized(query)
  const asksForRule = /\b(?:regla|filtro|automaticamente|automatico|automatica|automatiza|automatizar)\b/.test(value)
  const creationVerb = /\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir|necesito)\b/.test(value)
  const recurringPattern = /\b(?:cuando|cada vez que|al recibir|si llega|si llegan)\b/.test(value)
  const hasAction = /\b(?:papelera|eliminados|trash|archiva|archivar|archivados|marca|marcar|leido|leidos|mueve|mover|envia|enviar|manda|mandar|carpeta|etiqueta)\b/.test(value)
  return hasAction && (asksForRule || recurringPattern || creationVerb && /\b(?:regla|filtro)\b/.test(value))
}

export const detectNexiTrashRuleIntent = detectNexiRuleIntent

function cleanExtracted(value: string) {
  return value
    .trim()
    .replace(/^[,:;\-–—\s]+|[,:;\-–—\s]+$/g, '')
    .replace(/^(?:todos?|todas?)\s+(?:los|las)\s+/i, '')
    .replace(/^(?:los|las)\s+/i, '')
    .replace(/^de\s+/i, '')
    .replace(/^[“”"'‘’]+|[“”"'‘’]+$/g, '')
    .trim()
}

function quoteIfNeeded(value: string) {
  const cleaned = cleanExtracted(value)
  if (!cleaned) return ''
  if (/^\(.+\)$/.test(cleaned) || /^".+"$/.test(cleaned)) return cleaned
  return /\s/.test(cleaned) ? `"${cleaned.replace(/"/g, '\\"')}"` : cleaned
}

export function inferRuleAction(instruction: string): RuleAction {
  const value = normalized(instruction)
  if (/\b(?:papelera|eliminados|trash|borrar|eliminar)\b/.test(value)) return 'trash'
  if (/\b(?:archiva|archivar|archivados|archivo)\b/.test(value)) return 'archive'
  if (/\b(?:marca|marcar)\b.*\b(?:leido|leidos)\b/.test(value)) return 'markRead'
  if (/\b(?:mueve|mover|envia|enviar|manda|mandar|pasa|pasar)\b/.test(value)) return 'moveToFolder'
  return 'trash'
}

export function extractRuleDestination(instruction: string) {
  if (inferRuleAction(instruction) !== 'moveToFolder') return ''
  const match = instruction.match(/\b(?:mueve|mover|env[ií]a|enviar|manda|mandar|pasa|pasar).*?\s+(?:a|hacia)\s+(?:la\s+)?(?:carpeta\s+|etiqueta\s+)?[“"']?([^”"']+?)[”"']?(?:\.|$)/i)
  return match?.[1] ? cleanExtracted(match[1]) : ''
}

export function extractRuleQuery(instruction: string) {
  const value = instruction.trim()
  if (!value) return ''
  if (GMAIL_OPERATOR_PATTERN.test(value)) return value

  const email = value.match(EMAIL_PATTERN)
  if (email?.[0]) {
    const index = email.index ?? 0
    const before = value.slice(0, index)
    const recipientContext = /\b(?:destinatari[oa]s?|dirigid[oa]s?\s+a|enviad[oa]s?\s+a)\s*$/i.test(before)
    return `${recipientContext ? 'to' : 'from'}:${email[0]}`
  }

  const sender = value.match(/\b(?:correos?\s+(?:de|del)|mensajes?\s+(?:de|del)|remitente|desde)\s+(.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase|archiven|archive|marquen|marque)\b|$)/i)
  if (sender?.[1]) return `from:${quoteIfNeeded(sender[1])}`

  const subject = value.match(/\basunto\s+(?:sea|es|contenga|contiene|con)?\s*(.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase|archiven|archive|marquen|marque)\b|$)/i)
  if (subject?.[1]) return `subject:${quoteIfNeeded(subject[1])}`

  const quoted = value.match(/[“"]([^”"]{2,500})[”"]/)
  if (quoted?.[1]) return quoted[1].trim()

  const betweenMailAndAction = value.match(/\bcorreos?\s+(.+?)\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase|archiven|archive|marquen|marque)\b/i)
  if (betweenMailAndAction?.[1]) return cleanExtracted(betweenMailAndAction[1])

  return value
    .replace(/\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir|necesito)\b/gi, ' ')
    .replace(/\b(?:una|un|la|el)\s+(?:regla|filtro)\b/gi, ' ')
    .replace(/\bpara\s+(?:que|cuando)\b/gi, ' ')
    .replace(/\b(?:cuando|cada vez que)\s+(?:entren|entre|lleguen|llegue|reciba|recibas)\b/gi, ' ')
    .replace(/\b(?:todos?|todas?)\s+(?:los|las)\s+(?:correos?|mensajes?)\b/gi, ' ')
    .replace(/\b(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase|archiven|archive|marquen|marque)\b[\s\S]*$/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

export const extractTrashRuleQuery = extractRuleQuery
