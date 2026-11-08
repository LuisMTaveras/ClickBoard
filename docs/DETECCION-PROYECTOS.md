# Detección automática de proyectos

En Proyectos, seleccionar un espacio, introducir el nombre del proceso (Code, devenv, WINWORD…) y un fragmento del título de ventana que lo distinga. Añadir la regla y activar la detección. Se guarda localmente en project-detection.json; el interruptor se aplica inmediatamente.

Cada dos segundos se consulta el proceso y título de la ventana activa externa a ClipForge. No se leen documentos, carpetas ni repositorios. Ambas condiciones deben coincidir, sin distinguir mayúsculas. Varias reglas que apuntan al mismo proyecto son compatibles. Si coinciden proyectos distintos o no coincide ninguna regla, se conserva el espacio actual y se informa en el estado de la ventana principal.

El espacio se aplica a las tres vistas. Seleccionarlo manualmente desactiva el automatismo; puede reactivarse desde Proyectos. Las reglas persisten entre sesiones. Esta versión cambia el filtro del espacio, sin asignar automáticamente nuevas capturas. El contenido copiado desde aplicaciones distintas puede tener un origen diferente al contexto de trabajo; esa asignación requiere una política adicional.

Limitaciones: un título genérico o que cambia al cerrar pestañas puede impedir reconocer el proyecto. La detección no verifica rutas reales, identidad del documento o repositorio Git. No se ha ejecutado la aplicación ni pruebas de interacción; solo se ha compilado por instrucción del usuario.
