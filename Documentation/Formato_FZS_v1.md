# Formato de contenedor sellado `.fzs` — versión 1

**Especificación pública del formato**
Autor: Claudio Andino — Patagonia Robot
Implementación de referencia: ForZip 2.0.0 (Apache License 2.0)
Estado: estable — `formatVersion = 1`

---

## 0. Propósito y alcance

Este documento describe el formato de archivo `.fzs` (*ForZip Sealed*) con el detalle
necesario para que un tercero escriba su propio extractor **sin leer el código fuente de
ForZip**. Ese es el criterio de aceptación del documento: si algo acá no alcanza para
implementar la apertura de un sobre, el documento tiene un defecto.

El `.fzs` no es un formato propietario. La seguridad de un sobre reside **exclusivamente
en la contraseña**, nunca en el secreto del formato. Publicar la especificación no
debilita nada: un formato criptográfico cuya protección dependa de que nadie sepa cómo
funciona no es un formato seguro.

El motivo de fondo es la continuidad. Una herramienta que empaqueta evidencia introduce
un riesgo propio: que dentro de diez años el material sea inaccesible porque el programa
ya no existe o no corre en el sistema operativo de ese momento. En sede judicial ese
riesgo es más grave que el que se quiso evitar. Esta especificación, junto con el
extractor autónomo `Tools/fzs_extract.py`, es lo que lo neutraliza.

**Este documento no describe la lógica forense de ForZip** —manifiesto, informe, firma
CMS, veredicto por archivo—. Describe únicamente el sobre: cómo se cifra, cómo se abre y
cómo se verifica su integridad. Lo que hay adentro del sobre son archivos comunes,
inspeccionables con herramientas estándar.

---

## 1. Qué es un sobre `.fzs`

Un sobre es un archivo único que contiene un **contenedor interno cifrado con
AES-256-GCM**, troceado en bloques autenticados independientes, precedido por un header
en claro con los parámetros criptográficos necesarios para derivar la clave.

Un sobre no revela nada de su contenido: ni nombres de archivo, ni cantidad de archivos,
ni hashes individuales, ni causa, ni operador. Lo único que expone es su tamaño y la
fecha de sellado.

Cada sobre se acompaña de un **recibo público** (`.receipt.json`, §9), que circula en
claro y permite verificar que el sobre no fue alterado **sin necesidad de la
contraseña**.

---

## 2. Convenciones

- Todos los enteros del layout binario son **little-endian**, sin signo.
- El header es **JSON UTF-8 sin BOM y sin saltos de línea**.
- Los campos `*B64` son **Base64 estándar** (RFC 4648, alfabeto `A-Z a-z 0-9 + /`, con
  relleno `=`).
- Los hashes en texto se escriben en **hexadecimal minúscula**, sin separadores.
- Las fechas son **UTC en ISO 8601** con sufijo `Z`, con precisión de segundos:
  `2026-09-03T17:22:11Z`.
- La contraseña se convierte a bytes con **UTF-8**, sin terminador nulo y sin
  normalización Unicode previa.

---

## 3. Layout binario

```
Offset        Tamaño      Campo
------------  ----------  ------------------------------------------------------
0             8           magic      = ASCII "FZSEAL01"
8             2           formatVer  = uint16 LE, valor 1
10            4           headerLen  = uint32 LE, longitud del header JSON
14            headerLen   headerJson = UTF-8 sin BOM, sin saltos de línea
14+headerLen  ...         stream de chunks (§6)
```

Los primeros 14 bytes son de longitud fija. `headerLen` es la longitud **en bytes** del
JSON, no en caracteres: con contenido no ASCII no coinciden.

El tamaño total del archivo queda determinado por el header y sirve como comprobación
temprana de truncamiento:

```
tamañoTotal = 14 + headerLen + payloadSize + 16 * totalChunks
```

---

## 4. Header JSON

El header **no se cifra**, porque contiene los parámetros necesarios para derivar la
clave: no hay forma de cifrarlo con la clave que él mismo permite obtener. Por eso su
contenido está deliberadamente limitado a lo criptográficamente imprescindible.

```json
{
  "packageId": "8f3a1c9e-4b21-4d7e-9a03-1c5e7f2b8d64",
  "sealedAtUtc": "2026-09-03T17:22:11Z",
  "kdf": {
    "algo": "PBKDF2-HMAC-SHA256",
    "iterations": 600000,
    "saltB64": "<32 bytes en Base64>"
  },
  "cipher": {
    "algo": "AES-256-GCM",
    "chunkSize": 1048576,
    "nonceBaseB64": "<8 bytes en Base64>"
  },
  "payloadSize": 4294967296,
  "totalChunks": 4096
}
```

| Campo | Tipo | Significado |
|---|---|---|
| `packageId` | UUID v4 en minúscula, con guiones | Identificador aleatorio del paquete. **No** deriva de la causa, del operador, del nombre del archivo ni de la fecha. Es el único identificador que sale a la nube. |
| `sealedAtUtc` | fecha ISO 8601 UTC | Momento del sellado. |
| `kdf.algo` | cadena | Hoy siempre `"PBKDF2-HMAC-SHA256"`. Un lector que encuentre otro valor debe rechazar el archivo. |
| `kdf.iterations` | entero | Iteraciones de PBKDF2. Hoy `600000`. Está en el header para poder subirlo en el futuro sin romper compatibilidad: **un lector debe usar el valor del header, no una constante propia**. Debe rechazar, sin derivar la clave, un valor menor que 1 o mayor que `10000000`: el header se autentica recién con el primer chunk, y un valor alterado enorme dejaría al lector horas derivando una clave antes de descubrir la alteración. |
| `kdf.saltB64` | Base64 de 32 bytes | Salt aleatorio, único por paquete. |
| `cipher.algo` | cadena | Hoy siempre `"AES-256-GCM"`. Otro valor implica rechazar el archivo. |
| `cipher.chunkSize` | entero | Tamaño del bloque de texto plano, en bytes. Hoy `1048576` (1 MiB). |
| `cipher.nonceBaseB64` | Base64 de 8 bytes | Prefijo aleatorio del nonce, único por paquete. |
| `payloadSize` | entero de 64 bits | Longitud en bytes del contenedor interno **en claro**. |
| `totalChunks` | entero | Cantidad de chunks. Debe ser exactamente `ceil(payloadSize / chunkSize)`. |

**Está prohibido agregar al header cualquier campo derivado del contenido.** Nada de
nombres de archivo, cantidad de archivos, causa, dependencia ni operador. Un lector que
encuentre campos desconocidos puede ignorarlos, pero un sobre que los contenga no es un
sobre conforme.

Los dos campos de tamaño (`payloadSize`, `totalChunks`) revelan el orden de magnitud del
contenido, que el tamaño del archivo ya expone de todos modos; están para permitir la
detección de truncamiento.

### 4.1 Los bytes exactos del header importan

El AAD de cada chunk (§6.2) se calcula sobre el **SHA-256 de los bytes del header tal
como están en el archivo**, no sobre una reserialización del JSON. Un lector debe
conservar esos `headerLen` bytes tal cual los leyó: reordenar claves, cambiar el
espaciado o reescribir el JSON produce un hash distinto y el descifrado falla.

Esa es también la razón por la que alterar el header —subir o bajar `iterations`, cambiar
el salt, mentir sobre `payloadSize`— rompe el descifrado de **todos** los chunks: el
header está autenticado aunque esté en claro.

---

## 5. Derivación de clave

```
key = PBKDF2-HMAC-SHA256(
          password = UTF-8(contraseña),
          salt     = base64_decode(kdf.saltB64),      // 32 bytes
          c        = kdf.iterations,                  // 600000 por defecto
          dkLen    = 32 )                             // 256 bits, para AES-256
```

La clave derivada tiene **32 bytes** y se usa directamente como clave AES-256-GCM para
todos los chunks del sobre. No hay derivación por chunk: la unicidad la aporta el nonce.

**Sobre la elección de PBKDF2 y no Argon2id.** Es deliberada. Argon2id resiste mejor el
crackeo con hardware dedicado, pero en .NET exige una dependencia externa y amplía la
superficie a auditar de una herramienta forense; PBKDF2-HMAC-SHA256 está en la biblioteca
estándar de prácticamente todo lenguaje, que es exactamente lo que hace viable un
extractor de cien líneas dentro de diez años. El campo `iterations` queda en el header
para poder subir el costo sin romper compatibilidad.

---

## 6. Stream de chunks

El contenedor interno en claro se corta en bloques de `chunkSize` bytes. El último bloque
mide `payloadSize - (totalChunks - 1) * chunkSize`, que puede ser menor o igual a
`chunkSize`.

Cada chunk se cifra por separado con AES-256-GCM y se escribe así, en orden y sin ningún
prefijo de longitud:

```
por cada chunk i, de 0 a totalChunks-1:
    ciphertext[i]   (misma longitud que el texto plano del chunk)
    tag[i]          (16 bytes, tag de autenticación GCM)
```

**No hay prefijo de longitud por chunk** —la longitud se deduce de `payloadSize`, que
está autenticado—: reduce la maleabilidad del archivo y simplifica el parser.

Si `payloadSize` es 0, `totalChunks` es 0 y el stream está vacío. En la práctica el
contenedor interno nunca es vacío, porque siempre lleva al menos un archivo.

### 6.1 Nonce

```
nonce = nonceBase (8 bytes) || uint32LE(i)        // 12 bytes en total
```

Con el índice del chunk dentro del nonce, dos chunks del mismo sobre nunca comparten
nonce. Entre sobres distintos la clave ya es distinta —el salt es aleatorio de 32 bytes—,
así que la reutilización del par (clave, nonce), que es el modo de falla catastrófico de
GCM, es imposible.

### 6.2 AAD (datos adicionales autenticados)

```
headerHash = SHA-256(headerJsonBytes)                          // 32 bytes
aad        = headerHash (32) || uint32LE(i) (4) || isFinal (1) // 37 bytes

donde isFinal = 0x01 si i == totalChunks - 1
                0x00 en cualquier otro caso
```

El AAD no se escribe en el archivo: se reconstruye al abrir. Cada una de sus tres partes
cubre un ataque distinto:

- `headerHash` ata el chunk a **este** header: alterar los parámetros criptográficos
  invalida todos los tags.
- `uint32LE(i)` ata el chunk a **su posición**: reordenar o duplicar bloques invalida los
  tags.
- `isFinal` ata el chunk a **ser o no el último**: cortar el archivo por la mitad deja un
  nuevo último chunk que fue autenticado con `isFinal = 0x00`, y la verificación falla.

> **Detalle que un implementador debe respetar:** `isFinal` se determina desde
> `totalChunks` del header —que está autenticado—, **no** desde "llegué al fin del
> archivo". Deducirlo del EOF vuelve indetectable el truncamiento y anula la propiedad
> que este campo existe para dar.

---

## 7. Carga útil: el contenedor interno

El texto plano que resulta de descifrar y concatenar todos los chunks es un **archivo ZIP
con nivel de compresión `Store` (0), sin contraseña**, con todas las entradas en la raíz:

```
caso.zip                       (o caso.001, caso.002, … si es multivolumen)
caso.zip.manifest.json         manifiesto legible por máquina
caso.zip.manifest.json.p7s     firma CMS del manifiesto (si se firmó)
fzs_extract.py                 copia del extractor autónomo (§11)
```

Los nombres de arriba son un ejemplo: salvo el del extractor, salen del nombre que el
operador le dio al paquete y **no son parte del formato**. Tampoco lo es qué artefactos
van: los sobres de ForZip anteriores a 2.0 llevan además el informe `caso.zip.report.txt`
y su `caso.zip.report.txt.sha256`; desde 2.0 el informe se arma a pedido a partir del
manifiesto y no viaja en el sobre. Lo que sí es parte del formato es
que el contenedor sea un ZIP `Store` plano.

Se usa `Store` porque el contenido ya está comprimido: recomprimir solo consume tiempo.
Se usa ZIP y no un formato propio para que, una vez abierto el sobre, el contenido sea
inspeccionable con cualquier herramienta estándar.

La entrada `fzs_extract.py` la agrega ForZip al final del contenedor. Se omite en el único
caso en que un artefacto del paquete ya ocupa ese nombre: la evidencia manda y no se la
sombrea. Un lector no debe suponer que la entrada está: es una comodidad, no parte del
formato.

Un extractor conforme **no necesita entender nada de esto**: su trabajo termina cuando
escribe el contenedor interno en disco. Abrir el ZIP y verificar el manifiesto son pasos
independientes, que hace ForZip o cualquier otra herramienta.

---

## 8. Nomenclatura de los archivos de salida

```
FZS-8f3a1c9e.fzs                    ← lo único que se sube a la nube
FZS-8f3a1c9e.receipt.json           ← recibo público
FZS-8f3a1c9e.receipt.json.p7s       ← firma CMS del recibo (si hay certificado)
FZS-8f3a1c9e.acta.txt               ← acta de entrega de clave (nunca se sube)
Open_FZS.html             ← abridor para quien recibe (§11), copia genérica
```

`Open_FZS.html` es la misma página para todos los sobres: no contiene nada del
paquete y puede viajar con el `.fzs`. El acta registra su SHA-256 para que el receptor
compruebe que la copia que recibió es la original.

El sufijo son los **primeros 8 caracteres hexadecimales del `packageId`**, sin guiones.
Está prohibido derivar el nombre del número de causa, del operador o del nombre de la
evidencia: la correspondencia identificador ↔ causa vive en el registro interno del
organismo, nunca en la nube.

El nombre es una convención de ForZip, no parte del formato: un sobre renombrado sigue
siendo válido y se abre igual. Lo que sí cambia es la verificación pública, porque el
recibo declara el nombre con el que se selló (§9).

---

## 9. Recibo público (`.receipt.json`)

Es el único artefacto que puede circular en claro. Su contenido está **cerrado por
especificación**: son estos campos, ni uno más.

```json
{
  "receiptVersion": 1,
  "packageId": "8f3a1c9e-4b21-4d7e-9a03-1c5e7f2b8d64",
  "sealedFileName": "FZS-8f3a1c9e.fzs",
  "sealedSha256": "<hash del .fzs completo, hex minúscula>",
  "sealedSize": 4295098368,
  "sealedAtUtc": "2026-09-03T17:22:11Z",
  "forZipVersion": "2.0.0",
  "formatVersion": 1,
  "profile": "Reservado"
}
```

**Prohibido en el recibo, sin excepción:** nombres de archivo del contenido, rutas,
cantidad de archivos, hashes por archivo, número de causa, descripción, dependencia
judicial, nombre del operador, timestamps de los archivos originales.

`sealedSha256` es el hash **del sobre**, no del contenido. No permite ninguna comparación
contra bases de hashes de material conocido. Esa es exactamente la propiedad que se
busca.

El recibo puede venir acompañado de una firma CMS/PKCS#7 desprendida (`.p7s`) sobre la
totalidad de sus bytes, con sellado de tiempo RFC 3161 opcional. Se verifica con
herramientas estándar; por ejemplo:

```
openssl cms -verify -binary -inform DER -in FZS-8f3a1c9e.receipt.json.p7s \
            -content FZS-8f3a1c9e.receipt.json
```

---

## 10. Algoritmos

### 10.1 Apertura de un sobre

```
1.  Leer 8 bytes: si no son ASCII "FZSEAL01" -> no es un sobre ForZip.
2.  Leer uint16 LE formatVer: si es distinto de 1 -> versión no soportada.
3.  Leer uint32 LE headerLen y luego esos headerLen bytes -> headerJsonBytes.
    Conservar los bytes crudos.
4.  Parsear el JSON. Rechazar si kdf.algo != "PBKDF2-HMAC-SHA256"
    o cipher.algo != "AES-256-GCM".
5.  Comprobar que totalChunks == ceil(payloadSize / chunkSize).
6.  Comprobar que el tamaño del archivo es
    14 + headerLen + payloadSize + 16 * totalChunks.
    Si no coincide -> sobre truncado o con datos de más.
7.  headerHash = SHA-256(headerJsonBytes)
8.  key = PBKDF2-HMAC-SHA256(UTF-8(contraseña), salt, iterations, 32)
9.  para i de 0 a totalChunks-1:
        len   = min(chunkSize, payloadSize - i*chunkSize)
        ct    = leer len bytes
        tag   = leer 16 bytes
        nonce = nonceBase || uint32LE(i)
        aad   = headerHash || uint32LE(i) || (i == totalChunks-1 ? 0x01 : 0x00)
        pt    = AES-256-GCM-Descifrar(key, nonce, ct, tag, aad)
        si el tag no valida -> abortar informando el índice i
        escribir pt
10. La concatenación de los pt es el contenedor interno: un ZIP (§7).
```

Un implementador debe escribir a un archivo temporal y renombrarlo recién al terminar, o
borrarlo ante error o cancelación. Un sobre a medio abrir en disco es material a medio
descifrar sin ninguna marca de que lo está.

### 10.2 Verificación en dos niveles

La verificación de un sobre tiene dos niveles, y cuál corresponde lo decide la
disponibilidad de la contraseña.

**Nivel 1 — Público, sin contraseña.** Entrada: el `.fzs` y su `.receipt.json` (más el
`.p7s` si existe).

1. Verificar `magic` y `formatVer`.
2. Calcular el SHA-256 del `.fzs` completo y compararlo con `sealedSha256` del recibo.
3. Comparar `sealedSize` con el tamaño real del archivo.
4. Comparar el `packageId` del header del `.fzs` con el del recibo.
5. Si hay `.p7s`, verificar la firma CMS y reportar firmante y fecha; validar el token
   RFC 3161 y reportar la TSA. **Una firma inválida significa evidencia comprometida.**

Veredicto: sobre íntegro o sobre alterado, más firmante y fecha. Sin acceso al contenido
y sin necesidad de contraseña — que es lo que permite que un tercero controle la
integridad sin poder leer nada.

**Nivel 2 — Completo, con contraseña.** Todo lo anterior, y además:

6. Derivar la clave y descifrar los chunks en orden verificando cada tag GCM.
7. Comprobar que la cantidad de chunks descifrados coincide con `totalChunks` y el
   tamaño total con `payloadSize`.
8. Extraer el contenedor interno y verificar el `.manifest.json` que trae adentro:
   veredicto por archivo (OK / alterado / faltante / extra) y SHA-256 global del ZIP.

### 10.3 Cómo distinguir contraseña incorrecta de alteración

Un tag GCM que no valida significa una de dos cosas, y el formato permite separarlas por
el **patrón de fallas**:

- Si falla el chunk **0**, es decir el primero que se intenta, lo más probable es que la
  clave sea incorrecta: **contraseña incorrecta**.
- Si los primeros chunks validan y falla uno posterior, la clave es correcta y el archivo
  está **alterado en la posición N**. Hay que reportar el índice.

Es un criterio operativo, no una prueba criptográfica: una alteración en el chunk 0 con
la contraseña correcta se reporta como contraseña incorrecta. La distinción sirve para
darle al operador un mensaje útil, no para decidir nada forense — para eso está la
verificación de nivel 1, que no depende de la contraseña.

---

## 11. Implementaciones de referencia

- **ForZip 2.0.0 o posterior** — implementación completa: sella, abre y verifica en los
  dos niveles. Apache License 2.0.
- **`Tools/fzs_extract.py`** — extractor autónomo en Python 3, unas cien líneas, que solo
  usa la biblioteca estándar más `cryptography`. Hace una sola cosa: pedir la contraseña,
  descifrar el sobre y volcar el contenedor interno a disco. Sin interfaz, sin lógica
  forense, sin verificación de manifiesto.

- **`Tools/Open_FZS.html`** — abridor para quien recibe el sobre: una página HTML
  única, autocontenida y sin conexión, que corre en Microsoft Edge o Google Chrome. Usa Web
  Crypto (PBKDF2-HMAC-SHA256 y AES-256-GCM) y la escritura transaccional de archivos del
  navegador; lee y escribe por chunks, así que la memoria no crece con el tamaño del sobre.
  Produce el contenedor interno y nada más: no lo descomprime ni ejecuta su contenido. Su
  núcleo de apertura está en un bloque `<script id="fzs-nucleo">` separado de la interfaz.

El extractor **se escribió desde este documento y no desde el código C#**, y hay un test
de integración que sella con ForZip, abre con el script y compara byte a byte. Esa es la
validación cruzada: si el script y ForZip no coinciden, uno de los dos está mal y hay que
averiguar cuál. Cualquier cambio en el formato tiene que volver a pasar por ahí. El abridor
HTML pasa por la misma validación: su núcleo, tomado tal cual de la página publicada, se corre
en Node —que tiene la misma Web Crypto que el navegador— y su salida se compara byte a byte
con la de ForZip.

Se incluye además una copia del extractor **dentro del contenedor interno de cada sobre**
(§7), de modo que quien tenga el sobre y la contraseña tenga también la herramienta para
abrirlo — aunque para llegar a esa copia haga falta haberlo abierto una vez. La copia que
importa es la del repositorio y la de este documento; la del sobre es para quien hereda
un `.fzs` suelto y ya lo pudo abrir alguna vez.

---

## 12. Propiedades de seguridad y límites

**Lo que el formato garantiza:**

- Confidencialidad del contenido bajo AES-256-GCM, con clave derivada de la contraseña
  mediante 600.000 iteraciones de PBKDF2-HMAC-SHA256.
- Detección de cualquier modificación del contenido cifrado, del header o del orden de
  los bloques.
- Detección de truncamiento, por el `isFinal` autenticado (§6.2).
- Verificación de integridad **sin contraseña**, por el recibo público (§9).

**Lo que el formato no oculta ni resuelve:**

- **El tamaño del sobre revela el orden de magnitud del contenido.** No hay relleno.
- **La fecha de sellado está en claro** en el header.
- **La contraseña es el único factor.** Si se pierde, el contenido es irrecuperable; si
  se filtra, la protección cae por completo. La fortaleza de la contraseña es la
  fortaleza del sobre.
- **El formato no dice nada sobre dónde vive el sobre.** Sellar elimina la información
  visible, no la exposición institucional ni la jurisdicción aplicable de la
  infraestructura donde se lo guarde.

---

## 13. Versionado del formato

`formatVer` en el binario y `formatVersion` en el recibo se mueven juntos. La versión 1
es la descrita acá.

Un lector conforme debe **rechazar explícitamente** un `formatVer` mayor al que soporta,
con un mensaje que diga que hace falta una versión más nueva de la herramienta — nunca
intentar leerlo de todos modos.

Cambios que **no** requieren subir la versión, porque el lector los toma del header:
subir `kdf.iterations`, cambiar `cipher.chunkSize`, cambiar la longitud del salt.

Cambios que **sí** la requieren: otro algoritmo de cifrado o de derivación, otra
construcción de nonce o de AAD, otro layout binario.

---

*Especificación pública del formato `.fzs` — ForZip — Patagonia Robot*
*Copyright 2026 Claudio Andino — Licencia Apache 2.0*
