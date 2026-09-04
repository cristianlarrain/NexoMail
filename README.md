# NexoMail

Cliente web para consultar varias cuentas de correo desde una bandeja unificada. NexoMail no es un servidor de correo: en la primera fase usa datos de demostración y no guarda mensajes, cuerpos ni adjuntos.

## Arquitectura

- `src/backend/NexoMail.Domain`: modelos normalizados de correo.
- `src/backend/NexoMail.Application`: contrato `IMailProvider` y gateway de aplicación.
- `src/backend/NexoMail.Infrastructure`: proveedor demo y proveedor Gmail mediante OAuth 2.0.
- `src/backend/NexoMail.Api`: API REST, OAuth local y almacenamiento de credenciales cifradas.
- `src/frontend`: React, TypeScript, Vite, Tailwind 4 y TanStack Query.

La UI no conoce las clases de Graph o Gmail. El backend normaliza los datos y los expone como `MailSummary`, `MailMessage`, `MailAccount` y `ComposeMessage`. El HTML se filtra antes de mostrarse, bloqueando contenido activo e imágenes remotas.

## Ejecutar

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

`MailProviders:DemoMode` está habilitado en `src/backend/NexoMail.Api/appsettings.json`. Incluye tres cuentas y veintiún mensajes ficticios. En este modo enviar, responder, reenviar y marcar leído simulan las operaciones; solo el estado de lectura se mantiene mientras la API está en memoria.

## Conectar Gmail localmente

En Google Cloud, el cliente OAuth de tipo **Aplicación web** debe tener esta URI de redirección exacta:

`http://localhost:5052/api/oauth/google/callback`

Agrega tu correo como usuario de prueba y habilita los permisos `gmail.modify` y `gmail.send`. Después guarda las credenciales del cliente en User Secrets y desactiva el modo demo:

```powershell
dotnet user-secrets set "Google:ClientId" "TU_CLIENT_ID" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
dotnet user-secrets set "Google:ClientSecret" "TU_CLIENT_SECRET" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
dotnet user-secrets set "MailProviders:DemoMode" "false" --project .\src\backend\NexoMail.Api\NexoMail.Api.csproj
```

Reinicia la API, abre `http://localhost:5173/settings/accounts` y selecciona **Agregar Gmail**. El consentimiento ocurre directamente en Google; NexoMail guarda solo la cuenta y el refresh token cifrado con DPAPI de Windows. No guarda mensajes, cuerpos ni adjuntos.

## Tema

Los tokens visuales viven en `src/frontend/src/styles/theme.css`. Modificar las variables CSS de `:root` cambia el aspecto de toda la interfaz; el tema oscuro se define en `[data-theme="dark"]`.
