import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const page = readFileSync(new URL('../src/frontend/src/pages/MessagePage.tsx', import.meta.url), 'utf8')
const route = readFileSync(new URL('../src/frontend/src/pages/MessageRoute.tsx', import.meta.url), 'utf8')
const tools = readFileSync(new URL('../src/frontend/src/components/MessageNexiReaderTools.tsx', import.meta.url), 'utf8')

const navigation = page.indexOf('className="message-navigation"')
const title = page.indexOf('className="message-title-row"')
const toolbar = page.indexOf('className="message-actions message-action-toolbar unified-message-actions"')
const metadata = page.indexOf('className="message-meta"')

assert.ok(navigation >= 0 && navigation < title && title < toolbar && toolbar < metadata, 'El lector debe ordenar navegación, título, acciones y metadatos.')
assert.match(page, /<div className="message-standard-actions">/)
assert.match(page, /<MessageNexiReaderTools accountId=\{accountId\} messageId=\{messageId\} \/>/)
assert.doesNotMatch(route, /<MessageNexiReaderTools/)
assert.doesNotMatch(tools, /Entiende este correo y su conversación/)
assert.match(tools, /className="message-ai-actions"/)
assert.match(tools, /className="message-nexi-result"/)

console.log('PASS: acciones normales y Nexi comparten la barra del lector')
