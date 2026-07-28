# ForZip — Forensic Packing and Verification Tool

**ForZip** is an open-source forensic utility designed for the secure packing of digital evidence. Unlike conventional compression tools, ForZip implements a strict forensic workflow that ensures data integrity and traceability from the moment of capture through subsequent analysis.

## Key Features

*   **Single-Pass Hashing**: Computes cryptographic hashes of original files *during* compression (a single disk read), documenting the exact state of the evidence at its source without duplicating I/O.
*   **Advanced Security**: Implements military-grade **AES-256** encryption to protect the contents of evidence containers.
*   **Large Volume Support**: Natively uses **Zip64** technology, allowing the management of evidence containers exceeding 4GB, ideal for disk images and large databases.
*   **Forensic Report and Manifest**: Generates a human-readable `.report.txt` (operator metadata, system details, operation parameters, and per-file hashes with source paths and timestamps) and a machine-readable **`.manifest.json`** that serves as the source of truth for automated verification.
*   **Evidence Verification**: Re-hashes the ZIP contents against its manifest and issues a **per-file verdict** (OK / altered / missing / extra), plus verification of the container's global hash.
*   **Digital Signature**: Optional CMS/PKCS#7 signing of the manifest with the operator's X.509 certificate, making tampering evident. Independently verifiable (e.g. with OpenSSL).
*   **Modern and Dynamic Interface**: Designed with a premium dark aesthetic, optimized for long work sessions and featuring full support for real-time language switching (Spanish/English).

## Software Modules

1.  **Pack (Compress)**: The system's core. Allows adding files and folders, selecting hash algorithms (MD5, SHA-1, SHA-256, SHA-512), defining compression levels, and applying encryption.
2.  **Extract**: Secure decompression of ZIP files, with support for encrypted containers and detailed event logging.
3.  **Hash Batch**: Mass hash calculation tool for files on disk without needing to pack them, ideal for quick evidence inventories. From the Explorer context menu, *Calculate hash* instead opens a lightweight standalone window (7-Zip style) with name, path, size, timestamp and hashes, ready to copy to the clipboard or export as a `.txt` report, without launching the full application.
4.  **Verify**: Audit engine that checks a report's integrity (against its `.sha256`) or re-hashes a ZIP container against its manifest, also validating the digital signature when present.
5.  **Password Generator**: Integrated utility to create high-entropy passwords suitable for protecting sensitive evidence. Length is configurable (minimum 8 characters) and visually ambiguous characters (`0 O o I l 1 |`) are always excluded, so the password can be transcribed or dictated without errors.

## Quick Start Guide

1.  **To pack evidence**:
    *   Drag files into the "Pack" zone.
    *   Select the desired hash algorithms.
    *   Define the output file path.
    *   Press "Pack". Hashing and compression happen in a single pass (0-100%).
    *   Upon completion, fill in the operator details to generate the forensic report (optionally, sign the manifest with your certificate).

2.  **To verify integrity**:
    *   Go to the "Verify" tab.
    *   Load the `.report.txt` report (verifies its `.sha256`) or the `.manifest.json` (re-hashes the ZIP evidence).
    *   The system issues a per-file verdict and, if the manifest is signed, validates the digital signature.

## Download

Binaries live on the [releases page](https://github.com/PatagoniaRobot/ForZip/releases/latest): `ForZip.exe` for the graphical interface and `ForZip.Cli.exe` for the command line. They are self-contained: no installer, no administrator rights, and nothing written to the registry unless you enable the context-menu integration.

Every release ships a `.sha256` file next to each executable. **Verify what you downloaded** before running it against evidence:

```
certutil -hashfile ForZip.exe SHA256
```

Compare the result with the contents of `ForZip.exe.sha256`.

> The executables are **not yet signed with an Authenticode certificate**, so Windows will show a *"Windows protected your PC"* screen with *"Unknown publisher"* the first time. Click *More info → Run anyway*. Verifying the SHA-256 is what actually proves the file is the published one. Code signing is planned.

From version 1.3.0, ForZip can tell you when a newer version exists via **About → Check for updates**, and from 1.4.0 it can install it: always at your request, and only after verifying the published checksum of the download.

## System Requirements

*   **OS**: Windows 10/11 (x64). The graphical interface uses the Windows registry for the optional context-menu integration, so it does not run on Linux or macOS.
*   **Runtime**: .NET 8.0 (included in the self-contained version)

---
© 2026 Patagonia Robot — Advanced Forensic Technology  
Developed by Claudio Andino  
License: Apache License 2.0
