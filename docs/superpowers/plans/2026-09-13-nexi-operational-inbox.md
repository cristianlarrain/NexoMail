# Nexi Operational Inbox Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convertir Nexi Control Center en la pantalla inicial y permitir gestionar correos directamente desde su lista priorizada, conservando la bandeja actual como Vista clásica.

**Architecture:** Reutilizar las operaciones existentes de correo y seguimiento, añadir una preferencia persistida para suprimir urgencia y concentrar la interacción en `ControlCenter`/`NexiPriorityQueue`. El enrutamiento mantendrá `/inbox` como vista clásica y usará `/control-center` como inicio autenticado.

**Tech Stack:** React 19, React Router, TanStack Query, TypeScript, ASP.NET Core 10, EF Core, MSSQL/SQLite, MailKit y CSS responsivo.

**Spec:** `docs/superpowers/specs/2026-09-13-nexi-operational-inbox-design.md`

## Global Constraints

- Mantener funcionamiento desde 360 px y en temas claro y oscuro.
- No ejecutar respuestas ni eliminaciones sin acción explícita del usuario.
- Reutilizar endpoints de correo existentes y conservar las capacidades comerciales.
- Mantener `/inbox` como Vista clásica sin pérdida funcional.
- Las operaciones de producción compatibles con IONOS deben usar verbos admitidos por el hosting.

---

### Task 1: Navegación e inicio operativo

**Files:**
- Modify: `src/frontend/src/router.tsx`
- Modify: `src/frontend/src/layouts/AppLayout.tsx`
- Test: `scripts/nexi-operational-inbox-smoke.mjs`

**Interfaces:**
- Consumes: rutas existentes `/control-center` y `/inbox`.
- Produces: inicio autenticado en `/control-center` y acceso rotulado `Vista clásica` a `/inbox`.

- [ ] **Step 1: Write the failing test**

Crear un smoke test que exija que el logotipo navegue a `/control-center`, que la navegación principal muestre `Inicio` y `Vista clásica`, y que `/inbox` continúe renderizando `InboxPage`.

- [ ] **Step 2: Run test to verify it fails**

Run: `node scripts/nexi-operational-inbox-smoke.mjs`
Expected: FAIL indicando que el logotipo todavía abre `/inbox`.

- [ ] **Step 3: Write minimal implementation**

Actualizar `AppLayout` para usar `/control-center` como destino del logotipo y navegación inicial; rotular `/inbox` como `Vista clásica`. Conservar las rutas de cuentas y carpetas.

- [ ] **Step 4: Run test to verify it passes**

Run: `node scripts/nexi-operational-inbox-smoke.mjs`
Expected: PASS para navegación.

- [ ] **Step 5: Commit**

```bash
git add scripts/nexi-operational-inbox-smoke.mjs src/frontend/src/router.tsx src/frontend/src/layouts/AppLayout.tsx
git commit -m "feat: make Nexi Control Center the operational home"
```

### Task 2: Preferencia persistida para quitar urgencia

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/ControlCenterTrackingService.cs`
- Modify: `src/backend/NexoMail.Api/ControlCenterTrackingEndpoints.cs`
- Modify: `src/frontend/src/api/mailApi.ts`
- Test: `src/backend/NexoMail.ControlCenterSmokeTests/Program.cs`

**Interfaces:**
- Produces: `GetPriorityOverridesAsync(Guid? accountId)`, `SetPriorityOverrideAsync(Guid accountId, string messageId, bool suppressed)` y endpoints POST de consulta/cambio compatibles con IONOS.
- Consumes: `ControlCenterStates` con clave `priority:<messageId>` y estado `not_urgent`.

- [ ] **Step 1: Write the failing test**

Agregar al smoke test una supresión de urgencia, comprobar que aparece al consultar preferencias, revertirla y comprobar que desaparece.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests`
Expected: FAIL porque el servicio aún no expone preferencias de prioridad.

- [ ] **Step 3: Write minimal implementation**

Persistir sólo metadatos por usuario/cuenta/mensaje en `ControlCenterStates`. Exponer listado por cuenta y una operación POST `{ suppressed: boolean }`; validar que la cuenta pertenezca al usuario.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests`
Expected: PASS incluyendo supresión y restauración.

- [ ] **Step 5: Commit**

```bash
git add src/backend/NexoMail.Infrastructure/ControlCenterTrackingService.cs src/backend/NexoMail.Api/ControlCenterTrackingEndpoints.cs src/frontend/src/api/mailApi.ts src/backend/NexoMail.ControlCenterSmokeTests/Program.cs
git commit -m "feat: persist Nexi urgency overrides"
```

### Task 3: Acciones directas en la bandeja inteligente

**Files:**
- Modify: `src/frontend/src/components/ControlCenter.tsx`
- Modify: `src/frontend/src/components/NexiPriorityQueue.tsx`
- Modify: `src/frontend/src/styles/nexi-priority.css`
- Modify: `src/frontend/src/styles/nexi-control-center.css`
- Test: `scripts/nexi-operational-inbox-smoke.mjs`

**Interfaces:**
- Consumes: `mailApi.trackMessage`, `untrackMessage`, `updateControlCenterState`, `setPriorityOverride` y `trash`.
- Produces: handlers directos para responder/seguir, resolver, posponer, quitar urgencia, eliminar y ver.

- [ ] **Step 1: Write the failing test**

Exigir botones con verbos `Responder`/`Seguir`, `Resolver`, `Posponer`, `Quitar urgencia`, `Eliminar` y `Ver`, además de confirmación previa a papelera e invalidación de consultas.

- [ ] **Step 2: Run test to verify it fails**

Run: `node scripts/nexi-operational-inbox-smoke.mjs`
Expected: FAIL por acciones inexistentes en la cola priorizada.

- [ ] **Step 3: Write minimal implementation**

Centralizar mutaciones en `ControlCenter`, bloquear por fila durante una acción, actualizar optimistamente el snapshot, revertir en error y mostrar confirmación antes de `trash`. Pasar handlers explícitos a `NexiPriorityQueue` y mantener el asunto como acceso al detalle.

- [ ] **Step 4: Run test to verify it passes**

Run: `node scripts/nexi-operational-inbox-smoke.mjs`
Expected: PASS para acciones directas.

- [ ] **Step 5: Commit**

```bash
git add scripts/nexi-operational-inbox-smoke.mjs src/frontend/src/components/ControlCenter.tsx src/frontend/src/components/NexiPriorityQueue.tsx src/frontend/src/styles/nexi-priority.css src/frontend/src/styles/nexi-control-center.css
git commit -m "feat: add direct actions to Nexi operational inbox"
```

### Task 4: Filtros por categoría y cuenta, Vista clásica y responsividad

**Files:**
- Modify: `src/frontend/src/pages/ControlCenterPage.tsx`
- Modify: `src/frontend/src/components/ControlCenter.tsx`
- Modify: `src/frontend/src/components/NexiPriorityQueue.tsx`
- Modify: `src/frontend/src/styles/universal-mobile-layout.css`
- Modify: `scripts/test-universal-mobile-layout.mjs`
- Test: `scripts/nexi-operational-inbox-smoke.mjs`

**Interfaces:**
- Produces: encabezado con `Vista clásica`, selector lateral compartido, tarjeta/filtro activo y tarjetas de correo desde 360 px.
- Consumes: cuentas disponibles del snapshot, estado de filtro de `NexiPriorityQueue`, parámetro `account` y navegación `/inbox`.

- [ ] **Step 1: Write the failing tests**

Exigir acceso visible a Vista clásica, selector lateral aplicable a ambas bandejas, filtros Todos/Urgentes/Responder/Seguimiento/Sin leer y reglas móviles que conviertan cada fila en tarjeta sin tabla comprimida.

- [ ] **Step 2: Run tests to verify they fail**

Run: `node scripts/nexi-operational-inbox-smoke.mjs && pnpm --dir src/frontend test:mobile-layout`
Expected: FAIL por controles y reglas móviles faltantes.

- [ ] **Step 3: Write minimal implementation**

Agregar el acceso a Vista clásica en el encabezado, hacer que el selector lateral navegue con alcance contextual para ambas bandejas, conectar tarjetas con filtros, filtrar el snapshot por cuenta, conservar cuenta y categoría en la URL y adaptar lista/acciones a dos botones visibles más menú secundario en móvil.

- [ ] **Step 4: Run complete verification**

```bash
node scripts/nexi-operational-inbox-smoke.mjs
pnpm --dir src/frontend test:mobile-layout
pnpm --dir src/frontend lint
pnpm --dir src/frontend build
dotnet run --project src/backend/NexoMail.ControlCenterSmokeTests
```

Expected: todos los tests y compilaciones terminan con código 0; lint puede conservar únicamente advertencias preexistentes.

- [ ] **Step 5: Commit and publish**

```bash
git add src/frontend scripts src/backend docs/superpowers
git commit -m "feat: deliver Nexi operational inbox"
```

Ejecutar el flujo `IONOS production package`, verificar el artefacto y entregar su contenido extraído sin ZIP.
