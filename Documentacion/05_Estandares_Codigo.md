# ForZip — Estándares de Código

**Versión:** 1.0  
**Fecha:** 9 de mayo de 2026  
**Autor:** Claudio Andino (claudio@patagoniarobot.com)  
**Proyecto:** ForZip v1.0.0  

> Este documento define las convenciones de código obligatorias para todo el proyecto ForZip.  
> Aplica a agentes de IA ejecutores y a cualquier contribuidor humano.  
> Violar estos estándares invalida un pull request o entrega.

---

## Encabezado Obligatorio Apache 2.0

TODO archivo `.cs` del proyecto DEBE comenzar con este encabezado exacto. Sin excepción.

```csharp
// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================
//
//  Author : Claudio Andino
//  Email  : claudio@patagoniarobot.com
//
//  Copyright (c) 2026 Claudio Andino
//  Developed under the Patagonia Robot initiative
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at:
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
// =============================================================================
```

Después del encabezado va una línea en blanco y luego los `using`.

---

## Idioma

- **Comentarios:** en español. Ejemplo: `// Calcula el hash SHA-256 del archivo`.
- **Identificadores** (clases, métodos, propiedades, variables, namespaces): en inglés.
- **Cadenas de usuario** (mensajes, etiquetas): nunca hardcodeadas. Siempre a través de `ILocalizationService`.
- **Nombres de archivos `.cs`:** en inglés, coinciden con el nombre de la clase.

---

## Convenciones de Naming

| Elemento | Convención | Ejemplo |
|---|---|---|
| Namespace | PascalCase | `ForZip.Services` |
| Clase / struct / enum | PascalCase | `HashService`, `ZipOptions` |
| Interfaz | `I` + PascalCase | `IHashService` |
| Método público | PascalCase | `ComputeHashesAsync` |
| Método privado | PascalCase | `BuildReportHeader` |
| Propiedad pública | PascalCase | `CompressionLevel` |
| Campo privado | `_camelCase` | `_hashService` |
| Variable local | camelCase | `fileBytes` |
| Parámetro | camelCase | `filePath` |
| Constante | PascalCase | `MaxPasswordLength` |
| Enum value | PascalCase | `HashAlgorithmType.SHA256` |
| Evento | PascalCase | `ProgressChanged` |
| Método async | Sufijo `Async` | `CompressAsync` |
| Boolean | Prefijo `Is`/`Has`/`Can` | `IsProcessing`, `HasPassword` |

---

## Estructura de Namespaces

```
ForZip.Core
ForZip.Core.Services
ForZip.Core.Models
ForZip.Core.Interfaces
ForZip.GUI
ForZip.GUI.ViewModels
ForZip.GUI.Views
ForZip.GUI.Converters
ForZip.Cli
ForZip.Tests
ForZip.Tests.Services
```

Cada archivo `.cs` pertenece al namespace que corresponde a su carpeta. No se permiten clases en un namespace que no coincida con su ubicación física.

---

## Inyección de Dependencias

- **Siempre por constructor.** No usar service locator, no usar `new` para crear servicios.
- Los ViewModels reciben interfaces, no implementaciones concretas.
- El composition root está en `App.axaml.cs` (GUI) o `Program.cs` (CLI).

```csharp
// ✅ Correcto
public class ZipViewModel
{
    private readonly IZipService _zipService;
    private readonly IHashService _hashService;
    private readonly IReportService _reportService;

    public ZipViewModel(
        IZipService zipService,
        IHashService hashService,
        IReportService reportService)
    {
        _zipService = zipService;
        _hashService = hashService;
        _reportService = reportService;
    }
}

// ❌ Incorrecto — instanciación directa
public class ZipViewModel
{
    private readonly ZipService _zipService = new ZipService();
}

// ❌ Incorrecto — service locator
public class ZipViewModel
{
    private readonly IZipService _zipService = ServiceLocator.Get<IZipService>();
}
```

---

## Manejo de Async / Await

- Todos los métodos que hacen I/O (archivos, streams) son `async Task` o `async Task<T>`.
- Propagación de `CancellationToken` obligatoria en toda la cadena.
- Nunca usar `.Result` ni `.Wait()` (deadlock en UI thread).
- Nunca usar `async void` excepto en event handlers de Avalonia (donde es inevitable).
- Reportar progreso con `IProgress<T>` — nunca actualizar UI directamente desde un hilo de trabajo.

```csharp
// ✅ Correcto
public async Task CompressAsync(
    ZipOptions options,
    IProgress<(long bytesProcessed, long totalBytes)> progress,
    CancellationToken ct)
{
    // ...
    ct.ThrowIfCancellationRequested();
    progress.Report((bytesRead, totalSize));
}

// ❌ Incorrecto — bloquea el hilo UI
public void Compress(ZipOptions options)
{
    CompressAsync(options, null, CancellationToken.None).Result;
}
```

---

## Manejo de Errores

- No capturar `Exception` genérica salvo en el top-level (composition root) para logging.
- Capturar excepciones específicas: `IOException`, `UnauthorizedAccessException`, `OperationCanceledException`, etc.
- Nunca tragar excepciones silenciosamente (`catch { }`).
- Los servicios lanzan excepciones. Los ViewModels las capturan y las convierten en mensajes de usuario vía `ILocalizationService`.
- `OperationCanceledException` no es un error — se maneja como cancelación limpia.

```csharp
// ✅ Correcto — en el ViewModel
try
{
    await _zipService.CompressAsync(options, progress, _cts.Token);
    StatusMessage = _loc.Get("zip_success");
}
catch (OperationCanceledException)
{
    // Cancelación limpia, no es error
    StatusMessage = _loc.Get("operation_cancelled");
}
catch (IOException ex)
{
    StatusMessage = string.Format(_loc.Get("io_error"), ex.Message);
}
```

---

## Testing con xUnit

### Convenciones de nombres

Los tests siguen el patrón `Método_Escenario_ResultadoEsperado`:

```csharp
[Fact]
public async Task ComputeHashesAsync_KnownFile_ReturnsExpectedSha256()

[Fact]
public void GeneratePassword_AllClassesEnabled_ContainsAllClasses()

[Fact]
public async Task CompressAsync_CancellationRequested_ThrowsOperationCanceledException()
```

### Estructura

```csharp
[Fact]
public async Task ComputeHashesAsync_KnownFile_ReturnsExpectedSha256()
{
    // Arrange — preparar datos y dependencias
    var service = new HashService();
    var testFile = Path.Combine(TestDataPath, "sample.txt");
    var algorithms = new HashSet<HashAlgorithmType> { HashAlgorithmType.SHA256 };

    // Act — ejecutar la operación
    var result = await service.ComputeHashesAsync(
        testFile, algorithms, null, CancellationToken.None);

    // Assert — verificar resultado
    Assert.Equal("expected_hash_value", result.Hashes[HashAlgorithmType.SHA256]);
}
```

### Reglas

- Un `[Fact]` por escenario. No mezclar escenarios en un solo test.
- Usar `[Theory]` + `[InlineData]` para tests parametrizados (ej: distintos algoritmos de hash).
- No depender del orden de ejecución de tests.
- Archivos de test en `tests/ForZip.Tests/TestData/` (pequeños, incluidos en el repo).
- Contraseñas de test en `TestPasswords.cs`, claramente marcadas como datos de test.
- Tests deben correr offline (sin acceso a red).
- Tests deben limpiar archivos temporales que creen (`try/finally` o `IDisposable`).

### TestPasswords.cs

```csharp
// =============================================================================
//  (encabezado Apache 2.0)
// =============================================================================

namespace ForZip.Tests;

/// <summary>
/// Contraseñas hardcodeadas exclusivamente para tests unitarios.
/// NO usar estas contraseñas en producción ni como ejemplo para usuarios.
/// </summary>
public static class TestPasswords
{
    /// <summary>Contraseña simple para tests de compresión/descompresión.</summary>
    public const string Simple = "ForZip_Test_2026!";

    /// <summary>Contraseña con caracteres especiales y Unicode.</summary>
    public const string Complex = "F0rZ1p#T€st_Cömpl3x!";

    /// <summary>Contraseña larga para tests de estrés.</summary>
    public const string Long = "ThisIsAVeryLongPasswordForStressTestingForZipAES256Encryption_2026!@#$";
}
```

### NSubstitute (si se usa)

- Solo para mockear interfaces en tests de ViewModels.
- Los servicios core se testean con implementaciones reales (integration-like).
- No mockear `IHashService` para testear `HashService` — eso no tiene sentido.

---

## Formato de Código

### .editorconfig

```ini
# ForZip — Configuración de formato de código
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8-bom
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
# Organización de usings
dotnet_sort_system_directives_first = true
dotnet_separate_import_directive_groups = false

# Calificadores this.
dotnet_style_qualification_for_field = false:warning
dotnet_style_qualification_for_property = false:warning
dotnet_style_qualification_for_method = false:warning
dotnet_style_qualification_for_event = false:warning

# Preferencias de tipo
dotnet_style_predefined_type_for_locals_parameters_members = true:warning
dotnet_style_predefined_type_for_member_access = true:warning

# Modificadores
dotnet_style_require_accessibility_modifiers = always:warning
csharp_preferred_modifier_order = public,private,protected,internal,static,extern,new,virtual,abstract,sealed,override,readonly,unsafe,volatile,async:warning

# Expresiones
dotnet_style_object_initializer = true:suggestion
dotnet_style_collection_initializer = true:suggestion
csharp_style_var_for_built_in_types = false:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = false:suggestion

# Nuevas líneas
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true

# Indentación
csharp_indent_case_contents = true
csharp_indent_switch_labels = true

# Espaciado
csharp_space_after_cast = false
csharp_space_after_keywords_in_control_flow_statements = true

# Namespaces
csharp_style_namespace_declarations = file_scoped:warning

[*.{axaml,xaml,xml}]
indent_size = 2

[*.json]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

### Reglas de formato adicionales

- **Llaves:** Siempre en nueva línea (estilo Allman). Sin excepción, incluso para bloques de una sola línea.
- **Una clase por archivo.** El nombre del archivo coincide con el nombre de la clase.
- **Usings:** Al inicio del archivo, después del encabezado. System primero, luego terceros, luego propios.
- **Longitud de línea:** Máximo 120 caracteres. Si un método tiene muchos parámetros, uno por línea.
- **Regiones:** NO usar `#region`. Organizar con comentarios simples si hace falta.
- **Namespaces:** File-scoped (`namespace ForZip.Services;` en vez de `namespace ForZip.Services { ... }`).

```csharp
// ✅ Correcto — file-scoped namespace
namespace ForZip.Core.Services;

public class HashService : IHashService
{
    // ...
}

// ❌ Incorrecto — block-scoped namespace
namespace ForZip.Core.Services
{
    public class HashService : IHashService
    {
        // ...
    }
}
```

---

## Orden de Miembros en una Clase

Dentro de cada clase, los miembros se ordenan así:

1. Constantes
2. Campos privados (`_camelCase`)
3. Constructor(es)
4. Propiedades públicas
5. Métodos públicos
6. Métodos privados

Los miembros estáticos van antes de los de instancia dentro de cada grupo.

```csharp
public class ZipService : IZipService
{
    // 1. Constantes
    private const int BufferSize = 65536;

    // 2. Campos privados
    private readonly IHashService _hashService;

    // 3. Constructor
    public ZipService(IHashService hashService)
    {
        _hashService = hashService;
    }

    // 4. Propiedades públicas
    public bool IsProcessing { get; private set; }

    // 5. Métodos públicos
    public async Task CompressAsync(ZipOptions options, IProgress<(long, long)> progress, CancellationToken ct)
    {
        // ...
    }

    // 6. Métodos privados
    private void ValidateOptions(ZipOptions options)
    {
        // ...
    }
}
```

---

## Comentarios

- **Estilo:** `//` para comentarios de línea. `/// <summary>` para documentación XML en miembros públicos de interfaces.
- **Idioma:** español siempre.
- **No comentar lo obvio.** `// Incrementa el contador` sobre `counter++` es ruido.
- **Sí comentar el por qué.** `// Usamos 64 KB porque SharpZipLib tiene mejor rendimiento con este tamaño de bloque` es útil.
- **Sin TODOs huérfanos.** Si algo queda pendiente, se documenta como issue, no como comentario en el código.

```csharp
/// <summary>
/// Calcula hashes de un archivo en una sola pasada, alimentando todos los
/// algoritmos seleccionados simultáneamente para evitar lecturas múltiples.
/// </summary>
public async Task<HashResult> ComputeHashesAsync(
    string filePath,
    HashSet<HashAlgorithmType> algorithms,
    IProgress<double>? progress,
    CancellationToken ct)
{
    // Usamos bloques de 64 KB: equilibrio entre rendimiento de I/O y uso de memoria
    var buffer = new byte[BufferSize];
    // ...
}
```

---

## Seguridad

- **Contraseñas:** Nunca logear contraseñas ni incluirlas en reportes. El informe dice "AES-256 (contraseña aplicada)", no la contraseña.
- **RandomNumberGenerator:** Única fuente de aleatoriedad para generación de contraseñas. `System.Random` está prohibido para cualquier uso criptográfico.
- **Paths:** Validar siempre contra path traversal (`..`). Usar `Path.GetFullPath()` y verificar que el resultado está dentro del directorio esperado.
- **Inputs:** Validar parámetros al inicio de cada método público. Lanzar `ArgumentException` / `ArgumentNullException` con mensajes claros.

---

## Logging

- Logger propio (no Serilog ni NLog).
- Archivo en `Logs/ForZip_YYYYMMDD.log` al lado del exe.
- Formato: `[2026-05-09 14:30:00.123] [INFO ] Mensaje aquí`
- Niveles: `INFO`, `WARN`, `ERROR`.
- Rotación: eliminar archivos con más de 30 días al iniciar la app.
- Thread-safe (usar `lock` o `ConcurrentQueue`).
- Nunca logear contraseñas, datos personales, ni contenido de archivos.

```
[2026-05-09 14:30:00.123] [INFO ] Aplicación iniciada — ForZip v1.0.0
[2026-05-09 14:30:05.456] [INFO ] Compresión iniciada: 3 archivos, nivel 5, AES-256 activo
[2026-05-09 14:30:12.789] [INFO ] Compresión completada: caso_542_evidencia.zip (3.102.445 bytes)
[2026-05-09 14:30:13.001] [INFO ] Informe generado: ForZip_Report_20260509_143013.txt
[2026-05-09 14:35:00.000] [WARN ] Archivo bloqueado, reintentando: C:\temp\locked.dat
[2026-05-09 14:35:01.500] [ERROR] IOException: El proceso no tiene acceso al archivo 'C:\temp\locked.dat'
```

---

## Archivos AXAML (Avalonia)

- Indentación: 2 espacios.
- Los estilos globales van en `DarkTheme.axaml` / `LightTheme.axaml`, no inline.
- Los DataTemplates para mapear ViewModels → Views van en `App.axaml`.
- Nombres de controles: `x:Name="ProgressBar"` en PascalCase, solo si se necesitan desde code-behind.
- Bindings: siempre declarativos en AXAML. Código en code-behind solo para drag & drop y diálogos de archivo.

---

## Checklist para Cada Archivo Nuevo

Antes de entregar cualquier archivo `.cs` nuevo, verificar:

- [ ] Tiene el encabezado Apache 2.0 completo
- [ ] Namespace coincide con la ubicación física
- [ ] File-scoped namespace
- [ ] Comentarios en español
- [ ] Identificadores en inglés
- [ ] Campos privados con `_camelCase`
- [ ] Constructor injection (no `new` para servicios)
- [ ] Métodos async con `CancellationToken` si hacen I/O
- [ ] Sin `#region`
- [ ] Sin `Console.WriteLine` (usar logger)
- [ ] Sin `System.Random` para criptografía
- [ ] Sin cadenas de usuario hardcodeadas (usar `ILocalizationService`)
- [ ] Modificadores de acceso explícitos
- [ ] Sin warnings de compilación

---

**Fin del documento de estándares de código.**
