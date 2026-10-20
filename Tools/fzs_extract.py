#!/usr/bin/env python3
# -*- coding: utf-8 -*-
#
# Copyright 2026 Claudio Andino
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# ---------------------------------------------------------------------------
# fzs_extract.py - extractor autónomo de sobres sellados ForZip (.fzs)
#
# Hace una sola cosa: pedir la contraseña, descifrar el sobre y volcar el
# contenedor interno (un ZIP) a disco. Sin interfaz, sin lógica forense, sin
# verificación de manifiesto: para eso está ForZip.
#
# Existe para la continuidad. Si dentro de diez años ForZip ya no corre, esto
# —cien líneas de Python— alcanza para recuperar la evidencia.
#
# Escrito desde Documentation/Formato_FZS_v1.md, NO desde el código C# de
# ForZip: es la validación cruzada del formato. Si el script y ForZip no
# coinciden, uno de los dos está mal.
#
# Requisitos: Python 3.7+ y el paquete `cryptography`  (pip install cryptography)
#             El resto es biblioteca estándar.
#
# Uso:  python fzs_extract.py <sobre.fzs> [salida.zip]
#       La contraseña se pide por consola con el eco apagado. Si la entrada
#       estándar está redirigida, se lee de ahí (para uso automatizado).
#
# Códigos de salida:  0 abierto  |  1 uso o formato  |  2 contraseña incorrecta
#                     3 sobre alterado
# ---------------------------------------------------------------------------

import base64
import getpass
import hashlib
import json
import os
import struct
import sys

from cryptography.hazmat.primitives.ciphers.aead import AESGCM

MAGIC = b"FZSEAL01"
FORMAT_VERSION = 1
TAG_LEN = 16
MAX_ITERACIONES = 10000000  # Formato_FZS_v1 §3: un header con más se rechaza sin derivar


def _b64(texto):
    """Decodifica un campo Base64 del header."""
    return base64.b64decode(texto)


class FzsError(Exception):
    """Error de formato o de apertura, con su código de salida."""

    def __init__(self, mensaje, codigo=1):
        super().__init__(mensaje)
        self.codigo = codigo


def leer_header(f):
    """Lee la cabecera fija y devuelve (bytes crudos del JSON, JSON parseado)."""
    fijo = f.read(14)
    if len(fijo) != 14 or fijo[:8] != MAGIC:
        raise FzsError("No es un sobre ForZip válido (falta el marcador FZSEAL01).")
    version, header_len = struct.unpack("<HI", fijo[8:14])
    if version != FORMAT_VERSION:
        raise FzsError(
            "Versión de formato %d no soportada: este extractor lee la versión %d."
            % (version, FORMAT_VERSION)
        )
    crudo = f.read(header_len)
    if len(crudo) != header_len:
        raise FzsError("Header truncado: el sobre está incompleto.", 3)
    try:
        # Los bytes crudos se conservan: el AAD se calcula sobre ellos, nunca
        # sobre una reserialización del JSON.
        return crudo, json.loads(crudo.decode("utf-8"))
    except (UnicodeDecodeError, ValueError):
        raise FzsError("Header JSON malformado.")


def validar(header, crudo_len, tamano_archivo):
    """Comprueba los invariantes que el header declara sobre sí mismo."""
    if header["kdf"]["algo"] != "PBKDF2-HMAC-SHA256":
        raise FzsError("Derivación de clave no soportada: %s" % header["kdf"]["algo"])
    if header["cipher"]["algo"] != "AES-256-GCM":
        raise FzsError("Cifrado no soportado: %s" % header["cipher"]["algo"])
    if not 1 <= header["kdf"]["iterations"] <= MAX_ITERACIONES:
        raise FzsError("El header declara un número de iteraciones inválido.", 3)
    tamano = header["payloadSize"]
    chunk = header["cipher"]["chunkSize"]
    total = header["totalChunks"]
    if total != (tamano + chunk - 1) // chunk:
        raise FzsError("totalChunks no concuerda con payloadSize y chunkSize.", 3)
    esperado = 14 + crudo_len + tamano + TAG_LEN * total
    if tamano_archivo != esperado:
        raise FzsError(
            "Tamaño inesperado: el sobre debería medir %d bytes y mide %d "
            "(truncado o con datos de más)." % (esperado, tamano_archivo),
            3,
        )


def descifrar(f, salida, header, crudo, clave):
    """Descifra los chunks en orden y los escribe en `salida`."""
    aesgcm = AESGCM(clave)
    header_hash = hashlib.sha256(crudo).digest()
    base = _b64(header["cipher"]["nonceBaseB64"])
    tamano = header["payloadSize"]
    chunk = header["cipher"]["chunkSize"]
    total = header["totalChunks"]

    for i in range(total):
        largo = min(chunk, tamano - i * chunk)
        bloque = f.read(largo + TAG_LEN)
        if len(bloque) != largo + TAG_LEN:
            raise FzsError("Sobre truncado en el chunk %d." % i, 3)
        indice = struct.pack("<I", i)
        # isFinal sale de totalChunks, que está autenticado; nunca del fin de
        # archivo. Deducirlo del EOF volvería indetectable el truncamiento.
        final = b"\x01" if i == total - 1 else b"\x00"
        try:
            salida.write(
                aesgcm.decrypt(base + indice, bloque, header_hash + indice + final)
            )
        except Exception:
            if i == 0:
                raise FzsError("Contraseña incorrecta, o sobre alterado desde el inicio.", 2)
            raise FzsError("Sobre alterado: falla la autenticación del chunk %d." % i, 3)


def pedir_clave():
    """Pide la contraseña con el eco apagado; si la entrada está redirigida, la lee de ahí."""
    if sys.stdin is not None and sys.stdin.isatty():
        return getpass.getpass("Contraseña: ")
    return sys.stdin.readline().rstrip("\r\n")


def main(argv):
    if not 2 <= len(argv) <= 3:
        print("Uso: python fzs_extract.py <sobre.fzs> [salida.zip]", file=sys.stderr)
        return 1
    origen = argv[1]
    destino = argv[2] if len(argv) == 3 else os.path.splitext(origen)[0] + ".zip"
    if not os.path.isfile(origen):
        print("No se encontró el archivo: %s" % origen, file=sys.stderr)
        return 1

    parcial = destino + ".partial"
    try:
        with open(origen, "rb") as f:
            crudo, header = leer_header(f)
            validar(header, len(crudo), os.path.getsize(origen))
            print("Sobre %s, sellado el %s." % (header["packageId"], header["sealedAtUtc"]))
            clave = hashlib.pbkdf2_hmac(
                "sha256",
                pedir_clave().encode("utf-8"),
                _b64(header["kdf"]["saltB64"]),
                header["kdf"]["iterations"],
                32,
            )
            with open(parcial, "wb") as salida:
                descifrar(f, salida, header, crudo, clave)
        os.replace(parcial, destino)
    except FzsError as error:
        # Un sobre a medio abrir es material a medio descifrar sin ninguna
        # marca de que lo está: no se deja nunca en disco.
        _borrar(parcial)
        print("Error: %s" % error, file=sys.stderr)
        return error.codigo
    except KeyboardInterrupt:
        _borrar(parcial)
        print("\nCancelado.", file=sys.stderr)
        return 1

    print("Contenedor interno escrito en: %s" % destino)
    print("Es un ZIP común: ábralo con cualquier herramienta estándar.")
    return 0


def _borrar(ruta):
    try:
        os.remove(ruta)
    except OSError:
        pass


if __name__ == "__main__":
    sys.exit(main(sys.argv))
