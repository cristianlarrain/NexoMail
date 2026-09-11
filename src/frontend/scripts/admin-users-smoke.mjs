import { existsSync, readFileSync } from 'node:fs'

function ensure(condition, message) {
  if (!condition) throw new Error(message)
}

const backend = readFileSync('../backend/NexoMail.Api/CommercialEndpoints.cs', 'utf8')
const api = readFileSync('src/api/commercialApi.ts', 'utf8')
const router = readFileSync('src/router.tsx', 'utf8')
const plans = readFileSync('src/pages/AdminPlansPage.tsx', 'utf8')
const planPage = readFileSync('src/pages/PlanPage.tsx', 'utf8')
const usersPagePath = 'src/pages/AdminUsersPage.tsx'

ensure(backend.includes('MapGet("/admin/users"'), 'El backend debe exponer GET /commercial/admin/users.')
ensure(backend.includes('MapPatch("/admin/users/{userId:guid}/plan"'), 'El backend debe exponer PATCH para asignar el plan de un usuario.')
ensure(api.includes('adminUsers:'), 'commercialApi debe exponer adminUsers.')
ensure(api.includes('assignUserPlan:'), 'commercialApi debe exponer assignUserPlan.')
ensure(existsSync(usersPagePath), 'Debe existir AdminUsersPage.tsx.')
ensure(router.includes("import { AdminUsersPage } from './pages/AdminUsersPage'"), 'El router debe importar AdminUsersPage.')
ensure(router.includes("path: '/admin/users'"), 'El router debe registrar /admin/users.')
ensure(plans.includes('to="/admin/users"'), 'Tipos de cuenta debe enlazar a Administración de usuarios.')
ensure(planPage.includes('to="/admin/users"'), 'Plan y uso debe ofrecer acceso a Administración de usuarios para administradores.')

const usersPage = existsSync(usersPagePath) ? readFileSync(usersPagePath, 'utf8') : ''
ensure(usersPage.includes('Administración de usuarios'), 'La pantalla debe identificarse como Administración de usuarios.')
ensure(usersPage.includes('assignUserPlan'), 'La pantalla debe permitir asignar planes desde la API.')
ensure(usersPage.includes('connectedAccounts'), 'La tabla debe mostrar las cuentas conectadas del usuario.')
ensure(usersPage.includes('effectivePlanCode'), 'La tabla debe distinguir el plan efectivo del asignado.')

console.log('PASS: administración de usuarios -> API -> ruta -> pantalla -> asignación de plan')
