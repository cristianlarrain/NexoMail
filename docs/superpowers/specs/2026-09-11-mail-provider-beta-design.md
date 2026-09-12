# NexoMail — Proveedores de correo para marcha blanca de 30 días

Fecha: 2026-09-11
Estado: Diseño aprobado en conversación, pendiente de revisión final del documento antes de implementación
Rama objetivo: `feature/commercial-foundation`

## 1. Objetivo

Preparar NexoMail para una marcha blanca pública de 30 días con tres formas reales de conexión de cuentas:

1. Gmail / Google Workspace.
2. Microsoft 365 mediante Microsoft Graph.
3. IMAP / SMTP genérico en modalidad Beta.

El objetivo no es declarar compatibilidad universal con todos los servidores de correo, sino permitir pruebas reales con los dos ecosistemas OAuth principales y con cuentas estándar de dominio propio o proveedores compatibles con IMAP/SMTP.

Exchange Server local, integraciones Yahoo específicas y cuentas Microsoft personales Outlook.com/Hotmail quedan fuera de este alcance inicial.

## 2. Estado actual

NexoMail ya dispone de:

- `MailProviderType` con `Gmail`, `MicrosoftGraph` e `Imap`.
- `IMailProvider` y `IMailGateway` para despachar operaciones según el proveedor de cada cuenta.
- Gmail conectado de punta a punta mediante OAuth, credenciales protegidas y `GmailMailProvider`.
- `MailAccounts` multiusuario y límites de cuentas según plan comercial.
- `OAuthCredentials` para tokens OAuth protegidos.
- API, UI y flujo de configuración que actualmente sólo permiten agregar Gmail.

La arquitectura existente permite agregar Microsoft Graph e IMAP como proveedores sin reemplazar el gateway actual.

## 3. Alcance de la marcha blanca

### Gmail / Google Workspace

Estado: disponible.

Se conserva el flujo actual de OAuth y las capacidades existentes.

### Microsoft 365

Estado: disponible durante la marcha blanca.

Se conectarán únicamente cuentas profesionales, educativas e institucionales de Microsoft 365. El registro de aplicación se configurará para identidades organizacionales, no para cuentas Microsoft personales durante esta etapa.

La UI mostrará de forma visible:

> En algunas organizaciones, la conexión puede requerir autorización previa del administrador de Microsoft 365.

El hecho de que una organización requiera consentimiento administrativo no se tratará como fallo de NexoMail. El usuario recibirá un mensaje explicativo y podrá reintentar después de obtener autorización.

### IMAP / SMTP

Estado: disponible como `Beta`.

La configuración será manual y limitada a los parámetros estándar:

- correo electrónico;
- nombre visible;
- usuario de autenticación;
- contraseña o contraseña de aplicación;
- servidor IMAP;
- puerto IMAP;
- seguridad IMAP;
- servidor SMTP;
- puerto SMTP;
- seguridad SMTP.

NexoMail deberá probar IMAP y SMTP antes de persistir la cuenta.

No se promete autodetección universal ni compatibilidad con autenticaciones propietarias durante esta etapa.

## 4. Interfaz “Agregar cuenta”

El botón actual `Agregar Gmail` se reemplazará por `Agregar cuenta`.

Al presionarlo se abrirá un modal con opciones visuales y marcas reconocibles para cada proveedor:

- Gmail / Google Workspace — `Conectar con Google`.
- Microsoft 365 — `Conectar con Microsoft`.
- Otro correo — `IMAP / SMTP · Beta` — `Configurar manualmente`.

El modal podrá mostrar al pie proveedores futuros de forma deshabilitada y discreta:

- Outlook / Hotmail — próximamente.
- Yahoo Mail — próximamente.
- Exchange Server local — próximamente.

Estos elementos no deberán iniciar ningún flujo ni sugerir que ya están soportados.

Los logotipos/marcas se implementarán como recursos locales livianos del frontend, evitando dependencias externas de ejecución y sin cargar imágenes desde terceros.

## 5. Microsoft 365 — arquitectura

Se agregará un módulo `Microsoft` dentro de Infrastructure, equivalente conceptualmente al módulo `Google` existente.

Componentes previstos:

- `Microsoft365Options`: ClientId, ClientSecret, RedirectUri, Authority/Tenant y configuración necesaria.
- `MicrosoftOAuthService`: inicio OAuth, validación de `state`, intercambio de código, renovación de token y persistencia del refresh token protegido.
- `MicrosoftGraphMailProvider`: implementación de `IMailProvider` para Microsoft Graph.
- opcionalmente `MicrosoftGraphDraftProvider` si las pruebas confirman que el flujo actual de borradores requiere soporte en marcha blanca.

Se utilizará el gateway actual. El proveedor se registrará como `MailProviderType.MicrosoftGraph` y se envolverá con la misma protección de propiedad de cuenta que Gmail.

### Permisos

Se solicitará el conjunto mínimo que permita las funciones de correo comprometidas. La base de diseño contempla:

- identidad básica del usuario;
- acceso offline para renovación;
- lectura/escritura de correo;
- envío de correo.

No se agregarán permisos de calendario, archivos u otros servicios que no sean necesarios para esta marcha blanca.

### OAuth

Se agregarán endpoints autenticados equivalentes a Google:

- `/api/oauth/microsoft/start`
- `/api/oauth/microsoft/callback`

El callback creará o actualizará `MailAccounts` y su `OAuthCredential` asociado.

## 6. IMAP / SMTP — arquitectura

Se utilizará una biblioteca mantenida para protocolos IMAP/SMTP en .NET, preferentemente MailKit, en vez de implementar los protocolos manualmente.

Se agregará un módulo `Imap` con:

- modelo de configuración de conexión;
- servicio de validación de conexión;
- almacenamiento protegido de credenciales;
- `ImapMailProvider` para lectura y operaciones IMAP;
- envío SMTP para composición, respuesta y reenvío.

### Credenciales

Las contraseñas nunca se almacenarán en texto plano.

Se agregará una entidad específica asociada 1:1 a `MailAccount` con, como mínimo:

- `MailAccountId`;
- `Username`;
- `EncryptedPassword`;
- `ImapHost`;
- `ImapPort`;
- `ImapSecurity`;
- `SmtpHost`;
- `SmtpPort`;
- `SmtpSecurity`;
- `UpdatedAt`.

`EncryptedPassword` se protegerá con el mecanismo de Data Protection ya utilizado por NexoMail para secretos persistidos.

### Validación

Antes de crear una cuenta se probará:

1. resolución y conexión al host IMAP;
2. negociación TLS según configuración;
3. autenticación IMAP;
4. conexión SMTP;
5. negociación TLS SMTP;
6. autenticación SMTP.

Una falla devolverá un error funcional sin persistir la contraseña ni crear una cuenta incompleta.

### Seguridad de transporte

La UI no ofrecerá conexión sin cifrado como opción recomendada. Se soportarán modos seguros habituales, priorizando TLS implícito o STARTTLS según el puerto y la configuración del usuario.

## 7. Capacidades durante la Beta

El núcleo mínimo que debe funcionar en Gmail, Microsoft 365 e IMAP/SMTP es:

- listar cuentas;
- bandeja unificada;
- listar mensajes;
- abrir mensaje;
- adjuntos;
- enviar;
- responder;
- responder a todos;
- reenviar;
- marcar leído/no leído;
- carpetas;
- mover a carpeta;
- mover a papelera.

Las capacidades avanzadas actualmente acopladas a Gmail no se presentarán como universales durante la marcha blanca. Esto incluye, cuando corresponda:

- Google Contacts;
- reglas específicas de Gmail;
- analítica o Centro de Control que dependa directamente de servicios Gmail;
- otras funciones cuyo backend todavía sea Google-específico.

La UI deberá evitar ofrecer una acción a una cuenta cuyo proveedor no la soporte. No se dejarán botones que terminen en un `NotSupportedException` visible al usuario.

## 8. Borradores

Los borradores requieren atención especial porque el gateway separa `IMailProvider` de `IMailDraftProvider`.

Criterio para marcha blanca:

- Gmail conserva su soporte actual.
- Microsoft 365 deberá implementar borradores si el compositor actual los guarda automáticamente para todas las cuentas.
- IMAP podrá implementar APPEND a la carpeta de borradores únicamente si la detección de carpeta es suficientemente confiable; de lo contrario, NexoMail deshabilitará el guardado remoto de borradores para cuentas IMAP durante la Beta y lo informará sin impedir enviar correos.

No se simulará soporte de borradores cuando el proveedor no lo pueda garantizar.

## 9. Persistencia

`MailAccounts.Provider` continuará siendo la fuente para decidir qué proveedor maneja cada cuenta.

Se reutilizará `OAuthCredentials` para Google y Microsoft 365, ya que contiene un refresh token cifrado asociado 1:1 a `MailAccount`.

Se agregará una tabla separada para IMAP/SMTP porque sus parámetros y secretos son distintos a OAuth.

Al quitar una cuenta se deberán eliminar en cascada:

- credenciales OAuth o IMAP/SMTP;
- estados e índices asociados según las relaciones existentes.

Los cuerpos completos de correo y bytes de adjuntos continuarán sin persistirse localmente.

## 10. API de configuración

Además de los endpoints OAuth, se agregará un endpoint autenticado para IMAP/SMTP.

Flujo recomendado:

1. Frontend envía configuración IMAP/SMTP a un endpoint de conexión.
2. Backend valida límites comerciales de cuentas.
3. Backend prueba IMAP y SMTP.
4. Sólo después de ambas pruebas crea `MailAccount` y credenciales protegidas.
5. API responde con la cuenta creada.

Los mensajes de error distinguirán, sin exponer información sensible:

- servidor inaccesible;
- TLS no aceptado;
- usuario o contraseña rechazados;
- SMTP rechazado;
- configuración inválida.

## 11. Seguridad

Requisitos obligatorios:

- secretos sólo en backend;
- refresh tokens y contraseñas cifrados en reposo;
- CSRF en operaciones de configuración iniciadas desde la UI;
- validación de propiedad de `MailAccount` para todas las operaciones;
- no registrar contraseñas, tokens, códigos OAuth ni cuerpos de correo;
- límites comerciales verificados en backend, no sólo en frontend;
- timeouts razonables para IMAP/SMTP para evitar conexiones bloqueadas;
- mensajes de error sanitizados.

## 12. Experiencia de usuario

### Modal inicial

Título: `Agregar cuenta de correo`

Texto: `Elige cómo quieres conectar tu correo a NexoMail.`

Tarjetas:

**Gmail / Google Workspace**
Conexión segura con Google.
`Conectar con Google`

**Microsoft 365**
Cuenta profesional, educativa o institucional.
`Conectar con Microsoft`

Nota: `Algunas organizaciones requieren autorización previa de su administrador de Microsoft 365.`

**IMAP / SMTP · Beta**
Dominio propio u otro proveedor compatible.
`Configurar manualmente`

### Formulario IMAP

Se abrirá en un segundo modal o paso del mismo modal. Tendrá valores iniciales seguros habituales, pero nunca se asumirá que un servidor es correcto sin probarlo.

La acción principal será `Probar y conectar`.

## 13. Marcha blanca de 30 días

La aplicación se identificará como `Marcha blanca` o `Beta` en un lugar discreto de la experiencia, sin interferir con el uso normal.

Durante los 30 días se medirán al menos:

- conexiones exitosas/fallidas por tipo de proveedor;
- errores OAuth;
- errores de autenticación IMAP/SMTP por categoría, sin credenciales;
- fallos de lectura y envío;
- proveedor utilizado por las cuentas conectadas;
- feedback cualitativo de usuarios.

No se almacenarán datos adicionales del contenido de los correos para estas métricas.

## 14. Preparación de producción

La rama debe quedar lista para configurar secretos mediante variables de entorno o configuración segura:

- Google OAuth existente;
- Microsoft ClientId;
- Microsoft ClientSecret;
- Microsoft RedirectUri;
- autoridad/tenant organizacional;
- claves persistentes de Data Protection;
- conexión de base de datos;
- configuración de dominio público/redirects.

`MailProviders:DemoMode` deberá estar desactivado en producción.

Los archivos de ejemplo documentarán valores requeridos pero nunca incluirán secretos reales.

## 15. Exclusiones explícitas de esta fase

No se implementará en esta marcha blanca:

- Exchange Server on-premise como conector dedicado;
- autodiscover de Exchange;
- OAuth Yahoo dedicado;
- soporte garantizado para cualquier proveedor IMAP existente;
- Outlook.com/Hotmail como cuentas Microsoft personales;
- calendarios;
- OneDrive/SharePoint;
- sincronización de contactos Microsoft;
- sincronización completa offline de buzones.

Estas exclusiones evitan convertir la marcha blanca en una matriz de compatibilidad difícil de estabilizar.

## 16. Pruebas de aceptación

La implementación se considerará lista cuando:

1. El modal muestre Gmail, Microsoft 365 e IMAP/SMTP con sus marcas y estados correctos.
2. Gmail continúe conectando y funcionando sin regresiones.
3. Microsoft 365 complete OAuth organizacional y cree una cuenta utilizable.
4. El rechazo por política de consentimiento institucional se traduzca en un mensaje comprensible.
5. IMAP/SMTP no persista una cuenta si falla la prueba de conexión.
6. Las credenciales IMAP se almacenen cifradas.
7. Una cuenta IMAP válida pueda listar, leer y enviar correo.
8. Una cuenta Microsoft 365 válida pueda listar, leer y enviar correo.
9. La bandeja unificada pueda combinar cuentas de proveedores diferentes.
10. Operaciones no soportadas se oculten o deshabiliten antes de llegar al backend.
11. Los límites de cuentas por plan continúen aplicándose a todos los proveedores.
12. Build frontend y backend, smoke tests existentes y nuevas pruebas de proveedores terminen correctamente.

## 17. Decisión de arquitectura

Se conservará la arquitectura actual `MailAccount -> MailProviderType -> IMailProvider -> MailGateway` y se ampliará por proveedor.

No se creará un segundo gateway ni se duplicará la lógica de bandeja unificada.

Este enfoque mantiene a Gmail estable, agrega Microsoft Graph e IMAP de forma aislada y permite incorporar futuros proveedores sin rediseñar el núcleo de NexoMail.
