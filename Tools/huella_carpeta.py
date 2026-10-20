#!/usr/bin/env python3
# =============================================================================
#  ForZip — Huella de carpeta (script de referencia)
#  Recalcula, sin ForZip, las dos huellas de carpeta que muestra ForZip 2.0:
#    - la huella ForZip (SHA-256 de la lista ordenada "sha256  ruta/relativa");
#    - el "SHA256 for data" de 7-Zip (7z h -scrcSHA256).
#  Especificación: Documentation/Huella_de_Carpeta_v1.md
#
#  Uso:  python huella_carpeta.py <carpeta>
#
#  Copyright (c) 2026 Claudio Andino — Licencia Apache 2.0
# =============================================================================

import hashlib
import os
import sys


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def list_files(root):
    """Archivos de la carpeta, recursivo, sin seguir enlaces ni junctions (como ForZip)."""
    for current, dirs, files in os.walk(root, followlinks=False):
        dirs[:] = [d for d in dirs if not os.path.islink(os.path.join(current, d))
                   and not is_junction(os.path.join(current, d))]
        for name in files:
            full = os.path.join(current, name)
            if not os.path.islink(full):
                yield full


def is_junction(path):
    check = getattr(os.path, "isjunction", None)  # Python 3.12+
    return bool(check and check(path))


def folder_hashes(root):
    entries = []
    for full in list_files(root):
        relative = os.path.relpath(full, root).replace(os.sep, "/")
        entries.append((relative, sha256_file(full)))

    # Huella ForZip: líneas "sha256␠␠ruta\n" ordenadas por los bytes UTF-8 de la ruta.
    entries.sort(key=lambda e: e[0].encode("utf-8"))
    listing = "".join(f"{sha}  {path}\n" for path, sha in entries)
    forzip = hashlib.sha256(listing.encode("utf-8")).hexdigest()

    # 7-Zip "SHA256 for data": suma de las huellas como enteros little-endian, módulo 2^256, y los desbordes.
    total = sum(int.from_bytes(bytes.fromhex(sha), "little") for _, sha in entries)
    low, carries = total % (1 << 256), total >> 256
    sevenzip = low.to_bytes(32, "little").hex() + "-" + format(carries, "08X")

    return len(entries), forzip, sevenzip


def main():
    if len(sys.argv) != 2 or not os.path.isdir(sys.argv[1]):
        print("Uso: python huella_carpeta.py <carpeta>")
        return 1

    count, forzip, sevenzip = folder_hashes(sys.argv[1])
    print(f"Archivos      : {count}")
    print(f"Huella ForZip : {forzip}")
    print(f"7-Zip (datos) : {sevenzip}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
