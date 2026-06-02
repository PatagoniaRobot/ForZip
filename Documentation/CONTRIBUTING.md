<!--
  ForZip — Contributor & Pull Request Guide
  Guía de Contribución y Pull Requests
  Bilingual document — English first, Spanish below.
  Documento bilingüe — Inglés primero, Español debajo.
-->

# ForZip — Contributor & Pull Request Guide / Guía de Contribución

> **Language / Idioma:** [English](#english) · [Español](#español)

ForZip is an open-source, portable, auditable forensic ZIP utility. Its priority is
**reproducible, verifiable, court-defensible packaging of digital evidence** — not
compression speed. Please keep that goal in mind when contributing.

ForZip es una utilidad forense de empaquetado ZIP, de código abierto, portable y
auditable. Su prioridad es el **empaquetado reproducible, verificable y defendible en
tribunales de evidencia digital** — no la velocidad de compresión. Tené presente ese
objetivo al contribuir.

---

## English

### 1. Project layout

```
src/
  ForZip.Core/    Pure .NET 8 library — all business logic, no UI. Unit-testable.
    Interfaces/   Service contracts (IHashService, IZipService, IReportService,
                  IVerificationService, ISignatureService, ILogService, ...).
    Models/       DTOs (ZipOptions, ReportData, ForensicManifest, HashResult, ...).
    Services/     Implementations (Hash, Zip, Report, Verification, Signature, Log,
                  Config, Localization).
    Resources/    Strings_en.json / Strings_es.json (UI + report localization).
  ForZip.Cli/     Command-line front end (zip, unzip, hash, verify, sign, genpass).
  ForZip.GUI/     Avalonia UI front end (MVVM with CommunityToolkit.Mvvm).
tests/
  ForZip.Tests/   xUnit tests for Core and CLI.
```

The two front ends are thin: **all real logic lives in `ForZip.Core`** and must be
covered by tests there.

### 2. Forensic integrity model (read before touching verification)

- Compression hashes each file in a **single pass** (one disk read) and records the
  hashes, source path and UTC modification time.
- Each operation produces:
  - a human-readable report `*.report.txt` (UTF-8 with BOM),
  - a machine-readable manifest `*.manifest.json` — the **source of truth** for
    automated verification,
  - an external `*.sha256` sidecar for the report.
- The manifest may be **digitally signed** (detached CMS/PKCS#7, `*.p7s`) with the
  operator's X.509 certificate.
- `verify` re-hashes the ZIP contents against the manifest and returns a per-file
  verdict (OK / altered / missing / extra), plus the global ZIP SHA-256 and the
  signature status.

Do not reintroduce the old "self-signing line inside the report" model; it was removed
in favour of the external sidecar + manifest + optional CMS signature.

### 3. Development setup

1. Install the **.NET 8 SDK** (https://dotnet.microsoft.com/download).
2. Clone the repository.
3. `dotnet restore`
4. `dotnet build`
5. `dotnet test` — **all tests must pass before you start changing anything.**

To produce local single-file binaries for manual testing: run `publish.bat`
(outputs to `Publish/GUI` and `Publish/CLI`; this folder is git-ignored).

### 4. Coding standards

- **Comments in Spanish, identifiers in English.** (Matches the existing codebase.)
- Every **new source file** starts with the standard Apache 2.0 header block (copy it
  from any existing file).
- Any new or changed **UI / report string** must be added to **both**
  `Strings_en.json` and `Strings_es.json`.
- No hardcoded paths, **no telemetry, no network calls**, no registry usage. ForZip is
  fully portable.
- Prefer adding logic to `ForZip.Core` (testable) over the GUI/CLI layers.
- Keep dependencies minimal; new third-party packages need justification.
- Follow the existing async/streaming style for I/O (buffered, cancellable).

### 5. Pull request checklist

- [ ] `dotnet build` is clean (0 errors, 0 warnings).
- [ ] `dotnet test` passes; new behaviour is covered by tests.
- [ ] New files carry the Apache 2.0 header.
- [ ] Comments in Spanish, identifiers in English.
- [ ] New UI/report strings added to both `Strings_en.json` and `Strings_es.json`.
- [ ] No telemetry, no external services, no hardcoded paths.
- [ ] `CHANGELOG.md` updated under the **`[Unreleased]`** section.
- [ ] Documentation updated if behaviour changed (README files, this guide, in-app Help).

### 6. Branches & commits

- Branch names: `feature/<short-desc>`, `fix/<short-desc>`, `docs/<short-desc>`.
- Write clear, imperative commit messages explaining the *why*.
- Open the PR against `main`; describe the change, the motivation, and how you tested it.
- Never commit real evidence, secrets, certificates, or `Publish/` / `Logs/` artifacts.

### 7. Reporting issues

Include: ForZip version, OS and version, exact steps to reproduce, expected vs actual
behaviour, and **sample input that is never real evidence**.

### 8. Licensing

By submitting a contribution you agree it is licensed under the **Apache License 2.0**.

**Questions:** claudio@patagoniarobot.com

---

## Español

### 1. Estructura del proyecto

```
src/
  ForZip.Core/    Librería .NET 8 pura — toda la lógica de negocio, sin UI. Testeable.
    Interfaces/   Contratos de servicios (IHashService, IZipService, IReportService,
                  IVerificationService, ISignatureService, ILogService, ...).
    Models/       DTOs (ZipOptions, ReportData, ForensicManifest, HashResult, ...).
    Services/     Implementaciones (Hash, Zip, Report, Verification, Signature, Log,
                  Config, Localization).
    Resources/    Strings_en.json / Strings_es.json (localización de UI e informes).
  ForZip.Cli/     Front end de línea de comandos (zip, unzip, hash, verify, sign, genpass).
  ForZip.GUI/     Front end Avalonia UI (MVVM con CommunityToolkit.Mvvm).
tests/
  ForZip.Tests/   Tests xUnit de Core y CLI.
```

Las dos interfaces son delgadas: **toda la lógica real vive en `ForZip.Core`** y debe
estar cubierta por tests ahí.

### 2. Modelo de integridad forense (leé esto antes de tocar la verificación)

- La compresión hashea cada archivo en un **solo pase** (una sola lectura de disco) y
  registra los hashes, la ruta de origen y la marca temporal de modificación en UTC.
- Cada operación produce:
  - un informe legible `*.report.txt` (UTF-8 con BOM),
  - un manifiesto legible por máquina `*.manifest.json` — la **fuente de verdad** para
    la verificación automática,
  - un archivo externo `*.sha256` de integridad del informe.
- El manifiesto puede **firmarse digitalmente** (CMS/PKCS#7 desacoplada, `*.p7s`) con el
  certificado X.509 del operador.
- `verify` re-hashea el contenido del ZIP contra el manifiesto y devuelve un veredicto
  archivo por archivo (OK / alterado / faltante / añadido), además del SHA-256 global
  del ZIP y el estado de la firma.

No reintroduzcas el viejo modelo de "línea auto-firmante dentro del informe"; fue
eliminado en favor del sidecar externo + manifiesto + firma CMS opcional.

### 3. Preparación del entorno

1. Instalá el **SDK de .NET 8** (https://dotnet.microsoft.com/download).
2. Cloná el repositorio.
3. `dotnet restore`
4. `dotnet build`
5. `dotnet test` — **todos los tests deben pasar antes de empezar a cambiar algo.**

Para generar binarios locales de archivo único para pruebas manuales: ejecutá
`publish.bat` (genera `Publish/GUI` y `Publish/CLI`; esa carpeta está ignorada por git).

### 4. Estándares de código

- **Comentarios en español, identificadores en inglés.** (Igual que el código existente.)
- Todo **archivo nuevo** comienza con el bloque de cabecera estándar Apache 2.0
  (copialo de cualquier archivo existente).
- Toda **cadena de UI / informe** nueva o modificada debe agregarse a **ambos**
  `Strings_en.json` y `Strings_es.json`.
- Sin rutas hardcodeadas, **sin telemetría, sin llamadas de red**, sin uso del registro.
  ForZip es totalmente portable.
- Preferí agregar lógica en `ForZip.Core` (testeable) antes que en las capas GUI/CLI.
- Mantené las dependencias al mínimo; todo paquete de terceros nuevo necesita justificación.
- Seguí el estilo async/streaming existente para I/O (buffer, cancelable).

### 5. Checklist del Pull Request

- [ ] `dotnet build` limpio (0 errores, 0 advertencias).
- [ ] `dotnet test` pasa; el nuevo comportamiento está cubierto por tests.
- [ ] Los archivos nuevos llevan la cabecera Apache 2.0.
- [ ] Comentarios en español, identificadores en inglés.
- [ ] Cadenas de UI/informe nuevas agregadas a `Strings_en.json` y `Strings_es.json`.
- [ ] Sin telemetría, sin servicios externos, sin rutas hardcodeadas.
- [ ] `CHANGELOG.md` actualizado bajo la sección **`[Unreleased]`**.
- [ ] Documentación actualizada si cambió el comportamiento (READMEs, esta guía, Ayuda in-app).

### 6. Ramas y commits

- Nombres de rama: `feature/<desc-corta>`, `fix/<desc-corta>`, `docs/<desc-corta>`.
- Escribí mensajes de commit claros e imperativos, explicando el *por qué*.
- Abrí el PR contra `main`; describí el cambio, la motivación y cómo lo probaste.
- Nunca subas evidencia real, secretos, certificados, ni artefactos de `Publish/` / `Logs/`.

### 7. Reporte de problemas

Incluí: versión de ForZip, SO y versión, pasos exactos para reproducir, comportamiento
esperado vs. real, y **datos de ejemplo que nunca sean evidencia real**.

### 8. Licencia

Al enviar una contribución aceptás que queda licenciada bajo la **Apache License 2.0**.

**Consultas:** claudio@patagoniarobot.com
