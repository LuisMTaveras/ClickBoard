# ClipForge 0.2 — estado de implementación

Esta entrega amplía la revisión del 3 de noviembre de 2026. Se ha compilado la solución; no se han ejecutado pruebas ni abierto la aplicación, conforme a la instrucción del usuario. Las mejoras de interacción, DPI y LAN requieren comprobación real antes de considerarlas validadas.

## Implementado

| Área | Cambio |
| --- | --- |
| Memoria | Consultas ligeras sin originales de imágenes ni HTML/RTF; miniaturas de 128 px; original bajo demanda; páginas de 100 clips. Las imágenes antiguas obtienen miniatura al seleccionarlas. |
| Búsqueda | Desarrollo filtrado en SQL antes del límite; títulos, colecciones y etiquetas incluidos en las búsquedas; orden estable por favorito, fecha e identificador. |
| Archivos | Identidad por nombre y SHA-256 de contenido para nuevas capturas; reutilización de snapshots existentes intactos; eliminación de la copia temporal duplicada. |
| Presupuesto | Opciones de 512 MB, 2 GB y 5 GB; comprobación previa compartida entre captura, recepción y generación de TXT/PNG. |
| Limpieza | Revisión de hasta 1000 clips mayores de 30 días; protección de favoritos y metadatos; confirmación; comprobación de archivos del portapapeles activo y marcas de propiedad antes de eliminar copias. |
| Organización | Título, colección y etiquetas editables y buscables; se mantienen borradores al refrescar el mismo clip. |
| Desarrollo | Recortar, mayúsculas, minúsculas, original, formato JSON y deshacer; variables `${nombre}` con hasta 32 campos; original conservado. Transformaciones y valores se mantienen al refrescar la selección. |
| Acciones | Copiar, pegar y exportar separados; exportación con archivos parciales y sin sobreescritura de archivos del historial. TXT UTF-8 y PNG para texto e imágenes. |
| Privacidad | Pausa persistida y visible; exclusiones por nombre de proceso; respeto de `CanIncludeInClipboardHistory` cuando la aplicación de origen lo publica. |
| Fluidez | Destino de pegado capturado antes de cargar el original; selección rápida sin cargar BLOB; opción de cierre al perder foco; Enter respeta botones con foco. |
| Apariencia | Historial consolidado; desplazamiento del panel de detalle; tema del sistema en vivo; panel lateral en el monitor del cursor y manifiesto PerMonitorV2. |
| LAN | Progreso, velocidad, cancelación y hasta 100 estados de sesión; dos envíos simultáneos; timeout por inactividad durante los datos; recepción automática configurable por contacto. |
| Distribución | Versión visible 0.2.0; script portátil con destinos versionados y sin sobrescribir releases existentes. |

Las actualizaciones de fecha, favoritos o metadatos ya no reconstruyen el índice FTS del contenido completo. Solo se reindexan cambios de texto o vista previa.

## Límites y trabajo pendiente

- LAN: falta reanudación e idempotencia tras perder la confirmación final. Un reintento puede crear otra copia. Faltan conexión manual, bloqueos, cuotas diarias, bandeja persistente y validación entre dos equipos. Los estados de transferencia actuales desaparecen al reiniciar.
- Privacidad: no hay cifrado del contenido en reposo ni modo efímero. Excluir procesos no garantiza identificar todos los orígenes ni detectar secretos.
- Organización: colecciones y etiquetas son metadatos buscables; no hay todavía explorador dedicado de colecciones ni perfiles por proyecto.
- Archivos: carpetas y adjuntos virtuales sin rutas FileDrop siguen sin soporte. La deduplicación nueva no reorganiza el historial antiguo y necesita espacio temporal para copiar antes de reutilizar un snapshot.
- Almacenamiento: es un presupuesto preventivo, no una cuota rígida del sistema de archivos. SQLite puede mantener páginas asignadas tras borrar; no se ejecuta VACUUM automáticamente. Los exports generados y recibidos no se purgan mediante la limpieza de clips. No hay caducidad automática.
- Interfaz: pendientes validación visual, lector de pantalla, contraste y recorrido completo por teclado. Una compilación no verifica estas propiedades ni el resultado real de pegar en otras aplicaciones.
- Distribución: falta instalador firmado, actualizaciones con reversión y política de registro de errores. El script genera un paquete portátil dependiente de .NET 10; no se ha ejecutado para esta entrega.

## Decisión respecto a PowerToys

Se priorizan el archivo personal, los documentos con título, las plantillas y la compartición intencional entre contactos. Se adoptan preferencias integradas, acciones explícitas y vista previa de transformaciones. OCR, IA genérica y control remoto de teclado/ratón no se duplican en esta entrega. Las fuentes y comparación inicial están en [la revisión](REVISION-INTEGRAL.md).
