# Nexi Control Center como bandeja operativa principal

## Objetivo

Convertir Nexi Control Center en la pantalla inicial de NexoMail y en el lugar principal para revisar, priorizar y resolver correos. La bandeja unificada actual se conserva íntegramente como **Vista clásica**.

## Principios de experiencia

- Priorizar decisiones y acciones, no sólo indicadores.
- Permitir resolver las tareas frecuentes sin abrir el detalle del correo.
- Explicar brevemente por qué Nexi clasificó un mensaje.
- Usar verbos simples, iconos reconocibles y estados visibles.
- Mantener una salida directa hacia la bandeja clásica.
- Evitar acciones destructivas accidentales mediante confirmación y opción de deshacer.

## Navegación principal

- La entrada autenticada, el logotipo y la opción **Inicio** abrirán Nexi Control Center.
- La bandeja unificada existente se denominará **Vista clásica** y conservará su ruta funcional independiente.
- El encabezado del Control Center mostrará el alcance activo —todas las cuentas o una cuenta—, la hora de actualización, la acción **Actualizar** y el botón **Vista clásica**.
- El selector de cuenta seguirá controlando el alcance tanto del Control Center como de la Vista clásica.
- Informes, Estadísticas, Contactos y Documentos continuarán como secciones secundarias del Control Center, respetando las capacidades de cada plan.

## Jerarquía de la pantalla

### 1. Encabezado operativo

Incluye identidad de Nexi, alcance seleccionado, última actualización y acceso a Vista clásica. El buscador global permanece visible en la barra superior.

### 2. Tarjetas de resumen

Las tarjetas existentes se mantienen, pero funcionan como filtros de la lista principal:

- Urgentes.
- Por responder.
- En seguimiento.
- Sin leer.
- Más de 48 horas.

La tarjeta activa debe distinguirse visualmente y poder desactivarse volviendo a **Todos**.

### 3. Filtros rápidos y cuenta

La lista tendrá filtros compactos: **Todos**, **Urgentes**, **Responder**, **Seguimiento** y **Sin leer**. El selector de cuentas existente en el menú lateral será el único selector: mostrará **Todas las cuentas** y cada cuenta conectada con su color, nombre y dirección. Los filtros muestran cantidades, son compatibles con teclado y conservan el alcance de cuenta seleccionado en la URL.

Al elegir una cuenta, se actualizarán conjuntamente las tarjetas, cantidades, prioridades y lista operativa del Control Center; en Vista clásica filtrará la bandeja existente. Ambas vistas interpretarán el mismo alcance persistido en la URL para evitar estados contradictorios.

### 4. Bandeja inteligente

Será el contenido principal y ocupará el mayor espacio. El orden predeterminado será:

1. Urgentes vencidos o con fecha límite detectada.
2. Recibidos que requieren respuesta.
3. Seguimientos de enviados sin respuesta.
4. Informativos y probablemente resueltos.

Dentro de cada grupo se ordenará por antigüedad descendente del pendiente. El usuario podrá cambiar a orden cronológico reciente.

Cada elemento mostrará:

- Color y nombre de la cuenta.
- Remitente o destinatario.
- Asunto y extracto breve.
- Antigüedad.
- Clasificación actual.
- Razón breve de Nexi, cuando exista evidencia suficiente.
- Estado de lectura y seguimiento manual.

## Acciones directas por correo

Las acciones disponibles dependen del estado del mensaje:

| Acción | Comportamiento |
| --- | --- |
| **Responder** | Abre la redacción como respuesta, con asistencia de Nexi disponible. |
| **Seguir** | Activa seguimiento manual y confirma el cambio en la misma fila. |
| **Resolver** | Quita el mensaje de pendientes y seguimientos, sin eliminarlo del proveedor. |
| **Posponer** | Oculta temporalmente el pendiente por 1, 3 o 7 días. |
| **Quitar urgencia** | Elimina la clasificación urgente manual o confirmada; el correo permanece disponible y puede seguir en otra categoría. |
| **Eliminar** | Envía el correo a la papelera del proveedor tras confirmación. Muestra una notificación con **Deshacer** cuando el proveedor permita restaurarlo. |
| **Ver** | Abre el detalle completo conservando el retorno al Control Center y el filtro activo. |

En escritorio se muestran como botones compactos con icono y verbo. Las tres acciones contextualmente más útiles permanecen visibles; las demás se agrupan en **Más**. En móvil se muestran dos acciones principales y un menú **Más**, sin exceder el ancho de la tarjeta.

## Estados y persistencia

- **Resolver**, **Posponer** y el seguimiento manual reutilizarán el estado persistido del Control Center.
- **Quitar urgencia** requiere persistir una exclusión del usuario para evitar que la clasificación automática reaparezca inmediatamente.
- La exclusión de urgencia se identifica por usuario, cuenta y conversación; puede revertirse desde la misma fila.
- Eliminar utiliza la operación real del proveedor y actualiza las consultas de bandeja, mensaje y Control Center.
- Las actualizaciones optimistas se revierten si la API falla y muestran un mensaje específico de la acción.
- Dos acciones simultáneas sobre el mismo correo quedan bloqueadas hasta recibir respuesta.

## Seguridad y errores

- Eliminar siempre requiere confirmación con asunto y cuenta visibles.
- Resolver y quitar urgencia no eliminan el correo y se pueden revertir mediante una notificación temporal.
- Los permisos comerciales, autenticación y protección CSRF existentes continúan aplicándose.
- Los errores distinguen seguimiento, clasificación, eliminación, carga del mensaje y proveedor no disponible.
- Si una cuenta no responde, la pantalla conserva las demás cuentas y muestra cuál quedó fuera del análisis.

## Diseño responsivo

- Desde 360 px, cada correo se representa como tarjeta vertical.
- Remitente, asunto y clasificación ocupan el bloque superior; la razón de Nexi y los metadatos quedan debajo.
- Las acciones principales usan ancho completo o dos columnas; **Más** contiene las secundarias.
- Tarjetas de resumen y filtros permiten desplazamiento horizontal controlado sin comprimir el contenido.
- El selector lateral de cuenta permanece accesible en el menú móvil y no se duplica dentro del contenido.
- No se muestran tablas de siete columnas en teléfono.
- Tema claro y oscuro mantienen contraste en categorías, botones, focos y estados deshabilitados.

## Compatibilidad con Vista clásica

- La Vista clásica conserva lectura, selección múltiple, carpetas, paginación y acciones actuales.
- Los cambios realizados en cualquiera de las vistas invalidan y actualizan ambas consultas.
- Volver desde un mensaje restaura vista, cuenta, filtro y posición aproximada de desplazamiento.
- No se duplican reglas de negocio entre vistas: ambas consumen las mismas operaciones de correo y seguimiento.

## Componentes afectados

- Enrutamiento y navegación inicial.
- `AppLayout` y menú lateral.
- `ControlCenterPage`, `ControlCenter` y `NexiPriorityQueue`.
- API de correo para eliminación y actualización de clasificación.
- Persistencia del estado de clasificación del usuario.
- Estilos del Control Center y reglas móviles.
- Invalidación coordinada de consultas de bandeja y Control Center.

## Pruebas de aceptación

- La sesión autenticada abre Nexi Control Center como inicio.
- **Vista clásica** abre la bandeja unificada actual sin pérdida funcional.
- Cada tarjeta de resumen filtra la bandeja inteligente y refleja la cantidad visible.
- Seleccionar una cuenta filtra tarjetas, cantidades y correos; **Todas las cuentas** restablece el alcance unificado.
- Responder, seguir, resolver, posponer, quitar urgencia, eliminar y ver funcionan desde la lista sin abrir primero el detalle.
- Quitar urgencia persiste después de actualizar la página y puede revertirse.
- Eliminar exige confirmación y actualiza ambas vistas.
- Un error restaura el estado anterior de la fila y presenta una explicación concreta.
- El retorno desde el detalle conserva el contexto operativo.
- La pantalla funciona en 360, 390, 430, 768 px y escritorio, en tema claro y oscuro.
- Las pruebas existentes de bandeja, seguimiento, planes y Control Center continúan pasando.

## Fuera de alcance

- Automatizar respuestas o eliminaciones sin acción explícita del usuario.
- Crear un calendario de tareas o fechas futuras completo.
- Reemplazar la bandeja clásica o eliminar sus funciones.
- Cambiar las reglas comerciales de los planes.
