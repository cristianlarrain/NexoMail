# Nexo Intelligence Engine — diseño inicial de servicios

Fecha: 2026-09-14
Rama: `feature/nexo-intelligence-engine-services`
Estado: diseño para primera implementación.

## 1. Objetivo

Crear una capa de servicios reutilizable que permita a NexoMail evolucionar desde un clasificador acoplado al Centro de Control hacia un motor genérico de:

- accionabilidad;
- prioridad;
- estado de conversación;
- explicación de decisiones.

La primera implementación debe convivir con el Centro de Control actual y no reemplazarlo todavía. El objetivo de esta fase es crear contratos, modelos y servicios determinísticos base con pruebas independientes.

## 2. Restricciones obligatorias

1. Ninguna regla global puede depender de nombres, direcciones, dominios, instituciones o asuntos particulares de las cuentas usadas durante el desarrollo.
2. Los casos reales actuales pueden convertirse en regresiones, pero deben resolverse por señales generales.
3. El motor no debe depender de Gmail. Gmail será un adaptador de datos, no parte del núcleo.
4. La máquina de estados será determinística y auditable.
5. Un futuro modelo semántico debe ser intercambiable mediante interfaz.
6. La primera fase no hará fine-tuning ni enviará contenido a un nuevo proveedor externo.
7. No se modifica producción ni se reemplaza el clasificador vigente hasta una fase posterior de integración controlada.

## 3. Alternativas consideradas

### A. Seguir ampliando `ControlCenterMessageClassifier`

Ventaja: menor trabajo inmediato.

Desventaja: aumenta el sobreajuste, mezcla proveedor, política de producto y clasificación, y dificulta reutilizar el motor fuera del correo actual.

**Descartada como arquitectura de largo plazo.**

### B. Delegar toda la decisión a un LLM

Ventaja: flexibilidad semántica y rápida cobertura lingüística.

Desventaja: costo, latencia, menor auditabilidad, dependencia de proveedor y riesgo de que el modelo controle estados de negocio.

**Descartada como núcleo. Puede incorporarse posteriormente como señal semántica.**

### C. Motor híbrido modular

Señales objetivas + servicios determinísticos + proveedor semántico intercambiable + personalización separada.

**Seleccionada.** Permite comenzar sin un LLM y añadir modelos libres posteriormente sin cambiar los contratos del producto.

## 4. Ubicación en la solución

La primera fase utilizará los proyectos existentes:

### `NexoMail.Application`

Contendrá contratos y tipos públicos del motor. No debe referenciar EF Core, Gmail ni infraestructura.

Namespace propuesto:

`NexoMail.Application.Intelligence`

Archivos iniciales:

- `CommunicationModels.cs`
- `IActionabilityAnalyzer.cs`
- `IPriorityScorer.cs`
- `IConversationStateResolver.cs`
- `ICommunicationIntelligenceService.cs`

### `NexoMail.Infrastructure`

Contendrá implementaciones determinísticas iniciales.

Namespace propuesto:

`NexoMail.Infrastructure.Intelligence`

Archivos iniciales:

- `DeterministicActionabilityAnalyzer.cs`
- `DeterministicPriorityScorer.cs`
- `DeterministicConversationStateResolver.cs`
- `CommunicationIntelligenceService.cs`

En esta fase no se agrega persistencia nueva ni migraciones.

### Pruebas

Se agregará un proyecto o suite específica de regresión para Intelligence si la estructura actual lo permite sin sobrecargar los smoke tests del Centro de Control. La preferencia es un proyecto pequeño y aislado `NexoMail.IntelligenceSmokeTests` incluido en la solución.

## 5. Modelo de entrada

El núcleo recibirá una representación agnóstica del proveedor.

### `CommunicationMessage`

Campos mínimos:

- `MessageId`
- `ConversationId`
- `OccurredAt`
- `Direction` (`Received` / `Sent`)
- `Subject`
- `FromAddress`
- `ToAddresses`
- `CcAddresses`
- `IsRead`
- `IsDirectRecipient`
- `IsAutomated`
- `IsBulk`
- `HasListUnsubscribe`
- `AutoSubmitted`
- `Precedence`

El cuerpo completo no será obligatorio para el motor base. Se podrá añadir una vista semántica sanitizada en fases posteriores.

### `CommunicationConversation`

Incluye:

- identificador estable;
- mensajes ordenados;
- direcciones propias del usuario;
- instante de evaluación;
- señales de relación opcionales.

## 6. Accionabilidad

### Contrato

`IActionabilityAnalyzer.Analyze(CommunicationConversation)` devuelve `ActionabilityAssessment`.

Salida inicial:

- `IsActionable`
- `ActionType`
- `Confidence` entre 0 y 1
- `Deadline` opcional
- `ReasonCodes`

Tipos de acción iniciales:

- `None`
- `Reply`
- `Confirm`
- `Review`
- `CompleteTask`
- `WaitForExternal`
- `Unknown`

### Estrategia determinística inicial

Reglas de alta confianza y generales:

- mensajes automáticos/listas/bulk disminuyen accionabilidad;
- si el último mensaje es recibido y directo, aumenta accionabilidad;
- si el último mensaje es enviado a un tercero y no existe respuesta posterior, puede representar `WaitForExternal`;
- envíos exclusivamente a direcciones propias nunca son `WaitForExternal`;
- un hilo cuyo último mensaje indica una respuesta entrante ya no puede considerarse esperando al externo;
- el analizador no usa nombres, dominios o asuntos exactos particulares.

Las señales semánticas por texto serán una extensión futura y no se codificarán como listas específicas del dataset personal.

## 7. Estado de conversación

### Estados

Enum inicial `ConversationWorkState`:

- `New`
- `PendingUser`
- `WaitingExternal`
- `Resolved`
- `Cancelled`
- `Overdue`

### Reglas base

- recibido accionable como último mensaje → `PendingUser`;
- enviado a destinatario externo como último mensaje → `WaitingExternal`;
- respuesta externa posterior a un envío → vuelve a `PendingUser` si es accionable;
- una resolución explícita suministrada por otra capa puede llevar a `Resolved`;
- `Overdue` es un estado derivado cuando existe trabajo pendiente y se supera un umbral/plazo;
- nunca se infiere `Resolved` únicamente porque un asunto contiene una frase concreta.

La máquina de estados aceptará evidencia semántica futura (`StateEvidence`) sin depender de un proveedor específico.

## 8. Prioridad

### Contrato

`IPriorityScorer.Score(CommunicationConversation, ActionabilityAssessment, ConversationStateAssessment)` devuelve `PriorityAssessment`.

Salida:

- `Score` normalizado 0..100;
- `Band`: `Low`, `Normal`, `High`, `Critical`;
- `ReasonCodes`;
- desglose opcional de señales.

### Principios

- solamente elementos accionables reciben prioridad de trabajo;
- antigüedad es una señal, no la decisión principal;
- una solicitud directa no leída puede superar a una conversación antigua genérica;
- automatización/bulk reduce score;
- plazo conocido aumenta score al aproximarse;
- `PendingUser` se pondera distinto de `WaitingExternal`;
- ninguna señal individual dependiente de un usuario puede transformarse en regla global.

Los pesos iniciales serán constantes explícitas y testeables, no parámetros ocultos.

## 9. Servicio orquestador

`ICommunicationIntelligenceService.Analyze(CommunicationConversation)` ejecutará:

1. validación y normalización mínima;
2. `IActionabilityAnalyzer`;
3. `IConversationStateResolver`;
4. `IPriorityScorer`;
5. composición de `CommunicationIntelligenceResult`.

El resultado expondrá:

- accionabilidad;
- estado;
- prioridad;
- razones;
- versión del motor.

Esto permitirá persistir resultados posteriormente y comparar versiones durante migraciones.

## 10. Explicabilidad

Los servicios devolverán códigos, no frases localizadas. Ejemplos:

- `DIRECT_RECIPIENT`
- `UNREAD`
- `AUTOMATED`
- `BULK`
- `OWN_ACCOUNT_ONLY`
- `LATEST_RECEIVED`
- `LATEST_SENT_EXTERNAL`
- `WAITING_EXTERNAL`
- `PENDING_USER`
- `AGE_SIGNAL`
- `DEADLINE_SIGNAL`

La UI/Nexi será responsable de traducir los códigos a lenguaje natural.

## 11. Proveedor semántico futuro

Se reserva una interfaz futura `ISemanticCommunicationAnalyzer` que podrá ser implementada mediante:

- modelo local/ONNX;
- modelo abierto ejecutado en infraestructura propia;
- servicio externo autorizado;
- modelo especializado fine-tuned.

Su salida será evidencia estructurada y nunca modificará directamente persistencia o estados.

El núcleo seguirá funcionando si no existe proveedor semántico.

## 12. Privacidad

La primera fase trabaja sobre objetos en memoria y no crea datasets de entrenamiento.

Reglas de diseño:

- no registrar cuerpo o contenido sensible innecesario;
- separar telemetría técnica de contenido;
- no compartir aprendizaje individual entre usuarios;
- cualquier futura creación de dataset requerirá decisión y política separadas.

## 13. Integración posterior con Centro de Control

No forma parte del primer commit funcional.

La migración futura deberá ejecutarse en modo comparación:

- clasificador actual produce resultado A;
- Nexo Intelligence Engine produce resultado B;
- se registran métricas agregadas/diferencias sin alterar inicialmente la experiencia;
- solo después de validar generalización se cambia el Centro de Control al nuevo motor.

Así evitamos sustituir una lógica funcional por una arquitectura nueva sin evidencia.

## 14. Pruebas de aceptación de la primera fase

La implementación inicial se considera válida si demuestra al menos:

1. un recibido directo/no leído puede ser accionable sin conocer remitente o dominio;
2. una comunicación automática/bulk puede clasificarse como no accionable mediante metadatos generales;
3. un envío únicamente entre cuentas propias no queda esperando respuesta;
4. un envío a un tercero sin respuesta queda `WaitingExternal`;
5. una respuesta posterior del tercero cambia el estado de forma coherente;
6. prioridad no depende exclusivamente de antigüedad;
7. todas las decisiones entregan `ReasonCodes`;
8. los tests no contienen nombres, dominios o asuntos particulares del usuario como condición del motor;
9. la solución y los smoke tests existentes continúan compilando/pasando.

## 15. Fuera de alcance de esta primera fase

- entrenamiento/fine-tuning;
- conexión a Hugging Face u otro proveedor;
- embeddings/vector DB;
- persistencia de perfiles de relación;
- UI nueva;
- reemplazo del Centro de Control existente;
- exposición pública del motor como API;
- definición legal definitiva de licencia/open source.

## 16. Evolución prevista

Fase 1: contratos + baseline determinístico + tests.

Fase 2: adaptador desde `MailMessageIndex` y ejecución en paralelo con Control Center.

Fase 3: señales semánticas multilingües mediante proveedor intercambiable.

Fase 4: personalización por usuario y relación.

Fase 5: persistencia/versionado de resultados y aprendizaje.

Fase 6: empaquetado independiente (SDK/API/Docker) y decisión de liberación/comercialización.