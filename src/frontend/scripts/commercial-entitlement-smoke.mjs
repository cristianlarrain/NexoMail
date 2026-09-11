import { readFile } from 'node:fs/promises'

async function source(path) {
  try {
    return await readFile(new URL(`../${path}`, import.meta.url), 'utf8')
  } catch {
    throw new Error(`Falta ${path}, requerido para aplicar las funciones configurables de los planes.`)
  }
}

function ensure(condition, message) {
  if (!condition) throw new Error(message)
}

const [gate, constants, router, layout, controlCenter, messageRoute, messagePage, composePage, greetingAssistant, perspective, perspectivesPage, commercialSettings, commercialBilling] = await Promise.all([
  source('src/components/RequireEntitlement.tsx'),
  source('src/utils/commercialEntitlements.ts'),
  source('src/router.tsx'),
  source('src/layouts/AppLayout.tsx'),
  source('src/pages/ControlCenterPage.tsx'),
  source('src/pages/MessageRoute.tsx'),
  source('src/pages/MessagePage.tsx'),
  source('src/pages/ComposePage.tsx'),
  source('src/components/NexiGreetingImageAssistant.tsx'),
  source('src/components/NexoPerspective.tsx'),
  source('src/pages/PerspectivesPage.tsx'),
  source('src/styles/commercial-settings.css'),
  source('src/styles/commercial-billing.css'),
])

ensure(gate.includes('commercialApi.subscription') && gate.includes('entitlements.includes(entitlement)'),
  'RequireEntitlement debe consultar la suscripción efectiva y validar la capacidad solicitada.')
ensure(constants.includes("mailActions: 'mail_actions'") && constants.includes("controlCenterBasic: 'control_center_basic'") && constants.includes("nexiAi: 'nexi_ai'"),
  'Las capacidades comerciales del frontend deben usar códigos centralizados.')
ensure(router.includes('RequireEntitlement') && router.includes('commercialEntitlements.mailActions') && router.includes('commercialEntitlements.controlCenterBasic') && router.includes('commercialEntitlements.nexiAi'),
  'Las rutas de Redactar, Centro de Control y Nexi deben estar protegidas por capacidad.')
ensure(layout.includes('hasMailActions') && layout.includes('hasControlCenter'),
  'El menú debe ocultar acciones no incluidas en el plan efectivo.')
ensure(controlCenter.includes('hasFullControlCenter') && controlCenter.includes('disabled={!hasAdvancedAnalytics}') && controlCenter.includes('disabled={!hasFullControlCenter}'),
  'El Centro de Control debe respetar estadísticas avanzadas y el nivel completo.')
ensure(messageRoute.includes('hasNexi') && messageRoute.includes('MessageNexiReaderTools'),
  'Las herramientas Nexi del lector deben mostrarse sólo cuando Nexi esté habilitado.')
ensure(messagePage.includes('hasMailActions') && messagePage.includes('commercialEntitlements.mailActions'),
  'El lector debe ocultar responder, reenviar y organizar cuando el plan no incluye acciones de correo.')
ensure(messagePage.includes('hasTracking') && messagePage.includes('commercialEntitlements.trackingBasic'),
  'El lector debe ocultar seguimiento y finalización cuando el plan no incluye seguimiento esencial.')
ensure(messagePage.includes('hasMailActions && <button className="message-action-button primary-action"') && messagePage.includes('hasMailActions && <div className="reply-bar'),
  'Responder y la barra inferior del correo deben depender de la capacidad Acciones de correo.')
ensure(messagePage.includes('hasTracking && canManualTrack') && messagePage.includes('hasTracking && canTrackOrFinalize'),
  'Los controles de seguimiento del lector deben depender de Seguimiento esencial.')
ensure(composePage.includes('commercialApi.subscription') && composePage.includes('commercialEntitlements.nexiAi') && composePage.includes('hasNexi && <AiInlineWritingAssistant'),
  'El asistente de escritura del editor debe mostrarse sólo cuando Nexi e IA esté habilitado.')
ensure(greetingAssistant.includes('commercialApi.subscription') && greetingAssistant.includes('commercialEntitlements.nexiAi') && greetingAssistant.includes('if (!hasNexi) return null'),
  'El generador visual de saludos debe quedar inactivo cuando Nexi e IA no esté habilitado.')
ensure(perspective.includes('commercialApi.subscription') && perspective.includes('commercialEntitlements.nexiAi') && perspective.includes('hasNexi && <button'),
  'La acción Analizar de Perspectiva debe mostrarse sólo cuando Nexi e IA esté habilitado.')
ensure(perspectivesPage.includes('commercialApi.subscription') && perspectivesPage.includes('commercialEntitlements.nexiAi') && perspectivesPage.includes('autoAnalyze && hasNexi'),
  'Perspectivas no debe iniciar análisis automáticos sin la capacidad Nexi e IA.')
ensure(!commercialSettings.includes('color: var(--muted)') && commercialSettings.includes('color: var(--muted-foreground)'),
  'Plan y uso debe usar el token de texto secundario, no el color de fondo muted, para conservar contraste en modo oscuro.')
ensure(!commercialBilling.includes('color: var(--muted)') && commercialBilling.includes('color: var(--muted-foreground)'),
  'Los avisos de facturación y prueba deben usar texto secundario legible en modo oscuro.')

console.log('PASS: frontend respeta capacidades comerciales configurables')
