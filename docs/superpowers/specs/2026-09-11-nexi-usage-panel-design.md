# NexoMail — Panel privado de consumo Nexi

Fecha: 2026-09-11
Rama: `feature/commercial-foundation`
Estado: diseño aprobado funcionalmente; pendiente aprobación final de esta especificación antes del plan de implementación.

## 1. Objetivo

Incorporar un subsistema interno que permita al Owner / Administrador general medir el consumo real de Nexi por usuario y por tipo de operación, estimar su costo, observar tendencias semanales y mensuales y proyectar el costo de las pruebas Premium/Nexi de 30 días.

Este módulo no es visible para usuarios finales. Su finalidad es operar pilotos, fijar precios, detectar usos intensivos y conocer el margen potencial de los planes comerciales antes del lanzamiento abierto.

## 2. Alcance funcional

El panel debe permitir al Owner:

- ver consumo de la semana actual y compararlo con la semana anterior;
- ver consumo mensual y acumulado;
- ver operaciones, tokens de entrada y salida y costo estimado;
- separar consumo por usuario y por tipo de operación de Nexi;
- conocer días activos y frecuencia de uso;
- ver el consumo acumulado durante una prueba administrada;
- proyectar el costo estimado al término de una prueba de 30 días o de la duración efectiva configurada;
- aplicar un semáforo de costo proyectado en CLP;
- modificar los umbrales del semáforo;
- conservar 12 meses de detalle y mantener históricos mensuales permanentes.

No debe mostrar ni almacenar para fines estadísticos el contenido del prompt, cuerpo de correos, archivos, respuestas de Nexi ni texto generado por el usuario.

## 3. Autorización

El acceso será exclusivo de usuarios con `IsOwner = true`, representados en la interfaz como `Owner / Administrador general`.

Los administradores comunes (`IsAdministrator = true`) no tendrán acceso por defecto a costos, proyecciones ni consumo individual. Una futura delegación de esta capacidad queda fuera del alcance inicial.

Los endpoints del panel deberán validar la condición de Owner en backend; ocultar la opción en frontend no se considera una barrera de seguridad suficiente.

## 4. Arquitectura propuesta

### 4.1. Cliente central de OpenAI

Se introducirá una capa central para las llamadas de Nexi a OpenAI (`AiResponseClient` o nombre equivalente). Su responsabilidad será:

1. enviar solicitudes a la Responses API;
2. medir duración;
3. leer la información de uso devuelta por la respuesta;
4. devolver el contenido necesario al servicio llamante;
5. informar una operación de consumo a `AiUsageTracker`;
6. registrar fallos sin almacenar el cuerpo del correo, prompt ni respuesta de IA.

Los servicios actuales `AiSearchService`, `AiContextService`, `AiMailInsightsService` y `AiWritingService` deberán usar esta capa en lugar de duplicar el envío HTTP y la extracción de respuesta. Esto evita que nuevas funciones de Nexi queden sin medición.

Una operación que haga un segundo intento real contra OpenAI deberá registrar ese segundo consumo como otra llamada facturable. Ejemplo: si un reporte no logra estructurar JSON y realiza un nuevo intento, ambos consumos se contabilizan.

### 4.2. Servicio de registro

`AiUsageTracker` será responsable exclusivamente de persistir y agregar consumo. Recibirá datos técnicos de la llamada y no tendrá acceso al contenido de correos.

Datos mínimos por evento:

- `Id`;
- `UserId`;
- `OccurredAt`;
- `OperationType`;
- `Model`;
- `InputTokens`;
- `OutputTokens`;
- tokens adicionales disponibles en la respuesta, si corresponde;
- `DurationMs`;
- `Succeeded`;
- categoría genérica de error si falla;
- precio unitario de entrada usado para estimar el costo;
- precio unitario de salida usado para estimar el costo;
- `EstimatedCostUsd`;
- tipo de cambio CLP/USD usado en ese momento;
- `EstimatedCostClp`.

El costo almacenado es una estimación operacional, no un documento contable.

### 4.3. Tipos de operación

La primera versión debe distinguir, como mínimo:

- `search_interpretation` — interpretación inteligente de búsquedas;
- `mail_summary` — resumen de un correo;
- `thread_summary` — resumen de hilo;
- `mail_report` — reporte de varios correos;
- `mail_context_analysis` — preguntas/análisis sobre un conjunto de correos;
- `writing_assistant` — redacción, reescritura o asistencia al escribir;
- `other` — respaldo para nuevas operaciones no categorizadas todavía.

La categoría debe ser estable y apta para estadísticas; no debe depender del texto escrito por el usuario.

## 5. Precios y conversión

El cálculo no debe estimar tokens a partir de caracteres. Debe usar los conteos reales entregados por la API cuando estén disponibles.

La tabla de eventos guardará una instantánea del precio utilizado en el momento de la llamada para que un cambio futuro de modelo o tarifa no altere el histórico.

Configuración inicial:

- catálogo de precios por modelo en configuración de servidor;
- modelo desconocido: registrar tokens y marcar costo como no calculable en vez de inventarlo;
- tipo de cambio CLP/USD configurable para la estimación interna;
- cada evento conserva el tipo de cambio usado, de modo que los históricos no cambien retroactivamente.

La configuración de precios no se expondrá inicialmente al usuario final. El Owner podrá modificar los umbrales del semáforo desde la interfaz. La edición gráfica del precio por modelo queda fuera del alcance inicial; podrá agregarse cuando exista más de un modelo en producción.

## 6. Persistencia

### 6.1. Tabla de detalle `AiUsageEvents`

Una fila por llamada real al proveedor de IA.

Índices principales:

- `UserId + OccurredAt`;
- `OccurredAt`;
- `OperationType + OccurredAt`.

La relación con `Users` tendrá borrado en cascada sólo si la política definitiva de conservación legal permite eliminar también ese histórico. Hasta cerrar el análisis jurídico, la implementación debe encapsular esta decisión para poder cambiarla sin alterar el panel.

### 6.2. Tabla `AiUsageMonthlySummaries`

Acumulado permanente por usuario, año y mes:

- número de operaciones;
- operaciones exitosas y fallidas;
- tokens de entrada;
- tokens de salida;
- costo USD;
- costo CLP estimado;
- días con actividad.

La actualización será incremental al registrar cada evento, evitando reconstruir años de información desde la tabla de detalle.

### 6.3. Configuración `AiUsageSettings`

Configuración interna inicial:

- umbral verde máximo: `$1.500 CLP`;
- umbral amarillo máximo: `$3.000 CLP`;
- sobre `$3.000 CLP`: rojo;
- tipo de cambio CLP/USD de referencia para nuevas operaciones.

Los montos del semáforo serán modificables por el Owner.

## 7. Retención

- Detalle de `AiUsageEvents`: 12 meses.
- `AiUsageMonthlySummaries`: conservación permanente mientras la política legal/comercial no determine otra cosa.
- La limpieza nunca elimina el agregado mensual correspondiente.
- Un proceso de mantenimiento diario eliminará sólo eventos de detalle con antigüedad superior a 12 meses.

La política podrá revisarse durante el bloque de cumplimiento de la Ley 21.719 antes de producción comercial.

## 8. Estadísticas del panel

Ruta propuesta: `Administración → Consumo Nexi`.

### 8.1. Tarjetas superiores

- costo estimado de la semana actual;
- costo de la semana anterior;
- variación porcentual semanal;
- operaciones esta semana;
- usuarios activos con Nexi esta semana;
- costo estimado del mes;
- costo promedio por usuario activo.

### 8.2. Distribución de uso

Mostrar distribución de operaciones por categoría y evolución diaria/semanal. La primera implementación puede usar barras simples y tarjetas; no requiere una librería de visualización pesada si los componentes existentes son suficientes.

### 8.3. Tabla por usuario

Columnas mínimas:

- usuario;
- plan base;
- tipo de acceso efectivo;
- estado de prueba;
- días de prueba transcurridos/restantes cuando corresponda;
- operaciones de la semana;
- tokens totales;
- costo semanal;
- costo acumulado de la prueba;
- promedio diario;
- proyección al final de la prueba;
- semáforo.

Debe poder ordenarse al menos por costo acumulado y costo proyectado.

### 8.4. Detalle de usuario

Al seleccionar un usuario, mostrar:

- últimos 30 días;
- consumo diario;
- distribución por operación;
- costo acumulado;
- promedio diario;
- proyección de la prueba;
- fecha de término de prueba si existe.

No mostrar correos, asuntos, prompts ni resultados de Nexi.

## 9. Proyección de prueba

Para una prueba administrada activa, el panel usará `CommercialSubscriptions.CurrentPeriodStart` como inicio y `TrialEndsAt` como término. El flujo actual ya establece `CurrentPeriodStart` al otorgar una prueba administrada.

La proyección principal será:

`costo acumulado / días transcurridos con calendario × duración total de la prueba`.

Para evitar proyecciones engañosas durante las primeras horas, la interfaz debe identificar los casos con menos de un día completo como `proyección inicial`.

Cuando existan al menos 7 días de historial, también se calculará una proyección basada en el promedio de los últimos 7 días. La interfaz mostrará la proyección principal y podrá indicar si la tendencia reciente está aumentando o disminuyendo.

Para usuarios sin prueba activa se mostrará una proyección mensual de 30 días basada en el ritmo reciente, claramente etiquetada como estimación mensual y no como prueba.

## 10. Semáforo

Valores iniciales:

- verde: proyección `<= $1.500 CLP`;
- amarillo: `$1.501–$3.000 CLP`;
- rojo: `> $3.000 CLP`.

El color depende del costo proyectado, no del costo ya consumido. El panel debe seguir mostrando ambos montos para no ocultar el gasto real.

Los umbrales son globales y configurables por el Owner.

## 11. API administrativa

Endpoints propuestos, todos protegidos por Owner:

- `GET /api/ai-usage/admin/summary?period=week|month`;
- `GET /api/ai-usage/admin/users?...`;
- `GET /api/ai-usage/admin/users/{userId}`;
- `GET /api/ai-usage/admin/settings`;
- `PATCH /api/ai-usage/admin/settings`.

Los endpoints devolverán agregados y métricas, nunca contenido de correos ni texto enviado/recibido por la IA.

## 12. Privacidad y seguridad

El subsistema seguirá el principio de minimización:

- no almacenar prompts;
- no almacenar respuesta generada;
- no almacenar cuerpo, asunto ni remitente del correo para estas métricas;
- no almacenar contenido de adjuntos;
- no exponer costos en endpoints normales de usuario;
- autorización del lado servidor obligatoria;
- registrar sólo categorías de error genéricas, sin copiar respuestas completas del proveedor que pudieran incluir datos sensibles.

Este diseño será revisado posteriormente como parte del trabajo de adecuación a la Ley chilena 21.719 antes de producción comercial.

## 13. Comportamiento ante errores

- Si OpenAI responde con éxito y entrega uso, registrar tokens y costo.
- Si responde con éxito pero el uso no puede leerse, registrar la operación como exitosa con costo `no calculable` y dejarla visible como incidencia de medición.
- Si la llamada falla antes de recibir una respuesta facturable, registrar fallo con costo cero.
- Si falla la persistencia de estadísticas, la funcionalidad principal de Nexi no debe fallar por ese motivo; el error de telemetría debe registrarse en logs técnicos.
- La escritura del evento y del agregado mensual debe mantenerse consistente mediante transacción o una operación idempotente equivalente.

## 14. Pruebas

Backend:

- Owner puede consultar estadísticas; usuario normal y administrador no Owner reciben 403;
- una respuesta con uso genera un evento correcto;
- cálculo de costo para un modelo configurado;
- modelo desconocido no inventa costo;
- reintentos facturables generan eventos separados;
- agregado mensual coincide con la suma de eventos;
- cálculo semanal, mensual y comparación anterior;
- proyección de prueba usando `CurrentPeriodStart` y `TrialEndsAt`;
- semáforo según límites configurados;
- limpieza elimina sólo detalle >12 meses y conserva resumen mensual;
- fallo del tracker no rompe la respuesta normal de Nexi.

Frontend:

- ruta y menú sólo visibles para Owner;
- tarjetas, tabla y detalle consumen los endpoints administrativos;
- estados vacío, cargando y error;
- semáforo y proyección correctos;
- cambio de umbrales se refleja sin recargar datos sensibles;
- contraste correcto en modo claro y oscuro.

## 15. Criterios de aceptación

El módulo se considera listo cuando:

1. todas las llamadas productivas actuales de Nexi quedan registradas centralmente;
2. el Owner puede ver semana actual, semana anterior, mes y usuarios;
3. se puede conocer costo acumulado y proyectado de una prueba;
4. existen los umbrales configurables de $1.500/$3.000 CLP;
5. ningún endpoint o tabla de estadísticas almacena contenido de correo o prompts;
6. los históricos mensuales sobreviven a la limpieza del detalle;
7. las pruebas backend/frontend y builds existentes continúan verdes.

## 16. Fuera de alcance de este bloque

- límites automáticos o suspensión de Nexi por costo;
- mostrar consumo al usuario final;
- facturación basada en tokens;
- Mercado Pago;
- edición gráfica de precios por modelo;
- alertas por correo al Owner;
- NexoMail Empresas;
- migración de SQLite a la base productiva definitiva.

Estos puntos podrán usar la información generada por este subsistema más adelante.

## 17. Roadmap posterior actualizado

Después del panel, el orden de desarrollo recomendado es:

1. completar gestión de pruebas Premium/Nexi: finalización anticipada, histórico y retorno limpio a Freemium;
2. Microsoft OAuth + Microsoft Graph para Outlook.com, Hotmail y Microsoft 365, incluyendo lectura, envío, respuestas, borradores, carpetas, adjuntos, contactos, Centro de Control y Nexi;
3. IMAP/SMTP para cuentas de dominio propio y proveedores sin API específica;
4. preparar infraestructura de producción: dominio, HTTPS, separación de ambientes, base de datos productiva, backups, logs, monitoreo, recuperación, CORS y rate limiting;
5. gestionar secretos productivos y persistencia segura de claves de cifrado/tokens;
6. análisis y adecuación a Ley 21.719: mapa de datos, finalidades y bases jurídicas, minimización, retención, derechos de titulares, incidentes, contratos con encargados, transferencias internacionales y privacidad desde el diseño;
7. revisión jurídica profesional del producto, política de privacidad, términos de servicio y contratos con proveedores/clientes;
8. llevar Gmail a producción: proyecto OAuth definitivo, dominios y redirect URIs, pantalla de consentimiento, scopes y verificaciones aplicables;
9. llevar Microsoft y OpenAI a configuración productiva;
10. auditoría de seguridad y aislamiento multitenant;
11. piloto privado de 30 días con cuentas Premium/Nexi administradas y medición real del panel;
12. revisar precios y límites comerciales usando datos reales del piloto;
13. definir entidad/vendedor, cuenta bancaria, situación tributaria, DTE y cuenta de vendedor de Mercado Pago;
14. completar Mercado Pago: alta, renovación, rechazo, cancelación, webhooks y reconciliación con pruebas administradas;
15. lanzamiento comercial abierto: Freemium, prueba Premium, contratación y operación productiva;
16. NexoMail Empresas: organizaciones, usuarios, roles, políticas, analítica, límites, soporte, dominio personalizado y posteriormente White Label.

La migración desde SQLite a PostgreSQL o SQL Server deberá realizarse antes del lanzamiento comercial abierto y puede adelantarse al bloque de infraestructura de producción según el hosting elegido.
