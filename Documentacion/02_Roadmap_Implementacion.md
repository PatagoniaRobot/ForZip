# ForZip — Roadmap de Implementación

**Versión:** 1.0  
**Fecha:** 9 de mayo de 2026  
**Autor:** Claudio Andino (claudio@patagoniarobot.com)  
**Proyecto:** ForZip v1.0.0  
**Licencia:** Apache 2.0  

> Este documento es el plan maestro de ejecución del proyecto ForZip.  
> Está diseñado para ser consumido por agentes de IA ejecutores (Qwen, Gemini, Claude Code)  
> bajo la supervisión de Claudio Andino como director técnico.

---

## Reglas Inviolables para el Agente Ejecutor

Estas 9 reglas aplican a TODA fase. Violarlas invalida el trabajo.

1. **No inventar dependencias.** Solo usá las librerías listadas en la sección "Stack Técnico". Si necesitás algo más, preguntá antes de instalar.

2. **Encabezado obligatorio.** TODO archivo `.cs` nuevo debe llevar el encabezado Apache 2.0 completo definido en `05_Estandares_Codigo.md`. Sin excepción.

3. **Comentarios en español, identificadores en inglés.** Ejemplo: `// Calcula el hash del archivo` / `public byte[] ComputeHash(...)`.

4. **No tocar decisiones cerradas.** Las decisiones de la tabla de diseño (nombre, framework, librerías, paleta, licencia) están cerradas. No las cambies, no propongas alternativas.

5. **Un paso a la vez.** Completá cada fase y esperá validación humana (🔔) antes de pasar a la siguiente. No acumules fases.

6. **Tests primero en Fase 1.** Los servicios core se desarrollan con tests unitarios. No se avanza a Fase 2 sin que todos los tests pasen.

7. **Sin código muerto.** No dejes métodos vacíos, stubs sin implementar, ni `// TODO` sin un issue asociado. Si algo no se implementa todavía, no lo declares.

8. **Respetar la estructura de carpetas.** La estructura definida más abajo es la estructura final. No crees carpetas adicionales sin permiso.

9. **Reportar bloqueos inmediatamente.** Si algo no funciona, no lo parchees con workarounds. Reportá el problema exacto para que el director técnico decida.

---

## Stack Técnico Confirmado

| Componente | Tecnología | Versión | Licencia |
|---|---|---|---|
| Lenguaje | C# | 12 (.NET 8) | MIT |
| Runtime | .NET | 8.0 LTS | MIT |
| UI Framework | Avalonia UI | 11.x (última estable) | MIT |
| ZIP + AES-256 | SharpZipLib | última estable | Apache 2.0 |
| Tests | xUnit | última estable | Apache 2.0 |
| Mocks (si necesario) | NSubstitute | última estable | BSD |
| Logging | Propio (archivo rotativo) | — | — |

### Dependencias Prohibidas

- **NO** usar `System.IO.Compression` para crear ZIPs (no soporta AES-256).
- **NO** usar `System.Random` para generación de contraseñas (usar `RandomNumberGenerator`).
- **NO** agregar Entity Framework, Serilog, MediatR, AutoMapper, ni ningún framework pesado.
- **NO** usar librerías de hashing externas. Usar `System.Security.Cryptography` nativo.

---

## Estructura de Carpetas del Proyecto

```
D:\PROYECTOS\ForZip\
├── ForZip.sln
├── LICENSE
├── NOTICE
├── README.md
├── CONTRIBUTING.md
├── .editorconfig
├── .gitignore
│
├── src/
│   ├── ForZip.Core/                    # Lógica de negocio (sin dependencia de UI)
│   │   ├── ForZip.Core.csproj
│   │   ├── Services/
│   │   │   ├── HashService.cs          # MD5, SHA-1, SHA-256, SHA-512 en pasada única
│   │   │   ├── ZipService.cs           # Compresión/descompresión con SharpZipLib
│   │   │   ├── PasswordService.cs      # Generador criptográfico de contraseñas
│   │   │   ├── ReportService.cs        # Generación del informe forense .txt
│   │   │   ├── ConfigService.cs        # Persistencia de configuración (JSON)
│   │   │   └── LocalizationService.cs  # i18n ES/EN con detección automática
│   │   ├── Models/
│   │   │   ├── HashResult.cs           # Resultado de hashing por archivo
│   │   │   ├── ZipOptions.cs           # Opciones de compresión
│   │   │   ├── PasswordOptions.cs      # Opciones del generador de contraseñas
│   │   │   ├── ReportData.cs           # Datos para el informe forense
│   │   │   ├── OperatorInfo.cs         # Datos del operador/organismo
│   │   │   └── AppConfig.cs            # Modelo de configuración persistente
│   │   ├── Interfaces/
│   │   │   ├── IHashService.cs
│   │   │   ├── IZipService.cs
│   │   │   ├── IPasswordService.cs
│   │   │   ├── IReportService.cs
│   │   │   ├── IConfigService.cs
│   │   │   └── ILocalizationService.cs
│   │   └── Resources/
│   │       ├── Strings_es.json         # Cadenas en español
│   │       └── Strings_en.json         # Cadenas en inglés
│   │
│   ├── ForZip.GUI/                     # Aplicación Avalonia
│   │   ├── ForZip.GUI.csproj
│   │   ├── App.axaml
│   │   ├── App.axaml.cs
│   │   ├── Program.cs
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs
│   │   │   ├── ZipViewModel.cs
│   │   │   ├── UnzipViewModel.cs
│   │   │   ├── HashBatchViewModel.cs
│   │   │   ├── VerifyReportViewModel.cs
│   │   │   ├── PasswordGeneratorViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   └── AboutViewModel.cs
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml / .cs
│   │   │   ├── ZipView.axaml / .cs
│   │   │   ├── UnzipView.axaml / .cs
│   │   │   ├── HashBatchView.axaml / .cs
│   │   │   ├── VerifyReportView.axaml / .cs
│   │   │   ├── PasswordGeneratorView.axaml / .cs
│   │   │   ├── SettingsView.axaml / .cs
│   │   │   └── AboutView.axaml / .cs
│   │   ├── Styles/
│   │   │   ├── DarkTheme.axaml
│   │   │   └── LightTheme.axaml
│   │   ├── Assets/
│   │   │   ├── icon.ico                # Ícono temporal (Fase 6)
│   │   │   └── logo.png                # Logo temporal (Fase 6)
│   │   └── Converters/
│   │       └── (convertidores si hacen falta)
│   │
│   └── ForZip.Cli/                     # Modo línea de comandos
│       ├── ForZip.Cli.csproj
│       ├── Program.cs
│       └── CliParser.cs                # Parsing de argumentos (manual, sin lib)
│
├── tests/
│   └── ForZip.Tests/
│       ├── ForZip.Tests.csproj
│       ├── Services/
│       │   ├── HashServiceTests.cs
│       │   ├── ZipServiceTests.cs
│       │   ├── PasswordServiceTests.cs
│       │   ├── ReportServiceTests.cs
│       │   ├── ConfigServiceTests.cs
│       │   └── LocalizationServiceTests.cs
│       ├── TestData/
│       │   └── (archivos de prueba pequeños)
│       └── TestPasswords.cs            # Contraseñas hardcodeadas para tests
│
├── Documentacion/
│   ├── 01_Especificacion_Funcional.docx
│   ├── 02_Roadmap_Implementacion.md    # ← Este documento
│   ├── 03_Formato_Informe_Forense.md
│   ├── 04_Mockups_UI.md
│   └── 05_Estandares_Codigo.md
│
└── Logs/                               # Creada en runtime, rotación 30 días
```

---

## Fases de Implementación

---

### FASE 0 — Bootstrap del Proyecto

**Objetivo:** Crear la solución .NET, los proyectos, instalar dependencias, configurar `.editorconfig` y `.gitignore`. Al final de esta fase el proyecto compila vacío sin errores.

**Tareas:**

1. Crear la estructura de carpetas según el árbol de arriba.
2. Crear `ForZip.sln` con 4 proyectos:
   - `ForZip.Core` → Class Library (.NET 8)
   - `ForZip.GUI` → Avalonia Application (.NET 8)
   - `ForZip.Cli` → Console Application (.NET 8)
   - `ForZip.Tests` → xUnit Test Project (.NET 8)
3. Configurar referencias entre proyectos:
   - `ForZip.GUI` → referencia a `ForZip.Core`
   - `ForZip.Cli` → referencia a `ForZip.Core`
   - `ForZip.Tests` → referencia a `ForZip.Core`
4. Instalar NuGets:
   - `ForZip.Core`: `SharpZipLib`
   - `ForZip.GUI`: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`
   - `ForZip.Tests`: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`
5. Crear `.editorconfig` según `05_Estandares_Codigo.md`.
6. Crear `.gitignore` estándar para .NET.
7. Copiar `LICENSE`, `NOTICE`, `README.md`, `CONTRIBUTING.md` a la raíz.
8. Verificar: `dotnet build ForZip.sln` compila sin errores ni warnings.

**Criterio de aceptación:**
- `dotnet build` → 0 errores, 0 warnings
- `dotnet test` → 0 tests (pero el runner ejecuta sin error)
- Estructura de carpetas coincide exactamente con el árbol documentado

🔔 **Punto de validación humana.** Claudio revisa la estructura antes de continuar.

---

### FASE 1 — Servicios Core (sin UI)

**Objetivo:** Implementar toda la lógica de negocio en `ForZip.Core` con cobertura de tests. Esta fase NO toca la UI. Cada servicio se desarrolla con su interfaz, implementación y tests en paralelo.

**Orden de implementación:**

#### 1.1 — LocalizationService

- Cargar cadenas desde `Strings_es.json` / `Strings_en.json` (embebidos como recursos).
- Detectar idioma del SO (`CultureInfo.CurrentUICulture`). Si empieza con `es` → español; todo lo demás → inglés.
- Método `string Get(string key)` que devuelve la cadena localizada.
- Método `void SetLanguage(string code)` para cambio manual (Settings).
- Fallback: si la clave no existe en el idioma actual, buscar en inglés; si tampoco, devolver la clave misma entre corchetes `[key]`.

**Tests:**
- Cadena existente en ES → devuelve ES.
- Cadena existente forzando EN → devuelve EN.
- Cadena inexistente → devuelve `[key]`.
- Detección automática con cultura `es-AR` → español.
- Detección automática con cultura `en-US` → inglés.
- Detección automática con cultura `fr-FR` → fallback inglés.

#### 1.2 — HashService

- Método `Task<HashResult> ComputeHashesAsync(string filePath, HashSet<HashAlgorithmType> algorithms, IProgress<double> progress, CancellationToken ct)`.
- `HashAlgorithmType` es un enum: `MD5`, `SHA1`, `SHA256`, `SHA512`.
- Lectura en pasada única: leer el archivo una sola vez en bloques de 64 KB, alimentando todos los algoritmos seleccionados simultáneamente con `TransformBlock` / `TransformFinalBlock`.
- Devolver `HashResult` con diccionario `Algorithm → hex string (lowercase)`.
- Progreso reportado como porcentaje (0.0 a 1.0) basado en bytes leídos vs tamaño total.
- Cancelación cooperativa.
- Método estático `string ComputeSha256(string text)` para el hash auto-firmante del informe (opera sobre string UTF-8, no archivo).

**Tests:**
- Hash de un archivo conocido (crear archivo de test con contenido fijo) → verificar MD5, SHA-1, SHA-256, SHA-512 contra valores precalculados.
- Selección parcial (solo SHA-256) → resultado contiene solo SHA-256.
- Archivo vacío → hashes válidos del contenido vacío.
- Cancelación → `OperationCanceledException`.
- `ComputeSha256("test")` → valor precalculado.

#### 1.3 — PasswordService

- Método `string GeneratePassword(PasswordOptions options)`.
- `PasswordOptions`: `Length` (int, min 8, max 128), `IncludeUppercase` (bool), `IncludeLowercase` (bool), `IncludeDigits` (bool), `IncludeSymbols` (bool), `ExcludeAmbiguous` (bool — excluye `0OoIl1|`).
- Al menos una clase de caracteres debe estar activa; si ninguna lo está, lanzar `ArgumentException`.
- Usar `RandomNumberGenerator.GetBytes()` para selección de caracteres. Nunca `System.Random`.
- El resultado garantiza al menos un carácter de cada clase activa.
- Método `double CalculateEntropy(string password)` que devuelve bits de entropía: `length * log2(poolSize)`.

**Tests:**
- Contraseña de 16 chars con todas las clases → contiene al menos una de cada.
- Contraseña solo dígitos → contiene solo dígitos.
- ExcludeAmbiguous → no contiene `0OoIl1|`.
- Longitud < 8 → `ArgumentException`.
- Todas las clases false → `ArgumentException`.
- Entropía de 12 chars con pool de 94 → `≈ 78.84 bits`.
- Generación de 1000 contraseñas → todas cumplen las restricciones (test estadístico).

#### 1.4 — ZipService

- Método `Task CompressAsync(ZipOptions options, IProgress<(long bytesProcessed, long totalBytes)> progress, CancellationToken ct)`.
  - `ZipOptions`: `SourcePaths` (lista de archivos/carpetas), `OutputPath`, `CompressionLevel` (0/1/3/5/7/9), `Password` (string nullable), `HashAlgorithms` (set de algoritmos a calcular durante la compresión).
  - Si `Password` no es null/empty, usar AES-256 de SharpZipLib.
  - Progreso por bytes (no por archivo).
  - Preservar estructura relativa de carpetas.
  - Calcular hashes de cada archivo fuente mientras se comprime (reusar `HashService`).
  - Devolver o acumular `List<HashResult>` para el informe.
- Método `Task DecompressAsync(string zipPath, string outputDir, string? password, IProgress<(long, long)> progress, CancellationToken ct)`.
  - Extraer respetando estructura interna.
  - Si tiene AES y no se provee contraseña → excepción clara.
  - Progreso por bytes.

**Tests (usar `TestPasswords.cs`):**
- Comprimir archivo → descomprimir → contenido idéntico byte a byte.
- Comprimir con AES-256 → descomprimir con contraseña correcta → OK.
- Descomprimir con contraseña incorrecta → excepción.
- Nivel 0 (store) → ZIP válido, tamaño ≥ original.
- Nivel 9 → ZIP válido, tamaño < nivel 0 (para datos comprimibles).
- Comprimir carpeta con subcarpetas → estructura preservada.
- Cancelación → `OperationCanceledException`, archivo parcial eliminado.
- Archivo vacío (0 bytes) → se maneja sin error.
- Hashes calculados durante compresión → coinciden con hashes independientes.

#### 1.5 — ReportService

- Método `string GenerateReport(ReportData data, string language)`.
  - Genera el contenido del informe forense como string.
  - Formato exacto según `03_Formato_Informe_Forense.md`.
  - Al final, calcula el SHA-256 auto-firmante de todo el contenido del informe (excluyendo la última línea) y lo agrega.
- Método `Task SaveReportAsync(string content, string outputPath)`.
  - Guarda con encoding UTF-8 con BOM, line endings CRLF.
- Método `(bool isValid, string details) VerifyReport(string reportPath)`.
  - Lee el informe, extrae la última línea (el hash auto-firmante), recalcula el SHA-256 del resto, compara.
  - Devuelve `isValid` y un detalle descriptivo (coincide / no coincide / formato inválido).

**Tests:**
- Generar informe con datos conocidos → contiene todos los campos esperados.
- Hash auto-firmante → verificación devuelve `isValid = true`.
- Informe modificado (cambiar un byte) → verificación devuelve `isValid = false`.
- Informe sin línea de hash → `formato inválido`.
- Informe en ES y EN → encabezados en el idioma correcto.

#### 1.6 — ConfigService

- Persistencia en `config.json` al lado del exe.
- Modelo `AppConfig` con: idioma preferido, tema (dark/light), nivel de compresión default, algoritmos de hash default, datos del operador (`OperatorInfo`), carpeta de salida default.
- Método `AppConfig Load()` → carga o crea con defaults.
- Método `void Save(AppConfig config)`.
- Si el archivo está corrupto, hacer backup del corrupto y crear uno nuevo con defaults.

**Tests:**
- Save + Load → roundtrip perfecto.
- Archivo inexistente → defaults.
- Archivo corrupto (JSON inválido) → defaults + backup creado.
- Campos individuales persistidos correctamente.

**Criterio de aceptación de Fase 1:**
- `dotnet test` → todos los tests pasan (verde).
- Cada servicio tiene interfaz + implementación + tests.
- Cobertura razonable de happy path + error cases.
- 0 warnings de compilación.

🔔 **Punto de validación humana.** Claudio ejecuta `dotnet test` y revisa el output.

---

### FASE 2 — Modelos y ViewModels

**Objetivo:** Crear los ViewModels que conectan los servicios core con la UI futura. Usar MVVM puro: los ViewModels NO conocen las Views. Dependen de los servicios a través de interfaces inyectadas por constructor.

**Tareas:**

1. Crear `MainWindowViewModel` con:
   - Propiedad `CurrentView` (object, el ViewModel activo).
   - Comandos de navegación: `NavigateToZip`, `NavigateToUnzip`, `NavigateToHashBatch`, `NavigateToVerifyReport`, `NavigateToPasswordGenerator`, `NavigateToSettings`, `NavigateToAbout`.
   - Cada comando crea el ViewModel correspondiente y lo asigna a `CurrentView`.

2. Crear `ZipViewModel` con:
   - Lista de archivos/carpetas seleccionados (observable).
   - Propiedades: `OutputPath`, `CompressionLevel`, `Password`, `SelectedHashAlgorithms`, `IsProcessing`, `Progress`, `StatusMessage`.
   - Comandos: `AddFiles`, `AddFolder`, `RemoveSelected`, `ClearAll`, `Compress`, `Cancel`.
   - `Compress` ejecuta `ZipService.CompressAsync` + `ReportService` si hay hashes seleccionados.
   - Drag & drop se implementa en la View (Fase 3), pero el ViewModel expone `AddFilesCommand(IEnumerable<string> paths)`.

3. Crear `UnzipViewModel` con:
   - `ZipFilePath`, `OutputDirectory`, `Password`, `IsProcessing`, `Progress`, `StatusMessage`.
   - Comandos: `BrowseZip`, `BrowseOutput`, `Decompress`, `Cancel`.

4. Crear `HashBatchViewModel` con:
   - Lista de archivos (observable).
   - `SelectedHashAlgorithms`, `IsProcessing`, `Progress`, `StatusMessage`.
   - Comandos: `AddFiles`, `RemoveSelected`, `ClearAll`, `ComputeHashes`, `Cancel`, `ExportReport`.
   - Resultados visibles en una grilla/tabla.

5. Crear `VerifyReportViewModel` con:
   - `ReportFilePath`, `VerificationResult`, `IsValid`, `Details`.
   - Comandos: `BrowseReport`, `Verify`.

6. Crear `PasswordGeneratorViewModel` con:
   - Todas las opciones de `PasswordOptions` como propiedades bindables.
   - `GeneratedPassword`, `EntropyBits`, `EntropyLabel` (Débil/Media/Fuerte/Muy fuerte).
   - Comandos: `Generate`, `CopyToClipboard`.
   - Regenera automáticamente al cambiar cualquier opción.

7. Crear `SettingsViewModel` con:
   - Tabs lógicos: General (idioma, tema, carpeta de salida), Operador (nombre, organismo, cargo, email, teléfono — todos opcionales), Defaults (nivel compresión, algoritmos).
   - Comandos: `Save`, `ResetDefaults`.

8. Crear `AboutViewModel` con:
   - Propiedades de solo lectura: versión, autor, email, licencia, disclaimer.
   - Comando: `OpenHelp` (abrirá HTML externo).
   - Comando: `OpenWebsite` (abrirá URL de Patagonia Robot si existiera).

**Criterio de aceptación:**
- Todos los ViewModels compilan sin error.
- Cada ViewModel recibe sus dependencias por constructor (interfaces).
- Propiedades usan `INotifyPropertyChanged` (o `ObservableObject` base class).
- Comandos implementan `ICommand` (o `RelayCommand`).
- No hay ninguna referencia a Avalonia ni a clases de UI en `ForZip.Core` ni en los ViewModels que estén en Core.

**Nota:** Si Avalonia provee un toolkit MVVM (como `CommunityToolkit.Mvvm` o `ReactiveUI`), se puede usar para `ObservableObject` y `RelayCommand`. Confirmarlo con el director antes de elegir.

🔔 **Punto de validación humana.** Claudio revisa la estructura de ViewModels.

---

### FASE 3 — Interfaz Gráfica (Avalonia)

**Objetivo:** Construir todas las Views en Avalonia vinculadas a los ViewModels. Tema oscuro por defecto. Layout y colores según `04_Mockups_UI.md`.

**Tareas:**

1. **MainWindow:**
   - Sidebar izquierda con botones de navegación (íconos + texto).
   - Área de contenido a la derecha que muestra el `CurrentView`.
   - Barra de título personalizada con logo + nombre "ForZip v1.0.0".
   - DataTemplates para mapear cada ViewModel a su View.

2. **ZipView:**
   - Zona de drop (drag & drop de archivos/carpetas).
   - Lista de archivos agregados con botón de remover individual.
   - Selector de nivel de compresión (ComboBox o slider).
   - Campo de contraseña opcional (con toggle de visibilidad).
   - Checkboxes para algoritmos de hash.
   - Botón "Comprimir" / "Cancelar".
   - Barra de progreso con porcentaje y bytes procesados.
   - Mensaje de estado.

3. **UnzipView:**
   - Selector de archivo ZIP (Browse o drag & drop).
   - Selector de carpeta de salida.
   - Campo de contraseña opcional.
   - Botón "Descomprimir" / "Cancelar".
   - Barra de progreso.

4. **HashBatchView:**
   - Zona de drop para agregar archivos.
   - Checkboxes para algoritmos.
   - Tabla de resultados (archivo / MD5 / SHA-1 / SHA-256 / SHA-512).
   - Botón "Calcular" / "Cancelar" / "Exportar Informe".
   - Barra de progreso.

5. **VerifyReportView:**
   - Selector de archivo de informe (.txt).
   - Botón "Verificar".
   - Resultado visual: ícono ✅ verde / ❌ rojo con detalle.

6. **PasswordGeneratorView:**
   - Sliders/checkboxes para todas las opciones.
   - Campo de contraseña generada (solo lectura, fuente monoespaciada).
   - Indicador visual de entropía (barra de color: rojo → amarillo → verde).
   - Botón "Generar" / "Copiar".

7. **SettingsView:**
   - TabControl con 3 tabs: General, Operador, Defaults.
   - Botón "Guardar" / "Restaurar Defaults".

8. **AboutView:**
   - Logo, nombre, versión, autor, licencia.
   - Disclaimer forense.
   - Botón "Ayuda" (abre Help.html).

9. **Temas:**
   - `DarkTheme.axaml` con paleta: `#1a1a2e` (fondo principal), `#0f172a` (fondo secundario), `#334155` (bordes), `#e2e8f0` (texto principal), `#94a3b8` (texto secundario), `#10B981` (acento/éxito), `#ef4444` (error), `#f59e0b` (advertencia).
   - `LightTheme.axaml` con equivalentes claros.
   - Toggle en Settings con aplicación inmediata.

10. **Drag & Drop:**
    - Implementar en `ZipView`, `HashBatchView` y `UnzipView` (code-behind mínimo que delega al ViewModel).

**Criterio de aceptación:**
- La aplicación arranca y muestra la MainWindow con la sidebar.
- Se puede navegar entre todas las vistas.
- Tema oscuro se ve correctamente con la paleta definida.
- Drag & drop funciona (archivos aparecen en la lista).
- Todas las Views se vinculan a sus ViewModels (binding funcional).
- No hay lógica de negocio en code-behind; solo UI helpers (drag/drop, diálogos de archivo).

🔔 **Punto de validación humana.** Claudio revisa visualmente cada pantalla.

---

### FASE 4 — Integración End-to-End (GUI)

**Objetivo:** Conectar los ViewModels con los servicios reales y validar flujos completos desde la GUI.

**Tareas:**

1. Configurar inyección de dependencias en `App.axaml.cs`:
   - Registrar todos los servicios (interfaces → implementaciones).
   - Crear `MainWindowViewModel` con sus dependencias resueltas.

2. **Flujo de compresión E2E:**
   - Agregar archivos → seleccionar nivel → (opcionalmente) contraseña y hashes → Comprimir.
   - Barra de progreso se actualiza en tiempo real.
   - Al terminar, se genera el informe forense automáticamente.
   - Mensaje de éxito con ruta del ZIP y del informe.

3. **Flujo de descompresión E2E:**
   - Seleccionar ZIP → carpeta destino → (password si aplica) → Descomprimir.
   - Progreso real. Mensaje de éxito o error claro.

4. **Flujo de hash batch E2E:**
   - Agregar archivos → seleccionar algoritmos → Calcular.
   - Tabla se llena progresivamente.
   - Exportar informe genera .txt forense.

5. **Flujo de verificación E2E:**
   - Cargar informe → Verificar → resultado visual.

6. **Flujo de password generator E2E:**
   - Cambiar opciones → regenera automáticamente.
   - Copiar al clipboard funciona.

7. **Settings E2E:**
   - Cambiar idioma → toda la UI cambia.
   - Cambiar tema → aplica inmediatamente.
   - Datos del operador se guardan y aparecen pre-cargados en el informe.
   - Confirmar datos del operador antes de generar cada informe (diálogo de confirmación).

8. **Manejo de errores global:**
   - Errores de servicio → mensaje amigable en la UI (no excepciones crudas).
   - Archivos bloqueados, permisos denegados, disco lleno → mensajes claros.

**Criterio de aceptación:**
- Todos los flujos funcionan de principio a fin sin errores.
- Cancelación funciona correctamente (limpia archivos parciales).
- Cambio de idioma refleja en toda la UI.
- Informe forense generado cumple con el formato de `03_Formato_Informe_Forense.md`.
- Datos del operador se persisten y se confirman antes de cada informe.

🔔 **Punto de validación humana.** Claudio prueba cada flujo manualmente.

---

### FASE 5 — Modo CLI

**Objetivo:** Implementar `ForZip.Cli` con todas las funciones accesibles por línea de comandos. Mismo motor core que la GUI.

**Interfaz CLI:**

```
ForZip.exe zip -i <archivos/carpetas> -o <output.zip> [-l <level>] [-p <password>] [--hash md5,sha256,...] [--report <report.txt>]
ForZip.exe unzip -i <input.zip> -o <outputDir> [-p <password>]
ForZip.exe hash -i <archivos> [--algo md5,sha1,sha256,sha512] [--report <report.txt>]
ForZip.exe verify -r <report.txt>
ForZip.exe genpass [-n <length>] [--upper] [--lower] [--digits] [--symbols] [--no-ambiguous]
ForZip.exe --help
ForZip.exe --version
```

**Tareas:**

1. Implementar `CliParser` que parsee argumentos manualmente (sin librerías externas).
2. Cada comando llama a los mismos servicios core que la GUI.
3. Progreso en CLI: línea de progreso actualizable (`\r` con porcentaje).
4. Salida de hashes: formato tabulado legible en consola.
5. Códigos de salida: 0 = éxito, 1 = error de argumento, 2 = error de operación, 3 = verificación fallida.
6. `--help` muestra ayuda detallada bilingüe (detecta idioma del SO o flag `--lang es|en`).

**Criterio de aceptación:**
- Todos los comandos funcionan correctamente.
- `ForZip.exe zip -i testfile.txt -o out.zip --hash sha256 --report report.txt` genera ZIP + informe.
- `ForZip.exe verify -r report.txt` devuelve exit code 0 si válido, 3 si no.
- `ForZip.exe --help` muestra ayuda clara.
- Mismos resultados que la GUI para las mismas operaciones.

🔔 **Punto de validación humana.** Claudio prueba los comandos desde PowerShell.

---

### FASE 6 — Pulido, Íconos y Empaquetado

**Objetivo:** Preparar la aplicación para distribución.

**Tareas:**

1. **Ícono temporal:** crear ícono monocromático `#10B981` con motivo de caja+lupa. Formato `.ico` multi-resolución (16/32/48/256). El ícono definitivo lo diseña Claudio después.

2. **Logo temporal:** versión PNG 256x256 del ícono para `AboutView`.

3. **Logging:**
   - Crear sistema de log propio (no Serilog).
   - Archivo rotativo en `Logs/` al lado del exe.
   - Rotación: un archivo por día, eliminar archivos > 30 días.
   - Niveles: Info, Warning, Error.
   - Formato: `[2026-05-09 14:30:00] [INFO] Mensaje`

4. **Help.html:**
   - Archivo HTML bilingüe (ES + EN en secciones separadas o toggle).
   - Secciones: Inicio rápido, Compresión, Descompresión, Hash Batch, Verificación de Informe, Generador de Contraseñas, Configuración, CLI, Acerca de.
   - Se abre desde el botón "Ayuda" en AboutView (abrir con el navegador del sistema).

5. **Publicación:**
   ```
   dotnet publish ForZip.GUI/ForZip.GUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Publish
   ```
   - Verificar que el binario es un single exe funcional.
   - Verificar tamaño (~70-90 MB estimado).

6. **Limpieza final:**
   - Remover cualquier `Console.WriteLine` de debug.
   - Verificar que no hay warnings de compilación.
   - Verificar que todos los tests pasan.

**Criterio de aceptación:**
- `ForZip.exe` arranca correctamente desde la carpeta `Publish/`.
- Ícono se muestra en el exe y en la barra de título.
- Logs se crean correctamente en `Logs/`.
- Help.html se abre en el navegador.
- Tamaño del exe está en rango esperado.

🔔 **Punto de validación humana.** Claudio prueba el exe publicado en una carpeta limpia.

---

### FASE 7 — Documentación de Usuario

**Objetivo:** Crear documentación final para el usuario.

**Tareas:**

1. Actualizar `README.md` con:
   - Screenshots de la aplicación.
   - Instrucciones de uso GUI y CLI.
   - Tabla de hashes de los binarios publicados.

2. Crear `CHANGELOG.md` con las notas de la v1.0.0.

3. Revisar que el disclaimer aparece en:
   - `README.md` ✓
   - `AboutView` ✓
   - Informe forense (pie) ✓
   - `Help.html` ✓

4. Revisar que el encabezado Apache 2.0 está en TODOS los `.cs`.

**Criterio de aceptación:**
- Toda la documentación está completa y consistente.
- El disclaimer está en los 4 lugares definidos.
- Todos los `.cs` tienen encabezado Apache 2.0.

🔔 **Punto de validación humana.** Claudio hace revisión final completa.

---

## Comando de Publicación

```bash
dotnet publish ForZip.GUI/ForZip.GUI.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Publish
```

---

## Checklist Final de Cierre v1.0.0

- [ ] `dotnet build ForZip.sln` → 0 errores, 0 warnings
- [ ] `dotnet test` → todos los tests pasan
- [ ] Todos los flujos GUI funcionan E2E
- [ ] Todos los comandos CLI funcionan
- [ ] Informe forense cumple formato especificado
- [ ] Hash auto-firmante del informe se verifica correctamente
- [ ] AES-256 funciona correctamente (comprimir + descomprimir)
- [ ] Cambio de idioma ES↔EN funciona en GUI y CLI
- [ ] Cambio de tema oscuro↔claro funciona
- [ ] Datos del operador se persisten y se confirman antes de cada informe
- [ ] Drag & drop funciona en las 3 vistas aplicables
- [ ] Cancelación cooperativa funciona en todas las operaciones
- [ ] Logs se crean y rotan correctamente
- [ ] Help.html se abre correctamente
- [ ] Ícono se muestra en exe y barra de título
- [ ] Binario publicado funciona desde carpeta limpia
- [ ] Tamaño del exe: 70-90 MB
- [ ] Encabezado Apache 2.0 en todos los `.cs`
- [ ] Disclaimer en README, AboutView, informe forense y Help.html
- [ ] `LICENSE`, `NOTICE`, `README.md`, `CONTRIBUTING.md` en la raíz
- [ ] Sin código muerto ni TODOs huérfanos

---

**Fin del Roadmap de Implementación.**
