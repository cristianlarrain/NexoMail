# Inventario verificado de NexoMail

Revisión: 12 de septiembre de 2026. Base: `d13e87af7480980741814f18e6f49d713d3d2233`, compartida por `feature/ionos-production` y `feature/commercial-foundation` al revisar. No representa una certificación del despliegue: se inspeccionó código, no cuentas reales ni configuración de producción.

## Criterio de disponibilidad

**Implementado** significa que existe un flujo identificable en el código. Su disponibilidad efectiva depende de permisos, proveedor, configuración y estado de la suscripción. **Parcial** indica una restricción concreta. **Catálogo** identifica una oferta comercial sin flujo completo verificado. **Propuesto** no debe presentarse como disponible.

Las rutas y archivos siguientes son relativos a la raíz del repositorio. La evidencia de pantallas está en `src/frontend/src/pages/` y la de rutas en `src/frontend/src/router.tsx`.

## Funciones y pantallas

| Área / ruta | Funciones verificadas | Condiciones y evidencia |
|---|---|---|
| Acceso `/login` | Registro, verificación del correo, reenvío de verificación, inicio y cierre de sesión, recuperación mediante código y cambio de contraseña | Implementado; `AuthPage.tsx`, `src/backend/NexoMail.Api/Security/AuthEndpoints.cs`. Los correos de verificación y recuperación requieren configuración de envío. |
| Perfil `/settings/profile` | Nombre visible, fotografía con recorte, consulta de sesiones y cierre de accesos | Implementado; `ProfilePage.tsx`, `Security/UserSessionSecurity.cs`. El correo se muestra de solo lectura. |
| Cuentas `/settings/accounts` | Conexión de Gmail, Microsoft 365 e IMAP/SMTP; editar nombre y color; desconectar cuenta | Implementado con diferencias por proveedor; `AccountsPage.tsx`, `MailProviderBetaModule.cs`, servicios OAuth y `ImapAccountService.cs`. Límite de cuentas según suscripción. |
| Bandeja `/inbox`, `/account/:accountId` | Consultar varias cuentas o una cuenta, lectura, selección múltiple, ordenación, paginación y acciones sobre mensajes | Implementado; `InboxPage.tsx`, `src/backend/NexoMail.Api/Program.cs`. Resultados sujetos a las consultas y páginas recuperadas. |
| Carpetas `/archive`, `/ignored`, `/sent`, `/drafts`, `/spam`, `/trash` | Vistas de archivo, ignorados, enviados, borradores, spam y papelera; leído/no leído, mover y enviar a papelera | Implementado; `InboxPage.tsx`, `Program.cs`. Ignorar remitentes mantiene un registro propio de NexoMail; no equivale a bloquearlos en el proveedor. Vaciar carpeta depende del proveedor. |
| Mensaje `/message/:accountId/:messageId` | Lectura, adjuntos y vista previa, respuesta, responder a todos, reenvío, navegación y seguimiento | Implementado; `MessagePage.tsx`, `MessageRoute.tsx`, `Program.cs`. La vista previa depende del formato. |
| Redactar `/compose` | Selección de remitente, Para/CC/CCO, asunto, cuerpo, adjuntos, borradores y envío; dictado y asistencia de escritura | Implementado; `ComposePage.tsx`, `DraftEndpoints.cs`, `AiWritingAssistant.tsx`. Dictado según navegador y permiso de micrófono; borradores según proveedor; IA según habilitación. |
| Búsqueda `/search` | Búsqueda de correos, contactos y documentos; filtros y consulta en lenguaje natural | Implementado; `SearchPage.tsx`, `TopSearchBox.tsx`, `AiSearchService.cs`. Contactos/documentos dependen del índice; no es una búsqueda ilimitada de todo el historial. |
| Acciones desde búsqueda `/search-action` | Interpretación de intención y preparación de acciones sobre resultados | Implementado; `NexiSearchActionPage.tsx`, `utils/nexiSearchIntent.ts`. No confundir una propuesta con una operación ya ejecutada. |
| Reglas `/rules/new`, `/settings/rules` | Crear, listar y eliminar reglas; archivar, marcar leído, enviar a papelera o mover a destino | Implementado para Gmail; `MailRulePage.tsx`, `RulesPage.tsx`, `MailRuleEndpoints.cs`, `Google/GmailRuleService.cs`. No se verificó proveedor de reglas Microsoft/IMAP. |
| Centro `/control-center` | Prioridades, pendientes, seguimiento manual y estado resuelto | Implementado; `ControlCenterPage.tsx`, `ControlCenter.tsx`, `ControlCenterTrackingEndpoints.cs`. Los cálculos automáticos del servicio actual seleccionan cuentas Gmail. |
| Centro `?tab=report` | Informes semánticos por período, síntesis y acciones detectadas | Implementado; `NexiMailReport.tsx`, `AiEndpoints.cs`, `AiMailInsightsService.cs`; requiere Nexi e IA. El análisis trabaja con una selección limitada de mensajes. |
| Centro `?tab=statistics` | Estadísticas, actividad y evolución | Implementado; `ControlCenterStatistics.tsx`, `Google/GmailControlCenterActivityService.cs`; requiere estadísticas avanzadas. |
| Centro `?tab=contacts` | Interacción por contacto | Implementado; `ControlCenterContacts.tsx`, `Google/GmailMetadataIndexService.cs`; requiere Centro completo e índice Gmail. |
| Centro `?tab=documents` | Consultar y filtrar documentos indexados | Implementado; `ControlCenterDocuments.tsx`, `MetadataIndexEndpoints.cs`; índice de metadatos de adjuntos Gmail, no repositorio de archivos subidos. |
| Centro `?tab=context` | Conversación sobre un conjunto de correos | Implementado; `NexiContextWorkspace.tsx`, `AiContextService.cs`. La ruta admite contexto aunque no sea una pestaña visible de la barra principal. |
| Perspectivas `/perspectives` | Colección guardada, reflexión ampliada, compartir y eliminar | Implementado; `PerspectivesPage.tsx`, `utils/perspectiveCollection.ts`, `PerspectiveEndpoints.cs`. La colección usa almacenamiento local del navegador: no prometer sincronización entre dispositivos. |
| Apariencia `/settings/appearance` | Tema claro y oscuro | Implementado; `AppearancePage.tsx`. Preferencia local; la muestra de paleta no constituye un editor de marca completo. |
| Plan `/settings/plan` | Plan, capacidades, cuentas utilizadas, contratación y estado de suscripción | Implementado; `PlanPage.tsx`, `CommercialEndpoints.cs`, `BillingEndpoints.cs`. Cobros reales sujetos a configuración de Mercado Pago y confirmación del proveedor. |
| Administración `/admin/plans` | Crear, modificar, desactivar y eliminar planes | Implementado con comprobación de administrador; `AdminPlansPage.tsx`, `CommercialEndpoints.cs`. |
| Administración `/admin/users` | Consulta de usuarios, asignación de plan y pruebas | Implementado; `AdminUsersPage.tsx`, `CommercialEndpoints.cs`. No acredita por sí solo una administración delegada por organización. |
| Administración `/admin/ai-usage` | Consumo por usuario, operaciones, costos, proyecciones y ajustes de referencia | Implementado para Owner; `AdminAiUsagePage.tsx`, `AiUsageEndpoints.cs`, `AiUsageAdminService.cs`. Costos calculados con referencias configuradas; no equivalen a factura del proveedor. |
| Información pública `/`, `/legal/terms`, `/legal/privacy`, `/legal/security` | Presentación comercial y textos informativos | Implementado; `LandingPage.tsx`, `LegalPage.tsx`. El texto comercial no demuestra disponibilidad técnica. |

## Diferencias entre proveedores

| Capacidad | Gmail | Microsoft 365 | IMAP/SMTP |
|---|---|---|---|
| Conectar | OAuth de Google | OAuth de Microsoft; configuración predeterminada `organizations` | Servidores y credenciales IMAP/SMTP; Beta |
| Leer, enviar, responder, mover | Proveedor implementado | Proveedor Graph implementado | Proveedor implementado; carpetas según servidor |
| Guardar/actualizar/enviar borrador | `GmailDraftProvider` | `MicrosoftGraphDraftProvider` | No hay `IMailDraftProvider` registrado |
| Conversación | Proveedor de hilo | Proveedor de hilo | `GetThreadAsync` devuelve solo el mensaje actual |
| Reglas automáticas | `GmailRuleService` | Sin proveedor verificado | Sin proveedor verificado |
| Índice de contactos/documentos y cálculos automáticos del Centro | Servicios Gmail | No incluidos por los filtros actuales de esos servicios | No incluidos por los filtros actuales de esos servicios |
| Vaciar carpeta | Según proveedor | Según proveedor | Solo papelera durante Beta |

Fuentes: `src/backend/NexoMail.Api/MailProviderBetaModule.cs`, `src/backend/NexoMail.Infrastructure/Google/`, `src/backend/NexoMail.Infrastructure/Microsoft/`, `src/backend/NexoMail.Infrastructure/Imap/`, `src/backend/NexoMail.Application/IMailProvider.cs`.

## Habilidades actuales de Nexi

- Panel sensible a bandeja, mensaje, búsqueda, Centro y configuración: `components/nexi/NexiAssistantPanel.tsx`.
- Resumen del correo o conversación, identificación de acciones y generación de informes: `AiEndpoints.cs` y `AiMailInsightsService.cs`.
- Redacción y propuesta de respuesta en sus flujos específicos: `AiWritingAssistant.tsx`, `AiInlineWritingAssistant.tsx`, `AiWritingService.cs`. El botón «Sugerir respuesta» del panel lateral sigue deshabilitado: no dirigir al usuario a ese botón como funcional.
- Interpretación de búsqueda y análisis de contexto: `AiSearchService.cs`, `AiContextService.cs`. Este último analiza como máximo 20 mensajes y recorta el contenido de cada uno a 1.050 caracteres, dentro de un presupuesto de contexto. No afirmar lectura exhaustiva del buzón ni de todos los adjuntos.
- Accesos a seguimiento y finalización desde el panel. Ya hay mutaciones reales; esta documentación no las elimina.
- Ampliación de perspectivas y generación de imagen de saludo: `PerspectiveEndpoints.cs`, condicionadas a configuración y controles correspondientes.

**No implementado como parte de esta actualización documental:** conversación de ayuda fundamentada en estos archivos, recuperación de artículos por pantalla y sistema de tickets. El panel actual no carga esta documentación automáticamente.

## Planes: catálogo versus capacidad real

`CommercialPlans.cs` contiene valores iniciales: Freemium 2 cuentas y Premium 10. Los planes son editables. Nexi debe consultar la suscripción efectiva; no fijar precios, cuotas o derechos usando únicamente este archivo.

`CommercialEntitlements.cs` define capacidades básicas, IA, analítica, firmas/plantillas, gestión de usuarios, políticas, analítica organizacional, soporte prioritario y personalización. Un código de capacidad no prueba que exista su flujo.

Se consideran **catálogo, pendientes de verificación funcional completa**: editor de firmas y plantillas reutilizables, políticas organizacionales, estadísticas por organización, atención prioritaria con tickets y configuración White Label/dominio desde la aplicación. No se deben dar instrucciones de botones inexistentes para estas prestaciones.

## Datos y alcance

El sistema conserva usuarios, sesiones, cuentas, credenciales protegidas, estados de seguimiento, remitentes ignorados, índices de metadatos y consumo de IA (`Data/NexoMailDbContext.cs`). Por tanto, «no almacena nada» es incorrecto. Los cuerpos y adjuntos se recuperan del proveedor, pueden pasar por caché de lectura y por procesamiento de IA en las funciones correspondientes. El índice Gmail declara que no persiste cuerpos ni bytes de adjuntos. Las perspectivas y el tema usan almacenamiento del navegador.

Esta revisión no valida cumplimiento legal, configuración de producción, exactitud de precios externos ni funcionamiento con credenciales reales.
