# Huella de carpeta — versión 1

ForZip 2.0 muestra, para cada carpeta que se hashea entera (Hash Batch, el hash rápido del menú contextual y
`forzip hash -i <carpeta>`), dos valores:

| Valor | Qué es | Para qué sirve |
| --- | --- | --- |
| **Huella ForZip** | SHA-256 de la lista ordenada de archivos con su SHA-256 y su ruta relativa | Cambia si cambia un byte, un nombre o la ubicación de un archivo. Es la que se anota. |
| **7-Zip (datos)** | El "SHA256 for data" de `7z h -scrcSHA256` | Se coteja con 7-Zip, que tiene cualquiera. No depende de los nombres. |

Las dos se calculan a partir del SHA-256 de cada archivo, así que no hace falta volver a leer la carpeta.

## 1. Qué archivos entran

- Todos los archivos de la carpeta, recursivamente, incluidos los vacíos y los ocultos.
- **Las carpetas vacías no cuentan.** Una carpeta solo aparece a través de la ruta de sus archivos.
- **Los enlaces simbólicos y las junctions no se siguen** (igual que al empaquetar). ForZip los informa como "enlace no
  seguido"; no entran en ninguna de las dos huellas. Si la carpeta tiene enlaces, el valor de 7-Zip puede no coincidir
  con el que calcule 7-Zip, que puede seguirlos.
- Si un archivo no se pudo leer (bloqueado por el antivirus, en uso), **no hay huella**: ForZip lo dice en vez de dar un
  valor que no cubre toda la carpeta.

## 2. Huella ForZip

1. Para cada archivo, calcular su SHA-256 y escribirlo en **hexadecimal minúscula** (64 caracteres).
2. Tomar su **ruta relativa a la carpeta**, con `/` como separador y sin el nombre de la carpeta misma
   (`sub/acta.pdf`, no `caso/sub/acta.pdf`). Así, copiar o renombrar la carpeta no cambia su huella.
3. Armar una línea por archivo: `<sha256><espacio><espacio><ruta>\n` (el formato de `sha256sum`).
4. **Ordenar las líneas por los bytes UTF-8 de la ruta.** Es el orden de `sorted()` de Python sobre texto y el de
   `LC_ALL=C sort`; distingue mayúsculas (`Z` va antes que `a`).
5. La huella es el **SHA-256 de la concatenación de las líneas**, codificada en UTF-8, en hexadecimal minúscula.

Ejemplo: una carpeta con `a.txt` ("hola") y `sub/b.txt` ("chau") da la lista

```
b221d9dbb083a7f33428d7c2a3c3198ae925614d70210e28716ccaa7cd4ddb79  a.txt
2274631b81def59664f20cb9fa010e4cde57f64a263f2874dfde0fe346d59c60  sub/b.txt
```

y la huella es el SHA-256 de ese texto (con el salto de línea final):
`398320c92b527d0e43a1633bb80fae02320c298fd91ecbd1eb33e52326b0b12b`.

En un sistema con `sha256sum`, desde adentro de la carpeta:

```
find . -type f ! -type l -printf '%P\n' | LC_ALL=C sort | while IFS= read -r f; do sha256sum "$f"; done | sha256sum
```

(`sha256sum` agrega `*` delante del nombre en modo binario en algunos sistemas; la referencia es el script de la
sección 4.)

## 3. Valor de 7-Zip ("SHA256 for data")

Es lo que muestra `7z h -scrcSHA256 <carpeta>` en la línea `SHA256 for data`. Se estableció con pruebas de caja negra
contra 7-Zip 24.08, sin mirar su código fuente:

1. Leer el SHA-256 de cada archivo (los 32 bytes) como un **entero sin signo de 256 bits en little-endian**.
2. Sumarlos todos.
3. El valor es la suma **módulo 2^256**, escrita otra vez como 32 bytes little-endian en hexadecimal minúscula, seguida
   de un guion y la **cantidad de veces que la suma pasó de 2^256** (la suma dividida 2^256), en ocho dígitos
   hexadecimales: `d4953c…78da-00000000`.

Como es una suma, no depende del orden ni de los nombres: renombrar o mover un archivo dentro de la carpeta no la
cambia. Por eso la huella que se anota es la de ForZip; el valor de 7-Zip sirve para cotejar con una herramienta que
ya está instalada en todos lados.

7-Zip muestra además "SHA256 for data and names", que también depende de los nombres. ForZip no la reproduce.

## 4. Script de referencia

`Tools/huella_carpeta.py` (Python 3, sin dependencias) calcula los dos valores:

Con la carpeta del ejemplo de la sección 2:

```
python huella_carpeta.py caso
Archivos      : 2
Huella ForZip : 398320c92b527d0e43a1633bb80fae02320c298fd91ecbd1eb33e52326b0b12b
7-Zip (datos) : d4953cf731629d8a991ae47b9ec527d6c77d57989660369c504bda8a142378da-00000000
```

y `7z h -scrcSHA256 caso` muestra `SHA256 for data: d4953cf7…78da-00000000`, el mismo valor.

Los tests de ForZip lo corren y comparan sus resultados con los de ForZip, y comparan el valor de 7-Zip contra la
salida de `7z h` cuando 7-Zip está instalado.
