# Alcance aprobado por el usuario

La tarea consiste en implementar todos los puntos de esta lista. Este registro no sustituye la implementación. No se ejecutan pruebas ni se abre la aplicación por instrucción del usuario; se permite compilar.

| Función | Estado real |
| --- | --- |
| Pegado en cola: avanzar, saltar, retroceder y repetir mediante atajos | Implementado en código: selección ordenada, atajos globales activados con la cola, detección de conflictos, posición conservada ante error y notificaciones. Sin validación de ejecución. |
| Selección múltiple | Implementado en código: unir textos con separador elegible, exportar selección, enviar a papelera y enviar lote por red a un dispositivo disponible. Cancelación y recuento de archivos confirmados; sin reanudación/idempotencia ni validación de ejecución. |
| Espacios por proyecto | Implementado en código: crear espacios vacíos, elegir proyecto o ver todos, mover/quitar proyecto en lote y filtrar historial/favoritos/imágenes/desarrollo en las tres vistas. Los clips con variables sirven de plantillas del proyecto; las recetas guardadas pertenecen al proyecto activo. Sin validación de ejecución. |
| Reglas por aplicación | Implementado en código: exclusiones de captura y lista configurable de procesos donde pegar texto plano; el resto conserva el formato. Se guarda automáticamente con debounce y se aplica también a la cola de pegado. Sin validación de ejecución. |
| Comparador | Implementado en código: diferencias por línea, números de línea, resaltado de adiciones/eliminaciones, filtro de cambios y página integrada. Límite de dos millones de caracteres y 20 000 saltos; comparación por bloques cuando el cálculo por líneas superaría cuatro millones de celdas. Sin validación de ejecución. |
| Búsqueda avanzada | Implementada en código: tipo/extensión, etiqueta, app, antes y proyecto, con formulario integrado. Sin validación de ejecución. |
| Recetas de transformación | Implementado en código: editor integrado con nombres, pasos ordenados, guardar/reemplazar/eliminar, vista previa y deshacer completo. Hasta 100 recetas de 20 pasos. Guardado local atómico de configuración sin contenido de clips. Sin validación de ejecución. |
| Bandeja de recibidos con reintentos y reanudación | Parcial: nuevos recibidos persistidos con remitente/fecha, apertura de carpeta, actualización en vivo y reenvío al dispositivo seleccionado. Hasta 1000 registros recientes visibles. Pendiente reanudación e idempotencia de transferencias; no reconstruye recibos anteriores a esta versión. |
| Papelera recuperable | Implementado en código: página integrada, restauración múltiple, borrado definitivo con confirmación y protección de archivos usados por el portapapeles. Retención Nunca/7/30/90 días con guardado inmediato y revisión cada cinco minutos mientras la app está abierta. Sin validación de ejecución. |
| Modo temporal visible en tres vistas | Pendiente |
| Arrastrar archivos e imágenes hacia otras aplicaciones | Implementado en código desde el historial principal: archivos e imágenes seleccionados, entrega FileDrop con efecto Copiar. Las imágenes usan PNG generados dentro del presupuesto local. Sin validación de ejecución. |
| Ctrl+K para acciones | Parcial: paleta integrada con búsqueda y Enter para ejecutar exportación, envío, proyectos, comparación, cola, recetas, papelera, reglas y filtros. Falta resolver destinatarios por nombre desde el texto del comando. |
| Atajos directos de favoritos con conflictos | Implementado en código: asignación y eliminación por clip, persistencia, registro al iniciar, aviso de conflictos internos/Windows, comprobación de que siga siendo favorito antes de pegar y liberación al salir. Sin validación de ejecución. |
| Vista previa PDF, páginas, tamaño y nombre original | Pendiente |
| Destino de pegado visible | Parcial: botón principal muestra nombre del proceso destino y se actualiza al activar la ventana. Falta nombre amigable y presentación equivalente en modos rápidos. |

La búsqueda admite valores entre comillas: `proyecto:"Cliente San Juan" tipo:pdf`, `etiqueta:contrato app:Code antes:2026-10-01`. La fecha se interpreta como límite UTC exclusivo. Las etiquetas coinciden por fragmento; el proyecto por nombre exacto sin distinguir mayúsculas. Los filtros se combinan con AND y se aplican antes del límite de página.

## Cola de pegado

Seleccionar clips con Ctrl/Mayús y usar «Preparar cola con selección». El orden es el mostrado en el historial. Volver a la aplicación de destino y usar Ctrl+Alt+Mayús+V para pegar y avanzar; N para saltar; B para retroceder sin pegar; R para repetir el último pegado enviado correctamente. «Vaciar cola» libera los cuatro atajos. Los conflictos impiden activar la cola completa. La cola es de sesión; no se guarda al salir. Clips enviados a la papelera no se pegan desde una cola anterior. Un error no avanza; la ausencia de confirmación de la aplicación de destino limita la detección al envío del comando de Windows.

## Proyectos

En «Proyectos» se pueden crear espacios y mover los clips previamente seleccionados. Elegir un proyecto mantiene el filtro al pasar a Favoritos, Desarrollo o Imágenes y lo transmite a las vistas rápidas. «Ver todos» quita el filtro. Las recetas antiguas siguen siendo generales; las nuevas se guardan dentro del proyecto activo. Cambiar el nombre de colección en el detalle también cambia la asignación del clip. El proyecto activo dura la sesión; no asigna automáticamente nuevas capturas ni modifica archivos originales. No se incluye renombrar o eliminar proyectos en esta entrega.
