# ForZip — Mockups de Interfaz de Usuario

**Versión:** 1.0  
**Fecha:** 9 de mayo de 2026  
**Autor:** Claudio Andino (claudio@patagoniarobot.com)  
**Proyecto:** ForZip v1.0.0  

> Este documento define el layout visual de cada pantalla de ForZip.  
> Es la referencia obligatoria para la Fase 3 (Interfaz Gráfica).  
> Los mockups son en arte ASCII. Representan proporciones, no píxeles exactos.

---

## Dimensiones de Ventana

| Propiedad | Valor |
|---|---|
| Tamaño inicial | 1100 x 700 px |
| Tamaño mínimo | 900 x 600 px |
| Redimensionable | Sí |
| Sidebar ancho | 220 px fijo |
| Área de contenido | Ocupa el resto (fluido) |

---

## Paleta de Colores — Tema Oscuro (por defecto)

| Elemento | Color | Uso |
|---|---|---|
| `BgPrimary` | `#1a1a2e` | Fondo del área de contenido |
| `BgSecondary` | `#0f172a` | Fondo de la sidebar |
| `BgCard` | `#1e293b` | Fondo de tarjetas, paneles, inputs |
| `Border` | `#334155` | Bordes de paneles, inputs, separadores |
| `TextPrimary` | `#e2e8f0` | Texto principal, títulos |
| `TextSecondary` | `#94a3b8` | Texto secundario, placeholders, etiquetas |
| `Accent` | `#10B981` | Botones principales, íconos activos, éxito |
| `AccentHover` | `#059669` | Hover sobre botones principales |
| `Error` | `#ef4444` | Errores, botón cancelar, verificación fallida |
| `Warning` | `#f59e0b` | Advertencias, entropía media |
| `Violet` | `#818cf8` | Íconos de sidebar, acentos secundarios |
| `ProgressBg` | `#334155` | Fondo de barra de progreso |
| `ProgressFill` | `#10B981` | Relleno de barra de progreso |

### Paleta de Colores — Tema Claro

| Elemento | Color |
|---|---|
| `BgPrimary` | `#f8fafc` |
| `BgSecondary` | `#e2e8f0` |
| `BgCard` | `#ffffff` |
| `Border` | `#cbd5e1` |
| `TextPrimary` | `#1e293b` |
| `TextSecondary` | `#64748b` |
| `Accent` | `#059669` |
| `AccentHover` | `#047857` |
| `Error` | `#dc2626` |
| `Warning` | `#d97706` |
| `Violet` | `#6366f1` |
| `ProgressBg` | `#cbd5e1` |
| `ProgressFill` | `#059669` |

---

## Tipografía

| Uso | Fuente | Tamaño |
|---|---|---|
| Título de ventana | Segoe UI / Sans-serif | 14 px, Bold |
| Título de sección | Segoe UI / Sans-serif | 16 px, SemiBold |
| Texto general | Segoe UI / Sans-serif | 13 px, Regular |
| Etiquetas de campos | Segoe UI / Sans-serif | 12 px, Regular |
| Texto monoespaciado (hashes, contraseñas, rutas) | Consolas / Monospace | 12 px, Regular |
| Sidebar botones | Segoe UI / Sans-serif | 13 px, Regular |
| Barra de estado | Segoe UI / Sans-serif | 11 px, Regular |

---

## Convenciones de los Mockups

```
┌──────┐  Borde de panel/ventana
│      │
└──────┘

[  Botón  ]     Botón normal
[■ Botón  ]     Botón principal (accent)
( ● )  ( ○ )    Radio button (seleccionado / no)
[✓]  [ ]        Checkbox (marcado / no)
╔══════╗        Input de texto / campo editable
║      ║
╚══════╝
▼               ComboBox / Dropdown
━━━━━━━         Barra de progreso
···········     Zona de drag & drop (borde punteado)
```

---

## 1. MainWindow — Layout General

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  🔒 ForZip v1.0.0                                             ─  □  ✕    │
├────────────────────┬────────────────────────────────────────────────────────┤
│                    │                                                        │
│   ┌──────────────┐ │                                                        │
│   │ 📦 Comprimir │ │                                                        │
│   └──────────────┘ │                                                        │
│   ┌──────────────┐ │                                                        │
│   │ 📂 Extraer   │ │              ÁREA DE CONTENIDO                         │
│   └──────────────┘ │                                                        │
│   ┌──────────────┐ │         (Aquí se renderiza la View                     │
│   │ #  Hash Batch│ │          correspondiente al botón                      │
│   └──────────────┘ │          seleccionado en la sidebar)                   │
│   ┌──────────────┐ │                                                        │
│   │ ✓  Verificar │ │                                                        │
│   └──────────────┘ │                                                        │
│   ┌──────────────┐ │                                                        │
│   │ 🔑 Contraseña│ │                                                        │
│   └──────────────┘ │                                                        │
│                    │                                                        │
│   ────────────── │                                                          │
│                    │                                                        │
│   ┌──────────────┐ │                                                        │
│   │ ⚙  Ajustes   │ │                                                        │
│   └──────────────┘ │                                                        │
│   ┌──────────────┐ │                                                        │
│   │ ℹ  Acerca de │ │                                                        │
│   └──────────────┘ │                                                        │
│                    │                                                        │
│   ─── v1.0.0 ─── │                                                          │
├────────────────────┴────────────────────────────────────────────────────────┤
│  Estado: Listo                                                    ES | EN  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Comportamiento de la Sidebar:**

- Fondo `BgSecondary` (`#0f172a`).
- Ancho fijo 220 px. No se colapsa.
- El botón activo se resalta con fondo `BgCard` (`#1e293b`) y borde izquierdo de 3 px en `Accent` (`#10B981`).
- Los íconos de los botones usan `Violet` (`#818cf8`) cuando están inactivos y `Accent` (`#10B981`) cuando están activos.
- Separador horizontal entre los botones de función (Comprimir..Contraseña) y los de configuración (Ajustes, Acerca de).
- Versión al pie de la sidebar en `TextSecondary`.

**Barra de estado (footer):**

- Altura 28 px, fondo `BgSecondary`.
- Lado izquierdo: mensaje de estado (Listo / Procesando... / Error).
- Lado derecho: toggle de idioma `ES | EN` como botón clickeable.

**Vista por defecto al abrir:** ZipView (Comprimir).

---

## 2. ZipView — Comprimir

```
┌────────────────────────────────────────────────────────────────────┐
│  COMPRIMIR ARCHIVOS                                                │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┐  │
│  ·                                                              ·  │
│  ·        Arrastrá archivos o carpetas aquí                     ·  │
│  ·              o hacé clic para buscar                         ·  │
│  ·                                                              ·  │
│  └ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┘  │
│                                                                    │
│  Archivos seleccionados (3):                    [Limpiar todo]     │
│  ┌────────────────────────────────────────────────────────────┐    │
│  │ 📄 contrato_2024.pdf              1,2 MB          [✕]     │    │
│  │ 📄 factura_marzo.xlsx             342 KB          [✕]     │    │
│  │ 📁 Imagenes\  (12 archivos)       28,4 MB         [✕]     │    │
│  └────────────────────────────────────────────────────────────┘    │
│                                                                    │
│  ┌─ Opciones ───────────────────────────────────────────────────┐  │
│  │                                                               │  │
│  │  Nivel de compresión:  [ 5 - Normal          ▼]              │  │
│  │                                                               │  │
│  │  Contraseña (opcional): ╔══════════════════════╗  [👁]       │  │
│  │                         ║ ••••••••             ║              │  │
│  │                         ╚══════════════════════╝              │  │
│  │                         [ 🔑 Generar contraseña ]             │  │
│  │                                                               │  │
│  │  Algoritmos de hash:                                          │  │
│  │  [✓] SHA-256    [✓] SHA-512    [ ] MD5    [ ] SHA-1          │  │
│  │                                                               │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  Archivo de salida:                                                │
│  ╔═══════════════════════════════════════════════════╗  [...]      │
│  ║ C:\Evidencia\caso_542_evidencia.zip               ║             │
│  ╚═══════════════════════════════════════════════════╝              │
│                                                                    │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  45%  1,2 GB / 2,7 GB   │
│                                                                    │
│                           [■ Comprimir ]   [ Cancelar ]            │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Comportamiento:**

- **Zona de drag & drop:** Borde punteado `Border`, fondo transparente. Al pasar archivos encima, el borde cambia a `Accent` y el fondo toma `Accent` al 10% de opacidad. Clic en la zona abre diálogo de selección múltiple de archivos.
- **Lista de archivos:** Scroll vertical si hay muchos. Cada ítem tiene botón `[✕]` para eliminar individualmente. Las carpetas muestran la cantidad de archivos dentro y el tamaño total.
- **ComboBox de nivel:** Opciones: `0 - Almacenamiento`, `1 - Mínima`, `3 - Rápida`, `5 - Normal`, `7 - Alta`, `9 - Máxima`. Default según Settings.
- **Contraseña:** Toggle de visibilidad `[👁]`. Botón `Generar contraseña` abre PasswordGeneratorView en un diálogo modal y al aceptar copia la contraseña al campo.
- **Archivo de salida:** Campo editable + botón `[...]` para browse. Default: misma carpeta que el primer archivo seleccionado, nombre sugerido con timestamp.
- **Barra de progreso:** Oculta cuando no hay operación. Muestra porcentaje + bytes procesados / bytes totales.
- **Botón Comprimir:** Deshabilitado si no hay archivos seleccionados o no hay ruta de salida. Se convierte en "Cancelar" durante la operación (o se muestran ambos, con Comprimir deshabilitado).
- **Post-compresión:** Mensaje de éxito con rutas del ZIP y del informe generado. Opción para abrir la carpeta contenedora.

---

## 3. UnzipView — Extraer

```
┌────────────────────────────────────────────────────────────────────┐
│  EXTRAER ARCHIVOS                                                  │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┐  │
│  ·                                                              ·  │
│  ·        Arrastrá un archivo ZIP aquí                          ·  │
│  ·              o hacé clic para buscar                         ·  │
│  ·                                                              ·  │
│  └ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┘  │
│                                                                    │
│  Archivo ZIP:                                                      │
│  ╔═══════════════════════════════════════════════════╗  [...]      │
│  ║ C:\Evidencia\caso_542_evidencia.zip               ║             │
│  ╚═══════════════════════════════════════════════════╝              │
│                                                                    │
│  Carpeta de destino:                                               │
│  ╔═══════════════════════════════════════════════════╗  [...]      │
│  ║ C:\Evidencia\extraido\                            ║             │
│  ╚═══════════════════════════════════════════════════╝              │
│                                                                    │
│  Contraseña (si aplica): ╔══════════════════════╗  [👁]           │
│                          ║                      ║                  │
│                          ╚══════════════════════╝                  │
│                                                                    │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  72%  8,3 MB / 11,5 MB  │
│                                                                    │
│                            [■ Extraer ]   [ Cancelar ]             │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Comportamiento:**

- **Drag & drop:** Solo acepta un archivo `.zip`. Si se arrastra otra cosa, feedback visual de rechazo (borde `Error`).
- **Carpeta de destino:** Default: misma carpeta que el ZIP, subcarpeta con el nombre del ZIP sin extensión.
- **Contraseña:** Visible solo si se detecta que el ZIP está cifrado (o siempre visible con placeholder "Dejar vacío si no tiene contraseña").
- **Errores:** Contraseña incorrecta → mensaje claro con `Error`. ZIP corrupto → mensaje descriptivo.

---

## 4. HashBatchView — Hash Batch

```
┌────────────────────────────────────────────────────────────────────┐
│  HASH BATCH                                                        │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┐  │
│  ·        Arrastrá archivos aquí para calcular hashes           ·  │
│  ·              o hacé clic para buscar                         ·  │
│  └ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┘  │
│                                                                    │
│  Algoritmos:  [✓] SHA-256   [ ] SHA-512   [ ] MD5   [ ] SHA-1    │
│                                                                    │
│  Archivos (5):                                       [Limpiar]    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │ Nro  Archivo                   Tamaño      SHA-256           │  │
│  │ ───  ────────────────────────  ──────────  ──────────────── │  │
│  │  1   contrato_2024.pdf         1,2 MB      a1b2c3d4e5f6...  │  │
│  │  2   factura_marzo.xlsx        342 KB      b2c3d4e5f6a1...  │  │
│  │  3   captura.png               2,9 MB      c3d4e5f6a1b2...  │  │
│  │  4   log_sistema.txt           89 KB       ⏳ Calculando...  │  │
│  │  5   backup.sql                15,7 MB     ⏳ Pendiente      │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  60%  3/5 archivos            │
│                                                                    │
│           [■ Calcular ]   [ Cancelar ]   [ Exportar Informe ]      │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Comportamiento:**

- **Tabla de resultados:** Se llena progresivamente. Los hashes aparecen en fuente monoespaciada (`Consolas`). Los hashes largos se truncan visualmente con `...` pero se copian completos al hacer clic.
- **Progreso:** Doble indicador: barra de bytes + contador de archivos.
- **Exportar Informe:** Habilitado solo cuando todos los hashes están calculados. Genera informe forense en modo "Hash Batch" según `03_Formato_Informe_Forense.md`. Antes de generar, muestra diálogo de confirmación de datos del operador.
- **Clic en celda de hash:** Copia el hash completo al clipboard con feedback visual ("Copiado ✓" por 2 segundos).

---

## 5. VerifyReportView — Verificar Informe

```
┌────────────────────────────────────────────────────────────────────┐
│  VERIFICAR INFORME                                                 │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┐  │
│  ·        Arrastrá un informe ForZip (.txt) aquí                ·  │
│  ·              o hacé clic para buscar                         ·  │
│  └ · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·┘  │
│                                                                    │
│  Archivo de informe:                                               │
│  ╔═══════════════════════════════════════════════════╗  [...]      │
│  ║ C:\Evidencia\ForZip_Report_20260509_143000.txt    ║             │
│  ╚═══════════════════════════════════════════════════╝              │
│                                                                    │
│                          [■ Verificar ]                            │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                                                              │  │
│  │               ✅  INFORME VERIFICADO                         │  │
│  │                                                              │  │
│  │   El hash SHA-256 del informe coincide.                      │  │
│  │   El archivo no fue modificado desde su generación.          │  │
│  │                                                              │  │
│  │   Hash esperado:   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6... │  │
│  │   Hash calculado:  a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6... │  │
│  │                                                              │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Variante — Verificación Fallida:**

```
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                                                              │  │
│  │               ❌  INFORME NO VERIFICADO                      │  │
│  │                                                              │  │
│  │   El hash SHA-256 del informe NO coincide.                   │  │
│  │   El archivo pudo haber sido modificado.                     │  │
│  │                                                              │  │
│  │   Hash esperado:   a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4...      │  │
│  │   Hash calculado:  x9y8z7w6v5u4t3s2r1q0p9o8n7m6l5k4...      │  │
│  │                                                              │  │
│  └──────────────────────────────────────────────────────────────┘  │
```

**Comportamiento:**

- **Resultado:** Panel con fondo verde suave (`Accent` al 10%) para OK, fondo rojo suave (`Error` al 10%) para fallo.
- **Ícono:** Grande y centrado (✅ o ❌), 48 px.
- **Hashes:** Fuente monoespaciada. Se muestran completos (con scroll horizontal si hace falta).
- **Formato inválido:** Tercer estado con ícono ⚠️ amarillo (`Warning`) y mensaje "El archivo no tiene el formato esperado de un informe ForZip".

---

## 6. PasswordGeneratorView — Generador de Contraseñas

```
┌────────────────────────────────────────────────────────────────────┐
│  GENERADOR DE CONTRASEÑAS                                          │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                                                              │  │
│  │   kQ7$mP2!xR9&nL4@wE6#                                      │  │
│  │                                                              │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  Muy fuerte (98 bits)│
│  ██████████████████████████████████████████████  (verde)           │
│                                                                    │
│  ┌─ Opciones ───────────────────────────────────────────────────┐  │
│  │                                                               │  │
│  │  Longitud:  ────────────●──────────  24                      │  │
│  │             8                   128                            │  │
│  │                                                               │  │
│  │  [✓] Mayúsculas    (A-Z)                                     │  │
│  │  [✓] Minúsculas    (a-z)                                     │  │
│  │  [✓] Dígitos       (0-9)                                     │  │
│  │  [✓] Símbolos      (!@#$%^&*...)                             │  │
│  │  [ ] Excluir ambiguos  (0 O o I l 1 |)                       │  │
│  │                                                               │  │
│  └───────────────────────────────────────────────────────────────┘  │
│                                                                    │
│               [■ Generar nueva ]    [ 📋 Copiar ]                  │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Comportamiento:**

- **Campo de contraseña:** Solo lectura, fondo `BgCard`, fuente `Consolas` 16 px. La contraseña se colorea por tipo de carácter: mayúsculas `TextPrimary`, minúsculas `TextSecondary`, dígitos `Accent`, símbolos `Warning`.
- **Barra de entropía:** Color según nivel:
  - < 40 bits → `Error` (rojo), etiqueta "Débil" / "Weak"
  - 40-59 bits → `Warning` (amarillo), etiqueta "Media" / "Fair"
  - 60-79 bits → `Accent` (verde), etiqueta "Fuerte" / "Strong"
  - ≥ 80 bits → `Accent` (verde intenso), etiqueta "Muy fuerte" / "Very strong"
- **Slider de longitud:** Rango 8-128. Al mover el slider se regenera automáticamente.
- **Checkboxes:** Al cambiar cualquier checkbox se regenera automáticamente. Al menos uno debe estar activo; si el usuario intenta desmarcar el último, se impide (checkbox no se desmarca) con tooltip explicativo.
- **Copiar:** Copia al clipboard y muestra feedback "Copiado ✓" por 2 segundos junto al botón.
- **Regenerar automáticamente:** Cada cambio de opción regenera. El botón "Generar nueva" sirve para obtener otra contraseña con las mismas opciones.

---

## 7. SettingsView — Ajustes

```
┌────────────────────────────────────────────────────────────────────┐
│  AJUSTES                                                           │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌─ General ──┬─ Operador ──┬─ Valores por Defecto ─┐             │
│  │            │             │                        │             │
│  ├────────────┴─────────────┴────────────────────────┤             │
│  │                                                    │             │
│  │  Idioma:          [ Español              ▼]       │             │
│  │                                                    │             │
│  │  Tema:            ( ● ) Oscuro   ( ○ ) Claro      │             │
│  │                                                    │             │
│  │  Carpeta de salida por defecto:                    │             │
│  │  ╔════════════════════════════════════════╗ [...]  │             │
│  │  ║ C:\Evidencia\                          ║        │             │
│  │  ╚════════════════════════════════════════╝        │             │
│  │                                                    │             │
│  └────────────────────────────────────────────────────┘             │
│                                                                    │
│                     [■ Guardar ]   [ Restaurar valores ]           │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

### Tab "Operador"

```
│  ┌─ General ──┬─ Operador ──┬─ Valores por Defecto ─┐             │
│  │            │             │                        │             │
│  ├────────────┴─────────────┴────────────────────────┤             │
│  │                                                    │             │
│  │  Estos datos se incluyen en los informes forenses. │             │
│  │  Todos los campos son opcionales.                  │             │
│  │                                                    │             │
│  │  Nombre:     ╔════════════════════════════════╗    │             │
│  │              ║ Juan Carlos Pérez              ║    │             │
│  │              ╚════════════════════════════════╝    │             │
│  │  Cargo:      ╔════════════════════════════════╗    │             │
│  │              ║ Perito Informático             ║    │             │
│  │              ╚════════════════════════════════╝    │             │
│  │  Organismo:  ╔════════════════════════════════╗    │             │
│  │              ║ D.A.F.I. - Policía de Neuquén  ║    │             │
│  │              ╚════════════════════════════════╝    │             │
│  │  Email:      ╔════════════════════════════════╗    │             │
│  │              ║ jcperez@ejemplo.com            ║    │             │
│  │              ╚════════════════════════════════╝    │             │
│  │  Teléfono:   ╔════════════════════════════════╗    │             │
│  │              ║ +54 299 555-1234               ║    │             │
│  │              ╚════════════════════════════════╝    │             │
│  │                                                    │             │
│  └────────────────────────────────────────────────────┘             │
```

### Tab "Valores por Defecto"

```
│  ┌─ General ──┬─ Operador ──┬─ Valores por Defecto ─┐             │
│  │            │             │                        │             │
│  ├────────────┴─────────────┴────────────────────────┤             │
│  │                                                    │             │
│  │  Estos valores se pre-cargan en cada operación.    │             │
│  │  Pueden modificarse antes de ejecutar.             │             │
│  │                                                    │             │
│  │  Nivel de compresión:  [ 5 - Normal          ▼]   │             │
│  │                                                    │             │
│  │  Algoritmos de hash por defecto:                   │             │
│  │  [✓] SHA-256    [ ] SHA-512    [ ] MD5   [ ] SHA-1│             │
│  │                                                    │             │
│  └────────────────────────────────────────────────────┘             │
```

**Comportamiento:**

- **Cambio de idioma:** Aplica inmediatamente al seleccionar. Toda la UI se actualiza (sidebar, botones, etiquetas, mensajes).
- **Cambio de tema:** Aplica inmediatamente al cambiar el radio button.
- **Guardar:** Persiste todos los tabs a `config.json`. Feedback "Guardado ✓".
- **Restaurar valores:** Pide confirmación ("¿Restaurar todos los ajustes a sus valores por defecto?"), luego resetea todo.
- **Datos del operador:** Se pre-cargan en el diálogo de confirmación antes de cada informe.

---

## 8. AboutView — Acerca de

```
┌────────────────────────────────────────────────────────────────────┐
│  ACERCA DE                                                         │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│                         ┌──────────┐                               │
│                         │          │                               │
│                         │  [LOGO]  │                               │
│                         │          │                               │
│                         └──────────┘                               │
│                                                                    │
│                     ForZip v1.0.0                                   │
│              Forensic ZIP Tool — Open Source                        │
│                                                                    │
│  ─────────────────────────────────────────────────────────────     │
│                                                                    │
│  Autor       : Claudio Andino                                      │
│  Email       : claudio@patagoniarobot.com                          │
│  Licencia    : Apache License 2.0                                  │
│  Iniciativa  : Patagonia Robot                                     │
│                                                                    │
│  ─────────────────────────────────────────────────────────────     │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  AVISO LEGAL                                                 │  │
│  │                                                              │  │
│  │  Este software se distribuye "TAL CUAL" (AS IS), sin         │  │
│  │  garantías de ningún tipo. Es responsabilidad exclusiva      │  │
│  │  del operador validar la idoneidad de esta herramienta       │  │
│  │  y sus resultados para el uso forense en su jurisdicción.    │  │
│  │  ForZip no reemplaza el criterio profesional del perito.     │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                    │
│            [ 📖 Ayuda (Help.html) ]    [ 🔗 Sitio web ]           │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

**Comportamiento:**

- **Logo:** PNG 128x128 centrado. El temporal es el ícono de caja+lupa monocromático `#10B981`.
- **Disclaimer:** Panel con fondo `BgCard` y borde `Border`. Texto `TextSecondary`. Bilingüe según idioma activo.
- **Ayuda:** Abre `Help.html` en el navegador por defecto del sistema (`Process.Start` con `UseShellExecute = true`).
- **Sitio web:** Abre la URL de Patagonia Robot (o se deshabilita si no hay URL definida).

---

## 9. Diálogo de Confirmación de Datos del Operador

Este diálogo aparece ANTES de generar cualquier informe forense (desde ZipView o HashBatchView).

```
┌──────────────────────────────────────────────────────┐
│  CONFIRMAR DATOS DEL INFORME                          │
├──────────────────────────────────────────────────────┤
│                                                       │
│  Estos datos se incluirán en el informe forense.      │
│  Verificá que sean correctos antes de continuar.      │
│                                                       │
│  Nombre:     ╔══════════════════════════════╗         │
│              ║ Juan Carlos Pérez            ║         │
│              ╚══════════════════════════════╝         │
│  Cargo:      ╔══════════════════════════════╗         │
│              ║ Perito Informático           ║         │
│              ╚══════════════════════════════╝         │
│  Organismo:  ╔══════════════════════════════╗         │
│              ║ D.A.F.I.                     ║         │
│              ╚══════════════════════════════╝         │
│  Email:      ╔══════════════════════════════╗         │
│              ║ jcperez@ejemplo.com          ║         │
│              ╚══════════════════════════════╝         │
│  Teléfono:   ╔══════════════════════════════╗         │
│              ║ +54 299 555-1234             ║         │
│              ╚══════════════════════════════╝         │
│                                                       │
│  ── Datos del caso (opcionales) ──                    │
│                                                       │
│  Caso Nro.:  ╔══════════════════════════════╗         │
│              ║                              ║         │
│              ╚══════════════════════════════╝         │
│  Carátula:   ╔══════════════════════════════╗         │
│              ║                              ║         │
│              ╚══════════════════════════════╝         │
│  Juzgado:    ╔══════════════════════════════╗         │
│              ║                              ║         │
│              ╚══════════════════════════════╝         │
│                                                       │
│            [■ Generar Informe ]   [ Cancelar ]        │
│                                                       │
└──────────────────────────────────────────────────────┘
```

**Comportamiento:**

- Es un diálogo modal (bloquea la ventana principal).
- Los campos del operador vienen pre-cargados desde Settings. Son editables en el diálogo (los cambios se aplican solo al informe actual, no se persisten).
- Los campos de caso están vacíos por defecto. Son opcionales.
- "Generar Informe" cierra el diálogo y procede con la generación.
- "Cancelar" cierra sin generar.

---

## Comportamiento Global del Drag & Drop

| Vista | Acepta | Comportamiento |
|---|---|---|
| ZipView | Archivos y carpetas (múltiples) | Agrega a la lista |
| UnzipView | Un solo archivo `.zip` | Carga como archivo a extraer |
| HashBatchView | Archivos (múltiples, no carpetas) | Agrega a la lista |
| VerifyReportView | Un solo archivo `.txt` | Carga como informe a verificar |

**Feedback visual de drag & drop:**

- Al entrar en la zona de drop: borde cambia de punteado `Border` a sólido `Accent`, fondo `Accent` al 10%.
- Si el tipo no es aceptado: borde cambia a `Error`, ícono de prohibido.
- Al soltar: animación breve de fade-in en los ítems agregados.

---

## Responsive / Redimensionamiento

- **Sidebar:** Ancho fijo 220 px. Si la ventana es menor a 900 px de ancho, no se permite reducir más.
- **Área de contenido:** Fluida. Los campos de texto y tablas se estiran horizontalmente.
- **Tabla de hashes (HashBatchView):** Scroll horizontal si los hashes no entran en el ancho disponible.
- **Barra de progreso:** Ocupa el ancho completo del contenido menos márgenes.
- **Botones:** Centrados horizontalmente. No se apilan verticalmente al reducir tamaño.

---

**Fin del documento de mockups UI.**
