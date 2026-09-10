export type SavedPerspective = {
  id: string
  text: string
  source: string
  area: string
  savedAt: number
}

const STORAGE_KEY = 'nexomail-saved-perspectives-v1'
const CHANGE_EVENT = 'nexomail-perspectives-changed'

function read(): SavedPerspective[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw) as SavedPerspective[]
    return Array.isArray(parsed) ? parsed.filter(item => item && item.text && item.source) : []
  } catch {
    return []
  }
}

function write(items: SavedPerspective[]) {
  window.localStorage.setItem(STORAGE_KEY, JSON.stringify(items))
  window.dispatchEvent(new CustomEvent(CHANGE_EVENT))
}

export function getSavedPerspectives() {
  return read().sort((left, right) => right.savedAt - left.savedAt)
}

export function isPerspectiveSaved(text: string, source: string) {
  return read().some(item => item.text === text && item.source === source)
}

export function savePerspective(perspective: Omit<SavedPerspective, 'id' | 'savedAt'>) {
  const items = read()
  const existing = items.find(item => item.text === perspective.text && item.source === perspective.source)
  if (existing) return existing

  const saved: SavedPerspective = {
    ...perspective,
    id: typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`,
    savedAt: Date.now(),
  }
  write([saved, ...items])
  return saved
}

export function removeSavedPerspective(id: string) {
  write(read().filter(item => item.id !== id))
}

export function removeSavedPerspectiveByContent(text: string, source: string) {
  write(read().filter(item => !(item.text === text && item.source === source)))
}

export function subscribeToPerspectiveChanges(listener: () => void) {
  const onStorage = (event: StorageEvent) => {
    if (event.key === STORAGE_KEY) listener()
  }
  window.addEventListener(CHANGE_EVENT, listener)
  window.addEventListener('storage', onStorage)
  return () => {
    window.removeEventListener(CHANGE_EVENT, listener)
    window.removeEventListener('storage', onStorage)
  }
}

export function perspectiveShareText(perspective: Pick<SavedPerspective, 'text' | 'source' | 'area'>) {
  return `“${perspective.text}” — ${perspective.source}\n\nPerspectiva · ${perspective.area}\nNexi, la inteligencia que vive dentro de NexoMail.`
}

export async function sharePerspective(perspective: Pick<SavedPerspective, 'text' | 'source' | 'area'>) {
  const text = perspectiveShareText(perspective)
  const isPublicOrigin = !['localhost', '127.0.0.1'].includes(window.location.hostname)
  if (navigator.share) {
    await navigator.share({
      title: `Perspectiva · ${perspective.area} | NexoMail`,
      text,
      ...(isPublicOrigin ? { url: window.location.origin } : {}),
    })
    return 'shared' as const
  }

  await navigator.clipboard.writeText(`${text}${isPublicOrigin ? `\n${window.location.origin}` : ''}`)
  return 'copied' as const
}
