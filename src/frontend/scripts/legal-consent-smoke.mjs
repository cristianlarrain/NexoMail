import { existsSync, readFileSync } from 'node:fs'

function requireCondition(condition, message) {
  if (!condition) throw new Error(message)
}

const authPagePath = new URL('../src/pages/AuthPage.tsx', import.meta.url)
const modalPath = new URL('../src/components/LegalConsentModal.tsx', import.meta.url)
const legalDocumentsPath = new URL('../src/legal/legalDocuments.ts', import.meta.url)
const authPage = readFileSync(authPagePath, 'utf8')

requireCondition(authPage.includes('Revisar y aceptar condiciones'), 'El registro debe ofrecer revisar y aceptar las condiciones legales.')
requireCondition(authPage.includes('Condiciones legales aceptadas'), 'El registro debe mostrar el estado de consentimiento aceptado.')
requireCondition(authPage.includes('<LegalConsentModal'), 'El registro debe abrir el modal de consentimiento legal.')
requireCondition(existsSync(modalPath), 'Debe existir LegalConsentModal.tsx.')
requireCondition(existsSync(legalDocumentsPath), 'Los documentos legales deben compartirse desde un único módulo.')

const modal = readFileSync(modalPath, 'utf8')
const legalDocuments = readFileSync(legalDocumentsPath, 'utf8')

requireCondition(modal.includes('scrollHeight') && modal.includes('clientHeight'), 'El modal debe detectar que el usuario llegó al final del contenido.')
requireCondition(modal.includes('disabled={!hasReadToEnd}'), 'La aceptación debe permanecer deshabilitada hasta llegar al final.')
requireCondition(modal.includes('He leído y acepto'), 'El modal debe exigir una aceptación explícita.')
requireCondition(modal.includes('no almacena de forma persistente el contenido completo'), 'El modal debe destacar la minimización de almacenamiento del correo.')
requireCondition(legalDocuments.includes("title: 'Términos de Servicio'"), 'Debe incluir Términos de Servicio.')
requireCondition(legalDocuments.includes("title: 'Política de Privacidad'"), 'Debe incluir Política de Privacidad.')
requireCondition(legalDocuments.includes("title: 'Política de Seguridad'"), 'Debe incluir Política de Seguridad.')

console.log('Legal consent smoke: PASS')
