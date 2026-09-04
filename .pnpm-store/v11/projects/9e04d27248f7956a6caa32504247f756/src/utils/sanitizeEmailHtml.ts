/** Defensive presentation filter for provider HTML. */
export function sanitizeEmailHtml(html: string): string {
  const document = new DOMParser().parseFromString(html, 'text/html')
  document.querySelectorAll('script, style, iframe, object, embed, form, link, meta, base').forEach(node => node.remove())
  document.querySelectorAll('*').forEach(element => {
    for (const attribute of [...element.attributes]) {
      const name = attribute.name.toLowerCase()
      const value = attribute.value.trim().toLowerCase()
      if (name.startsWith('on') || name === 'style' || value.startsWith('javascript:') || value.startsWith('data:text/html')) element.removeAttribute(attribute.name)
    }
    if (element instanceof HTMLImageElement) {
      const source = element.getAttribute('src')?.trim() ?? ''
      if (!/^(https?:|data:image\/|cid:)/i.test(source)) element.removeAttribute('src')
      else { element.loading = 'lazy'; element.referrerPolicy = 'no-referrer' }
      element.alt ||= 'Imagen del correo'
    }
    if (element instanceof HTMLAnchorElement) { element.target = '_blank'; element.rel = 'noopener noreferrer' }
  })
  return document.body.innerHTML
}
