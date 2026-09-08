function normalized(value: string) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
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
    .replace(/\b(?:mueve|mover|muevelos|muevelas|manda|mandar|envia|enviar|pasar|pasa)\b(?:\s+(?:a|hacia))?\s+(?:la\s+)?papelera\b/g, ' ')
    .replace(/\b(?:busca|buscar|buscame|muestra|mostrar|muestrame|encuentra|encontrar|quiero|necesito|por favor)\b/g, ' ')
    .replace(/\b(?:los|las|el|la|todos|todas|correos|correo|mensajes|mensaje|de|del|en|mi|mis|usuario|usuarios|bandeja|entrada)\b/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}
