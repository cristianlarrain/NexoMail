function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

const EMAIL_PATTERN = /[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/i
const GMAIL_OPERATOR_PATTERN = /(?:^|\s)(?:from|to|cc|bcc|subject|label|in|is|has|larger|smaller|after|before|newer|older|filename):/i

export function detectNexiTrashRuleIntent(query: string) {
  const value = normalized(query)
  const asksForRule = /\b(?:regla|filtro|automaticamente|automatico|automatica)\b/.test(value)
  const trashDestination = /\b(?:papelera|eliminados|eliminado|borrados|borrado|trash)\b/.test(value)
  const creationVerb = /\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir)\b/.test(value)
  const conditionalTrigger = /\b(?:cuando|cada vez que|al recibir|si llega|si llegan|entren|lleguen|llegue|reciba|recibas)\b/.test(value)
  const moveVerb = /\b(?:envia|envie|envien|mueve|mueva|muevan|manda|mande|manden|pasa|pase|pasen)\b/.test(value)

  return trashDestination && ((asksForRule && creationVerb) || (conditionalTrigger && moveVerb))
}

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

export function extractTrashRuleQuery(instruction: string) {
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

  const sender = value.match(/\b(?:correos?\s+(?:de|del)|mensajes?\s+(?:de|del)|remitente|desde)\s+(.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:carpeta\s+de\s+)?(?:papelera|eliminados|trash)|$)/i)
  if (sender?.[1]) return `from:${quoteIfNeeded(sender[1])}`

  const subject = value.match(/\basunto\s+(?:sea|es|contenga|contiene|con)?\s*(.+?)(?=\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:carpeta\s+de\s+)?(?:papelera|eliminados|trash)|$)/i)
  if (subject?.[1]) return `subject:${quoteIfNeeded(subject[1])}`

  const quoted = value.match(/[“"]([^”"]{2,500})[”"]/)
  if (quoted?.[1]) return quoted[1].trim()

  const betweenMailAndDestination = value.match(/\bcorreos?\s+(.+?)\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:papelera|eliminados|trash)\b/i)
  if (betweenMailAndDestination?.[1]) return cleanExtracted(betweenMailAndDestination[1])

  const fromPattern = value.match(/\b(?:de|desde)\s+(.+?)\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:papelera|eliminados|trash)\b/i)
  if (fromPattern?.[1]) return cleanExtracted(fromPattern[1])

  return value
    .replace(/\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir|necesito)\b/gi, ' ')
    .replace(/\b(?:una|un|la|el)\s+(?:regla|filtro)\b/gi, ' ')
    .replace(/\bpara\s+que\b|\bpara\s+cuando\b|\bcuando\s+(?:entren|lleguen|llegue|entre|reciba|recibas)\b/gi, ' ')
    .replace(/\b(?:todos?|todas?)\s+(?:los|las)\s+(?:correos?|mensajes?)\b/gi, ' ')
    .replace(/\b(?:se\s+)?(?:vayan|vaya|envien|envíen|envie|envíe|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:carpeta\s+de\s+)?(?:papelera(?:\s+de\s+reciclaje)?|eliminados|trash)\b/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}
