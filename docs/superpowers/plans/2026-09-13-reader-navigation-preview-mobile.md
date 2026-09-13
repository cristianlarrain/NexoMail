# Reader Navigation Preview and Mobile Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unificar las acciones del lector, reparar la vista previa, ordenar la navegación por rol y mejorar la legibilidad móvil de NexoMail desde 360 px.

**Architecture:** Mantener los endpoints y componentes existentes, extraer únicamente la construcción de URL de adjuntos a una función comprobable y reorganizar la interfaz mediante clases semánticas. La navegación seguirá usando React Router y `localStorage`; los cambios responsivos se concentrarán en las hojas móviles existentes para evitar reglas dispersas.

**Tech Stack:** React 19, TypeScript, React Router, TanStack Query, CSS, .NET 10 Minimal API, Node smoke tests, pnpm.

**Spec:** `docs/superpowers/specs/2026-09-13-reader-navigation-preview-mobile-design.md`

## Global Constraints

- Soporte mínimo desde 360 px.
- Funcionamiento equivalente en temas claro y oscuro.
- Texto general móvil mínimo de 15 px e información secundaria mínima de 13 px.
- Controles táctiles con 44 px de alto o superficie equivalente.
- Perspectiva global oculta completamente en teléfonos.
- No modificar permisos comerciales, sincronización ni algoritmos de búsqueda.
- El paquete IONOS debe compilar y superar los smoke tests antes de publicarse.

---

### Task 1: Construcción segura de URLs de adjuntos

**Files:**
- Create: `src/frontend/src/utils/attachmentUrl.ts`
- Create: `scripts/attachment-preview-smoke.mjs`
- Modify: `src/frontend/src/api/mailApi.ts`
- Modify: `src/backend/NexoMail.Api/Program.cs`

**Interfaces:**
- Produces: `buildAttachmentUrl(accountId: string, messageId: string, attachment: MailAttachment, download?: boolean): string`.
- Consumes: ruta existente `GET /api/mail/messages/{accountId}/{messageId}/attachments/{attachmentId}`.

- [ ] **Step 1: Write the failing test**

Crear casos con espacios, `+`, `/`, `?` y `#` que exijan segmentos codificados, `fileName` codificado y fragmento PDF agregado después de la consulta. Verificar también que descarga no incluya fragmento.

- [ ] **Step 2: Run test to verify it fails**

Run: `node scripts/attachment-preview-smoke.mjs`

Expected: FAIL porque `buildAttachmentUrl` aún no existe y la URL actual no satisface todos los casos reservados.

- [ ] **Step 3: Write minimal implementation**

Implementar una función pura que produzca:

```ts
const path = `/api/mail/messages/${encodeURIComponent(accountId)}/${encodeURIComponent(messageId)}/attachments/${encodeURIComponent(attachment.id)}`
const query = new URLSearchParams({ fileName: attachment.name })
if (download) query.set('download', 'true')
return `${path}?${query}${!download && isPdf ? '#page=1&view=Fit&zoom=page-fit&navpanes=0&pagemode=none' : ''}`
```

Normalizar en el backend los errores de adjunto como JSON para 400, 404 y 502 sin devolver páginas HTML dentro del visor.

- [ ] **Step 4: Run test to verify it passes**

Run: `node scripts/attachment-preview-smoke.mjs && pnpm --dir src/frontend build`

Expected: PASS y build con exit code 0.

- [ ] **Step 5: Commit**

```bash
git add scripts/attachment-preview-smoke.mjs src/frontend/src/utils/attachmentUrl.ts src/frontend/src/api/mailApi.ts src/backend/NexoMail.Api/Program.cs
git commit -m "fix: make attachment previews use safe urls"
```

### Task 2: Barra única del lector y herramientas Nexi compactas

**Files:**
- Create: `scripts/message-reader-layout-smoke.mjs`
- Modify: `src/frontend/src/pages/MessagePage.tsx`
- Modify: `src/frontend/src/components/MessageNexiReaderTools.tsx`
- Modify: `src/frontend/src/styles/message-actions.css`
- Modify: `src/frontend/src/styles/attachment-preview.css`

**Interfaces:**
- `MessageNexiReaderTools` recibe `accountId`, `messageId` y `compactActions?: boolean`.
- La barra `unified-message-actions` contiene `message-standard-actions` y `message-ai-actions`.

- [ ] **Step 1: Write the failing test**

Comprobar que el orden renderizado sea navegación, título, barra unificada y metadatos; que Nexi aporte sus botones dentro del grupo IA; y que el resultado del análisis permanezca debajo y sea colapsable.

- [ ] **Step 2: Run test to verify it fails**

Run: `node scripts/message-reader-layout-smoke.mjs`

Expected: FAIL porque las herramientas Nexi todavía ocupan una sección independiente sobre la navegación.

- [ ] **Step 3: Write minimal implementation**

Separar el control de acciones del resultado de Nexi. Insertar `Resumir` y `Analizar conversación` al final de la barra principal; conservar las mutaciones, mensajes de error y resultado existentes. Usar `flex-wrap`, agrupación visual y prioridad de lectura sin duplicar botones.

- [ ] **Step 4: Run test to verify it passes**

Run: `node scripts/message-reader-layout-smoke.mjs && pnpm --dir src/frontend build`

Expected: PASS y build con exit code 0.

- [ ] **Step 5: Commit**

```bash
git add scripts/message-reader-layout-smoke.mjs src/frontend/src/pages/MessagePage.tsx src/frontend/src/components/MessageNexiReaderTools.tsx src/frontend/src/styles/message-actions.css src/frontend/src/styles/attachment-preview.css
git commit -m "feat: unify reader and Nexi actions"
```

### Task 3: Selector persistente de cuentas

**Files:**
- Modify: `src/frontend/scripts/sidebar-modern-navigation-smoke.mjs`
- Modify: `src/frontend/src/layouts/AppLayout.tsx`
- Modify: `src/frontend/src/styles/sidebar-modern.css`

**Interfaces:**
- Consumes: `selectSidebarAccount(accountId?: string)` y el filtro contextual actual.
- Produces: estado `accountsCollapsed` persistido bajo `nexomail-sidebar-accounts-collapsed`.

- [ ] **Step 1: Write the failing test**

Exigir lista visible inicialmente, control con `aria-expanded`, persistencia en `localStorage` y opciones dentro del flujo del sidebar, no en un popover absoluto.

- [ ] **Step 2: Run test to verify it fails**

Run: `node src/frontend/scripts/sidebar-modern-navigation-smoke.mjs`

Expected: FAIL porque el selector actual se abre como popover y parte cerrado.

- [ ] **Step 3: Write minimal implementation**

Reemplazar `accountsOpen` por `accountsCollapsed`, mostrar las opciones mientras no esté colapsado y conservar el comportamiento compacto cuando todo el sidebar esté colapsado. Limitar la altura del listado con desplazamiento vertical en móvil.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd src/frontend && node scripts/sidebar-modern-navigation-smoke.mjs && pnpm build`

Expected: PASS y build con exit code 0.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/scripts/sidebar-modern-navigation-smoke.mjs src/frontend/src/layouts/AppLayout.tsx src/frontend/src/styles/sidebar-modern.css
git commit -m "feat: keep account selector expanded"
```

### Task 4: Navegación por rol y retorno desde búsqueda

**Files:**
- Create: `scripts/navigation-role-smoke.mjs`
- Modify: `src/frontend/src/layouts/AppLayout.tsx`
- Modify: `src/frontend/src/pages/SearchPage.tsx`
- Modify: `src/frontend/src/pages/AdminUsersPage.tsx`
- Modify: `src/frontend/src/pages/AdminPlansPage.tsx`

**Interfaces:**
- Owner: `Panel de Administración` navega a `/admin/users`.
- Usuario normal: `Plan y uso` navega a `/settings/plan`.
- SearchPage: `returnFromSearch()` usa historial o `/control-center?account={id}` como respaldo.

- [ ] **Step 1: Write the failing test**

Exigir etiquetas y destinos diferenciados por owner, enlaces internos desde Administración de usuarios y una acción Volver antes del encabezado de resultados.

- [ ] **Step 2: Run test to verify it fails**

Run: `node scripts/navigation-role-smoke.mjs`

Expected: FAIL porque owner todavía ve Plan y uso y la búsqueda no tiene Volver.

- [ ] **Step 3: Write minimal implementation**

Renderizar condicionalmente el acceso administrativo en sidebar y menú de perfil. Agregar en Administración de usuarios accesos a Tipos de cuenta, Consumo Nexi y Plan y uso. Añadir `ArrowLeft` y retorno con respaldo contextual en SearchPage.

- [ ] **Step 4: Run test to verify it passes**

Run: `node scripts/navigation-role-smoke.mjs && pnpm --dir src/frontend build`

Expected: PASS y build con exit code 0.

- [ ] **Step 5: Commit**

```bash
git add scripts/navigation-role-smoke.mjs src/frontend/src/layouts/AppLayout.tsx src/frontend/src/pages/SearchPage.tsx src/frontend/src/pages/AdminUsersPage.tsx src/frontend/src/pages/AdminPlansPage.tsx
git commit -m "feat: separate owner administration navigation"
```

### Task 5: Centro de Control y escala móvil global

**Files:**
- Modify: `scripts/mobile-layout-smoke.mjs`
- Modify: `src/frontend/src/styles/control-center-cleanup.css`
- Modify: `src/frontend/src/styles/control-center-spacing.css`
- Modify: `src/frontend/src/styles/universal-mobile-layout.css`
- Modify: `src/frontend/src/styles/mobile-foundation.css`
- Modify: `src/frontend/src/styles/sidebar-modern.css`

**Interfaces:**
- Consume las clases existentes `nexi-control-header`, `control-tabs`, `control-metrics`, `global-nexo-perspective` y `TopSearchBox`.
- Produce reglas móviles únicas bajo `@media (max-width: 767px)`.

- [ ] **Step 1: Write the failing test**

Exigir perspectiva con `display: none`, búsqueda y cabecera Nexi con `width: 100%`, pestañas sin compresión, cuadrícula adaptable, texto general mínimo de 15 px, secundarios de 13 px y controles de 44 px.

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir src/frontend test:mobile-layout`

Expected: FAIL porque la escala actual conserva textos pequeños y no oculta Perspectiva global en todas las secciones.

- [ ] **Step 3: Write minimal implementation**

Consolidar bajo el breakpoint móvil las reglas de tipografía y superficie táctil. Usar `grid-template-columns: repeat(auto-fit, minmax(...))` para métricas, `width: 100%` para buscador y cabecera, y `overflow-x: auto` con `flex: 0 0 auto` para pestañas. Ocultar `.global-nexo-perspective` sólo en móvil.

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir src/frontend test:mobile-layout && pnpm --dir src/frontend build`

Expected: PASS y build con exit code 0.

- [ ] **Step 5: Commit**

```bash
git add scripts/mobile-layout-smoke.mjs src/frontend/src/styles/control-center-cleanup.css src/frontend/src/styles/control-center-spacing.css src/frontend/src/styles/universal-mobile-layout.css src/frontend/src/styles/mobile-foundation.css src/frontend/src/styles/sidebar-modern.css
git commit -m "fix: improve Control Center and mobile readability"
```

### Task 6: Validación consolidada y paquete productivo

**Files:**
- Modify only if a validation exposes a regression in files already listed above.

**Interfaces:**
- Produces: rama publicable y artefacto `NexoMail-IONOS-production`.

- [ ] **Step 1: Run all focused checks**

```bash
node scripts/attachment-preview-smoke.mjs
node scripts/message-reader-layout-smoke.mjs
node scripts/navigation-role-smoke.mjs
pnpm --dir src/frontend test:mobile-layout
node scripts/nexi-operational-inbox-smoke.mjs
```

Expected: todos PASS.

- [ ] **Step 2: Run frontend quality gates**

```bash
pnpm --dir src/frontend lint
pnpm --dir src/frontend build
git diff --check
```

Expected: cero errores de lint, build con exit code 0 y diff limpio. Las advertencias preexistentes deben registrarse sin introducir nuevas.

- [ ] **Step 3: Publish branch and verify GitHub workflows**

Actualizar `feature/account-colors-nexi-mobile-premium` sin forzar la referencia. Verificar Commercial subscription, Draft lifecycle, Control center y IONOS production package para el SHA publicado.

- [ ] **Step 4: Inspect production artifact**

Confirmar dentro del artefacto:

```text
NexoMail.Api.dll
NexoMail.Infrastructure.dll
NexoMail.Application.dll
NexoMail.Domain.dll
web.config
wwwroot/index.html
```

- [ ] **Step 5: Final commit if validation required corrections**

```bash
git status --short
git log -1 --oneline
```

Expected: árbol limpio y último SHA identificado para el despliegue.
