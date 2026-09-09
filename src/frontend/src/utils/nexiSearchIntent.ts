function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

export type NexiMailAction = 'trash' | 'archive' | 'mark_read' | 'mark_unread' | 'track' | 'untrack' | 'finalize' | 'prepare_reply'
export type NexiMailSubset = 'all' | 'unread' | 'read' | 'with_attachments' | 'pending' | 'informational'

function isNegated(value: string, verb: string) {
  return new RegExp(`\\bno\\s+(?:los\\s+|las\\s+|estos\\s+|estas\\s+)?${verb}`).test(value)
}

export function detectNexiMailSubset(query: string): NexiMailSubset {
  const value = normalized(query)

  if (/\b(?:informativo|informativos|informativa|informativas)\b/.test(value)
    || /\b(?:no\s+requiere|no\s+requieren|sin)\s+(?:ninguna\s+)?(?:accion|respuesta)\b/.test(value)
    || /\bsin\s+accion\s+pendiente\b/.test(value)) return 'informational'

  if (/\b(?:requiere|requieren)\s+(?:una\s+)?(?:respuesta|accion)\b/.test(value)
    || /\bpendiente(?:s)?\s+de\s+(?:responder|respuesta)\b/.test(value)
    || /\b(?:los|las|correos|mensajes)\b[^.]{0,24}\bpendiente(?:s)?\b/.test(value)) return 'pending'

  if (/\b(?:solo\s+|solamente\s+|unicamente\s+)?(?:los|las)\s+(?:que\s+estan\s+)?(?:no\s+leidos?|no\s+leidas?|sin\s+leer)\b/.test(value)
    || /\b(?:correos|mensajes)\s+sin\s+leer\b/.test(value)) return 'unread'

  if (/\b(?:solo\s+|solamente\s+|unicamente\s+)?(?:los|las)\s+(?:que\s+estan\s+)?(?:leidos?|leidas?)\b/.test(value)) return 'read'

  if (/\b(?:los|las|correos|mensajes)\b[^.]{0,20}\bcon\s+(?:archivo|archivos|adjunto|adjuntos)\b/.test(value)) return 'with_attachments'

  return 'all'
}

export function detectNexiMailAction(query: string): NexiMailAction | null {
  const value = normalized(query)

  const unread = /\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejarlos|dejarlas)\b[^.]{0,35}\b(?:como\s+)?(?:no\s+leido|no\s+leidos|no\s+leida|no\s+leidas|sin\s+leer)\b/.test(value)
  if (unread) return 'mark_unread'

  const read = /\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejarlos|dejarlas)\b[^.]{0,35}\b(?:como\s+)?(?:leido|leidos|leida|leidas)\b/.test(value)
  if (read) return 'mark_read'

  const untrack = /\b(?:quita|quitar|elimina|eliminar|saca|sacar)\b[^.]{0,30}\bseguimiento\b|\bdeja(?:r)?\s+de\s+seguir\b/.test(value)
  if (untrack) return 'untrack'

  const track = /\b(?:marca|marcar|poner|pon|agrega|agregar)\b[^.]{0,30}\bseguimiento\b|\b(?:seguir|sigue|siguelos|siguelas)\b/.test(value)
  if (track) return 'track'

  const archive = /\b(?:archiva|archivar|archive|archivalos|archivalas|archivarlos|archivarlas)\b/.test(value)
    || (/\barchivad(?:o|os|a|as)\b/.test(value) && /\b(?:mueve|mover|manda|mandar|envia|enviar|pasa|pasar)\b/.test(value))
  if (archive && !isNegated(value, '(?:archiv|archive)')) return 'archive'

  if (requestsTrashAction(query)) return 'trash'

  const prepareReply = /\b(?:prepara|preparar|redacta|redactar|genera|generar|crea|crear)\b[^.]{0,38}\b(?:respuesta|respuestas|contestacion|contestaciones)\b/.test(value)
    || /\b(?:responde|responder|contesta|contestar)\b[^.]{0,28}\b(?:estos|estas|los|las|correos|mensajes)\b/.test(value)
  if (prepareReply) return 'prepare_reply'

  const finalize = /\b(?:finaliza|finalizar|finalice|finalizalos|finalizalas|resuelve|resolver|resuelvelos|resuelvelas)\b/.test(value)
    || /\bdar(?:los|las)?\s+por\s+(?:resuelto|resueltos|resuelta|resueltas|finalizado|finalizados|finalizada|finalizadas)\b/.test(value)
    || (/\b(?:no\s+requiere|no\s+requieren|sin)\s+(?:ninguna\s+)?accion\b/.test(value)
      && /\b(?:finaliza|finalizar|resuelve|resolver|dar)\b/.test(value))
  if (finalize) return 'finalize'

  return null
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
  return /\b(bandeja de entrada|inbox|recibidos|correo recibido|correos recibidos)\b/.test(value)
}

export function sanitizeActionSearch(value: string) {
  return normalized(value)
    .replace(/\b(?:elimina|eliminar|elimine|eliminen|eliminalo|eliminalos|eliminala|eliminalas|eliminarlos|eliminarlas|eliminara|borra|borrar|borre|borren|borralo|borralos|borrala|borralas|borrarlos|borrarlas|borrara)\b/g, ' ')
    .replace(/\b(?:archiva|archivar|archive|archivalos|archivalas|archivarlos|archivarlas|finaliza|finalizar|finalice|finalizalos|finalizalas|resuelve|resolver|resuelvelos|resuelvelas)\b/g, ' ')
    .replace(/\b(?:prepara|preparar|redacta|redactar|genera|generar|crea|crear|responde|responder|contesta|contestar)\b[^.]{0,18}\b(?:respuesta|respuestas|contestacion|contestaciones)?\b/g, ' ')
    .replace(/\b(?:marca|marcar|marcalos|marcalas|poner|pon|deja|dejarlos|dejarlas|agrega|agregar|quita|quitar|saca|sacar|seguir|sigue|siguelos|siguelas)\b/g, ' ')
    .replace(/\b(?:como\s+)?(?:no\s+leido|no\s+leidos|no\s+leida|no\s+leidas|sin\s+leer|leido|leidos|leida|leidas|seguimiento)\b/g, ' ')
    .replace(/\b(?:informativo|informativos|informativa|informativas)\b/g, ' ')
    .replace(/\b(?:no\s+requiere|no\s+requieren|sin|requiere|requieren)\s+(?:ninguna\s+|una\s+)?(?:accion|respuesta)\b/g, ' ')
    .replace(/\bpendiente(?:s)?\s+de\s+(?:responder|respuesta)\b/g, ' ')
    .replace(/\b(?:con\s+)?(?:archivo|archivos|adjunto|adjuntos)\b/g, ' ')
    .replace(/\bdar(?:los|las)?\s+por\s+(?:resuelto|resueltos|resuelta|resueltas|finalizado|finalizados|finalizada|finalizadas)\b/g, ' ')
    .replace(/\b(?:mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasar|pasa)\b(?:\s+(?:a|hacia))?\s+(?:la\s+)?(?:papelera|archivados?)\b/g, ' ')
    .replace(/\b(?:busca|buscar|buscame|muestra|mostrar|muestrame|encuentra|encontrar|quiero|necesito|por favor|ahora|solo|solamente|unicamente)\b/g, ' ')
    .replace(/\b(?:los|las|el|la|estos|estas|todos|todas|correos|correo|mensajes|mensaje|de|del|en|mi|mis|usuario|usuarios|bandeja|entrada)\b/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}
