# Diseño: identidad de cuentas, búsqueda Nexi y experiencia móvil

Fecha: 2026-09-13  
Rama: `feature/ionos-production`

## Objetivo

Mejorar cuatro aspectos relacionados de NexoMail:

1. distinguir visualmente las cuentas conectadas sin que todas usen el mismo color;
2. hacer que Nexi entienda búsquedas naturales e imprecisas sin perder control ni privacidad;
3. permitir el uso completo de la aplicación desde teléfonos de 360 px de ancho en adelante.
4. otorgar Premium automáticamente durante 30 días a cada usuario nuevo mientras la campaña de pruebas esté activa.

## Alcance y decisiones

### 1. Colores automáticos por cuenta

Se incorporará una paleta central de colores vivos con contraste comprobado en los temas claro y oscuro. Al conectar una cuenta, el servidor elegirá el color menos utilizado por ese usuario; si todos ya se usan, continuará de forma determinista para evitar que cuentas consecutivas reciban el mismo color.

La selección será independiente del proveedor: Google, Microsoft e IMAP usarán el mismo servicio. El color seguirá almacenado con la cuenta y podrá cambiarse desde la interfaz existente. Los colores ya personalizados no se modificarán. No se migrarán automáticamente las cuentas existentes; únicamente las nuevas recibirán la nueva asignación.

La validación aceptará exclusivamente colores hexadecimales `#RRGGBB` pertenecientes a un rango visual seguro para ambos temas. La interfaz mostrará una vista previa y conservará el valor anterior si el servidor rechaza el cambio.

### 2. Búsqueda progresiva de Nexi

Se mantendrá la búsqueda actual como primera etapa para conservar precisión y velocidad. Si la consulta menciona una persona, Nexi intentará resolver el nombre contra contactos y participantes visibles en las cuentas conectadas del usuario. La comparación será tolerante a acentos, mayúsculas y pequeñas diferencias ortográficas, por ejemplo `Lucas` y `Lukas`.

Flujo:

1. interpretar la intención y construir una consulta conservadora;
2. resolver nombres usando solamente contactos y participantes autorizados;
3. ejecutar la búsqueda precisa;
4. si no hay resultados, reintentar automáticamente con variantes cercanas y después con una consulta más amplia;
5. informar brevemente qué entendió Nexi y si amplió la búsqueda.

La ampliación no inventará direcciones ni consultará fuentes externas. Respetará la cuenta, carpeta, fechas, remitente, estado y demás filtros expresados por el usuario. Habrá límites de variantes y reintentos para evitar ruido, latencia y consumo innecesario. La búsqueda semántica con un índice vectorial queda fuera de este alcance.

### 3. Diseño responsivo desde 360 px

Se ajustará el sistema de layout compartido con tres rangos:

- 360–767 px: teléfono;
- 768–1023 px: tableta;
- 1024 px o más: escritorio.

En teléfono, la barra lateral se convertirá en un panel deslizable; el buscador de Nexi seguirá visible en una versión compacta; la cabecera redistribuirá sus controles sin superposiciones; formularios, paneles y diálogos usarán el ancho disponible; y los botones podrán apilarse o ajustarse.

Las tablas con información esencial se representarán como tarjetas cuando cada fila pueda leerse como una entidad. Las tablas administrativas que necesiten conservar columnas usarán desplazamiento horizontal contenido, encabezado legible y acciones accesibles, sin ensanchar toda la página. Ningún componente provocará desplazamiento horizontal global.

Los puntos de adaptación vivirán en primitivas y estilos compartidos. Se permitirán ajustes específicos de pantalla solo cuando cambie la jerarquía de información, evitando parches aislados por página.

### 4. Premium automático de bienvenida

La prueba Premium se otorgará una sola vez al completar correctamente la verificación del correo. Durará 30 días, no exigirá tarjeta y conservará Freemium como plan base. Al vencer, el acceso efectivo volverá automáticamente a Freemium, salvo que exista una suscripción pagada vigente.

La campaña se controlará mediante configuración del servidor y estará activa por defecto durante el período de pruebas. Desactivarla impedirá nuevas concesiones sin cancelar pruebas ya iniciadas. No se aplicará retroactivamente a usuarios existentes y una verificación repetida no renovará ni extenderá la prueba. El proveedor comercial se identificará como `welcome_trial`, separado de las pruebas manuales `admin_trial`.

## Arquitectura

Se crearán tres unidades independientes:

- un selector de color de cuenta en el dominio/servicio de cuentas, consumido por todos los conectores;
- un resolutor de términos personales y una estrategia de reintento dentro del flujo de búsqueda de Nexi;
- primitivas responsivas compartidas para navegación, cabecera, contenedores, formularios, diálogos y tablas.
- un servicio idempotente de bienvenida invocado después de verificar el correo y gobernado por configuración.

Las API públicas existentes se conservarán siempre que sea posible. El editor de cuenta seguirá enviando nombre y color mediante el método compatible con IONOS ya desplegado. Las respuestas de búsqueda podrán añadir metadatos opcionales sobre interpretación y ampliación sin romper clientes anteriores.

## Errores y degradación

Si no se puede calcular el color, se usará un color seguro de respaldo y la conexión continuará. Si falla la resolución de nombres, Nexi ejecutará la búsqueda conservadora actual. Si un reintento ampliado falla, se mostrará el resultado anterior o un mensaje accionable, sin ocultar el error original.

La experiencia móvil no dependerá de JavaScript para evitar desbordamientos básicos; los estilos deberán mantener navegación y contenido utilizables incluso durante estados de carga o error.

## Pruebas y criterios de aceptación

### Colores

- cuentas nuevas consecutivas reciben colores distintos mientras haya opciones disponibles;
- la asignación funciona igual para Google, Microsoft e IMAP;
- un color personalizado se conserva;
- formato, contraste y fallback tienen pruebas unitarias.

### Nexi

- encuentra `Lukas Larraín` aunque el dictado entregue `Lucas Larraín`, cuando existe una coincidencia autorizada;
- una búsqueda exacta exitosa no dispara ampliaciones;
- una búsqueda sin resultados reintenta con un número limitado de variantes;
- conserva filtros explícitos y no mezcla datos de otros usuarios;
- expone al usuario la interpretación y la ampliación aplicadas;
- tiene pruebas unitarias del resolutor y pruebas de integración del flujo de reintento.

### Responsividad

Se verificarán al menos 360, 390, 430, 768 y 1024 px, además de escritorio amplio. En 360 px:

- el buscador de Nexi es accesible;
- navegación, menús, formularios y diálogos son utilizables;
- no existe desplazamiento horizontal global;
- tablas y acciones mantienen toda la información esencial;
- los objetivos táctiles y textos principales siguen siendo legibles.

También se ejecutarán las pruebas actuales de backend y frontend, compilación de producción y una revisión manual en temas claro y oscuro.

### Premium de bienvenida

- se concede Premium por 30 días después de verificar el correo cuando la campaña está activa;
- no se concede durante el registro pendiente de verificación;
- una segunda verificación o llamada repetida no extiende la fecha final;
- una campaña desactivada no concede nuevas pruebas;
- usuarios existentes no reciben la prueba retroactivamente;
- una suscripción pagada vigente nunca es reemplazada;
- al vencer, el plan efectivo vuelve a Freemium.

## Entrega

La implementación se dividirá en cambios verificables y se publicará sin ZIP, siguiendo el flujo actual de IONOS. El despliegue no incluirá secretos en Git. Antes de declarar terminado el trabajo se comprobarán los flujos de conexión y edición de cuentas, búsquedas exactas y ampliadas, y las pantallas principales en los anchos definidos.
