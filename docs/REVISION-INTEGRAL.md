# Revisión estática de ClipForge — 3 de noviembre de 2026

> Este documento conserva el diagnóstico inicial. Varios hallazgos se han abordado posteriormente: consulta el [estado de la versión 0.2](MEJORAS-0.2.md) para distinguir lo implementado de los pendientes.

Alcance: captura y copias locales, persistencia SQLite/FTS, clasificación, selección y búsqueda, pegado Win32, vistas principal/rápidas, configuración, identidad/contactos y transporte LAN. Se revisó código y documentación oficial de PowerToys. No se abrió la aplicación ni se ejecutaron pruebas, conforme a la instrucción del usuario. Compilar no demuestra funcionamiento real ni seguridad del protocolo LAN. Esta revisión no sustituye una auditoría visual o de seguridad independiente.

## Correcciones aplicadas

- Instancia única por usuario para versiones nuevas, con señal para mostrar la instancia existente. Evita listeners, atajos y servidores duplicados. Las versiones antiguas que ya están abiertas no participan en esta protección.
- Los resultados de consultas anteriores ya no pueden reemplazar una consulta más reciente. El estado de carga corresponde a la última petición.
- El título que se está editando se conserva cuando se recarga el mismo clip.
- Se captura el token de cancelación antes de esperar, evitando acceder a una fuente ya descartada.
- Las consultas fallidas de SQLite muestran un estado y no dejan una tarea de búsqueda sin gestionar.
- El pegado espera a que se liberen Ctrl/Alt/Shift. Evita transformar accidentalmente Ctrl+V en Ctrl+Shift+V al usar Shift+Enter.
- Se impiden operaciones de pegado simultáneas y se muestra un mensaje específico cuando ya existe una.
- La búsqueda por símbolos usa coincidencia literal en lugar de devolver todo el historial. Los comodines de SQL se escapan.
- Se controlan más errores de decodificación de miniaturas.
- La clasificación de código tiene límites de longitud y tiempo de ejecución de expresiones regulares. El contenido original no se recorta.

## Hallazgos pendientes y prioridades

### P1 — Almacenamiento sin presupuesto global
`ClipboardCaptureService.CaptureFilesAsync` crea un directorio distinto para cada copia. El hash incluye esas rutas, por lo que copiar repetidamente el mismo documento crea nuevas copias. El límite de 50 MB es por archivo; no limita el total del historial ni de los recibidos.

Recomendación: almacenar archivos por hash de contenido, referencias desde los clips, cuota global configurable y caducidad. Conservar favoritos/títulos explícitos y ofrecer limpieza con vista previa. La limpieza debe considerar las referencias del portapapeles activo.

### P1 — Consultas cargan los BLOB de todas las imágenes
`SqliteClipboardRepository.SearchCore` carga el contenido completo de hasta 500 imágenes o 2000 candidatos del filtro de desarrollo. Convertir miniaturas después no evita cargar los bytes originales.

Recomendación: paginación, miniaturas persistidas y carga del original únicamente al seleccionar/pegar. Añadir índices de tamaño/tipo/categoría.

### P1 — Red requiere validación independiente
La implementación usa TLS mutuo, identidades fijadas al certificado y confirmación manual de identidad. No se ha comprobado entre dos equipos. El descubrimiento UDP no autentica anuncios; TLS evita enviar a una identidad distinta, pero un anuncio falso puede degradar el descubrimiento. No hay cuotas diarias de recepción, control fino de contactos ni panel de bloqueos.

Recomendación: revisión del protocolo y validación entre equipos antes de considerarlo estable; límites de tasa, bloqueo temporal de desconocidos, permisos de recepción por contacto, registro local de eventos sin contenido sensible y conexión manual alternativa.

### P1 — Transferencias con plazo fijo
`LanTransferService` cancela a los cinco minutos, aunque el tamaño configurable llega a 10 GB. No expone progreso, cancelación del usuario ni reanudación. Un fallo después de guardar pero antes de confirmar puede hacer que un reintento cree otra copia.

Recomendación: identificador de transferencia, confirmación idempotente, progreso y velocidad, cancelación y tiempo por inactividad; después reanudación.

### P2 — Panel lateral usa el monitor principal
`DockedQuickWindow.PlaceAtScreenEdge` se basa en `SystemParameters.WorkArea`. No elige el monitor donde está trabajando el usuario ni adapta explícitamente DPI por monitor.

### P2 — Tema del sistema no sigue cambios en vivo
`ThemeManager.Apply` consulta el tema al aplicarlo, pero no hay suscripción a cambios del sistema.

### P2 — Filtro de desarrollo se aplica después del límite
Se consultan hasta 2000 clips y luego se filtran en memoria. Contenido de desarrollo más antiguo puede quedar fuera aunque coincida. Debe filtrarse en SQL antes de paginar.

### P2 — Portapapeles/Historial muestran prácticamente lo mismo
`SetSectionAsync` no establece una diferencia funcional para esas dos rutas. Consolidar ambas o definir explícitamente una vista del portapapeles actual y otra del archivo persistente.

### P2 — Accesibilidad de los modos rápidos
El manejo global de Enter puede interceptar botones con foco. Faltan revisión de lector de pantalla, recorrido de Tab, escalado y contraste medido. La compilación no verifica estos puntos.

### P2 — Privacidad y protección local
Texto y archivos están guardados sin cifrado de contenido. Faltan pausa de captura, exclusiones por aplicación, caducidad y un modo efímero visible. No prometer detección infalible de contraseñas.

### P2 — Carpetas y archivos virtuales
La captura actual rechaza carpetas y no materializa adjuntos virtuales que no proporcionen rutas FileDrop. Deben explicarse esas limitaciones y diseñarse antes de anunciar soporte universal.

### P3 — Trazabilidad y distribución
Hay varias carpetas de ejecutables. Conviene una salida estable de distribución, versión visible, registro de errores depurado, instalador y proceso de actualización con reversión. Mantener identidad, historial y claves al actualizar.

## PowerToys: reutilizar y diferenciar

Fuentes consultadas:
- https://learn.microsoft.com/en-us/windows/powertoys/advanced-paste
- https://learn.microsoft.com/en-us/windows/powertoys/mouse-without-borders
- https://learn.microsoft.com/en-us/windows/powertoys/command-palette/overview

Advanced Paste ya incluye texto plano, Markdown/JSON, archivos TXT/PNG/HTML, OCR y transformaciones opcionales. Mouse Without Borders comparte teclado/ratón, portapapeles y archivos, con límite documentado de 100 MB para transferencia por portapapeles. Command Palette también permite acceder al historial. Si esas son las únicas necesidades, usar PowerToys evita mantener otra aplicación.

Para ClipForge, priorizar diferencias concretas:
1. Archivo personal de documentos y fragmentos: títulos, etiquetas/colecciones, búsqueda por nombre original y tipo, deduplicación y cuotas.
2. Flujo de desarrollo: transformaciones deterministas con vista previa y deshacer, snippets con variables y perfiles por proyecto. Evitar construir de entrada otro proveedor de IA u OCR genérico.
3. Compartición intencional con contactos: bandeja de recibidos, progreso, cancelación y permisos por contacto. No competir inicialmente con el control remoto de teclado y ratón.
4. Acciones explícitas: copiar, pegar y exportar como acciones distinguibles; estado real de destino y errores recuperables.
5. Comodidades copiables de PowerToys: atajos por acción, vista previa antes de transformar, cerrar el selector al perder foco como preferencia y estados de conexión comprensibles.

## Orden recomendado

Primero almacenamiento/memoria y validación del pegado/red. Después cuotas, pausa/exclusiones, progreso/cancelación y colecciones. Finalmente integración opcional con herramientas existentes e instalador. Las ideas de esta sección son propuestas, no funciones implementadas en esta revisión.
