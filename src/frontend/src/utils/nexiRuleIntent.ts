function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

export function detectNexiTrashRuleIntent(query: string) {
  const value = normalized(query)
  const asksForRule = /\b(?:regla|filtro|automaticamente|automatico|automatica)\b/.test(value)
  const trashDestination = /\b(?:papelera|eliminados|eliminado|borrados|borrado|trash)\b/.test(value)
  const creationVerb = /\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir)\b/.test(value)
  return asksForRule && trashDestination && creationVerb
}

function cleanExtracted(value: string) {
  return value
    .trim()
    .replace(/^[,:;\-–—\s]+|[,:;\-–—\s]+$/g, '')
    .replace(/^(?:todos?|todas?)\s+(?:los|las)\s+/i, '')
    .replace(/^(?:los|las)\s+/i, '')
    .replace(/^de\s+/i, '')
    .trim()
}

export function extractTrashRuleQuery(instruction: string) {
  const quoted = instruction.match(/[“"]([^”"]{2,500})[”"]/)
  if (quoted?.[1]) return quoted[1].trim()

  const betweenMailAndDestination = instruction.match(/\bcorreos?\s+(.+?)\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:papelera|eliminados|trash)\b/i)
  if (betweenMailAndDestination?.[1]) return cleanExtracted(betweenMailAndDestination[1])

  const fromPattern = instruction.match(/\b(?:de|desde)\s+(.+?)\s+(?:se\s+)?(?:vayan|vaya|envien|envíen|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:papelera|eliminados|trash)\b/i)
  if (fromPattern?.[1]) return cleanExtracted(fromPattern[1])

  return instruction
    .replace(/\b(?:crea|crear|creame|configura|configurar|haz|hacer|genera|generar|define|definir)\b/gi, ' ')
    .replace(/\b(?:una|un|la|el)\s+(?:regla|filtro)\b/gi, ' ')
    .replace(/\bpara\s+que\b/gi, ' ')
    .replace(/\b(?:todos?|todas?)\s+(?:los|las)\s+correos?\b/gi, ' ')
    .replace(/\b(?:se\s+)?(?:vayan|vaya|envien|envíen|muevan|mueva|manden|mande|pasen|pase)\s+(?:a|al|hacia)\s+(?:la\s+|los\s+)?(?:carpeta\s+de\s+)?(?:papelera|eliminados|trash)\b/gi, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}
