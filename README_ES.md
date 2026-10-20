# ForZip — Herramienta Forense de Empaquetado y Verificación

**ForZip** es una utilidad forense de código abierto diseñada para el empaquetado seguro de evidencia digital. A diferencia de los compresores convencionales, ForZip implementa un flujo de trabajo estrictamente forense que garantiza la integridad y trazabilidad de los datos desde el momento de su captura hasta su análisis posterior.

## Características Principales

*   **Hashing en un Solo Pase**: Calcula los hashes criptográficos de los archivos originales *durante* la compresión (una sola lectura de disco), documentando el estado exacto de la evidencia en su origen sin duplicar el I/O.
*   **Seguridad Avanzada**: Implementa cifrado de grado militar **AES-256** para proteger el contenido de los contenedores de evidencia.
*   **Soporte de Grandes Volúmenes**: Utiliza tecnología **Zip64** de forma nativa, permitiendo gestionar contenedores de evidencia que superan los 4GB, ideal para imágenes de disco y grandes bases de datos.
*   **Informes y Manifiesto Forenses**: Cada paquete lleva un **manifiesto `.manifest.json`** legible por máquina (metadatos del operador, sistema, parámetros y hashes individuales, con rutas de origen y marcas temporales), fuente de verdad para la verificación automática. El informe legible en texto se arma a pedido desde el manifiesto, al empaquetar o después de verificar, con el veredicto.
*   **Verificación de Evidencia**: Re-hashea el contenido del ZIP contra su manifiesto y emite un veredicto **archivo por archivo** (OK / alterado / faltante / añadido), además de verificar el hash global del contenedor.
*   **Firma Digital**: Firma opcional del manifiesto (CMS/PKCS#7) con el certificado X.509 del operador, haciendo evidente cualquier manipulación. Verificable de forma independiente (p. ej. con OpenSSL).
*   **Interfaz Moderna y Dinámica**: Diseñada con una estética oscura premium, optimizada para largos periodos de trabajo y con soporte completo para cambio de idioma (Español/Inglés) en tiempo real.

## Módulos del Software

1.  **Empaquetar (Compress)**: El núcleo del sistema. Permite agregar archivos y carpetas, seleccionar algoritmos de hash (MD5, SHA-1, SHA-256, SHA-512), definir el nivel de compresión y aplicar cifrado.
2.  **Extraer (Extract)**: Descompresión segura de archivos ZIP, con soporte para contenedores cifrados y registro detallado de eventos en la bitácora. Desde el menú contextual, *Extraer aquí*, *Extraer a subcarpeta* y *Extraer…* abren su propia ventana (estilo 7-Zip) sin levantar la aplicación completa.
3.  **Hash Batch**: Herramienta de cálculo masivo de hashes para archivos en disco sin necesidad de empaquetarlos, ideal para inventarios rápidos de evidencia. Desde el menú contextual del Explorador, la opción *Calcular hash* abre en cambio una ventana liviana e independiente (estilo 7-Zip) con nombre, ruta, tamaño, fecha y hashes, lista para copiar al portapapeles o exportar como informe `.txt`, sin levantar la aplicación completa.
4.  **Verificar**: Motor de auditoría que re-hashea un contenedor ZIP contra su manifiesto, validando además la firma digital cuando existe. Desde el menú contextual, *Verificar* abre su propia ventana con el veredicto, el detalle y el informe a pedido.
5.  **Generador de Contraseñas**: Utilidad integrada para crear contraseñas de alta entropía adecuadas para la protección de evidencia sensible. La longitud es configurable (mínimo 8 caracteres) y los caracteres visualmente ambiguos (`0 O o I l 1 |`) se excluyen siempre, para que la contraseña se pueda transcribir o dictar sin errores.

## Guía Rápida de Uso

1.  **Para empaquetar evidencia**:
    *   Arrastre los archivos a la zona de "Empaquetar".
    *   Seleccione los algoritmos de hash deseados.
    *   Defina la ruta del archivo de salida.
    *   Presione "Empaquetar". El hasheado y la compresión ocurren en un solo pase (0-100%).
    *   Al finalizar, complete los datos del operador para generar el manifiesto (opcionalmente, fírmelo con su certificado). Anote la huella del paquete en la planilla de cadena de custodia; *Guardar informe…* escribe el informe en texto si lo necesita.

2.  **Para verificar integridad**:
    *   Vaya a la pestaña "Verificar".
    *   Cargue el `.manifest.json` o el paquete (re-hashea la evidencia del ZIP). Los informes `.report.txt` de versiones anteriores se siguen verificando contra su `.sha256`.
    *   El sistema emite un veredicto archivo por archivo y, si el manifiesto está firmado, valida la firma digital.

## Perfil Reservado y sobre sellado `.fzs` (desde 2.0.0)

Un ZIP cifrado protege el contenido, pero no el envoltorio: el nombre del archivo y el manifiesto siguen a la vista. Cuando el paquete se sube a almacenamiento en nube de terceros, eso alcanza para que un indexador conozca la causa, la cantidad de archivos y los nombres de la evidencia — y para que un cliente de sincronización descomprima por su cuenta lo que encuentre.

El **perfil Reservado** encierra el paquete terminado —ZIP, manifiesto y firma— en un **sobre sellado `.fzs`**: un contenedor cifrado con AES-256-GCM cuya clave se deriva de la contraseña con PBKDF2-HMAC-SHA256 a 600.000 iteraciones. El sobre no revela nombres, ni cantidad de archivos, ni causa, ni operador. Solo su tamaño y la fecha de sellado.

Del sellado salen tres piezas con destinos distintos:

*   **`FZS-xxxxxxxx.fzs`** — el sobre. Es lo único que se sube a la nube.
*   **`FZS-xxxxxxxx.receipt.json`** — el recibo público, que va al expediente. Declara el hash del *sobre*, no del contenido, así que permite verificar que nadie lo alteró **sin necesidad de la contraseña** y sin dar acceso a nada.
*   **El acta de entrega de clave** — para el acto presencial en que la contraseña se entrega bajo recibo y firma. Nunca se sube.

La verificación tiene dos niveles y ForZip resuelve solo cuál corresponde: sin contraseña emite el veredicto público (sobre íntegro o alterado, más firmante y fecha); con la contraseña agrega el veredicto archivo por archivo de siempre.

### El formato es abierto

La seguridad del sobre reside en la contraseña, nunca en el secreto del formato. Por eso el `.fzs` está publicado, no escondido:

*   **[`Documentation/Formato_FZS_v1.md`](Documentation/Formato_FZS_v1.md)** — la especificación completa: layout binario, parámetros del KDF, construcción del nonce y del AAD, y el algoritmo de verificación, al nivel de detalle necesario para escribir un extractor propio sin leer el código C#.
*   **[`Tools/fzs_extract.py`](Tools/fzs_extract.py)** — un extractor autónomo de unas cien líneas de Python 3, que solo necesita la biblioteca estándar más `cryptography`. Descifra el sobre y vuelca su contenido: nada más. Se escribió desde la especificación y no desde el código, y un test de integración comprueba en cada cambio del formato que ambos coinciden byte a byte. Viaja además una copia dentro de cada sobre.

Eso es lo que neutraliza el riesgo que un formato propio introduce: dentro de diez años, alguien con Python abre la evidencia aunque ForZip ya no corra.

### Custodia del binario que selló

El informe declara con qué versión de ForZip se generó cada paquete. Falta el otro lado, y es procedimiento del organismo, no del programa: **archivar el ejecutable exacto de cada versión usada en producción, junto con su SHA-256, en el registro interno del organismo** — no en la nube. Es la práctica de laboratorio de trabajar con versiones validadas y congeladas, y es lo que permite reproducir una verificación años después con la misma herramienta que la produjo. Sin eso, la especificación publicada y el extractor quedan a mitad de camino.

### Lo que esta versión no resuelve

El operador tiene derecho a conocer los límites de la herramienta:

*   **El tamaño del sobre revela el orden de magnitud del contenido.** No hay relleno.
*   **Sigue existiendo una copia de la evidencia en infraestructura de un tercero.** Sellar elimina la información visible, no la exposición institucional ni la jurisdicción aplicable. La decisión de subir material sensible a nube de terceros debe estar respaldada por protocolo escrito del organismo, no por criterio individual del operador.
*   **La contraseña es el único factor.** Si se pierde, el contenido es irrecuperable; si se filtra, la protección cae por completo. La entrega presencial bajo recibo y firma es parte del sistema de seguridad, no un trámite administrativo.
*   **Los ejecutables siguen sin firma Authenticode.** Pendiente desde 1.3.0.

## Descarga

Los binarios están en la [página de publicaciones](https://github.com/PatagoniaRobot/ForZip/releases/latest): `ForZip.exe` para la interfaz gráfica y `ForZip.Cli.exe` para la línea de comandos. Son autocontenidos: no hay instalador, no hacen falta permisos de administrador y no se escribe nada en el registro salvo que active la integración con el menú contextual.

Cada publicación incluye un archivo `.sha256` junto a cada ejecutable. **Verifique lo que descargó** antes de usarlo sobre evidencia:

```
certutil -hashfile ForZip.exe SHA256
```

Compare el resultado con el contenido de `ForZip.exe.sha256`.

> Los ejecutables **todavía no están firmados con un certificado Authenticode**, así que Windows va a mostrar la pantalla *"Windows protegió su PC"* con *"Editor desconocido"* la primera vez. Hay que ir a *Más información → Ejecutar de todas formas*. La verificación del SHA-256 es lo que realmente acredita que el archivo es el publicado. La firma de código está prevista.

Desde la versión 1.3.0, ForZip puede avisarle si hay una versión nueva desde **Acerca de → Buscar actualizaciones**, y desde la 1.4.0 puede instalarla: siempre a pedido suyo y solo después de verificar la huella publicada de lo que descargó.

## Requisitos del Sistema

*   **SO**: Windows 10/11 (x64). La interfaz gráfica usa el registro de Windows para la integración opcional con el menú contextual, por lo que no corre en Linux ni macOS.
*   **Runtime**: .NET 10 (incluido en la versión self-contained)

---
© 2026 Patagonia Robot — Tecnología Forense Avanzada  
Desarrollado por Claudio Andino  
Licencia: Apache License 2.0
