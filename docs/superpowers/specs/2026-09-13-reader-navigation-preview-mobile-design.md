# Mejoras de lectura navegación vista previa y experiencia móvil

## Objetivo

Optimizar el espacio y la legibilidad de NexoMail, unificar las acciones del lector de correo, reparar la vista previa de adjuntos y hacer que la navegación responda de forma consistente entre escritorio y teléfonos desde 360 px.

## Alcance

La mejora cubre el lector de correo, el selector lateral de cuentas, la vista previa de adjuntos, Nexi Control Center, los resultados de búsqueda, la navegación administrativa y la tipografía móvil. Debe funcionar en los temas claro y oscuro.

## Lector de correo

El encabezado se organizará verticalmente en este orden:

1. Navegación para volver y avanzar o retroceder entre mensajes.
2. Título del correo.
3. Barra única de acciones.
4. Metadatos del remitente.
5. Contenido y adjuntos.

La barra única reunirá las acciones que hoy están separadas. Las operaciones de correo se mostrarán primero: Responder, Responder a todos, Reenviar, Archivar o Restaurar, Ignorar remitente, Spam, seguimiento o finalización y Mover a Papelera. Las acciones de IA, Resumir y Analizar conversación, ocuparán un grupo visual separado al final de la misma barra.

El resultado generado por Nexi continuará apareciendo debajo de la barra, con opción de colapsarlo. Ya no existirá una tarjeta introductoria independiente que consuma una fila completa.

En anchos reducidos la barra podrá distribuirse en varias filas. Ninguna acción debe superponerse, salirse del contenedor ni reducir el texto por debajo de la escala móvil definida.

## Selector lateral de cuentas

Cuando el menú lateral esté expandido, la lista de cuentas permanecerá visible inicialmente dentro del flujo del menú y no como una ventana flotante. El encabezado de la sección permitirá colapsarla y expandirla. La preferencia se guardará en `localStorage`.

Al colapsar completamente la barra lateral se mostrará sólo el indicador de la cuenta seleccionada y se conservará el acceso al selector mediante su tooltip. En teléfonos, el listado seguirá el comportamiento del panel lateral desplegable y no debe exceder la altura disponible.

La selección continuará filtrando tanto Nexi Control Center como la Vista clásica.

## Vista previa de adjuntos

La URL del recurso y el fragmento de configuración del visor se construirán por separado. Todos los segmentos y parámetros enviados al servidor estarán codificados; el fragmento del visor PDF se agregará únicamente en el navegador y nunca formará parte de la solicitud HTTP al backend.

El backend seguirá entregando el contenido en modo inline para vista previa y con disposición de descarga cuando se solicite descargar. Los errores se distinguirán como archivo inexistente, proveedor sin respuesta, formato no compatible o solicitud inválida.

Se validarán como mínimo:

- PDF con espacios y caracteres especiales en el nombre.
- Imagen compatible.
- Descarga del mismo adjunto.
- Identificadores de mensaje y adjunto con caracteres reservados.
- Error controlado cuando el proveedor no entrega contenido.

## Nexi Control Center

Las métricas usarán una cuadrícula basada en el ancho real del contenedor. En escritorio aprovecharán el espacio disponible sin dejar una tarjeta aislada por una definición rígida de columnas. En tableta y móvil se reorganizarán progresivamente hasta una columna cuando sea necesario.

La cabecera de Nexi, sus pestañas y el acceso a Vista clásica compartirán el ancho central sin desbordamientos. En móvil, el buscador superior de Nexi y la navegación de secciones ocuparán el ancho completo disponible. Las pestañas podrán desplazarse horizontalmente cuando no quepan, sin comprimir el texto.

## Navegación de búsqueda

Los resultados de búsqueda incorporarán una acción visible Volver antes del encabezado. Usará el historial cuando exista una vista anterior y, como respaldo, regresará al Centro de Control conservando el filtro de cuenta cuando esté presente.

## Navegación administrativa

Para usuarios normales se mantendrá el acceso Plan y uso con destino `/settings/plan`.

Para owners se reemplazará por Panel de Administración. Este acceso llevará a Administración de usuarios y desde allí mantendrá accesos claros a Tipos de cuenta, Consumo Nexi y Plan y uso. El menú de perfil utilizará el mismo criterio.

Las rutas seguirán protegidas por la autorización existente del backend y del frontend; cambiar la navegación no otorgará privilegios nuevos.

## Experiencia móvil

El soporte comienza en 360 px. En teléfonos se ocultará completamente el bloque global Perspectiva en todas las secciones para liberar espacio vertical.

La escala mínima será:

- Texto general: 15 px.
- Botones, menús y campos: 15 a 16 px.
- Asuntos y remitentes: 15 a 16 px.
- Información secundaria: 13 px.
- Títulos principales: 22 a 24 px.
- Controles táctiles: 44 px de alto o superficie equivalente.

El diseño reorganizará el contenido antes de reducir tipografía. Las tablas o filas con varias columnas se transformarán en tarjetas o usarán desplazamiento controlado según el tipo de información.

## Manejo de errores

Las acciones del lector conservarán sus estados pendiente, éxito y error. Nexi mostrará su error junto al área de resultados sin bloquear las acciones normales del correo. La vista previa no mostrará directamente páginas HTML de IIS dentro del visor; presentará un estado de error propio y conservará la opción Descargar.

## Pruebas y aceptación

Se agregarán pruebas de comportamiento para:

- Orden y agrupación de las acciones del lector.
- Persistencia del selector de cuentas expandido o colapsado.
- Construcción segura de URL de vista previa y descarga.
- Navegación Volver desde búsqueda.
- Etiqueta y destino administrativos según plan owner.
- Ocultamiento de Perspectiva y escala tipográfica móvil.
- Distribución de tarjetas y barras a 360, 390, 430, 768 y escritorio.

La compilación del frontend, los smoke tests comerciales, borradores, Centro de Control y el paquete IONOS deben finalizar correctamente antes de entregar el productivo.

## Fuera de alcance

No se modifican los permisos comerciales, los algoritmos de búsqueda de Nexi, la sincronización de correo ni la estructura de planes. Tampoco se incorporan nuevos formatos de vista previa distintos de los que el navegador puede representar de manera segura.
