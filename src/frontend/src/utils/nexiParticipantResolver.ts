type Participant = { name: string; email: string }

export type ParticipantResolution = { query: string; matchedName: string }

function normalize(value: string) {
  return value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('es')
}

function words(value: string) {
  return value.match(/[\p{L}\p{N}]+/gu) ?? []
}

function distance(left: string, right: string) {
  const a = normalize(left)
  const b = normalize(right)
  const row = Array.from({ length: b.length + 1 }, (_, index) => index)
  for (let i = 1; i <= a.length; i += 1) {
    let diagonal = row[0]
    row[0] = i
    for (let j = 1; j <= b.length; j += 1) {
      const previous = row[j]
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, diagonal + (a[i - 1] === b[j - 1] ? 0 : 1))
      diagonal = previous
    }
  }
  return row[b.length]
}

export function resolveParticipantQuery(query: string, participants: Participant[]): ParticipantResolution | null {
  const queryWords = words(query).filter(word => word.length >= 3)
  if (queryWords.length === 0) return null

  const candidates = participants.flatMap(participant => {
    const participantWords = words(participant.name).filter(word => word.length >= 3)
    if (participantWords.length === 0) return []
    const replacements = queryWords.map(queryWord => {
      const best = participantWords
        .map(participantWord => ({ participantWord, score: distance(queryWord, participantWord) }))
        .sort((left, right) => left.score - right.score)[0]
      return { queryWord, ...best }
    })
    const exactMatches = replacements.filter(item => item.score === 0).length
    const closeMatches = replacements.filter(item => item.score === 1 && item.queryWord.length >= 4).length
    if (closeMatches === 0 || (queryWords.length > 1 && exactMatches === 0)) return []
    return [{ participant, replacements, score: replacements.reduce((sum, item) => sum + Math.min(item.score, 4), 0) }]
  }).sort((left, right) => left.score - right.score)

  if (candidates.length === 0 || (candidates[1] && candidates[1].score === candidates[0].score)) return null
  const selected = candidates[0]
  let resolved = query
  for (const replacement of selected.replacements.filter(item => item.score === 1)) {
    resolved = resolved.replace(new RegExp(`\\b${replacement.queryWord}\\b`, 'iu'), replacement.participantWord)
  }
  return normalize(resolved) === normalize(query) ? null : { query: resolved, matchedName: selected.participant.name }
}
