import { readFileSync } from 'node:fs'

const root = new URL('../', import.meta.url)
const endpoint = readFileSync(new URL('src/backend/NexoMail.Api/CommercialEndpoints.cs', root), 'utf8')
const api = readFileSync(new URL('src/frontend/src/api/commercialApi.ts', root), 'utf8')
const page = readFileSync(new URL('src/frontend/src/pages/AdminUsersPage.tsx', root), 'utf8')

const checks = [
  [endpoint, 'CommercialAdminConnectedAccountDto', 'El backend no expone el detalle de cuentas conectadas.'],
  [endpoint, 'ConnectedMailAccounts', 'El DTO administrativo no contiene las cuentas conectadas.'],
  [api, 'connectedMailAccounts:', 'El cliente no tipa las cuentas conectadas.'],
  [page, 'Cuentas conectadas', 'La tabla no presenta la columna de cuentas conectadas.'],
  [page, 'commercial-connected-account', 'La tabla no representa cada cuenta y su color.'],
]

for (const [source, token, error] of checks) {
  if (!source.includes(token)) throw new Error(error)
}

console.log('PASS: administración muestra las cuentas de correo conectadas')
