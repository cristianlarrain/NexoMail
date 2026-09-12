# NexoMail — instrucción para crear el video definitivo

## Objetivo
Crear el video comercial definitivo de NexoMail para la landing page y reutilizarlo posteriormente en redes sociales. Debe mostrar el producto real, transmitir control, productividad, seguimiento e inteligencia aplicada al correo, y reemplazar la maqueta actual sin rediseñar la sección de video de la landing.

## Formato principal
- Resolución: 1920×1080.
- Relación: 16:9.
- Duración objetivo: 50–60 segundos.
- FPS: 30.
- Formato final: MP4 H.264.
- Versión posterior: 9:16 de 25–30 segundos para Reels/Stories.

## Dirección visual
- El producto real es el protagonista.
- Usar capturas reales y actualizadas de NexoMail.
- Evitar actores, oficinas genéricas y stock humano.
- Estética tecnológica, ejecutiva, limpia y sobria.
- Base oscura grafito/azul petróleo.
- Turquesa como color principal de NexoMail.
- Amarillo cálido y naranja sólo como acentos de atención y métricas.
- Movimiento suave: zoom, paneo, profundidad ligera y resaltados discretos.
- No alterar ni inventar elementos de la interfaz real.

## Pantallas mínimas a mostrar
1. Bandeja de entrada unificada.
2. Agregar cuenta: Gmail / Google Workspace, Microsoft 365 e IMAP / SMTP.
3. Nexi Control Center — Estadísticas.
4. Nexi Control Center — Informes / resumen ejecutivo / prioridades.
5. Nexi como asistente contextual y de priorización.

## Storyboard base

### 0–7 s — Problema
Visual: bandeja de entrada con carga de correos y foco en el volumen de mensajes.

Locución:
“Su correo no debería ser sólo una bandeja llena de mensajes.”

Texto en pantalla:
**Su correo contiene trabajo pendiente.**

### 7–15 s — Centralización
Visual: pantalla para agregar cuenta, resaltando Gmail, Microsoft 365 e IMAP / SMTP.

Locución:
“NexoMail reúne sus cuentas en una sola interfaz y le permite trabajar desde un único lugar.”

Texto:
**Gmail · Microsoft 365 · IMAP / SMTP**
**Todos sus correos. Un solo lugar.**

### 15–24 s — Operación diaria
Visual: bandeja unificada, navegación, remitentes, asuntos y seguimiento prioritario.

Locución:
“Revise, ordene y responda correos con una experiencia más clara, enfocada y productiva.”

Texto:
**Bandeja unificada**
**Seguimiento prioritario**
**Más claridad para actuar**

### 24–35 s — Centro de Control
Visual: Nexi Control Center / Estadísticas, volumen por cuenta, recibidos, enviados y actividad.

Locución:
“El Centro de Control transforma el correo en información útil: actividad, volumen, seguimiento y prioridades por cuenta.”

Texto:
**Centro de Control**
**Actividad del correo**
**Visibilidad operativa**

### 35–46 s — Nexi e inteligencia
Visual: Nexi Control Center / Informes, resumen ejecutivo, correos analizados y prioridades.

Locución:
“Nexi le ayuda a interpretar el contenido, detectar lo que requiere atención y convertir información dispersa en decisiones más rápidas.”

Texto:
**Resumen ejecutivo**
**Priorización asistida por Nexi**
**Lo importante, visible primero**

### 46–55 s — Cierre
Visual: composición limpia con interfaz NexoMail y cierre con logotipo.

Locución:
“Menos tiempo buscando mensajes. Más claridad para actuar. NexoMail. Todos sus correos. Un solo lugar.”

Texto:
**NexoMail**
**Todos sus correos. Un solo lugar.**
**Comience gratis**

## Locución recomendada
Usar voz masculina latinoamericana profesional, calmada y segura. La maqueta utilizó ElevenLabs “Brian — calm Latin American Spanish male narrator”; puede mantenerse si suena natural en la versión final.

Texto maestro:

“Su correo no debería ser sólo una bandeja llena de mensajes. NexoMail reúne sus cuentas en una sola interfaz y le permite trabajar desde un único lugar. Revise, ordene y responda correos con una experiencia más clara, enfocada y productiva. El Centro de Control transforma el correo en información útil: actividad, volumen, seguimiento y prioridades por cuenta. Nexi le ayuda a interpretar el contenido, detectar lo que requiere atención y convertir información dispersa en decisiones más rápidas. Gmail, Microsoft 365 e IMAP o SMTP pueden convivir en un mismo entorno. Menos tiempo buscando mensajes. Más claridad para actuar. NexoMail. Todos sus correos. Un solo lugar.”

## Música
- Electrónica minimalista y moderna.
- Aproximadamente 100–110 BPM.
- Sin voz.
- Inicio con ligera tensión y evolución hacia una sensación de control y claridad.
- Sintetizadores suaves, pulso limpio y percusión contenida.
- Nunca debe competir con la narración.

## Sonido y montaje
- Voz principal claramente por encima de la música.
- Transiciones suaves, sin efectos llamativos innecesarios.
- Pequeños sonidos de interfaz sólo si aportan claridad.
- Evitar exceso de animación.
- Mantener legibilidad total de la interfaz y textos.

## Integración en la landing
La maqueta actual ya está integrada en el bloque “Vea NexoMail en acción”. Cuando exista el video definitivo:

1. Sustituir únicamente la fuente del video por el MP4 definitivo.
2. Mantener el reproductor 16:9 y su diseño actual.
3. Mantener controles manuales y reproducción no automática salvo decisión posterior.
4. Preferir un archivo propio del proyecto o CDN controlado por NexoMail/EIDOS Digital, no una URL temporal de generación.
5. Usar un poster basado en una captura real del producto.

Ruta sugerida para el archivo definitivo en frontend:
`src/frontend/public/media/nexomail-demo.mp4`

URL de consumo sugerida en la landing:
`/media/nexomail-demo.mp4`

## Criterios de aprobación final
El video definitivo debe:
- parecer una demostración real de NexoMail, no un video genérico de IA;
- conservar fielmente la interfaz;
- mostrar claramente el valor del Centro de Control y Nexi;
- explicar la centralización de cuentas;
- mantener ritmo comercial sin parecer publicitario en exceso;
- ser comprensible sin audio mediante textos breves en pantalla;
- terminar con una llamada clara a probar NexoMail.

## Estado actual
Existe una maqueta funcional de aproximadamente 55 segundos usada como referencia visual y actualmente integrada en la landing. Debe considerarse sólo como prototipo; el objetivo futuro es reemplazarla por esta producción definitiva conservando la misma ubicación y estructura del reproductor.