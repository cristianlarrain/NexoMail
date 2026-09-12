export type LegalDocument = 'terms' | 'privacy' | 'security'

export type LegalSection = {
  title: string
  paragraphs: string[]
}

export type LegalDocumentContent = {
  title: string
  intro: string
  sections: LegalSection[]
}

export const legalDocumentOrder: LegalDocument[] = ['terms', 'privacy', 'security']

export const legalDocuments: Record<LegalDocument, LegalDocumentContent> = {
  terms: {
    title: 'Términos de Servicio',
    intro: 'Estos términos regulan el uso de NexoMail y de las funciones de Nexi disponibles dentro de la plataforma.',
    sections: [
      {
        title: '1. Servicio',
        paragraphs: [
          'NexoMail permite conectar y gestionar cuentas de correo compatibles desde una interfaz unificada. El usuario mantiene la titularidad de sus cuentas y de los contenidos administrados mediante proveedores externos.',
          'Nexi es la inteligencia integrada de NexoMail y puede ayudar a buscar, resumir, analizar, priorizar y redactar. Sus resultados son propuestas asistidas y deben ser revisados por el usuario antes de adoptar decisiones, enviar mensajes o ejecutar acciones relevantes.',
        ],
      },
      {
        title: '2. Cuenta y seguridad',
        paragraphs: [
          'El usuario debe proporcionar información válida, proteger sus credenciales y utilizar la plataforma únicamente mediante cuentas que esté autorizado a administrar.',
          'NexoMail puede aplicar controles de sesión, límites de uso, verificación de correo, suspensión preventiva y otras medidas razonables destinadas a proteger el servicio y a sus usuarios.',
        ],
      },
      {
        title: '3. Uso permitido',
        paragraphs: [
          'No se permite utilizar NexoMail para acceder sin autorización a cuentas ajenas, distribuir malware, realizar fraude, vulnerar derechos de terceros, eludir medidas de seguridad o ejecutar actividades contrarias a la legislación aplicable.',
          'El uso de servicios de Google, proveedores de inteligencia artificial, plataformas de pago u otros terceros también queda sujeto a las condiciones que esos proveedores establezcan para sus propios servicios.',
        ],
      },
      {
        title: '4. Planes y pagos',
        paragraphs: [
          'Las características, límites, precios y periodicidad aplicables se muestran antes de contratar un plan. Las condiciones comerciales vigentes al momento de la contratación forman parte de estos términos.',
          'Cuando exista renovación automática, cancelación, devolución o cobro gestionado por un proveedor de pagos, se informarán las condiciones aplicables antes de confirmar la contratación.',
        ],
      },
      {
        title: '5. Disponibilidad y responsabilidad',
        paragraphs: [
          'NexoMail depende parcialmente de redes, APIs y servicios de terceros. No se garantiza disponibilidad ininterrumpida y pueden existir interrupciones por mantenimiento, cambios de proveedor o causas fuera del control razonable de NexoMail.',
          'Las funciones de inteligencia artificial pueden producir resultados incompletos o incorrectos. El usuario conserva el control final y la responsabilidad de revisar la información antes de utilizarla.',
        ],
      },
      {
        title: '6. Legislación aplicable',
        paragraphs: [
          'El servicio se orienta inicialmente a usuarios en Chile y debe interpretarse conforme a la legislación chilena aplicable, sin perjuicio de las normas imperativas que pudieran corresponder en otras jurisdicciones.',
        ],
      },
    ],
  },
  privacy: {
    title: 'Política de Privacidad',
    intro: 'NexoMail aplica un criterio de minimización: tratar únicamente los datos necesarios para prestar, proteger y mejorar las funciones solicitadas por el usuario.',
    sections: [
      {
        title: '1. Datos tratados',
        paragraphs: [
          'Podemos tratar datos de registro de la cuenta NexoMail, información básica de las cuentas de correo conectadas, tokens de autorización protegidos, preferencias, datos de sesión y metadatos operativos necesarios para búsqueda, organización, seguridad y funcionamiento del servicio.',
          'La arquitectura está diseñada para no persistir en la base operacional de NexoMail los cuerpos completos de los correos ni el contenido de sus archivos adjuntos. Esos contenidos pueden ser consultados temporalmente desde el proveedor de correo cuando una función solicitada por el usuario lo requiere.',
        ],
      },
      {
        title: '2. Funciones de inteligencia artificial',
        paragraphs: [
          'Cuando el usuario invoca una función de Nexi, el contenido estrictamente necesario para responder a esa solicitud puede transmitirse al proveedor de inteligencia artificial configurado. NexoMail debe limitar ese contexto a lo necesario para la función solicitada.',
          'El uso de inteligencia artificial no autoriza a NexoMail a enviar correos automáticamente salvo que el usuario confirme expresamente una acción de envío prevista por la interfaz.',
        ],
      },
      {
        title: '3. Finalidades',
        paragraphs: [
          'Los datos se tratan para autenticar al usuario, conectar cuentas, mostrar mensajes, ejecutar búsquedas, generar análisis solicitados, mantener preferencias, gestionar planes, prevenir abuso, registrar eventos de seguridad y prestar soporte.',
        ],
      },
      {
        title: '4. Proveedores y transferencias',
        paragraphs: [
          'NexoMail puede apoyarse en proveedores de correo, infraestructura, inteligencia artificial y pagos. Cada integración debe limitarse a los datos necesarios y configurarse con controles razonables de seguridad y acceso.',
          'Cuando el tratamiento implique servicios ubicados fuera de Chile, se deben observar las reglas aplicables a transferencias y tratamiento internacional de datos personales.',
        ],
      },
      {
        title: '5. Derechos y conservación',
        paragraphs: [
          'El usuario podrá solicitar acceso, corrección o eliminación de información personal bajo control de NexoMail en los términos que permita la legislación aplicable y las obligaciones de conservación que correspondan.',
          'Los datos operativos deben conservarse sólo durante el tiempo necesario para las finalidades declaradas, seguridad, cumplimiento contractual o exigencias legales.',
        ],
      },
      {
        title: '6. Marco chileno',
        paragraphs: [
          'A la fecha de esta versión, NexoMail considera la Ley N.º 19.628 sobre protección de la vida privada y prepara su cumplimiento con la Ley N.º 21.719, que perfecciona el régimen de protección de datos personales y entra en vigencia el 1 de diciembre de 2026.',
        ],
      },
    ],
  },
  security: {
    title: 'Política de Seguridad',
    intro: 'La seguridad de NexoMail se basa en reducción de datos, autenticación, protección de credenciales y controles para limitar accesos o acciones no autorizadas.',
    sections: [
      {
        title: '1. Credenciales y acceso',
        paragraphs: [
          'Las contraseñas de NexoMail deben almacenarse mediante hash seguro y no como texto legible. Los tokens utilizados para conectar proveedores externos deben protegerse mediante mecanismos de cifrado o protección de datos apropiados.',
          'Las sesiones utilizan cookies protegidas y la aplicación incorpora controles contra solicitudes no autorizadas, límites frente a intentos repetidos y validaciones para operaciones sensibles.',
        ],
      },
      {
        title: '2. Separación de usuarios',
        paragraphs: [
          'Los datos, cuentas conectadas y operaciones deben quedar asociados al usuario autenticado. NexoMail debe impedir que un usuario consulte o modifique información perteneciente a otro usuario sin autorización expresa.',
        ],
      },
      {
        title: '3. Minimización y correo',
        paragraphs: [
          'NexoMail procura trabajar con el proveedor de correo como fuente principal y conservar únicamente la información operacional necesaria. El diseño evita almacenar cuerpos completos de mensajes y contenido de adjuntos en la base operacional.',
        ],
      },
      {
        title: '4. Nexi e inteligencia artificial',
        paragraphs: [
          'El contenido de correos debe tratarse como entrada no confiable para los modelos de IA. Las instrucciones contenidas dentro de un correo no deben poder modificar las reglas internas de Nexi ni provocar acciones automáticas.',
          'Las propuestas de Nexi no sustituyen la confirmación del usuario en operaciones que puedan enviar, eliminar, mover o modificar información relevante.',
        ],
      },
      {
        title: '5. Incidentes y mejoras',
        paragraphs: [
          'Los eventos relevantes de seguridad deben poder investigarse mediante registros operativos apropiados, evitando registrar secretos innecesarios. Ante una vulneración confirmada se aplicarán medidas de contención, corrección y comunicación conforme a las obligaciones legales vigentes.',
          'La seguridad es un proceso continuo. Esta política y los controles técnicos pueden actualizarse a medida que cambien el producto, los proveedores y la normativa aplicable.',
        ],
      },
    ],
  },
}
