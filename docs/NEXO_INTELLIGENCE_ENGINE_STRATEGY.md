# Nexo Intelligence Engine — estrategia de producto y futura liberación

Fecha: 2026-09-14
Estado: visión estratégica; no define todavía una licencia pública definitiva.

## 1. Origen

El desarrollo del Centro de Control de NexoMail mostró que el verdadero problema no es solamente ordenar correo. El valor reutilizable está en transformar comunicaciones en trabajo gestionable:

- determinar si una comunicación requiere acción;
- estimar su importancia y urgencia;
- mantener el estado de una conversación o situación a medida que llegan nuevos mensajes;
- explicar por qué algo fue clasificado como prioritario;
- aprender preferencias individuales sin convertirlas en reglas globales.

Esta capacidad se denominará provisionalmente **Nexo Intelligence Engine**.

NexoMail será el primer consumidor del motor, pero el diseño no debe depender de Gmail, de un proveedor concreto ni de las cuentas usadas durante el desarrollo.

## 2. Problema que resuelve

La bandeja tradicional mezcla al menos tres conceptos diferentes:

1. **Importancia**: ¿qué tan relevante es esto para esta persona?
2. **Accionabilidad**: ¿hay algo que esta persona deba hacer, responder, confirmar, revisar o decidir?
3. **Urgencia**: si requiere acción, ¿cuándo debería atenderse frente a otras tareas?

Además, una conversación cambia de estado. Un mensaje puede iniciar una tarea, otro puede dejarla esperando respuesta y un mensaje posterior puede resolverla. El motor debe razonar sobre la situación completa, no tratar cada correo como una tarea independiente.

## 3. Principio de generalización

Los correos reales usados durante el desarrollo sirven como **casos de validación y regresión**, no como fuente de reglas específicas del producto.

No se aceptarán reglas globales basadas en:

- nombres de personas concretas;
- direcciones particulares;
- dominios particulares;
- asuntos exactos pertenecientes a una cuenta;
- instituciones concretas;
- cantidad específica de cuentas de un usuario.

El motor debe funcionar para un usuario nuevo cuyos mensajes, idioma, organización y volumen sean diferentes a los del entorno de desarrollo.

## 4. Arquitectura conceptual

El motor se divide en módulos independientes:

### 4.1 Nexo.Signals

Extrae señales observables y explicables de mensajes e hilos:

- remitente y destinatarios;
- destinatario directo, CC o lista;
- cantidad de participantes;
- leído/no leído;
- dirección recibido/enviado;
- encabezados estándar como Auto-Submitted, Precedence, List-Id y List-Unsubscribe;
- comportamiento del hilo;
- reciprocidad y frecuencia histórica;
- antigüedad y tiempo desde la última acción;
- presencia de fechas, plazos o solicitudes;
- señales de automatización y distribución masiva.

### 4.2 Nexo.Actionability

Responde: **¿requiere acción del usuario?**

Salida mínima prevista:

- actionable: bool;
- actionType;
- confidence;
- detectedDeadline;
- explanation codes.

El sistema puede combinar reglas determinísticas de alta confianza con un analizador semántico intercambiable.

### 4.3 Nexo.ThreadState

Mantiene una máquina de estados explícita. Estados iniciales propuestos:

- New;
- PendingUser;
- WaitingExternal;
- Resolved;
- Cancelled;
- Overdue.

La IA o el clasificador propone evidencia; la transición final de estado se realiza mediante reglas determinísticas y auditables.

### 4.4 Nexo.Priority

Ordena únicamente elementos accionables. El puntaje debe combinar señales genéricas como:

- plazo o vencimiento;
- solicitud explícita;
- no leído;
- destinatario directo;
- conversación activa;
- reciprocidad con el contacto;
- tiempo pendiente;
- estado WaitingExternal/PendingUser;
- señales de baja relevancia o automatización.

La antigüedad por sí sola nunca debe definir prioridad.

### 4.5 Nexo.Relationship

Personalización por usuario. Aprende señales como frecuencia de intercambio, respuesta habitual y relevancia histórica. Estos datos son privados del usuario y no se convierten automáticamente en reglas globales.

### 4.6 Explainability

Cada decisión debe poder producir razones legibles y códigos auditables, por ejemplo:

- DIRECT_RECIPIENT;
- UNREAD;
- EXPLICIT_REQUEST;
- DEADLINE_DETECTED;
- WAITING_FOR_EXTERNAL;
- AUTOMATED_MESSAGE;
- BULK_LIST;
- LOW_USER_INTERACTION.

Nexi podrá convertir esos códigos en una explicación natural.

## 5. Modelo semántico

El diseño debe permitir distintos proveedores de inferencia mediante una interfaz estable. En una primera etapa puede existir una implementación determinística sin dependencia de LLM.

Posteriormente podrán probarse modelos abiertos multilingües o un modelo especializado/fine-tuned. El modelo semántico nunca será propietario de la máquina de estados ni de la política de negocio.

## 6. Privacidad y aprendizaje

El motor debe separar claramente:

- modelo/reglas globales;
- parámetros de producto;
- perfil aprendido por usuario;
- datos originales de mensajes.

Los mensajes privados no deben incorporarse a un dataset público o entrenamiento compartido por defecto. Cualquier futuro entrenamiento con datos reales requerirá una política específica de consentimiento, minimización, anonimización/pseudonimización y retención.

## 7. Formas futuras de distribución

El motor puede evolucionar hacia uno o varios formatos:

- biblioteca .NET;
- paquete independiente;
- servicio HTTP/REST;
- contenedor Docker;
- servicio SaaS;
- modelo publicable en Hugging Face u otro repositorio, si su licencia lo permite.

## 8. Estrategias de liberación/comercialización

### Opción A — completamente abierto

Código y eventualmente modelo disponibles públicamente. Favorece comunidad, adopción y reputación técnica, pero reduce exclusividad comercial.

### Opción B — open core / híbrido

Contratos, SDK o parte del modelo pueden ser abiertos; orquestación, aprendizaje personal, estado avanzado, administración y servicios empresariales permanecen propietarios.

Esta es actualmente la opción estratégica preferida para evaluación futura.

### Opción C — motor propietario como API

NexoMail conserva implementación y modelos, y terceros consumen una API por uso, cuenta, usuario o volumen.

## 9. Posibles consumidores

El motor no debe limitarse al correo. Puede aplicarse a:

- clientes de correo;
- bandejas compartidas;
- CRM;
- ticketing y mesas de ayuda;
- atención al cliente;
- universidades y servicios públicos;
- estudios jurídicos;
- sistemas documentales;
- Teams, Slack u otros canales;
- mensajería empresarial cuando exista integración autorizada.

El patrón general es:

**comunicación → accionabilidad → responsable → plazo → prioridad → estado**.

## 10. Activo tecnológico

La hipótesis estratégica es que NexoMail puede convertirse en la aplicación visible mientras que **Nexo Intelligence Engine** constituye un activo tecnológico reutilizable y eventualmente licenciable o comercializable de forma independiente.

Toda evolución técnica debe preservar esa posibilidad mediante interfaces agnósticas del proveedor y ausencia de reglas específicas de un usuario.