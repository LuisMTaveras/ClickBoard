# ClipForge

Gestor de portapapeles para Windows, diseñado para guardar elementos localmente y recuperarlos desde tres vistas:

- **Aplicación general:** búsqueda, historial, favoritos, imágenes y vista previa.
- **Selector rápido:** ventana centrada con búsqueda y navegación por teclado.
- **Panel lateral:** acceso rápido desde el borde de la pantalla, filtros por tipo y opción para fijarlo encima de otras ventanas.

## Cambios de la versión 0.2

- Historial paginado, miniaturas persistidas y originales cargados al seleccionar, copiar o pegar.
- Títulos, colecciones y etiquetas buscables. Transformaciones locales con vista previa, deshacer y plantillas con variables `${nombre}`.
- Acciones independientes para copiar, pegar y exportar. La exportación guarda imágenes como PNG, texto como TXT y conserva el formato de los archivos.
- Presupuesto de almacenamiento configurable, deduplicación de nuevas capturas de archivos y limpieza guiada de clips antiguos sin proteger.
- Pausa de captura, exclusiones por aplicación y opción para cerrar las vistas rápidas al perder el foco.
- Transferencias LAN con progreso, velocidad, cancelación y permiso de recepción automática por contacto.
- Tema del sistema actualizado en vivo y panel lateral situado en el monitor del cursor, con adaptación de DPI.

Detalles y limitaciones: [estado de implementación](docs/MEJORAS-0.2.md). Estos cambios se han compilado, pero no se ha ejecutado la aplicación ni pruebas de funcionamiento por indicación del usuario.

## Tecnología

- .NET 10 y WPF para una aplicación de escritorio nativa de Windows.
- C# con CommunityToolkit.Mvvm para el modelo de vista.
- SQLite con FTS5 para persistencia local y búsqueda de texto.
- APIs de Windows para escuchar cambios del portapapeles, registrar atajos globales y volver a la ventana anterior al pegar.

## Requisitos

- Windows 10 u 11.
- .NET 10 SDK.

## Ejecutar

Desde la raíz del proyecto:

```powershell
dotnet restore ClickBoard.sln
dotnet build ClickBoard.sln
dotnet run --project src/ClickBoard.App/ClickBoard.App.csproj
```

## Atajos

| Acción | Atajo |
| --- | --- |
| Selector rápido | `Ctrl + Shift + Espacio` |
| Panel lateral | `Ctrl + Alt + Espacio` |
| Buscar en la aplicación general | `Ctrl + F` |
| Limpiar búsqueda o cerrar una vista rápida vacía | `Esc` |
| Navegar y pegar en el selector rápido | `↑` / `↓` y `Enter` |
| Acción rápida alterna | `Shift + Enter` para pegar como texto plano |
| Favorito rápido | `Ctrl + F` dentro del selector o panel |

Los atajos globales, el tema, las acciones de `Enter` y el tamaño máximo por archivo se pueden ajustar desde **Configuración**. El límite inicial es de 50 MB por archivo y admite valores entre 1 y 10240 MB. Si alguno de los archivos seleccionados supera el límite, no se guarda esa selección en el historial; la copia normal de Windows no se modifica.

La configuración está integrada en la ventana principal. Las secciones **General**, **Atajos globales**, **Pegado rápido**, **Archivos** y **Privacidad** se pueden abrir desde la barra lateral o localizar mediante el buscador (`Ctrl + F`). Los cambios se aplican y guardan automáticamente. El tamaño máximo y las aplicaciones excluidas se aplican tras una breve pausa al escribir o al salir del campo; si el tamaño es inválido, se mantiene el anterior.

Selecciona un clip en la ventana principal, escribe título, colección y etiquetas en el panel de vista previa y pulsa **Guardar**. Estos metadatos permiten encontrarlo sin cambiar el nombre del archivo original. Un campo vacío elimina el valor personalizado. El guardado automático de Configuración no se aplica a esta edición de clips.

Para eliminar un clip, selecciónalo y usa **Eliminar del historial** o la tecla `Supr`. Se pide confirmación. Las copias locales creadas por esta versión se eliminan cuando su carpeta tiene la marca de propiedad; los archivos originales y las copias antiguas sin esa marca no se borran automáticamente.

## Red y contactos

La red está desactivada inicialmente. Abre **Red y contactos**, asigna un nombre al equipo y activa el modo red en ambos dispositivos. La búsqueda usa difusión IPv4 local en el puerto UDP 49573 y las transferencias usan TCP 49573. Una red aislada o un firewall pueden impedir el descubrimiento o la conexión.

El nombre del equipo y los cambios de nombre de los contactos existentes se guardan automáticamente tras una breve pausa al escribir. Agregar un contacto nuevo sigue requiriendo confirmar su identidad.

Cada equipo muestra una identidad. Para agregar un contacto, compara el código completo mostrado en la confirmación con el código que aparece en el otro equipo por un canal de confianza. Cada equipo debe agregar al otro para que ambos reciban automáticamente. Puedes desactivar la recepción automática de cada contacto para exigir confirmación; las identidades desconocidas requieren aceptación antes de recibir. La conexión usa TLS con certificados de dispositivo y una comprobación de integridad SHA-256. El nombre anunciado por la red no verifica por sí solo la identidad de un dispositivo.

Los archivos recibidos se guardan en `%LOCALAPPDATA%\ClickBoard\Received` sin sobrescribir nombres existentes. El límite por archivo de **Configuración → Archivos** también se aplica al enviar y recibir. Se envía un archivo por operación. El modo red permanece activado tras reiniciar hasta que lo desactives. Los contactos y la identidad se guardan localmente; la clave privada está protegida para el usuario de Windows.

ClipForge también aparece en el área de notificación de Windows; desde allí se puede volver a abrir o salir de la aplicación.

## Almacenamiento

La base de datos se crea en `%LOCALAPPDATA%\ClickBoard\history.db`. El historial permanece en este equipo. La base SQLite no está cifrada en esta versión.

Las preferencias se guardan en `%LOCALAPPDATA%\ClickBoard\settings.json`.

El presupuesto inicial es de 2 GB e incluye historial, copias, recibidos y archivos generados para pegar en carpetas. Se comprueba antes de nuevas capturas/recepciones; no elimina datos automáticamente. SQLite puede conservar espacio asignado después de borrar clips, y las operaciones de la base de datos pueden añadir sobrecarga. No es una cuota de disco rígida. La limpieza guiada solicita confirmación y protege favoritos, títulos, colecciones, etiquetas y archivos usados por el portapapeles activo.

La deduplicación de archivos se aplica a las nuevas capturas del mismo conjunto de nombres y contenido. No migra ni unifica copias antiguas. El pegado en carpetas reutiliza archivos TXT/PNG generados por contenido.

## Empaquetado portátil

`scripts/Package.ps1 -Version 0.2.0` publica una carpeta versionada y un ZIP en `dist`, sin sustituir una versión existente. Requiere haber restaurado las dependencias. No ejecuta pruebas ni abre la aplicación. El paquete depende del runtime de escritorio de .NET 10; todavía no incluye instalador, firma ni actualización automática.

Los archivos nuevos se copian a `%LOCALAPPDATA%\ClickBoard\files` y se conservan junto al historial, por lo que ocupan espacio en disco. Los clips de archivos guardados por versiones anteriores todavía apuntan a sus rutas originales. La captura de carpetas y de archivos virtuales que no proporcionen rutas de Windows todavía no está disponible. El pegado se envía a la última aplicación activa fuera de ClipForge.
