# ForZip — Formato del Informe Forense

**Versión:** 1.0  
**Fecha:** 9 de mayo de 2026  
**Autor:** Claudio Andino (claudio@patagoniarobot.com)  
**Proyecto:** ForZip v1.0.0  

> Este documento define el formato exacto del informe forense `.txt` que genera ForZip.  
> Es la referencia obligatoria para `ReportService`. Cualquier desviación es un bug.

> **⚠️ ACTUALIZACIÓN (post-1.0.0) — modelo de integridad revisado**
>
> La **línea auto-firmante** descrita más abajo (un hash SHA-256 embebido como última
> línea del propio informe) fue **eliminada**. El modelo de integridad actual es:
>
> - **Sidecar `.sha256`**: archivo externo con el hash SHA-256 del informe. `VerifyReport`
>   ahora contrasta el informe contra este sidecar (no contra una línea interna).
> - **Manifiesto `.manifest.json`**: documento legible por máquina (operador, caso,
>   parámetros, y por cada archivo: ruta de origen, tamaño, marca temporal UTC y hashes).
>   Es la fuente de verdad para la **verificación de evidencia** (re-hash del contenido
>   del ZIP, veredicto archivo por archivo).
> - **Firma digital opcional** del manifiesto: CMS/PKCS#7 desacoplada (`.p7s`) con el
>   certificado X.509 del operador.
>
> Las secciones sobre "línea auto-firmante" y su algoritmo de verificación quedan como
> referencia histórica del diseño 1.0.0; no reflejan el comportamiento actual.

---

## Especificaciones Técnicas del Archivo

| Propiedad | Valor |
|---|---|
| Formato | Texto plano `.txt` |
| Encoding | UTF-8 con BOM (3 bytes: `EF BB BF`) |
| Line endings | CRLF (`\r\n`) |
| Ancho máximo recomendado | 100 caracteres por línea |
| Nombre por defecto | `ForZip_Report_YYYYMMDD_HHmmss.txt` |

---

## Estructura General

El informe tiene 8 secciones fijas en este orden:

1. Encabezado con separador
2. Datos del operador y organismo
3. Datos del caso (opcionales)
4. Información del entorno
5. Parámetros de la operación
6. Resultados — Listado de archivos con hashes
7. Resultado global (hash del ZIP si aplica)
8. Disclaimer + línea auto-firmante

Cada sección se separa con una línea en blanco. Los separadores usan `=` (80 caracteres).

---

## Reglas de Formato

- Los campos opcionales que estén vacíos NO se imprimen. No dejar líneas como `Caso: (vacío)`.
- Los hashes se imprimen siempre en **lowercase hexadecimal**.
- Las tabulaciones en la tabla de hashes usan espacios (no `\t`). Alineación por columnas con padding.
- La fecha/hora usa formato ISO 8601 con timezone: `2026-05-09T14:30:00-03:00`.
- La línea auto-firmante es SIEMPRE la última línea del archivo, sin CRLF final después de ella.
- El hash auto-firmante se calcula sobre TODO el contenido del archivo EXCLUYENDO la última línea (incluyendo el CRLF que la precede). Se hashea el texto como bytes UTF-8 (con BOM incluido).

---

## Formato Exacto — Ejemplo en Español

```
================================================================================
  INFORME FORENSE DE INTEGRIDAD — ForZip v1.0.0
  Generado por ForZip — Herramienta forense de compresión y verificación
  https://github.com/patagoniarobot/forzip
================================================================================

DATOS DEL OPERADOR
  Nombre       : Juan Carlos Pérez
  Cargo        : Perito Informático
  Organismo    : División de Análisis Forense Informático (D.A.F.I.)
  Email        : jcperez@ejemplo.com
  Teléfono     : +54 299 555-1234

DATOS DEL CASO
  Caso Nro.    : IPP-2026-00542
  Carátula     : Fraude informático — Causa Nro. 12345/2026
  Juzgado      : Juzgado Federal Nro. 2, Neuquén

INFORMACIÓN DEL ENTORNO
  Fecha y hora : 2026-05-09T14:30:00-03:00
  Sistema op.  : Microsoft Windows 11 Pro (10.0.26100)
  Equipo       : DESKTOP-ABC123
  Usuario SO   : jcperez
  Versión      : ForZip v1.0.0

PARÁMETROS DE LA OPERACIÓN
  Operación    : Compresión
  Nivel        : 5 (Normal)
  Cifrado      : AES-256 (contraseña aplicada)
  Algoritmos   : SHA-256, SHA-512
  Archivo ZIP  : C:\Evidencia\caso_542_evidencia.zip

ARCHIVOS PROCESADOS (3 archivos)

  Nro.  Archivo                                    Tamaño         SHA-256
  ----  -----------------------------------------  -------------  ----------------------------------------------------------------
     1  Documentos\contrato_2024.pdf               1.245.678 B    a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2
     2  Documentos\factura_marzo.xlsx               342.109 B     b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3
     3  Imagenes\captura_pantalla.png              2.891.004 B    c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4

  Nro.  Archivo                                    Tamaño         SHA-512
  ----  -----------------------------------------  -------------  ----------------------------------------------------------------
     1  Documentos\contrato_2024.pdf               1.245.678 B    a1b2c3d4e5f6...  (128 caracteres hex)
     2  Documentos\factura_marzo.xlsx               342.109 B     b2c3d4e5f6a1...  (128 caracteres hex)
     3  Imagenes\captura_pantalla.png              2.891.004 B    c3d4e5f6a1b2...  (128 caracteres hex)

  Total de archivos : 3
  Tamaño total      : 4.478.791 B (4,27 MB)

HASH GLOBAL DEL ARCHIVO ZIP
  Archivo : caso_542_evidencia.zip
  Tamaño  : 3.102.445 B (2,96 MB)
  SHA-256 : d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5

================================================================================
  DISCLAIMER
  Este informe fue generado automáticamente por ForZip, una herramienta de
  software libre distribuida bajo licencia Apache 2.0. Los resultados son
  proporcionados "TAL CUAL" (AS IS), sin garantías de ningún tipo. Es
  responsabilidad exclusiva del operador validar la idoneidad de esta
  herramienta y sus resultados para el uso forense en su jurisdicción.
  ForZip no reemplaza el criterio profesional del perito actuante.
================================================================================

SHA-256 de este informe (excluyendo esta línea): e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6
```

---

## Formato Exacto — Ejemplo en Inglés

```
================================================================================
  FORENSIC INTEGRITY REPORT — ForZip v1.0.0
  Generated by ForZip — Forensic compression and verification tool
  https://github.com/patagoniarobot/forzip
================================================================================

OPERATOR INFORMATION
  Name         : Jane Smith
  Title        : Digital Forensics Examiner
  Organization : Cyber Forensics Unit
  Email        : jsmith@example.com
  Phone        : +1 555-123-4567

CASE INFORMATION
  Case No.     : CF-2026-0087
  Description  : Data exfiltration — Internal Investigation
  Court        : Federal District Court, Southern District

ENVIRONMENT INFORMATION
  Date and time: 2026-05-09T10:15:00-04:00
  Operating sys: Microsoft Windows 11 Pro (10.0.26100)
  Hostname     : FORENSIC-WS01
  OS user      : jsmith
  Version      : ForZip v1.0.0

OPERATION PARAMETERS
  Operation    : Compression
  Level        : 9 (Maximum)
  Encryption   : AES-256 (password applied)
  Algorithms   : MD5, SHA-1, SHA-256
  ZIP file     : D:\Cases\CF-2026-0087\evidence_package.zip

FILES PROCESSED (2 files)

  No.   File                                       Size           MD5
  ----  -----------------------------------------  -------------  --------------------------------
     1  exports\database_dump.sql                  15.678.902 B   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
     2  exports\user_logs.csv                       2.345.678 B   b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5

  No.   File                                       Size           SHA-1
  ----  -----------------------------------------  -------------  ----------------------------------------
     1  exports\database_dump.sql                  15.678.902 B   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2
     2  exports\user_logs.csv                       2.345.678 B   b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3

  No.   File                                       Size           SHA-256
  ----  -----------------------------------------  -------------  ----------------------------------------------------------------
     1  exports\database_dump.sql                  15.678.902 B   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2
     2  exports\user_logs.csv                       2.345.678 B   b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3

  Total files  : 2
  Total size   : 18.024.580 B (17,19 MB)

ZIP FILE HASH
  File   : evidence_package.zip
  Size   : 12.456.789 B (11,88 MB)
  SHA-256: c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4

================================================================================
  DISCLAIMER
  This report was automatically generated by ForZip, a free and open-source
  tool distributed under the Apache License 2.0. Results are provided
  "AS IS", without warranties of any kind. It is the sole responsibility
  of the operator to validate the suitability of this tool and its results
  for forensic use in their jurisdiction. ForZip does not replace the
  professional judgment of the examiner.
================================================================================

SHA-256 of this report (excluding this line): f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1
```

---

## Variantes de Operación

### Hash Batch (sin compresión)

Cuando el usuario usa el modo "Hash Batch" (hashear sin comprimir), el informe cambia:

- **Sección "Parámetros de la operación":**
  - `Operación: Hash Batch` / `Operation: Hash Batch`
  - No se muestra Nivel, Cifrado ni Archivo ZIP.
- **Sección "Hash global del archivo ZIP":** se omite completamente.

Ejemplo parcial (ES):

```
PARÁMETROS DE LA OPERACIÓN
  Operación    : Hash Batch
  Algoritmos   : MD5, SHA-256

ARCHIVOS PROCESADOS (5 archivos)

  Nro.  Archivo                                    Tamaño         MD5
  ----  -----------------------------------------  -------------  --------------------------------
     1  evidencia\foto_001.jpg                      4.567.890 B   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4
  ...

  Total de archivos : 5
  Tamaño total      : 23.456.789 B (22,37 MB)
```

### Verificación de Informe

Cuando se ejecuta "Verify Report", NO se genera un informe nuevo. El resultado se muestra solo en pantalla (GUI) o en consola (CLI). El formato de salida en CLI es:

```
[OK] Informe verificado: el hash SHA-256 coincide.
     Archivo: C:\ruta\al\informe.txt
     Hash esperado:  a1b2c3d4...
     Hash calculado: a1b2c3d4...
```

O en caso de fallo:

```
[FAIL] Informe NO verificado: el hash SHA-256 NO coincide.
       Archivo: C:\ruta\al\informe.txt
       Hash esperado:  a1b2c3d4...
       Hash calculado: x9y8z7w6...
       El archivo pudo haber sido modificado después de su generación.
```

---

## Reglas de Implementación para ReportService

1. **Construir el informe como `StringBuilder`**, línea por línea, respetando el orden exacto.

2. **Campos opcionales:** Datos del operador, datos del caso y cada campo individual dentro de ellos son opcionales. Si un campo está vacío o null, no se imprime esa línea. Si toda una sección está vacía (ej: ningún dato de caso), no se imprime el encabezado de sección.

3. **Formato de tamaño:** Los bytes se formatean con separador de miles (punto en ES: `1.245.678 B`, coma en EN: `1,245,678 B`). El equivalente en MB/GB se muestra entre paréntesis con 2 decimales y la coma/punto decimal según el idioma.

4. **Tabla de hashes:** Se genera UNA tabla por cada algoritmo seleccionado. Cada tabla tiene su propio encabezado con el nombre del algoritmo. Las columnas se alinean con espacios (no tabs). El ancho de la columna de hash varía según el algoritmo:
   - MD5: 32 caracteres
   - SHA-1: 40 caracteres
   - SHA-256: 64 caracteres
   - SHA-512: 128 caracteres

5. **Múltiples algoritmos:** Si se seleccionaron varios algoritmos, se genera una tabla separada por cada uno, en el orden: MD5 → SHA-1 → SHA-256 → SHA-512. No se mezclan en una sola tabla (sería ilegible con SHA-512).

6. **Separador de secciones:** Los separadores `====` tienen exactamente 80 caracteres. Se usan solo al inicio (encabezado) y al final (disclaimer).

7. **Línea auto-firmante:**
   - Es la ÚLTIMA línea del archivo.
   - No tiene CRLF después (el archivo termina en el último carácter del hash).
   - El cálculo: tomar todos los bytes del archivo desde el inicio (incluyendo BOM) hasta el CRLF que precede a la línea auto-firmante (inclusive el CRLF). Calcular SHA-256 de esos bytes. El resultado es el hash en lowercase hex.
   - Texto de la línea (ES): `SHA-256 de este informe (excluyendo esta línea): <hash>`
   - Texto de la línea (EN): `SHA-256 of this report (excluding this line): <hash>`

8. **Niveles de compresión — etiquetas para el informe:**

   | Nivel | Etiqueta ES | Etiqueta EN |
   |---|---|---|
   | 0 | Almacenamiento (sin compresión) | Store (no compression) |
   | 1 | Mínima | Minimum |
   | 3 | Rápida | Fast |
   | 5 | Normal | Normal |
   | 7 | Alta | High |
   | 9 | Máxima | Maximum |

9. **Cifrado:** Si se usó contraseña, mostrar `AES-256 (contraseña aplicada)` / `AES-256 (password applied)`. Si no, mostrar `Ninguno` / `None`.

10. **Ruta de archivos en la tabla:** Mostrar la ruta relativa al directorio base seleccionado, usando backslash (`\`) como separador (convención Windows).

---

## Algoritmo de Verificación

Para implementar `VerifyReport()`:

```
1. Leer el archivo completo como bytes.
2. Encontrar la última línea (buscar el último CRLF, todo lo que sigue es la línea auto-firmante).
3. Extraer el hash esperado de la línea auto-firmante (los últimos 64 caracteres).
4. Tomar todos los bytes desde el inicio hasta (e incluyendo) el último CRLF antes de la línea auto-firmante.
5. Calcular SHA-256 de esos bytes.
6. Comparar el hash calculado con el esperado (case-insensitive).
7. Devolver (coincide, detalle).
```

Casos de error:
- El archivo no tiene ningún CRLF → formato inválido.
- La última línea no contiene un hash de 64 caracteres hex → formato inválido.
- La última línea no empieza con el prefijo esperado (ES o EN) → formato inválido, pero intentar verificar igualmente si se encuentra un hash de 64 chars al final.

---

**Fin del documento de formato de informe forense.**
