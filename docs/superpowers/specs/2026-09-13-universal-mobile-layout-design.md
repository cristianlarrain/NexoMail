# Diseño responsivo transversal de NexoMail

## Objetivo

Garantizar que todas las pantallas autenticadas funcionen desde 360 px sin contenido cortado, superpuesto ni comprimido, manteniendo visible el buscador Nexi y convirtiendo la bandeja de correos en una lectura móvil clara.

## Causa observada

Las reglas móviles actuales sólo cubren un grupo de contenedores y compiten con estilos anteriores cargados desde varios archivos. La bandeja conserva una cuadrícula de escritorio reducida: oculta campos, pero continúa acoplando selección, remitente, fecha y acciones en una sola fila estrecha.

## Diseño aprobado

### Estructura global

- En 360–767 px, la barra superior usará dos filas: menú/avatar en la primera y buscador Nexi al ancho completo en la segunda.
- El buscador permanecerá disponible en todas las rutas autenticadas.
- Los contenedores directos de `main-content` tendrán `min-width: 0`, ancho máximo de 100% y padding móvil uniforme.
- Cabeceras, barras de acciones, filtros, formularios, modales, tarjetas y métricas pasarán a una columna cuando no exista espacio suficiente.
- Las tablas administrativas conservarán todas sus columnas dentro de un contenedor con desplazamiento horizontal y señal visual de desplazamiento.

### Bandeja de entrada

- Bajo 768 px se ocultará el encabezado tabular.
- Cada `.message-row` será una tarjeta con áreas diferenciadas para selección/estado, remitente, asunto/extracto, fecha y acciones.
- Remitente y asunto admitirán elipsis sin empujar controles fuera del viewport.
- La fecha y acciones quedarán en una franja inferior; no compartirán columnas comprimidas con el asunto.
- Se mantendrán selección, estado leído/no leído, adjuntos, seguimiento y menú de acciones.

### Cobertura

El ajuste común cubrirá bandeja, lectura de correo, redacción, búsqueda, Centro de Control, documentos, contactos, cuentas, perfil, planes, administración, uso de IA y paneles de Nexi.

## Pruebas de aceptación

- Verificación automática de reglas móviles obligatorias y ausencia de anchos rígidos sin contenedor desplazable.
- Compilación y lint del frontend.
- Revisión en viewports de 360, 390, 430, 768 y escritorio.
- En cada viewport: buscador visible, ancho de documento igual al viewport y ausencia de superposición en bandeja.
- Validación de tema claro y oscuro.

## Fuera de alcance

- No se modifica la lógica de búsqueda Nexi.
- No se cambia la información disponible en escritorio.
- No se alteran endpoints ni datos persistidos.
