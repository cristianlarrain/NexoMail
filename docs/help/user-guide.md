# Guía de uso y respuestas de ayuda

Versión documental: 2026-09-12. Alcance y evidencia: [inventario](feature-inventory.md). Esta guía describe el código revisado; una función puede requerir configuración, proveedor o plan. No sustituye el estado efectivo de la cuenta.

## Acceder y recuperar la cuenta

Abra `/login`. Para una cuenta nueva, complete el registro y la verificación solicitada. Si olvidó la contraseña, utilice la recuperación y siga el proceso de código y nueva contraseña. Si no llega el correo, revise spam y la dirección indicada; si persiste, solicite asistencia por el canal que el servicio tenga publicado. Nexi no debe pedir contraseñas ni códigos de verificación en el chat.

## Conectar y organizar cuentas

Abra Configuración → Cuentas de correo (`/settings/accounts`). Seleccione el proveedor y complete su conexión. Google y Microsoft muestran su propio consentimiento. IMAP/SMTP requiere los parámetros entregados por su proveedor; no copie las credenciales a una conversación de soporte. Una cuenta con autenticación en dos pasos puede requerir contraseña de aplicación si su proveedor la admite.

Desde la cuenta conectada puede editar su nombre visible y color. Antes de agregar otra, consulte el uso y límite del plan. Desconectar una cuenta en NexoMail no significa eliminar el buzón del proveedor.

**¿Por qué Microsoft no conecta?** Revise el mensaje mostrado. La integración requiere configuración OAuth y puede estar sujeta a permisos de la organización. No recomendar eludirlos. La configuración base usa cuentas de organizaciones; no garantizar compatibilidad con cuentas personales sin comprobar el entorno.

## Leer y organizar mensajes

Abra Bandeja o seleccione una cuenta. Utilice las opciones de ordenación, filtros y paginación para localizar mensajes. Abra un correo para leerlo, revisar adjuntos y responder o reenviar. Las acciones de mover, marcar leído y enviar a papelera afectan al proveedor cuando se ejecutan correctamente.

La selección múltiple permite actuar sobre los mensajes seleccionados. Verifique cuenta, selección y destino antes de confirmar una operación. Vaciar papelera puede eliminar definitivamente información; Nexi de ayuda explica el procedimiento, pero no lo ejecuta.

**¿Ignorar equivale a bloquear?** No. NexoMail mantiene su registro de remitentes ignorados. No debe asegurar que el proveedor dejará de recibir sus mensajes.

**¿Por qué no aparece una vista previa?** La compatibilidad depende del formato. Puede utilizar la descarga disponible y abrir el archivo con una aplicación compatible.

## Redactar, dictar y usar IA

Abra Redactar (`/compose`), seleccione remitente, destinatarios, asunto y cuerpo. Agregue CC/CCO y adjuntos cuando corresponda. Revise el contenido antes de enviar.

Si su plan y la configuración habilitan Nexi, use la asistencia de escritura para preparar o mejorar el texto. Una propuesta de IA no es un correo enviado. La ayuda de producto tampoco debe enviar mensajes.

**¿No funciona el micrófono?** El dictado depende de reconocimiento de voz compatible y permisos del navegador. Revise el permiso de micrófono; si la función no está disponible, escriba el texto.

**¿Puedo guardar borradores?** El código incluye proveedores de borradores Gmail y Microsoft Graph. IMAP/SMTP no tiene ese proveedor registrado en la versión revisada. No prometa guardado ni recuperación automática para todos los proveedores.

## Buscar y crear reglas

Use la búsqueda superior o `/search`. Puede consultar correos y acotar los resultados por ámbito. Contactos y documentos se basan en un índice; no todos los proveedores ni todos los mensajes históricos están incluidos.

Para reglas, abra `/settings/rules` y el flujo de creación en `/rules/new`. En Gmail se pueden definir criterios y una acción: archivar, marcar leído, enviar a papelera o mover a un destino. Revise los criterios y el destino antes de crear la regla. El proveedor actual de reglas es Gmail; no garantizar el mismo flujo para Microsoft o IMAP.

## Centro de Control

Abra `/control-center`. La pantalla principal se denomina «Nexi Control Center» y sus pestañas visibles son **Prioridades, Informes, Estadísticas, Contactos y Documentos**. La documentación histórica que decía «Operación» o mostraba «Nexi» como pestaña principal no coincide completamente con esta revisión.

- **Prioridades:** revisar conversaciones que pueden requerir respuesta y seguimiento. Un pendiente detectado es una señal operativa, no una obligación confirmada. Puede gestionar seguimiento y finalizar elementos según las acciones disponibles.
- **Informes:** seleccionar período y generar la síntesis. Requiere IA habilitada. Los resultados proceden de una selección de correos; no afirmar que cubren todo el buzón.
- **Estadísticas:** consultar actividad y evolución. Requiere la capacidad correspondiente.
- **Contactos:** consultar interacción a partir del índice.
- **Documentos:** localizar metadatos de adjuntos indexados; no representa un disco de documentos independientes.

**¿Por qué faltan datos de alguna cuenta?** Los servicios de cálculos automáticos e índices revisados seleccionan Gmail. Además, los períodos, límites e indexación condicionan los resultados. No presentar una ausencia de resultados como prueba de que no existen mensajes.

**¿Por qué aparece un candado?** Consulte Plan y uso. Nexi e IA, estadísticas avanzadas y Centro completo son capacidades distintas. La respuesta debe basarse en los permisos efectivos del usuario.

## Perspectivas y apariencia

En `/perspectives` puede consultar elementos guardados, ampliarlos cuando la función esté disponible, compartirlos y eliminarlos. La colección se guarda localmente en el navegador; no asegurar que aparecerá en otro dispositivo.

En `/settings/appearance` puede elegir tema claro u oscuro. La preferencia es local. No hay un editor completo de identidad corporativa verificado en esa pantalla.

## Perfil y sesiones

En `/settings/profile` puede modificar el nombre visible y fotografía. El correo de la cuenta es de solo lectura. En Sesiones activas puede revisar accesos y cerrar los que ya no utilice.

## Plan y uso

Abra `/settings/plan` para consultar el plan, capacidades y uso de cuentas. Los valores efectivos provienen del servicio, ya que el catálogo es editable. Si contrató un plan y aún está pendiente, el retorno desde la pasarela no demuestra por sí solo que el cobro esté confirmado. Revise el estado mostrado antes de intentar contratar otra vez.

No dar por incluido soporte prioritario operativo, un editor de firmas, políticas de organización o personalización completa solo porque aparecen como características comerciales.

## Administración

Los administradores autorizados disponen de tipos de cuenta en `/admin/plans` y gestión de usuarios en `/admin/users`. El panel `/admin/ai-usage` corresponde exclusivamente al Owner y presenta consumo y costos estimados. La ayuda a un usuario común no debe mostrar información de otras cuentas ni guiarlo a controles administrativos como si tuviera acceso.

## Cuando no existe una respuesta verificada

Respuesta de referencia: «No tengo información verificada para confirmar esa función en esta versión. Puedo explicarle las opciones documentadas de esta pantalla».

El sistema de tickets y la conversación de ayuda fundamentada en esta guía son una siguiente etapa. Hasta que exista integración real, Nexi no debe afirmar que abrió una solicitud, entregó un número de seguimiento o avisó al soporte.
