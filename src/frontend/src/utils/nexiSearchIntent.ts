function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

export type NexiMailAction = 'trash' | 'archive' | 'move_inbox' | 'move_spam' | 'mark_read' | 'mark_unread' | 'track' | 'untrack' | 'finalize' | 'prepare_reply'
export type NexiMailSubset = 'all' | 'unread' | 'read' | 'with_attachments' | 'pending' | 'informational'
export type NexiMailSourceFolder = 'inbox' | 'sent' | 'archive' | 'spam' | 'trash'
export type NexiMailPlanStep = {
  action: NexiMailAction
  subset: NexiMailSubset
  clause: string
}

function isNegated(value: string, verb: string) {
  return new RegExp(`\\bno\\s+(?:los\\s+|las\\s+|estos\\s+|estas\\s+)?${verb}`).test(value)
}

export function detectNexiMailSubset(query: string): NexiMailSubset {
  const value = normalized(query)

  if (/\b(?:informativo|informativos|informativa|informativas)\b/.test(value)
    || /\b(?:no\s+requiere|no\s+requieren|sin)\s+(?:ninguna\s+)?(?:accion|respuesta|contestacion)\b/.test(value)
    || /\bsin\s+accion\s+pendiente\b/.test(value)) return 'informational'

  if (/\b(?:requiere|requieren)\s+(?:una\s+)?(?:respuesta|accion|contestacion)\b/.test(value)
    || /\bpendiente(?:s)?\s+de\s+(?:responder|respuesta|contestar|contestacion)\b/.test(value)
    || /\b(?:los|las|correos|mensajes)\b[^.]{0,24}\bpendiente(?:s)?\b/.test(value)) return 'pending'

  if (/\b(?:solo\s+|solamente\s+|unicamente\s+)?(?:los|las)\s+(?:que\s+estan\s+)?(?:no\s+leidos?|no\s+leidas?|sin\s+leer)\b/.test(value)
    || /\b(?:correos|mensajes)\s+sin\s+leer\b/.test(value)) return 'unread'

  if (/\b(?:solo\s+|solamente\s+|unicamente\s+)?(?:los|las)\s+(?:que\s+estan\s+)?(?:leidos?|leidas?)\b/.test(value)) return 'read'

  if (/\b(?:los|las|correos|mensajes)\b[^.]{0,20}\bcon\s+(?:archivo|archivos|adjunto|adjuntos)\b/.test(value)) return 'with_attachments'

  return 'all'
}

export function detectNexiSourceFolder(query: string): NexiMailSourceFolder | null {
  const value = normalized(query)
  if (/\b(?:de|desde|en)\s+(?:la\s+)?papelera\b/.test(value)) return 'trash'
  if (/\b(?:de|desde|en)\s+(?:el\s+)?spam\b/.test(value)) return 'spam'
  if (/\b(?:de|desde|en)\s+(?:los\s+)?archivados?\b/.test(value)
    || (/\barchivad(?:o|os|a|as)\b/.test(value) && !/\b(?:a|hacia)\s+(?:los\s+)?archivados?\b/.test(value))) return 'archive'
  if (/\b(?:de|desde|en)\s+(?:los\s+)?enviados?\b/.test(value)) return 'sent'
  if (/\b(?:de|desde|en)\s+(?:la\s+)?(?:bandeja(?:\s+de\s+entrada)?|inbox|recibidos?)\b/.test(value)) return 'inbox'
  return null
}

function detectSingleNexiMailAction(query: string): NexiMailAction | null {
  const value = normalized(query)

  const unread = /\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejarlos|dejarlas)\b[^.]{0,35}\b(?:como\s+)?(?:no\s+leido|no\s+leidos|no\s+leida|no\s+leidas|sin\s+leer)\b/.test(value)
  if (unread) return 'mark_unread'

  const read = /\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejarlos|dejarlas)\b[^.]{0,35}\b(?:como\s+)?(?:leido|leidos|leida|leidas)\b/.test(value)
  if (read) return 'mark_read'

  const untrack = /\b(?:quita|quitar|elimina|eliminar|saca|sacar)\b[^.]{0,30}\bseguimiento\b|\bdeja(?:r)?\s+de\s+seguir\b/.test(value)
  if (untrack) return 'untrack'

  const track = /\b(?:marca|marcar|poner|pon|agrega|agregar|deja|dejar)\b[^.]{0,30}\bseguimiento\b|\b(?:seguir|sigue|siguelos|siguelas)\b/.test(value)
  if (track) return 'track'

  const moveVerb = '(?:mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasa|pasar)'
  const moveInbox = new RegExp(`\\b${moveVerb}\\b[^.]{0,55}\\b(?:a|hacia)\\s+(?:la\\s+)?(?:bandeja(?:\\s+de\\s+entrada)?|inbox)\\b`).test(value)
  if (moveInbox) return 'move_inbox'

  const moveSpam = new RegExp(`\\b${moveVerb}\\b[^.]{0,55}\\b(?:a|hacia)\\s+(?:el\\s+)?spam\\b`).test(value)
  if (moveSpam) return 'move_spam'

  const archive = /\b(?:archiva|archivar|archive|archivalos|archivalas|archivarlos|archivarlas)\b/.test(value)
    || (/\barchivad(?:o|os|a|as)\b/.test(value) && /\b(?:mueve|mover|manda|mandar|envia|enviar|pasa|pasar)\b/.test(value))
  if (archive && !isNegated(value, '(?:archiv|archive)')) return 'archive'

  if (requestsTrashAction(query)) return 'trash'

  const prepareReply = /\b(?:prepara|preparar|preparame|redacta|redactar|redactame|genera|generar|generame|crea|crear|creame)\b[^.]{0,42}\b(?:respuesta|respuestas|contestacion|contestaciones)\b/.test(value)
    || /\b(?:responde|responder|contestame|contesta|contestar)\b[^.]{0,28}\b(?:estos|estas|los|las|correos|mensajes)\b/.test(value)
  if (prepareReply) return 'prepare_reply'

  const finalize = /\b(?:finaliza|finalizar|finalice|finalizalos|finalizalas|resuelve|resolver|resuelvelos|resuelvelas)\b/.test(value)
    || /\bdar(?:los|las)?\s+por\s+(?:resuelto|resueltos|resuelta|resueltas|finalizado|finalizados|finalizada|finalizadas)\b/.test(value)
    || (/\b(?:no\s+requiere|no\s+requieren|sin)\s+(?:ninguna\s+)?accion\b/.test(value)
      && /\b(?:finaliza|finalizar|resuelve|resolver|dar)\b/.test(value))
  if (finalize) return 'finalize'

  return null
}

function effectiveSubset(action: NexiMailAction, detected: NexiMailSubset): NexiMailSubset {
  if (action === 'finalize') return 'pending'
  if (action === 'prepare_reply' && detected === 'all') return 'pending'
  return detected
}

function splitPlanClauses(query: string) {
  const value = query
    .replace(/\b(?:luego|despues|posteriormente|a continuacion)\b/gi, ',')
    .replace(/[;\n]+/g, ',')
  const rough = value.split(/\s*,\s*|\s+\by\b\s+(?=(?:ahora\s+)?(?:archiv|elimin|borr|marca|pon|poner|agrega|quita|saca|deja|segu|final|resuel|prepar|redact|genera|crea|respond|contest|muev|mand|envi|pasa))/i)
  return rough.map(part => part.trim()).filter(Boolean)
}

export function detectNexiMailPlan(query: string): NexiMailPlanStep[] {
  const clauses = splitPlanClauses(query)
  const steps: NexiMailPlanStep[] = []

  for (const clause of clauses) {
    const action = detectSingleNexiMailAction(clause)
    if (!action) continue
    const subset = effectiveSubset(action, detectNexiMailSubset(clause))
    steps.push({ action, subset, clause })
  }

  if (steps.length > 0) return steps

  const fallbackAction = detectSingleNexiMailAction(query)
  if (!fallbackAction) return []
  return [{ action: fallbackAction, subset: effectiveSubset(fallbackAction, detectNexiMailSubset(query)), clause: query.trim() }]
}

export function detectNexiMailAction(query: string): NexiMailAction | null {
  return detectNexiMailPlan(query)[0]?.action ?? null
}

export function requestsTrashAction(query: string) {
  const value = normalized(query)
  if (/\bno\s+(?:los\s+|las\s+|estos\s+|estas\s+)?(?:elim|borr)/.test(value)) return false

  const explicitDelete = /\b(?:elimina|eliminar|elimine|eliminen|eliminalo|eliminalos|eliminala|eliminalas|eliminarlos|eliminarlas|eliminara|borra|borrar|borre|borren|borralo|borralos|borrala|borralas|borrarlos|borrarlas|borrara)\b/.test(value)
  const moveToTrash = /\bpapelera\b/.test(value) && /\b(?:mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasar|pasa)\b/.test(value)
  return explicitDelete || moveToTrash
}

export function requestsInboxScope(query: string) {
  const value = normalized(query)
  const inboxDestination = /\b(?:mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasa|pasar)\b[^.]{0,55}\b(?:a|hacia)\s+(?:la\s+)?(?:bandeja(?:\s+de\s+entrada)?|inbox)\b/.test(value)
  if (inboxDestination) return false
  return /\b(bandeja de entrada|inbox|recibidos|correo recibido|correos recibidos)\b/.test(value)
}

export function sanitizeActionSearch(value: string) {
  return normalized(value)
    .replace(/\b(?:elimina|eliminar|elimine|eliminen|eliminalo|eliminalos|eliminala|eliminalas|eliminarlos|eliminarlas|eliminara|borra|borrar|borre|borren|borralo|borralos|borrala|borralas|borrarlos|borrarlas|borrara)\b/g, ' ')
    .replace(/\b(?:archiva|archivar|archive|archivalos|archivalas|archivarlos|archivarlas|finaliza|finalizar|finalice|finalizalos|finalizalas|resuelve|resolver|resuelvelos|resuelvelas)\b/g, ' ')
    .replace(/\b(?:prepara|preparar|preparame|redacta|redactar|redactame|genera|generar|generame|crea|crear|creame|responde|responder|contestame|contesta|contestar)\b[^.]{0,18}\b(?:respuesta|respuestas|contestacion|contestaciones)?\b/g, ' ')
    .replace(/\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejar|dejarlos|dejarlas|agrega|agregar|quita|quitar|saca|sacar|seguir|sigue|siguelos|siguelas|mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasa|pasar)\b/g, ' ')
    .replace(/\b(?:como\s+)?(?:no\s+leido|no\s+leidos|no\s+leida|no\s+leidas|sin\s+leer|leido|leidos|leida|leidas|seguimiento)\b/g, ' ')
    .replace(/\b(?:a|hacia)\s+(?:la\s+|el\s+|los\s+)?(?:bandeja(?:\s+de\s+entrada)?|inbox|spam|papelera|archivados?)\b/g, ' ')
    .replace(/\b(?:informativo|informativos|informativa|informativas)\b/g, ' ')
    .replace(/\b(?:no\s+requiere|no\s+requieren|sin|requiere|requieren)\s+(?:ninguna\s+|una\s+)?(?:accion|respuesta|contestacion)\b/g, ' ')
    .replace(/\bpendiente(?:s)?\s+de\s+(?:responder|respuesta|contestar|contestacion)\b/g, ' ')
    .replace(/\b(?:con\s+)?(?:archivo|archivos|adjunto|adjuntos)\b/g, ' ')
    .replace(/\bdar(?:los|las)?\s+por\s+(?:resuelto|resueltos|resuelta|resueltas|finalizado|finalizados|finalizada|finalizadas)\b/g, ' ')
    .replace(/\b(?:busca|buscar|buscame|muestra|mostrar|muestrame|encuentra|encontrar|quiero|necesito|por favor|ahora|solo|solamente|unicamente|luego|despues|posteriormente|continuacion)\b/g, ' ')
    .replace(/\b(?:los|las|el|la|estos|estas|todos|todas|correos|correo|mensajes|mensaje|de|del|en|mi|mis|usuario|usuarios|bandeja|entrada)\b/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}
