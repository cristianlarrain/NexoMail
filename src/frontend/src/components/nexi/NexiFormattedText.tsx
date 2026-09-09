import type { ReactNode } from 'react'

type Block =
  | { type: 'heading'; text: string }
  | { type: 'paragraph'; text: string }
  | { type: 'unordered'; items: string[] }
  | { type: 'ordered'; items: string[] }

function cleanHeading(value: string) {
  return value
    .replace(/^#{1,6}\s+/, '')
    .replace(/^\*\*(.+?)\*\*:?\s*$/, '$1')
    .replace(/\*\*/g, '')
    .replace(/:\s*$/, '')
    .trim()
}

function parseBlocks(value: string): Block[] {
  const lines = value.replace(/\r/g, '').split('\n')
  const blocks: Block[] = []
  let paragraph: string[] = []
  let unordered: string[] = []
  let ordered: string[] = []

  function flushParagraph() {
    if (paragraph.length === 0) return
    blocks.push({ type: 'paragraph', text: paragraph.join(' ').trim() })
    paragraph = []
  }

  function flushLists() {
    if (unordered.length > 0) blocks.push({ type: 'unordered', items: unordered })
    if (ordered.length > 0) blocks.push({ type: 'ordered', items: ordered })
    unordered = []
    ordered = []
  }

  for (const rawLine of lines) {
    const line = rawLine.trim()
    if (!line) {
      flushParagraph()
      flushLists()
      continue
    }

    const heading = /^#{1,6}\s+/.test(line) || /^\*\*[^*]+\*\*:?$/.test(line)
    if (heading) {
      flushParagraph()
      flushLists()
      blocks.push({ type: 'heading', text: cleanHeading(line) })
      continue
    }

    const orderedMatch = line.match(/^\d+[.)]\s+(.+)$/)
    if (orderedMatch) {
      flushParagraph()
      if (unordered.length > 0) {
        blocks.push({ type: 'unordered', items: unordered })
        unordered = []
      }
      ordered.push(orderedMatch[1].trim())
      continue
    }

    const unorderedMatch = line.match(/^[-•]\s+(.+)$/)
    if (unorderedMatch) {
      flushParagraph()
      if (ordered.length > 0) {
        blocks.push({ type: 'ordered', items: ordered })
        ordered = []
      }
      unordered.push(unorderedMatch[1].trim())
      continue
    }

    flushLists()
    paragraph.push(line)
  }

  flushParagraph()
  flushLists()
  return blocks
}

function inlineContent(value: string): ReactNode[] {
  const parts = value.split(/(\*\*[^*]+\*\*)/g).filter(Boolean)
  return parts.map((part, index) => {
    const bold = part.match(/^\*\*(.+)\*\*$/)
    return bold ? <strong key={`${index}-${bold[1]}`}>{bold[1]}</strong> : <span key={`${index}-${part}`}>{part}</span>
  })
}

export function NexiFormattedText({ text }: { text: string }) {
  const blocks = parseBlocks(text)

  return <div className="nexi-formatted-text">
    {blocks.map((block, index) => {
      if (block.type === 'heading') return <h4 key={`${index}-${block.text}`}>{inlineContent(block.text)}</h4>
      if (block.type === 'unordered') return <ul key={`ul-${index}`}>{block.items.map((item, itemIndex) => <li key={`${itemIndex}-${item}`}>{inlineContent(item)}</li>)}</ul>
      if (block.type === 'ordered') return <ol key={`ol-${index}`}>{block.items.map((item, itemIndex) => <li key={`${itemIndex}-${item}`}>{inlineContent(item)}</li>)}</ol>
      return <p key={`${index}-${block.text}`}>{inlineContent(block.text)}</p>
    })}
  </div>
}
