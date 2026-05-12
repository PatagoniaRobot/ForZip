# Nota de Transferencia — Estado del Proyecto ForZip

**Fecha de generación:** 9 de mayo de 2026  
**Para:** Claude (cualquier instancia) en futuras sesiones con Claudio Andino  
**De:** Claude (instancia del 9 de mayo de 2026, sesión de documentación + ejecución)

---

## Resumen Ejecutivo

ForZip es un utilitario forense de compresión open source (Apache 2.0) bajo la marca Patagonia Robot. Es complementario a PRImager, no parte de él. Sirve como demo de buena fe y material didáctico para los cursos de Claudio. En una sola sesión se generó toda la documentación fundacional y un agente externo ejecutó las Fases 0 a 4 del roadmap. El proyecto compila, tiene 24 tests pasando, y tiene GUI funcional. Faltan las Fases 5 (CLI), 6 (pulido) y 7 (documentación de usuario).

---

## Lo que se hizo en esta sesión

### Documentación generada por Claude (5 archivos)

Todos en `D:\PROYECTOS\ForZip\Documentacion\`:

1. **01_Especificacion_Funcional.docx** — Documento Word A4, 11 secciones: visión, 44 requerimientos funcionales, 10 no funcionales, arquitectura (4 proyectos, MVVM, DI), 6 módulos detallados (HashService, ZipService, PasswordService, ReportService, ConfigService, LocalizationService), manejo de errores, seguridad, 5 casos de uso, UI, CLI, glosario. Headings azul `#1E3A8A`, código en Consolas.

2. **02_Roadmap_Implementacion.md** — Plan maestro para agentes ejecutores. 9 reglas inviolables, stack técnico con dependencias prohibidas, estructura de carpetas completa, 8 fases (0-7) con tareas detalladas, criterios de aceptación verificables, puntos de validación humana (🔔), interfaz CLI, comando de publicación, checklist final de cierre.

3. **03_Formato_Informe_Forense.md** — Formato exacto del informe .txt forense. Ejemplos completos en ES y EN, variantes para Hash Batch y Verificación, 10 reglas de implementación para ReportService (una tabla por algoritmo, formato de tamaño según locale, línea auto-firmante sin CRLF final, etiquetas bilingües de niveles de compresión), algoritmo de verificación con casos de error.

4. **04_Mockups_UI.md** — Mockups ASCII de 9 pantallas: MainWindow con sidebar 220px, ZipView, UnzipView, HashBatchView, VerifyReportView, PasswordGeneratorView, SettingsView (3 tabs), AboutView, diálogo modal de confirmación de datos del operador. Paletas oscura y clara completas, tipografía, dimensiones (1100x700 inicial, 900x600 mínimo), comportamiento de drag & drop por vista, responsive.

5. **05_Estandares_Codigo.md** — Encabezado Apache 2.0 exacto, naming (PascalCase/camelCase/_camelCase), file-scoped namespaces, constructor injection, async/await con CancellationToken, manejo de errores, testing con xUnit (patrón Método_Escenario_Resultado, TestPasswords.cs), .editorconfig completo, orden de miembros en clase, comentarios en español, logging, checklist por archivo.

### Prompts generados por Claude (2 archivos)

- **Prompt_Fase0_Bootstrap.md** — Prompt ejecutado por el agente. Creación de solución, 4 proyectos, NuGets, estructura de carpetas, 6 interfaces vacías, JSON de recursos, .editorconfig, .gitignore. Resultado: compilación 0 errores 0 warnings.

- **Prompt_Fase1_Servicios_Core.md** — Prompt para los 6 servicios core con tests. Incluye interfaces completas, modelos, reglas de implementación, JSONs bilingües (~80 claves cada uno), tests específicos con nombres y asserts. El agente lo ejecutó y además avanzó Fases 2-4 por iniciativa de Claudio.

### Ejecución por agente externo (Fases 0-4)

Un agente (no Claude) ejecutó las fases. Resultado reportado por Claudio:

- **Fase 0 (Bootstrap):** Estructura creada, compila limpio.
- **Fase 1 (Servicios Core):** 6 servicios implementados, 24 tests pasando.
- **Fase 2 (ViewModels):** 8 ViewModels con CommunityToolkit.Mvvm (decisión tomada por el agente, validada por Claudio).
- **Fase 3 (UI Avalonia):** 8 vistas con temas oscuro/claro, drag & drop, sidebar.
- **Fase 4 (Integración):** DI con Microsoft.Extensions.DependencyInjection, manejo global de excepciones con crash.log, script build_release.bat.

**Extras agregados por pedido de Claudio** (basados en experiencia con PR-Zipper):
- Zip64 en modo Dynamic (soporte >4GB)
- Compresión multivolumen (split zip: .z01, .z02, .zip)
- Preservación de metadatos de fecha

---

## Estado actual del código

### Estructura confirmada

```
D:\PROYECTOS\ForZip\
├── ForZip.sln
├── LICENSE, NOTICE, README.md, CONTRIBUTING.md
├── .editorconfig, .gitignore
├── build_release.bat
├── Documentacion\
│   ├── 01_Especificacion_Funcional.docx
│   ├── 02_Roadmap_Implementacion.md
│   ├── 03_Formato_Informe_Forense.md
│   ├── 04_Mockups_UI.md
│   ├── 05_Estandares_Codigo.md
│   └── 06_Handover_Transferencia_Tecnica.md  (del agente)
├── src\
│   ├── ForZip.Core\
│   │   ├── Interfaces\ (6 interfaces)
│   │   ├── Models\ (7 modelos, incluye FileReportItem.cs extra)
│   │   ├── Services\ (6 servicios)
│   │   └── Resources\ (Strings_es.json, Strings_en.json)
│   ├── ForZip.GUI\
│   │   ├── ViewModels\ (8 + ViewModelBase.cs)
│   │   ├── Views\ (8 vistas .axaml/.cs)
│   │   ├── Styles\ (DarkTheme.axaml, LightTheme.axaml)
│   │   ├── ViewLocator.cs
│   │   ├── App.axaml/.cs (con DI)
│   │   └── Program.cs (con crash handler)
│   └── ForZip.Cli\ (solo Program.cs por defecto — NO implementado)
├── tests\
│   └── ForZip.Tests\ (24 tests, todos pasando)
└── Publish\ (destino de binarios)
```

### Tests: 24 pasando, 0 fallando, 59ms

### Compilación: 0 errores, 0 warnings

---

## Lo que FALTA hacer

### Fase 5 — CLI (NO implementado)

`ForZip.Cli/Program.cs` está vacío (placeholder del template). Hay que implementar:
- CliParser (parsing manual, sin librerías externas)
- Comandos: zip, unzip, hash, verify, genpass
- Códigos de salida: 0=éxito, 1=arg, 2=error, 3=verify fail
- Progreso en consola con `\r`
- Ayuda bilingüe con --help y --version
- Detalle completo en `02_Roadmap_Implementacion.md` Fase 5

### Fase 6 — Pulido y Empaquetado

- Ícono temporal monocromático #10B981 (caja+lupa), .ico multi-resolución
- Logo PNG 256x256 para AboutView
- Sistema de logging propio con rotación 30 días en Logs/
- Help.html bilingüe (9 secciones)
- Verificar publicación single-file con build_release.bat
- Limpieza de Console.WriteLine, verificar 0 warnings

### Fase 7 — Documentación de Usuario

- Actualizar README.md con screenshots y uso
- Crear CHANGELOG.md v0.9.0 Beta
- Verificar disclaimer en los 4 lugares (README, AboutView, informe, Help.html)
- Verificar encabezado Apache 2.0 en todos los .cs

### Mejoras sugeridas por el agente (roadmap futuro)

- Descompresión automática de multivolumen (detectar .z01 automáticamente)
- Firma digital del binario (certificado de desarrollador, SmartScreen)
- Detección de write-blocker en medio de origen

---

## Lo que hay que hacer AL INICIO de la próxima sesión

**Test integral E2E.** Claudio pidió explícitamente arrancar la próxima sesión con un test integral. Esto implica:

1. Compilar con `build_release.bat` (o `dotnet publish` manual)
2. Abrir el exe desde la carpeta Publish/ (carpeta limpia, sin entorno de desarrollo)
3. Probar cada flujo:
   - **Comprimir:** agregar archivos (drag & drop + browse), seleccionar nivel, poner contraseña, seleccionar hashes, comprimir → verificar ZIP + informe generado
   - **Extraer:** abrir el ZIP recién creado, extraer con contraseña, verificar contenido
   - **Hash Batch:** agregar archivos, calcular, exportar informe
   - **Verificar Informe:** cargar informe generado → debe dar OK. Modificar un byte → debe dar FAIL
   - **Generador de Contraseñas:** cambiar opciones, verificar regeneración, copiar
   - **Settings:** cambiar idioma (ES↔EN), cambiar tema (oscuro↔claro), guardar datos de operador, verificar persistencia
   - **About:** verificar datos, abrir Help (si existe)
4. Verificar split zip si está funcional
5. Verificar Zip64 con archivo grande si es posible
6. Reportar bugs encontrados para corregir antes de seguir con Fases 5-7

---

## Contexto técnico clave

| Aspecto | Valor |
|---|---|
| Ruta | `D:\PROYECTOS\ForZip\` |
| Framework | .NET 8.0 LTS |
| UI | Avalonia UI 11.x |
| MVVM Toolkit | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| ZIP | SharpZipLib (customizado para Split + Zip64) |
| Tests | xUnit, 24 tests |
| Licencia | Apache 2.0 |
| Versión actual | v0.9.5 Beta (según handover del agente) |
| Paleta | #1a1a2e / #0f172a / #334155 / #e2e8f0 / #10B981 / #ef4444 |
| Binario | Single-file self-contained win-x64 |
| Idiomas | ES + EN, autodetect del SO |

---

## Decisiones de diseño cerradas (NO cambiar sin permiso de Claudio)

Todo lo listado en la nota de transferencia original sigue vigente. Adicionalmente:
- CommunityToolkit.Mvvm fue aprobado por Claudio como toolkit MVVM
- Microsoft.Extensions.DependencyInjection fue aprobado para DI
- Zip64 y split zip son features confirmadas (pedidas por Claudio basándose en PR-Zipper)
- FileReportItem.cs es un modelo extra que el agente agregó (verificar si se usa o es redundante)

---

**Fin de la nota de transferencia.**  
Próxima sesión: test integral E2E, luego Fases 5-7.
