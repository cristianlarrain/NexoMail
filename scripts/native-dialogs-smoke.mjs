import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = fileURLToPath(new URL('../src/frontend/src/', import.meta.url))

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const files = []
  for (const entry of entries) {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) files.push(...await sourceFiles(path))
    else if (/\.(ts|tsx)$/.test(entry.name)) files.push(path)
  }
  return files
}

const files = await sourceFiles(frontendRoot)
const nativeDialog = /\b(?:window\.)?(?:alert|confirm|prompt)\s*\(/
const offenders = []

for (const file of files) {
  const source = await readFile(file, 'utf8')
  if (nativeDialog.test(source)) offenders.push(file.replace(frontendRoot, ''))
}

if (offenders.length) {
  throw new Error(`No se permiten diálogos nativos del navegador: ${offenders.join(', ')}`)
}

console.log('Native browser dialogs smoke test passed.')
