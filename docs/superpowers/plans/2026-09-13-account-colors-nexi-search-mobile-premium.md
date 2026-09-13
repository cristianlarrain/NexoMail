# Account Colors, Nexi Search, Mobile and Premium Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar colores distintos por cuenta, búsqueda Nexi tolerante, una interfaz utilizable desde 360 px y Premium automático por 30 días para usuarios nuevos verificados.

**Architecture:** Cuatro unidades independientes se integran con los flujos actuales: prueba comercial idempotente al verificar, selector central de colores, ampliación progresiva de búsquedas y estilos responsivos compartidos. Se preservan autenticación, CSRF, aislamiento por usuario y compatibilidad con IONOS/SQL Server y SQLite.

**Tech Stack:** .NET 8, ASP.NET Core Minimal APIs, EF Core, SQL Server/SQLite, React, TypeScript, Vite, CSS.

**Spec:** `docs/superpowers/specs/2026-09-13-account-colors-nexi-search-mobile-design.md`

## Global Constraints

- Ancho mínimo soportado: 360 px.
- Premium de bienvenida: una sola vez, 30 días, sin tarjeta, después de verificar el correo.
- Campaña activable por configuración; desactivarla no cancela pruebas vigentes.
- No modificar colores personalizados ni cuentas existentes.
- No consultar fuentes externas ni mezclar información entre usuarios.
- Mantener compatibilidad con IONOS, SQL Server y SQLite.

---

### Task 1: Premium automático de bienvenida

**Files:**
- Modify: `src/backend/NexoMail.Infrastructure/CommercialSubscriptionMutations.cs`
- Modify: `src/backend/NexoMail.Infrastructure/CommercialAccessStore.cs`
- Modify: `src/backend/NexoMail.Api/Security/AuthEndpoints.cs`
- Modify: `src/backend/NexoMail.Api/Program.cs`
- Test: `src/backend/NexoMail.CommercialSmokeTests/WelcomeTrialSmoke.cs`

**Interfaces:**
- Consumes: `NexoMailDbContext`, usuario verificado y configuración `Commercial:WelcomeTrialEnabled`.
- Produces: `GrantWelcomeTrialAsync(database, userId, 30, ct)` idempotente y proveedor `welcome_trial`.

- [ ] Escribir la prueba que verifica concesión, idempotencia, vencimiento, campaña desactivada y protección de suscripciones pagadas.
- [ ] Ejecutar `dotnet run --project src/backend/NexoMail.CommercialSmokeTests` y confirmar el fallo por ausencia de `GrantWelcomeTrialAsync`.
- [ ] Implementar la mutación mínima e invocarla después de verificar el correo.
- [ ] Ejecutar nuevamente la prueba y confirmar que pasa.
- [ ] Confirmar compatibilidad con SQLite y SQL Server mediante las pruebas comerciales existentes.
- [ ] Commit: `feat: grant welcome premium trial after email verification`.

### Task 2: Colores automáticos por cuenta

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/AccountColorSelector.cs`
- Modify: conectores Google, Microsoft e IMAP que crean `MailAccountEntity`.
- Test: `src/backend/NexoMail.CommercialSmokeTests/AccountColorSelectorSmoke.cs`

**Interfaces:**
- Consumes: colores de cuentas activas del usuario.
- Produces: `AccountColorSelector.Select(IReadOnlyCollection<string>)` con color seguro y determinista.

- [ ] Escribir pruebas de paleta, rotación, fallback y conservación del color personalizado.
- [ ] Ejecutar la prueba y confirmar el fallo por ausencia del selector.
- [ ] Implementar el selector central y conectarlo a los tres proveedores.
- [ ] Ejecutar pruebas comerciales y de conexión de proveedores.
- [ ] Commit: `feat: assign distinct colors to connected accounts`.

### Task 3: Búsqueda progresiva de Nexi

**Files:**
- Create: `src/backend/NexoMail.Infrastructure/NexiParticipantResolver.cs`
- Modify: `src/backend/NexoMail.Infrastructure/AiSearchService.cs`
- Modify: respuesta y presentación de búsqueda en frontend.
- Test: pruebas smoke de resolución y reintento.

**Interfaces:**
- Consumes: consulta interpretada y participantes autorizados del usuario.
- Produces: hasta tres variantes normalizadas y metadatos `interpretedQuery`, `expanded`, `variants`.

- [ ] Escribir pruebas para `Lucas`/`Lukas`, acentos, aislamiento, límites y conservación de filtros.
- [ ] Ejecutar las pruebas y confirmar el fallo esperado.
- [ ] Implementar resolución por distancia acotada y reintento sólo tras cero resultados.
- [ ] Mostrar en la interfaz la interpretación y ampliación aplicadas.
- [ ] Ejecutar pruebas de búsqueda exacta y ampliada.
- [ ] Commit: `feat: broaden Nexi searches with authorized name variants`.

### Task 4: Experiencia móvil desde 360 px

**Files:**
- Modify: primitivas de layout y estilos compartidos en `src/frontend/src/styles/`.
- Modify: navegación, barra superior, buscador, modales y tablas que exceden el viewport.
- Test: compilación TypeScript y comprobación automatizada de reglas responsivas.

**Interfaces:**
- Consumes: estructura actual de `AppShell`, barra superior, panel lateral y tablas.
- Produces: navegación desplegable, buscador compacto y contenedores sin desbordamiento global.

- [ ] Agregar comprobaciones que fallen si faltan los breakpoints 360/768/1024 o existen anchos mínimos globales incompatibles.
- [ ] Corregir primero layout, cabecera, panel lateral y buscador.
- [ ] Adaptar formularios, diálogos y tablas administrativas con tarjetas o scroll contenido.
- [ ] Ejecutar `pnpm build` y revisar 360, 390, 430, 768 y 1024 px en temas claro y oscuro.
- [ ] Commit: `fix: support NexoMail layouts from 360px`.

### Task 5: Validación consolidada

**Files:**
- Modify: documentación de despliegue sólo si aparecen nuevas variables.

**Interfaces:**
- Consumes: los cuatro entregables anteriores.
- Produces: paquete desplegable comprobado para IONOS.

- [ ] Ejecutar pruebas comerciales, proveedores, borradores y Centro de Control.
- [ ] Ejecutar compilación completa de backend y frontend.
- [ ] Revisar que no existan secretos ni cambios ajenos.
- [ ] Publicar la rama y entregar el commit final con instrucciones de despliegue.
