# NexoMail

Cliente web para consultar varias cuentas de correo desde una bandeja unificada. NexoMail no es un servidor de correo y no guarda cuerpos completos de mensajes ni bytes de adjuntos; consulta el contenido desde cada proveedor cuando el usuario lo necesita y mantiene sólo la información operativa o índices de metadatos requeridos por sus funciones.

## Documentación funcional y ayuda de Nexi

Revisión de código: 12 de septiembre de 2026, base `d13e87af7480980741814f18e6f49d713d3d2233`. No acredita el funcionamiento del entorno de producción.

- [Inventario de funciones, habilidades, planes y límites por proveedor](docs/help/feature-inventory.md).
- [Guía de uso y preguntas frecuentes por pantalla](docs/help/user-guide.md).
- [Contrato de la futura ayuda contextual de Nexi](docs/help/nexi-help-policy.md).

La conversación de ayuda basada en estos archivos todavía no está conectada al asistente. Los flujos actuales de análisis y acciones sobre correos conservan su funcionamiento. La presencia de una prestación en el catálogo comercial no demuestra que su flujo esté implementado; el inventario separa ambos casos.

## Marcha blanca

La versión actual está preparada para una marcha blanca de 30 días con tres formas de conexión:

- **Gmail / Google Workspace** mediante OAuth 2.0.
- **Microsoft 365** mediante Microsoft Graph y OAuth organizacional.
- **IMAP / SMTP** en modalidad **Beta**, con configuración manual y validación de ambos servidores antes de guardar la cuenta.

Outlook/Hotmail personal, Yahoo dedicado y Exchange Server local aparecen como próximos proveedores y no forman parte del alcance inicial.

Las cuentas Microsoft 365 institucionales pueden requerir autorización del administrador de la organización antes de permitir que NexoMail acceda al correo.

## Arquitectura

- `src/backend/NexoMail.Domain`: modelos normalizados de correo.
- `src/backend/NexoMail.Application`: contratos `IMailProvider`, `IMailDraftProvider` y gateway de aplicación.
- `src/backend/NexoMail.Infrastructure`: Gmail, Microsoft Graph, IMAP/SMTP, cifrado de credenciales y persistencia.
- `src/backend/NexoMail.Api`: API REST, OAuth, autenticación y endpoints de configuración.
- `src/frontend`: React, TypeScript, Vite y TanStack Query.

La UI no depende de las clases internas de Gmail, Graph o MailKit. El backend normaliza los datos y los expone como `MailSummary`, `MailMessage`, `MailAccount` y `ComposeMessage`. El HTML se filtra antes de mostrarse, bloqueando contenido activo e imágenes remotas.

## Ejecutar en desarrollo

Requisitos: .NET SDK 10 y Node.js con pnpm.

```powershell
dotnet run --project src/backend/NexoMail.Api --urls http://localhost:5052
```

En otra terminal:

```powershell
Set-Location src/frontend
pnpm install
pnpm dev
```

Abra la dirección mostrada por Vite (por defecto `http://localhost:5173`). El proxy de Vite redirige `/api` al backend.

## Modo demostración

`MailProviders:DemoMode` está habilitado por defecto en `src/backend/NexoMail.Api/appsettings.json`. En producción debe estar desactivado. El modo demo incluye cuentas y mensajes ficticios y no usa los proveedores reales.

## Gmail / Google Workspace

En Google Cloud, el cliente OAuth de tipo **Aplicación web** debe registrar la URI de redirección correspondiente al ambiente.

Desarrollo:

`http://localhost:5052/api/oauth/google/callback`

Las credenciales deben almacenarse fuera del repositorio. Para desarrollo puede usar User Secrets:

```powershell
dotnet user-secrets set "Google:ClientId" "TU_CLIENT_ID" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
dotnet user-secrets set "Google:ClientSecret" "TU_CLIENT_SECRET" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
```

NexoMail almacena el refresh token protegido mediante Data Protection; no almacena la contraseña de Google.

## Microsoft 365

Registrar NexoMail como aplicación web en Microsoft Entra ID para cuentas organizacionales. Durante la marcha blanca `AuthorityTenant` debe permanecer en `organizations`.

Desarrollo:

`http://localhost:5052/api/oauth/microsoft/callback`

Configuración local mediante User Secrets:

```powershell
dotnet user-secrets set "Microsoft365:ClientId" "TU_CLIENT_ID" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
dotnet user-secrets set "Microsoft365:ClientSecret" "TU_CLIENT_SECRET" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
dotnet user-secrets set "Microsoft365:AuthorityTenant" "organizations" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
```

Permisos delegados requeridos por la marcha blanca:

- `User.Read`
- `Mail.ReadWrite`
- `Mail.Send`
- `offline_access`

NexoMail no solicita permisos de calendario, archivos, OneDrive o SharePoint para este flujo. Una organización puede exigir consentimiento administrativo antes de autorizar estos permisos.

## IMAP / SMTP Beta

La opción **IMAP / SMTP · Beta** solicita:

- correo y nombre visible;
- usuario y contraseña o contraseña de aplicación;
- host, puerto y seguridad IMAP;
- host, puerto y seguridad SMTP.

Sólo se ofrecen conexiones cifradas `SSL/TLS` o `STARTTLS`. Antes de persistir una cuenta, el backend valida DNS, conexión TLS y autenticación en IMAP y SMTP. Las contraseñas se protegen con Data Protection antes de almacenarse y no se registran en logs.

Durante esta marcha blanca IMAP/SMTP no ofrece borradores remotos. El envío, lectura y operaciones principales de correo usan MailKit/MimeKit.

## Configuración mínima para producción

Antes de publicar un ambiente real:

1. Configure `MailProviders:DemoMode=false`.
2. Configure una base de datos persistente y respaldada.
3. Registre las URIs HTTPS públicas de callback en Google y Microsoft.
4. Configure `Google:ClientId`, `Google:ClientSecret`, `Google:RedirectUri` y `Google:FrontendUrl` mediante secretos del hosting.
5. Configure `Microsoft365:ClientId`, `Microsoft365:ClientSecret`, `Microsoft365:RedirectUri`, `Microsoft365:FrontendUrl` y `Microsoft365:AuthorityTenant=organizations` mediante secretos del hosting.
6. Mantenga las claves de ASP.NET Data Protection en almacenamiento persistente. Si esas claves se pierden, NexoMail no podrá descifrar refresh tokens ni contraseñas IMAP guardadas previamente.
7. Use HTTPS en frontend, API y callbacks.
8. No incluya Client Secrets, contraseñas, tokens ni claves de Data Protection dentro del repositorio o imágenes de despliegue públicas.

Ejemplo para desactivar el modo demo localmente:

```powershell
dotnet user-secrets set "MailProviders:DemoMode" "false" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
```

## Configuración de cuentas

Abra `/settings/accounts` y seleccione **Agregar cuenta**. El modal ofrece Gmail, Microsoft 365 e IMAP/SMTP Beta y muestra los proveedores futuros como no disponibles.

## Tema

Los tokens visuales viven en `src/frontend/src/styles/theme.css`. Modificar las variables CSS de `:root` cambia el aspecto de toda la interfaz; el tema oscuro se define en `[data-theme="dark"]`.
