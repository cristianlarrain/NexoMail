import { existsSync, readFileSync } from 'node:fs'

function ensure(condition, message) {
  if (!condition) throw new Error(message)
}

const backend = readFileSync('../backend/NexoMail.Api/CommercialEndpoints.cs', 'utf8')
const api = readFileSync('src/api/commercialApi.ts', 'utf8')
const router = readFileSync('src/router.tsx', 'utf8')
const plans = readFileSync('src/pages/AdminPlansPage.tsx', 'utf8')
const planPage = readFileSync('src/pages/PlanPage.tsx', 'utf8')
const adminStyles = readFileSync('src/styles/commercial-admin.css', 'utf8')
const usersPagePath = 'src/pages/AdminUsersPage.tsx'

ensure(backend.includes('MapGet("/admin/users"'), 'El backend debe exponer GET /commercial/admin/users.')
ensure(backend.includes('MapPatch("/admin/users/{userId:guid}/plan"'), 'El backend debe exponer PATCH para asignar el plan de un usuario.')
ensure(backend.includes('MapPost("/admin/users/{userId:guid}/trial"'), 'El backend debe exponer POST para otorgar una prueba temporal.')
ensure(api.includes('adminUsers:'), 'commercialApi debe exponer adminUsers.')
ensure(api.includes('assignUserPlan:'), 'commercialApi debe exponer assignUserPlan.')
ensure(api.includes('grantUserTrial:'), 'commercialApi debe exponer grantUserTrial.')
ensure(existsSync(usersPagePath), 'Debe existir AdminUsersPage.tsx.')
ensure(router.includes("import { AdminUsersPage } from './pages/AdminUsersPage'"), 'El router debe importar AdminUsersPage.')
ensure(router.includes("path: '/admin/users'"), 'El router debe registrar /admin/users.')
ensure(plans.includes('to="/admin/users"'), 'Tipos de cuenta debe enlazar a Administración de usuarios.')
ensure(planPage.includes('to="/admin/users"'), 'Plan y uso debe ofrecer acceso a Administración de usuarios para administradores.')

const usersPage = existsSync(usersPagePath) ? readFileSync(usersPagePath, 'utf8') : ''
ensure(usersPage.includes('Administración de usuarios'), 'La pantalla debe identificarse como Administración de usuarios.')
ensure(usersPage.includes('assignUserPlan'), 'La pantalla debe permitir asignar planes desde la API.')
ensure(usersPage.includes('grantUserTrial'), 'La pantalla debe permitir otorgar pruebas temporales desde la API.')
ensure(usersPage.includes('trialDays'), 'La prueba temporal debe permitir definir su duración.')
ensure(usersPage.includes("'premium' | 'nexi'"), 'La pantalla debe permitir elegir entre prueba Premium y prueba sólo de Nexi.')
ensure(usersPage.includes('connectedAccounts'), 'La tabla debe mostrar las cuentas conectadas del usuario.')
ensure(usersPage.includes('effectivePlanCode'), 'La tabla debe distinguir el plan efectivo del asignado.')
ensure(usersPage.includes("effectivePlanCode === 'owner'"), 'La pantalla debe reconocer el acceso interno Owner por separado del plan comercial.')
ensure(usersPage.includes('Owner / Administrador general'), 'La pantalla debe identificar visualmente al Owner / Administrador general.')
ensure(/disabled=\{isOwner\s*\|\|/s.test(usersPage), 'El plan comercial del Owner no debe poder reasignarse desde la grilla.')
ensure(planPage.includes("data.effectivePlanCode === 'owner'"), 'Plan y uso debe reconocer el acceso interno Owner.')
ensure(planPage.includes('Owner / Administrador general'), 'Plan y uso debe mostrar el nivel interno Owner en lugar de presentarlo como un plan comercial normal.')
ensure(/const canCheckout = !isOwner\s*&&/s.test(planPage), 'El Owner no debe iniciar una contratación comercial desde Plan y uso.')
ensure(planPage.includes("data.subscription.provider === 'admin_trial'"), 'Plan y uso debe explicar cuando el acceso corresponde a una prueba gratuita administrada.')
ensure(/\.commercial-admin-table th\s*\{[^}]*background:/s.test(adminStyles), 'Los encabezados de las grillas administrativas deben tener un fondo visible.')
ensure(/\.commercial-admin-table th\s*\{[^}]*color:\s*(?:#fff|white|var\(--[^)]*on[^)]*\))/s.test(adminStyles), 'Los encabezados de las grillas administrativas deben usar texto de alto contraste.')
ensure(/\.commercial-admin-guidance\s*\{[^}]*color:\s*var\(--foreground\)/s.test(adminStyles), 'El aviso administrativo debe usar texto principal de alto contraste, también en modo oscuro.')

console.log('PASS: administración de usuarios -> API -> planes -> pruebas temporales -> Owner protegido')
