# PROMPT PARA AGENTE EJECUTOR — ForZip Fase 1: Servicios Core (sin UI)

## Contexto

El proyecto ForZip ya tiene la estructura base (Fase 0 completada). Ahora vas a implementar toda la lógica de negocio en `ForZip.Core` con cobertura de tests en `ForZip.Tests`. NO tocás nada de GUI ni CLI en esta fase.

Documentación de referencia en `D:\PROYECTOS\ForZip\Documentacion\`:
- `01_Especificacion_Funcional.docx` — Detalle de cada módulo (sección 4)
- `02_Roadmap_Implementacion.md` — Fase 1 completa con specs y tests esperados
- `03_Formato_Informe_Forense.md` — Formato EXACTO del informe .txt (referencia obligatoria para ReportService)
- `05_Estandares_Codigo.md` — Encabezado Apache 2.0, naming, convenciones

## Reglas inviolables

1. TODO archivo `.cs` nuevo lleva el encabezado Apache 2.0 completo (está en `05_Estandares_Codigo.md`).
2. Comentarios en español, identificadores en inglés.
3. File-scoped namespaces siempre.
4. Constructor injection para dependencias entre servicios.
5. Métodos de I/O son `async Task` con `CancellationToken` propagado.
6. Nunca `System.Random` para criptografía. Solo `RandomNumberGenerator`.
7. Nunca `Console.WriteLine`. Si necesitás debug, usá `Debug.WriteLine`.
8. Orden de miembros en clase: constantes → campos privados → constructor → propiedades públicas → métodos públicos → métodos privados.
9. Tests siguen el patrón `Método_Escenario_ResultadoEsperado`.

## Lo que tenés que implementar

Implementá los 6 servicios en este orden exacto. Cada servicio tiene: interfaz (actualizar la existente en `Interfaces/`), implementación (en `Services/`), modelos (en `Models/`) y tests (en `tests/ForZip.Tests/Services/`).

---

### 1. LocalizationService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/ILocalizationService.cs`
- Crear: `src/ForZip.Core/Services/LocalizationService.cs`
- Llenar: `src/ForZip.Core/Resources/Strings_es.json` y `Strings_en.json`
- Crear: `tests/ForZip.Tests/Services/LocalizationServiceTests.cs`

**Interfaz:**
```csharp
public interface ILocalizationService
{
    string Get(string key);
    void SetLanguage(string code);
    string CurrentLanguage { get; }
}
```

**Implementación:**
- Cargar cadenas desde `Strings_es.json` / `Strings_en.json` embebidos como recursos del assembly.
- Detectar idioma del SO con `CultureInfo.CurrentUICulture`. Si empieza con `"es"` → español; todo lo demás → inglés.
- `Get(key)`: buscar en idioma actual; si no existe, buscar en inglés (fallback); si tampoco, devolver `[key]`.
- `SetLanguage(code)`: aceptar `"es"` o `"en"`. Cambiar el idioma activo inmediatamente.
- Para leer los JSON embebidos usá `Assembly.GetExecutingAssembly().GetManifestResourceStream()`. El nombre del recurso será algo como `ForZip.Core.Resources.Strings_es.json` (verificá el nombre exacto con `GetManifestResourceNames()`).

**Strings iniciales (mínimas, se agregan más en fases futuras):**

`Strings_es.json`:
```json
{
  "app_name": "ForZip",
  "app_version": "v1.0.0",
  "zip_success": "Compresión completada exitosamente.",
  "unzip_success": "Extracción completada exitosamente.",
  "hash_success": "Cálculo de hashes completado.",
  "verify_valid": "Informe verificado: el hash SHA-256 coincide.",
  "verify_invalid": "Informe NO verificado: el hash SHA-256 NO coincide.",
  "verify_bad_format": "El archivo no tiene el formato esperado de un informe ForZip.",
  "operation_cancelled": "Operación cancelada por el usuario.",
  "cancel": "Cancelar",
  "compress": "Comprimir",
  "extract": "Extraer",
  "calculate": "Calcular",
  "verify": "Verificar",
  "generate": "Generar",
  "copy": "Copiar",
  "save": "Guardar",
  "settings": "Ajustes",
  "about": "Acerca de",
  "hash_batch": "Hash Batch",
  "password_gen": "Generador de Contraseñas",
  "compression_level": "Nivel de compresión",
  "password_optional": "Contraseña (opcional)",
  "hash_algorithms": "Algoritmos de hash",
  "output_file": "Archivo de salida",
  "input_file": "Archivo de entrada",
  "destination_folder": "Carpeta de destino",
  "drag_drop_files": "Arrastrá archivos o carpetas aquí\no hacé clic para buscar",
  "drag_drop_zip": "Arrastrá un archivo ZIP aquí\no hacé clic para buscar",
  "drag_drop_report": "Arrastrá un informe ForZip (.txt) aquí\no hacé clic para buscar",
  "drag_drop_hash": "Arrastrá archivos aquí para calcular hashes\no hacé clic para buscar",
  "selected_files": "Archivos seleccionados",
  "clear_all": "Limpiar todo",
  "export_report": "Exportar Informe",
  "files_processed": "Archivos procesados",
  "total_files": "Total de archivos",
  "total_size": "Tamaño total",
  "report_title": "INFORME FORENSE DE INTEGRIDAD — ForZip {0}",
  "report_subtitle": "Generado por ForZip — Herramienta forense de compresión y verificación",
  "report_operator": "DATOS DEL OPERADOR",
  "report_case": "DATOS DEL CASO",
  "report_env": "INFORMACIÓN DEL ENTORNO",
  "report_params": "PARÁMETROS DE LA OPERACIÓN",
  "report_files": "ARCHIVOS PROCESADOS ({0} archivos)",
  "report_zip_hash": "HASH GLOBAL DEL ARCHIVO ZIP",
  "report_disclaimer_title": "DISCLAIMER",
  "report_disclaimer_text": "Este informe fue generado automáticamente por ForZip, una herramienta de\nsoftware libre distribuida bajo licencia Apache 2.0. Los resultados son\nproporcionados \"TAL CUAL\" (AS IS), sin garantías de ningún tipo. Es\nresponsabilidad exclusiva del operador validar la idoneidad de esta\nherramienta y sus resultados para el uso forense en su jurisdicción.\nForZip no reemplaza el criterio profesional del perito actuante.",
  "report_self_hash": "SHA-256 de este informe (excluyendo esta línea): {0}",
  "report_op_name": "Nombre",
  "report_op_title": "Cargo",
  "report_op_org": "Organismo",
  "report_op_email": "Email",
  "report_op_phone": "Teléfono",
  "report_case_number": "Caso Nro.",
  "report_case_desc": "Carátula",
  "report_case_court": "Juzgado",
  "report_env_datetime": "Fecha y hora",
  "report_env_os": "Sistema op.",
  "report_env_hostname": "Equipo",
  "report_env_user": "Usuario SO",
  "report_env_version": "Versión",
  "report_param_operation": "Operación",
  "report_param_level": "Nivel",
  "report_param_encryption": "Cifrado",
  "report_param_algorithms": "Algoritmos",
  "report_param_zipfile": "Archivo ZIP",
  "report_op_compression": "Compresión",
  "report_op_hash_batch": "Hash Batch",
  "report_encryption_aes": "AES-256 (contraseña aplicada)",
  "report_encryption_none": "Ninguno",
  "report_level_0": "Almacenamiento (sin compresión)",
  "report_level_1": "Mínima",
  "report_level_3": "Rápida",
  "report_level_5": "Normal",
  "report_level_7": "Alta",
  "report_level_9": "Máxima",
  "report_file_col": "Archivo",
  "report_size_col": "Tamaño",
  "report_hash_col_no": "Nro.",
  "report_global_file": "Archivo",
  "report_global_size": "Tamaño",
  "entropy_weak": "Débil",
  "entropy_fair": "Media",
  "entropy_strong": "Fuerte",
  "entropy_very_strong": "Muy fuerte",
  "error_no_algorithms": "Debe seleccionar al menos un algoritmo de hash.",
  "error_file_not_found": "El archivo no existe: {0}",
  "error_io": "Error de acceso al archivo: {0}",
  "error_wrong_password": "La contraseña es incorrecta. Verificá que sea la misma que se usó al comprimir.",
  "error_invalid_level": "Nivel de compresión inválido: {0}. Valores válidos: 0, 1, 3, 5, 7, 9.",
  "error_no_files": "No se seleccionaron archivos.",
  "error_no_char_class": "Debe activar al menos una clase de caracteres.",
  "error_password_length": "La longitud debe estar entre 8 y 128 caracteres.",
  "settings_general": "General",
  "settings_operator": "Operador",
  "settings_defaults": "Valores por Defecto",
  "settings_language": "Idioma",
  "settings_theme": "Tema",
  "settings_theme_dark": "Oscuro",
  "settings_theme_light": "Claro",
  "settings_output_dir": "Carpeta de salida por defecto",
  "settings_reset": "Restaurar valores",
  "settings_saved": "Ajustes guardados.",
  "confirm_operator_title": "CONFIRMAR DATOS DEL INFORME",
  "confirm_operator_msg": "Estos datos se incluirán en el informe forense.\nVerificá que sean correctos antes de continuar.",
  "confirm_generate": "Generar Informe",
  "copied": "Copiado ✓",
  "ready": "Listo",
  "processing": "Procesando..."
}
```

`Strings_en.json`:
```json
{
  "app_name": "ForZip",
  "app_version": "v1.0.0",
  "zip_success": "Compression completed successfully.",
  "unzip_success": "Extraction completed successfully.",
  "hash_success": "Hash calculation completed.",
  "verify_valid": "Report verified: SHA-256 hash matches.",
  "verify_invalid": "Report NOT verified: SHA-256 hash does NOT match.",
  "verify_bad_format": "The file does not have the expected format of a ForZip report.",
  "operation_cancelled": "Operation cancelled by the user.",
  "cancel": "Cancel",
  "compress": "Compress",
  "extract": "Extract",
  "calculate": "Calculate",
  "verify": "Verify",
  "generate": "Generate",
  "copy": "Copy",
  "save": "Save",
  "settings": "Settings",
  "about": "About",
  "hash_batch": "Hash Batch",
  "password_gen": "Password Generator",
  "compression_level": "Compression level",
  "password_optional": "Password (optional)",
  "hash_algorithms": "Hash algorithms",
  "output_file": "Output file",
  "input_file": "Input file",
  "destination_folder": "Destination folder",
  "drag_drop_files": "Drag files or folders here\nor click to browse",
  "drag_drop_zip": "Drag a ZIP file here\nor click to browse",
  "drag_drop_report": "Drag a ForZip report (.txt) here\nor click to browse",
  "drag_drop_hash": "Drag files here to calculate hashes\nor click to browse",
  "selected_files": "Selected files",
  "clear_all": "Clear all",
  "export_report": "Export Report",
  "files_processed": "Files processed",
  "total_files": "Total files",
  "total_size": "Total size",
  "report_title": "FORENSIC INTEGRITY REPORT — ForZip {0}",
  "report_subtitle": "Generated by ForZip — Forensic compression and verification tool",
  "report_operator": "OPERATOR INFORMATION",
  "report_case": "CASE INFORMATION",
  "report_env": "ENVIRONMENT INFORMATION",
  "report_params": "OPERATION PARAMETERS",
  "report_files": "FILES PROCESSED ({0} files)",
  "report_zip_hash": "ZIP FILE HASH",
  "report_disclaimer_title": "DISCLAIMER",
  "report_disclaimer_text": "This report was automatically generated by ForZip, a free and open-source\ntool distributed under the Apache License 2.0. Results are provided\n\"AS IS\", without warranties of any kind. It is the sole responsibility\nof the operator to validate the suitability of this tool and its results\nfor forensic use in their jurisdiction. ForZip does not replace the\nprofessional judgment of the examiner.",
  "report_self_hash": "SHA-256 of this report (excluding this line): {0}",
  "report_op_name": "Name",
  "report_op_title": "Title",
  "report_op_org": "Organization",
  "report_op_email": "Email",
  "report_op_phone": "Phone",
  "report_case_number": "Case No.",
  "report_case_desc": "Description",
  "report_case_court": "Court",
  "report_env_datetime": "Date and time",
  "report_env_os": "Operating sys",
  "report_env_hostname": "Hostname",
  "report_env_user": "OS user",
  "report_env_version": "Version",
  "report_param_operation": "Operation",
  "report_param_level": "Level",
  "report_param_encryption": "Encryption",
  "report_param_algorithms": "Algorithms",
  "report_param_zipfile": "ZIP file",
  "report_op_compression": "Compression",
  "report_op_hash_batch": "Hash Batch",
  "report_encryption_aes": "AES-256 (password applied)",
  "report_encryption_none": "None",
  "report_level_0": "Store (no compression)",
  "report_level_1": "Minimum",
  "report_level_3": "Fast",
  "report_level_5": "Normal",
  "report_level_7": "High",
  "report_level_9": "Maximum",
  "report_file_col": "File",
  "report_size_col": "Size",
  "report_hash_col_no": "No.",
  "report_global_file": "File",
  "report_global_size": "Size",
  "entropy_weak": "Weak",
  "entropy_fair": "Fair",
  "entropy_strong": "Strong",
  "entropy_very_strong": "Very strong",
  "error_no_algorithms": "At least one hash algorithm must be selected.",
  "error_file_not_found": "File not found: {0}",
  "error_io": "File access error: {0}",
  "error_wrong_password": "Incorrect password. Make sure it matches the one used during compression.",
  "error_invalid_level": "Invalid compression level: {0}. Valid values: 0, 1, 3, 5, 7, 9.",
  "error_no_files": "No files selected.",
  "error_no_char_class": "At least one character class must be enabled.",
  "error_password_length": "Length must be between 8 and 128 characters.",
  "settings_general": "General",
  "settings_operator": "Operator",
  "settings_defaults": "Defaults",
  "settings_language": "Language",
  "settings_theme": "Theme",
  "settings_theme_dark": "Dark",
  "settings_theme_light": "Light",
  "settings_output_dir": "Default output folder",
  "settings_reset": "Reset defaults",
  "settings_saved": "Settings saved.",
  "confirm_operator_title": "CONFIRM REPORT DATA",
  "confirm_operator_msg": "This information will be included in the forensic report.\nPlease verify it is correct before proceeding.",
  "confirm_generate": "Generate Report",
  "copied": "Copied ✓",
  "ready": "Ready",
  "processing": "Processing..."
}
```

**Tests (LocalizationServiceTests.cs):**
- `Get_ExistingKeyInSpanish_ReturnsSpanishValue`: forzar `SetLanguage("es")`, pedir `"cancel"` → `"Cancelar"`.
- `Get_ExistingKeyInEnglish_ReturnsEnglishValue`: forzar `SetLanguage("en")`, pedir `"cancel"` → `"Cancel"`.
- `Get_NonExistentKey_ReturnsKeyInBrackets`: pedir `"clave_inexistente"` → `"[clave_inexistente]"`.
- `SetLanguage_SwitchesToEnglish_GetReturnsEnglish`: empezar en ES, cambiar a EN, verificar.
- `SetLanguage_SwitchesToSpanish_GetReturnsSpanish`: empezar en EN, cambiar a ES, verificar.

---

### 2. HashService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/IHashService.cs`
- Crear: `src/ForZip.Core/Services/HashService.cs`
- Crear: `src/ForZip.Core/Models/HashResult.cs`
- Crear: `src/ForZip.Core/Models/HashAlgorithmType.cs`
- Crear: `tests/ForZip.Tests/Services/HashServiceTests.cs`

**Interfaz:**
```csharp
public interface IHashService
{
    Task<HashResult> ComputeHashesAsync(string filePath, HashSet<HashAlgorithmType> algorithms, IProgress<double>? progress, CancellationToken ct);
    string ComputeSha256(string text);
}
```

**Modelos:**

`HashAlgorithmType.cs`:
```csharp
public enum HashAlgorithmType
{
    MD5,
    SHA1,
    SHA256,
    SHA512
}
```

`HashResult.cs`:
```csharp
public class HashResult
{
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public Dictionary<HashAlgorithmType, string> Hashes { get; set; } = new();
}
```

**Implementación:**
- Lectura en pasada única: abrir `FileStream`, leer en bloques de 64 KB (`65536` bytes), alimentar todos los algoritmos seleccionados simultáneamente con `TransformBlock` / `TransformFinalBlock`.
- Crear una instancia de `System.Security.Cryptography.HashAlgorithm` por cada algoritmo seleccionado (`MD5.Create()`, `SHA1.Create()`, `SHA256.Create()`, `SHA512.Create()`).
- Para cada bloque leído, llamar `TransformBlock` en todos los algoritmos. Al final, `TransformFinalBlock`.
- Progreso = bytesLeídos / tamañoTotal (double 0.0 a 1.0).
- Hashes en lowercase hex: `BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()`.
- `ComputeSha256(string text)`: convertir a bytes UTF-8, hashear con SHA256, devolver lowercase hex.
- Validaciones: lanzar `ArgumentException` si el set de algoritmos está vacío, `FileNotFoundException` si el archivo no existe.
- Disponer los algoritmos con `using` o `Dispose()`.

**Tests (HashServiceTests.cs):**

Crear un archivo de test `tests/ForZip.Tests/TestData/sample.txt` con contenido exacto `ForZip test file content` (sin newline al final). Precalculá los hashes de ese contenido para los asserts.

- `ComputeHashesAsync_KnownFile_ReturnsCorrectSha256`: hashear sample.txt con SHA-256, verificar contra valor precalculado.
- `ComputeHashesAsync_KnownFile_ReturnsCorrectMd5`: ídem con MD5.
- `ComputeHashesAsync_AllAlgorithms_ReturnsAllFour`: pedir los 4, verificar que el diccionario tiene 4 entradas.
- `ComputeHashesAsync_OnlySha256_ReturnsOnlySha256`: pedir solo SHA-256, verificar que tiene solo 1 entrada.
- `ComputeHashesAsync_EmptyFile_ReturnsValidHashes`: crear archivo temporal vacío (0 bytes), verificar que los hashes son los del contenido vacío.
- `ComputeHashesAsync_NoAlgorithms_ThrowsArgumentException`: pasar set vacío → `ArgumentException`.
- `ComputeHashesAsync_FileNotFound_ThrowsFileNotFoundException`: path inexistente → `FileNotFoundException`.
- `ComputeHashesAsync_Cancellation_ThrowsOperationCanceledException`: cancelar el token antes de llamar → `OperationCanceledException`.
- `ComputeSha256_KnownString_ReturnsExpectedHash`: `ComputeSha256("test")` → `"9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"`.

Para los hashes precalculados de `sample.txt`, calculalos vos mismo al crear el archivo de test (leé el archivo, hashealo, y usá esos valores en los asserts). O usá el método `ComputeSha256` que estás implementando para verificar coherencia cruzada.

---

### 3. PasswordService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/IPasswordService.cs`
- Crear: `src/ForZip.Core/Services/PasswordService.cs`
- Crear: `src/ForZip.Core/Models/PasswordOptions.cs`
- Crear: `tests/ForZip.Tests/Services/PasswordServiceTests.cs`

**Interfaz:**
```csharp
public interface IPasswordService
{
    string GeneratePassword(PasswordOptions options);
    double CalculateEntropy(PasswordOptions options, int length);
}
```

**Modelo:**
```csharp
public class PasswordOptions
{
    public int Length { get; set; } = 16;
    public bool IncludeUppercase { get; set; } = true;
    public bool IncludeLowercase { get; set; } = true;
    public bool IncludeDigits { get; set; } = true;
    public bool IncludeSymbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; } = false;
}
```

**Implementación:**
- Construir el pool de caracteres según las opciones activas:
  - Uppercase: `ABCDEFGHIJKLMNOPQRSTUVWXYZ`
  - Lowercase: `abcdefghijklmnopqrstuvwxyz`
  - Digits: `0123456789`
  - Symbols: `!@#$%^&*()-_=+[]{}|;:',.<>?/~`
  - Si `ExcludeAmbiguous`: remover `0`, `O`, `o`, `I`, `l`, `1`, `|` del pool.
- Si ninguna clase está activa → `ArgumentException`.
- Si longitud < 8 o > 128 → `ArgumentException`.
- Generar usando `RandomNumberGenerator.GetBytes()`. Para evitar sesgo modular, usar rejection sampling: generar un byte, si `byte < (256 - 256 % poolSize)` aceptar `pool[byte % poolSize]`, si no, descartar y generar otro.
- Garantizar al menos un carácter de cada clase activa: generar uno de cada clase primero, llenar el resto aleatoriamente, luego shuffle todo el array con Fisher-Yates usando `RandomNumberGenerator`.
- `CalculateEntropy`: `length * Math.Log2(poolSize)`.

**Tests (PasswordServiceTests.cs):**
- `GeneratePassword_AllClasses_ContainsAllClasses`: 16 chars con todo activado → tiene al menos una mayúscula, una minúscula, un dígito, un símbolo.
- `GeneratePassword_OnlyDigits_ContainsOnlyDigits`: solo dígitos → todos los chars son 0-9.
- `GeneratePassword_ExcludeAmbiguous_DoesNotContainAmbiguous`: con ExcludeAmbiguous → no contiene `0OoIl1|`.
- `GeneratePassword_CorrectLength_ReturnsRequestedLength`: pedir 24 → longitud es 24.
- `GeneratePassword_LengthTooShort_ThrowsArgumentException`: longitud 5 → `ArgumentException`.
- `GeneratePassword_LengthTooLong_ThrowsArgumentException`: longitud 200 → `ArgumentException`.
- `GeneratePassword_NoClassesEnabled_ThrowsArgumentException`: todo en false → `ArgumentException`.
- `CalculateEntropy_KnownValues_ReturnsExpectedBits`: 12 chars, pool de 94 (todas las clases, no ambiguous excluded) → `≈ 78.84` (verificá con `12 * Math.Log2(94)`).
- `GeneratePassword_1000Passwords_AllMeetConstraints`: generar 1000, verificar que todas tienen la longitud correcta y contienen al menos un char de cada clase activa.

---

### 4. ZipService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/IZipService.cs`
- Crear: `src/ForZip.Core/Services/ZipService.cs`
- Crear: `src/ForZip.Core/Models/ZipOptions.cs`
- Crear: `tests/ForZip.Tests/Services/ZipServiceTests.cs`
- Crear: `tests/ForZip.Tests/TestPasswords.cs`

**Interfaz:**
```csharp
public interface IZipService
{
    Task<List<HashResult>> CompressAsync(ZipOptions options, IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct);
    Task DecompressAsync(string zipPath, string outputDir, string? password, IProgress<(long bytesProcessed, long totalBytes)>? progress, CancellationToken ct);
}
```

**Modelo:**
```csharp
public class ZipOptions
{
    public List<string> SourcePaths { get; set; } = new();
    public string OutputPath { get; set; } = string.Empty;
    public int CompressionLevel { get; set; } = 5;
    public string? Password { get; set; }
    public HashSet<HashAlgorithmType> HashAlgorithms { get; set; } = new();
}
```

**TestPasswords.cs** (en la raíz de ForZip.Tests):
```csharp
// (encabezado Apache 2.0)
namespace ForZip.Tests;

/// <summary>
/// Contraseñas hardcodeadas exclusivamente para tests unitarios.
/// NO usar estas contraseñas en producción ni como ejemplo para usuarios.
/// </summary>
public static class TestPasswords
{
    public const string Simple = "ForZip_Test_2026!";
    public const string Complex = "F0rZ1p#T€st_Cömpl3x!";
    public const string Long = "ThisIsAVeryLongPasswordForStressTestingForZipAES256Encryption_2026!@#$";
}
```

**Implementación:**
- Usar `ICSharpCode.SharpZipLib.Zip` para crear/leer ZIPs.
- `CompressAsync`: crear `ZipOutputStream`, recorrer todos los archivos de `SourcePaths` (expandir carpetas recursivamente), agregar cada uno como `ZipEntry`. Si `Password` no es null/empty, configurar AES-256 en el `ZipOutputStream`. Calcular el tamaño total de todos los archivos antes de empezar para reportar progreso por bytes. Si `HashAlgorithms` no está vacío, hashear cada archivo usando `IHashService` (inyectado por constructor) y acumular los `HashResult`. Niveles válidos: 0,1,3,5,7,9 — mapear al `CompressionMethod` y `SetLevel` de SharpZipLib.
- `DecompressAsync`: abrir `ZipInputStream`, si tiene password configurarla. Extraer cada entry respetando la estructura de carpetas. Validar contra Zip Slip (que la ruta extraída esté dentro de `outputDir`).
- Si se cancela durante compresión, eliminar el archivo ZIP parcial.
- Recibir `IHashService` por constructor.

**Tests (ZipServiceTests.cs):**
- Todos los tests crean archivos temporales y los limpian en `Dispose()` o `finally`.
- `CompressAndDecompress_SimpleFile_ContentIdentical`: comprimir un archivo, descomprimir, verificar bytes idénticos.
- `CompressAndDecompress_WithAes256_ContentIdentical`: comprimir con `TestPasswords.Simple`, descomprimir con la misma contraseña, verificar.
- `Decompress_WrongPassword_ThrowsException`: comprimir con password, descomprimir con otra → excepción.
- `Compress_Level0Store_ValidZip`: comprimir con nivel 0, verificar que el ZIP es válido y el tamaño es ≥ original.
- `Compress_Level9_SmallerThanLevel0`: comprimir datos comprimibles (ej: texto repetitivo) con nivel 9 vs nivel 0, verificar que nivel 9 es más chico.
- `Compress_FolderWithSubfolders_PreservesStructure`: crear carpeta con subcarpetas, comprimir, descomprimir, verificar estructura.
- `Compress_Cancellation_ThrowsAndCleansUp`: cancelar durante compresión → `OperationCanceledException` y el archivo ZIP parcial no existe.
- `Compress_EmptyFile_HandledCorrectly`: archivo de 0 bytes → se comprime y descomprime sin error.
- `Compress_WithHashes_ReturnsHashResults`: comprimir con SHA-256, verificar que devuelve lista de HashResult con hashes correctos.

---

### 5. ReportService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/IReportService.cs`
- Crear: `src/ForZip.Core/Services/ReportService.cs`
- Crear: `src/ForZip.Core/Models/ReportData.cs`
- Crear: `src/ForZip.Core/Models/OperatorInfo.cs`
- Crear: `src/ForZip.Core/Models/OperationType.cs`
- Crear: `tests/ForZip.Tests/Services/ReportServiceTests.cs`

**IMPORTANTE:** El formato EXACTO del informe está en `03_Formato_Informe_Forense.md`. Leé ese archivo completo antes de implementar. Cada detalle cuenta: separadores de 80 `=`, alineación de columnas, formato de tamaño con separador de miles según idioma, una tabla por algoritmo, la línea auto-firmante sin CRLF al final.

**Interfaz:**
```csharp
public interface IReportService
{
    string GenerateReport(ReportData data, string language);
    Task SaveReportAsync(string content, string outputPath);
    (bool isValid, string details) VerifyReport(string reportPath);
}
```

**Modelos:**

`OperationType.cs`:
```csharp
public enum OperationType
{
    Compression,
    HashBatch
}
```

`OperatorInfo.cs`:
```csharp
public class OperatorInfo
{
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? Organization { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}
```

`ReportData.cs`:
```csharp
public class ReportData
{
    public OperatorInfo? Operator { get; set; }
    public string? CaseNumber { get; set; }
    public string? CaseDescription { get; set; }
    public string? Court { get; set; }
    public OperationType Operation { get; set; }
    public int CompressionLevel { get; set; }
    public bool HasPassword { get; set; }
    public HashSet<HashAlgorithmType> Algorithms { get; set; } = new();
    public string? ZipFilePath { get; set; }
    public long? ZipFileSize { get; set; }
    public string? ZipHash { get; set; }
    public List<HashResult> FileResults { get; set; } = new();
    public string ForZipVersion { get; set; } = "v1.0.0";
}
```

**Implementación:**
- Recibir `IHashService` y `ILocalizationService` por constructor.
- `GenerateReport`: construir con `StringBuilder`, línea por línea, respetando el formato de `03_Formato_Informe_Forense.md` exactamente. Usar `ILocalizationService` cargando un idioma temporal si hace falta (o construir un diccionario local con las claves de reporte). Usar `\r\n` para todos los line endings. Al final, calcular SHA-256 del contenido (como bytes UTF-8 con BOM) excluyendo la última línea, y agregar la línea auto-firmante SIN `\r\n` después.
- `SaveReportAsync`: escribir con `new UTF8Encoding(true)` (con BOM) y asegurar CRLF.
- `VerifyReport`: leer todos los bytes, encontrar el último `\r\n`, separar contenido del hash, recalcular, comparar case-insensitive.

**Tests (ReportServiceTests.cs):**
- `GenerateReport_WithAllData_ContainsAllSections`: generar con datos completos → contiene los encabezados de todas las secciones.
- `GenerateReport_SelfHashVerifies_ReturnsValid`: generar, guardar, verificar → `isValid = true`.
- `VerifyReport_ModifiedReport_ReturnsInvalid`: generar, guardar, modificar un byte, verificar → `isValid = false`.
- `VerifyReport_BadFormat_ReturnsFormatError`: archivo sin estructura de informe → `isValid = false`, details contiene "formato".
- `GenerateReport_InSpanish_HasSpanishHeaders`: generar en "es" → contiene "INFORME FORENSE DE INTEGRIDAD".
- `GenerateReport_InEnglish_HasEnglishHeaders`: generar en "en" → contiene "FORENSIC INTEGRITY REPORT".
- `GenerateReport_HashBatchMode_OmitsZipSection`: operación HashBatch → no contiene "HASH GLOBAL".
- `GenerateReport_EmptyOperator_OmitsOperatorSection`: sin datos de operador → no contiene "DATOS DEL OPERADOR".

---

### 6. ConfigService

**Archivos a crear/modificar:**
- Modificar: `src/ForZip.Core/Interfaces/IConfigService.cs`
- Crear: `src/ForZip.Core/Services/ConfigService.cs`
- Crear: `src/ForZip.Core/Models/AppConfig.cs`
- Crear: `tests/ForZip.Tests/Services/ConfigServiceTests.cs`

**Interfaz:**
```csharp
public interface IConfigService
{
    AppConfig Load();
    void Save(AppConfig config);
}
```

**Modelo:**
```csharp
public class AppConfig
{
    public string Language { get; set; } = "es";
    public string Theme { get; set; } = "dark";
    public int DefaultCompressionLevel { get; set; } = 5;
    public HashSet<HashAlgorithmType> DefaultHashAlgorithms { get; set; } = new() { HashAlgorithmType.SHA256 };
    public string? DefaultOutputDirectory { get; set; }
    public OperatorInfo Operator { get; set; } = new();
}
```

**Implementación:**
- Ruta del config: `Path.Combine(AppContext.BaseDirectory, "config.json")`.
- `Load()`: si no existe → devolver `new AppConfig()` con defaults. Si existe pero el JSON es inválido → hacer backup como `config.json.backup_YYYYMMDD_HHmmss`, devolver defaults.
- `Save()`: serializar con `JsonSerializer.Serialize` con opciones `WriteIndented = true` y escribir a disco.
- Recibir la ruta base por constructor para facilitar testing (así los tests pueden usar un directorio temporal).

**Tests (ConfigServiceTests.cs):**
- `Load_NoFile_ReturnsDefaults`: sin config.json → defaults (Language "es", Theme "dark", etc.).
- `SaveAndLoad_Roundtrip_PreservesAllFields`: guardar config con valores custom, cargar, verificar igualdad.
- `Load_CorruptJson_ReturnsDefaultsAndCreatesBackup`: escribir JSON inválido en config.json, cargar → defaults, verificar que existe archivo `.backup_*`.
- `Save_CreatesJsonFile`: guardar, verificar que el archivo existe y tiene contenido JSON válido.

---

## Verificación final

Cuando termines los 6 servicios:

```
dotnet build ForZip.sln
dotnet test
```

**Criterio de aceptación:**
- `dotnet build` → 0 errores, 0 warnings
- `dotnet test` → todos los tests pasan (verde)
- Cada servicio tiene: interfaz actualizada + implementación + tests

Reportame:
- Output de `dotnet build` (0 errores, 0 warnings)
- Output de `dotnet test` (cantidad de tests, todos pasando)
- Lista de archivos creados/modificados
