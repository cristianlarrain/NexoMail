# Centro de Control — nomenclatura de interfaz

Este documento conserva la propuesta histórica de nomenclatura. Para instrucciones de uso prevalecen los nombres comprobados en `src/frontend/src/pages/ControlCenterPage.tsx` al 12 de septiembre de 2026: **Nexi Control Center**, con pestañas **Prioridades, Informes, Estadísticas, Contactos y Documentos**. El contexto conversacional sigue accesible mediante `?tab=context`, pero no figura como pestaña principal visible.

Consulte la [guía actual de uso](help/user-guide.md) y el [inventario verificado](help/feature-inventory.md). Los nombres de las secciones siguientes son una referencia conceptual histórica, no instrucciones de navegación vigentes.

## Regla general

- **Centro de Control** se usa sólo como nombre del espacio principal.
- **Nexi** se usa cuando existe intervención real del asistente o una acción explícita de IA; no como prefijo decorativo de todos los bloques.
- Un mismo concepto no debe aparecer con nombres equivalentes en distintos niveles.
- Los subtítulos deben explicar la función del bloque, no repetir su título.
- Las cifras oficiales se muestran una sola vez; los demás bloques interpretan, comparan o permiten actuar sobre ellas.

## Navegación principal

- **Operación**: estado actual, prioridades, hallazgos, volumen y evolución.
- **Nexi**: conversación y acciones sobre un conjunto de correos.
- **Informes**: síntesis semántica de un período.
- **Contactos**: análisis de interacción por persona.
- **Documentos**: archivo de adjuntos indexados.

## Operación

Orden conceptual:

1. **Lectura del día** — interpretación breve de lo que merece atención.
2. **Estado operativo** — marco general de las conversaciones con posible acción pendiente.
3. **Indicadores clave** — cifras oficiales: recibidos sin responder, enviados sin respuesta, sin leer y más de 48 horas.
4. **Priorización inteligente** — orden de atención: urgente, requiere respuesta, seguimiento, informativo y probablemente resuelto.
5. **Hallazgos** — señales complementarias que no repiten indicadores.
6. **Volumen por cuenta** — detalle de recibidos y enviados por día y cuenta.
7. **Evolución** — comparación del volumen entre períodos.

## Informes

- Encabezado: **Síntesis del período**.
- Resumen generado: **Resumen ejecutivo**.
- Solicitudes detectadas: **Acciones detectadas**.
- Mensajes que requieren gestión: **Con acción pendiente**.
- Mensajes sin acción: **Sólo informativos**.

## Contactos

- Sección: **Interacción por contacto**.
- Contexto breve: **Relaciones**.

## Documentos

- Sección: **Documentos indexados**.
- Contexto breve: **Archivo**.

## Términos reservados

- **Prioridad**: clasificación de qué atender primero.
- **Pendiente**: conversación que todavía puede requerir respuesta o seguimiento.
- **Seguimiento**: acción posterior sobre una conversación enviada o marcada manualmente.
- **Volumen**: cantidad de correos recibidos y enviados.
- **Evolución**: comparación entre períodos.
- **Informe**: análisis semántico generado para un período.
- **Hallazgo**: información derivada que no es una repetición directa de una métrica.
