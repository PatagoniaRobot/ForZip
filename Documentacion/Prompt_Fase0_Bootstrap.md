# PROMPT PARA AGENTE EJECUTOR — ForZip Fase 0: Bootstrap del Proyecto

## Contexto

Vas a crear el proyecto **ForZip** desde cero. ForZip es una herramienta forense open source de compresión ZIP con hashing, cifrado AES-256 y verificación de integridad. C# / .NET 8 / Avalonia UI / SharpZipLib / xUnit. Licencia Apache 2.0.

La documentación completa del proyecto está en `D:\PROYECTOS\ForZip\Documentacion\`. Consultá esos archivos cuando necesites detalle:

- `01_Especificacion_Funcional.docx` — Qué hace ForZip (requerimientos, arquitectura, módulos)
- `02_Roadmap_Implementacion.md` — Plan de ejecución por fases (ESTÁS EN LA FASE 0)
- `03_Formato_Informe_Forense.md` — Formato del informe .txt
- `04_Mockups_UI.md` — Diseño de las pantallas
- `05_Estandares_Codigo.md` — Convenciones de código, encabezado Apache 2.0, .editorconfig

## Tu tarea: ejecutar la FASE 0 completa

Trabajás en `D:\PROYECTOS\ForZip\`. Creá toda la estructura del proyecto desde cero. Al terminar, `dotnet build ForZip.sln` debe compilar con 0 errores y 0 warnings, y `dotnet test` debe ejecutar sin error (0 tests por ahora).

## Pasos exactos

### 1. Crear la solución y los 4 proyectos

```
cd D:\PROYECTOS\ForZip
dotnet new sln -n ForZip
mkdir src\ForZip.Core
mkdir src\ForZip.GUI
mkdir src\ForZip.Cli
mkdir tests\ForZip.Tests
dotnet new classlib -n ForZip.Core -o src/ForZip.Core -f net8.0
dotnet new avalonia.app -n ForZip.GUI -o src/ForZip.GUI -f net8.0
dotnet new console -n ForZip.Cli -o src/ForZip.Cli -f net8.0
dotnet new xunit -n ForZip.Tests -o tests/ForZip.Tests -f net8.0
dotnet sln add src/ForZip.Core/ForZip.Core.csproj
dotnet sln add src/ForZip.GUI/ForZip.GUI.csproj
dotnet sln add src/ForZip.Cli/ForZip.Cli.csproj
dotnet sln add tests/ForZip.Tests/ForZip.Tests.csproj
```

Si el template `avalonia.app` no está instalado, instalalo primero:
```
dotnet new install Avalonia.Templates
```

### 2. Configurar referencias entre proyectos

```
dotnet add src/ForZip.GUI/ForZip.GUI.csproj reference src/ForZip.Core/ForZip.Core.csproj
dotnet add src/ForZip.Cli/ForZip.Cli.csproj reference src/ForZip.Core/ForZip.Core.csproj
dotnet add tests/ForZip.Tests/ForZip.Tests.csproj reference src/ForZip.Core/ForZip.Core.csproj
```

### 3. Instalar NuGets

```
dotnet add src/ForZip.Core/ForZip.Core.csproj package SharpZipLib
dotnet add tests/ForZip.Tests/ForZip.Tests.csproj package NSubstitute
```

Avalonia ya viene con sus paquetes desde el template. Verificá que `ForZip.GUI.csproj` tenga `Avalonia`, `Avalonia.Desktop` y `Avalonia.Themes.Fluent`. Si no, agregalos:
```
dotnet add src/ForZip.GUI/ForZip.GUI.csproj package Avalonia
dotnet add src/ForZip.GUI/ForZip.GUI.csproj package Avalonia.Desktop
dotnet add src/ForZip.GUI/ForZip.GUI.csproj package Avalonia.Themes.Fluent
```

### 4. Crear la estructura de carpetas vacía

Dentro de `src/ForZip.Core/`:
```
mkdir Services
mkdir Models
mkdir Interfaces
mkdir Resources
```

Dentro de `src/ForZip.GUI/`:
```
mkdir ViewModels
mkdir Views
mkdir Styles
mkdir Assets
mkdir Converters
```

Dentro de `tests/ForZip.Tests/`:
```
mkdir Services
mkdir TestData
```

En la raíz del proyecto:
```
mkdir Logs
```

Eliminá los archivos placeholder que crean los templates (`Class1.cs` en Core, `UnitTest1.cs` en Tests, etc.).

### 5. Crear las interfaces vacías en ForZip.Core/Interfaces/

Creá estos 6 archivos. Cada uno con el encabezado Apache 2.0 completo (ver abajo), el namespace `ForZip.Core.Interfaces;` (file-scoped) y la interfaz vacía por ahora. Los métodos se agregan en Fase 1.

- `IHashService.cs` → `public interface IHashService { }`
- `IZipService.cs` → `public interface IZipService { }`
- `IPasswordService.cs` → `public interface IPasswordService { }`
- `IReportService.cs` → `public interface IReportService { }`
- `IConfigService.cs` → `public interface IConfigService { }`
- `ILocalizationService.cs` → `public interface ILocalizationService { }`

### 6. Crear archivos de recursos vacíos

`src/ForZip.Core/Resources/Strings_es.json`:
```json
{
}
```

`src/ForZip.Core/Resources/Strings_en.json`:
```json
{
}
```

Asegurate de que estén marcados como Embedded Resource en el `.csproj` de Core:
```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\Strings_es.json" />
  <EmbeddedResource Include="Resources\Strings_en.json" />
</ItemGroup>
```

### 7. Crear .editorconfig

En la raíz `D:\PROYECTOS\ForZip\.editorconfig`. El contenido exacto está en `05_Estandares_Codigo.md`, sección "Formato de Código / .editorconfig". Copialo textual.

### 8. Crear .gitignore

Creá `D:\PROYECTOS\ForZip\.gitignore` con el contenido estándar para .NET:

```
bin/
obj/
.vs/
*.user
*.suo
*.cache
Publish/
Logs/
config.json
*.DotSettings.user
```

### 9. Verificar compilación

```
dotnet build ForZip.sln
dotnet test
```

Ambos deben pasar con 0 errores y 0 warnings. Si hay warnings, resolvelos antes de terminar.

## Encabezado Apache 2.0 obligatorio para TODO archivo .cs

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

## Reglas inviolables

1. Comentarios en español, identificadores en inglés.
2. File-scoped namespaces siempre (`namespace ForZip.Core.Interfaces;`).
3. No dejes archivos placeholder de los templates (Class1.cs, UnitTest1.cs, etc.).
4. No agregues código que no se pidió. Solo estructura, interfaces vacías y configuración.
5. No inventes dependencias adicionales.

## Resultado esperado

Cuando termines, reportame:
- Output de `dotnet build ForZip.sln` (debe ser 0 errores, 0 warnings)
- Output de `dotnet test` (debe ejecutar sin error, 0 tests)
- Listado del árbol de carpetas final
