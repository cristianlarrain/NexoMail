import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
const accounts = read('src/pages/AccountsPage.tsx')
const modal = read('src/components/AddAccountModal.tsx')
const api = read('src/api/accountProviderApi.ts')
const logos = read('src/components/MailProviderLogo.tsx')
const main = read('src/main.tsx')

const assert = (condition, message) => {
  if (!condition) throw new Error(message)
}

assert(accounts.includes('Agregar cuenta'), 'AccountsPage debe usar el CTA Agregar cuenta.')
assert(!accounts.includes('Agregar Gmail</button>'), 'AccountsPage no debe mantener el CTA exclusivo Agregar Gmail.')
assert(accounts.includes('<AddAccountModal'), 'AccountsPage debe montar AddAccountModal.')
assert(accounts.includes("provider === 'MicrosoftGraph'"), 'AccountsPage debe rotular MicrosoftGraph como Microsoft 365.')
assert(accounts.includes("provider === 'Imap'"), 'AccountsPage debe rotular IMAP/SMTP Beta.')
assert(modal.includes('Gmail / Google Workspace'), 'El modal debe ofrecer Gmail / Google Workspace.')
assert(modal.includes('Microsoft 365'), 'El modal debe ofrecer Microsoft 365.')
assert(modal.includes('IMAP / SMTP'), 'El modal debe ofrecer IMAP / SMTP.')
assert(modal.includes('/api/oauth/google/start'), 'Gmail debe conservar su endpoint OAuth.')
assert(modal.includes('/api/oauth/microsoft/start'), 'Microsoft 365 debe usar su endpoint OAuth.')
assert(modal.includes('autorización previa'), 'Microsoft 365 debe advertir sobre autorización institucional.')
assert(modal.includes('Probar y conectar'), 'IMAP debe validar antes de conectar.')
assert(modal.includes('Outlook / Hotmail') && modal.includes('Yahoo Mail') && modal.includes('Exchange Server'), 'Los proveedores futuros deben aparecer en el modal.')
assert(modal.includes('Marcha blanca · 30 días'), 'El modal debe identificar la marcha blanca de 30 días.')
assert(api.includes('/api/mail/accounts/imap/connect'), 'El frontend debe llamar al endpoint IMAP/SMTP Beta.')
assert(logos.includes("provider === 'gmail'") && logos.includes("provider === 'microsoft'"), 'Deben existir marcas visuales locales para Gmail y Microsoft.')
assert(main.includes("./styles/account-providers.css"), 'Los estilos del modal de proveedores deben cargarse globalmente.')

console.log('Mail provider beta smoke test passed.')
