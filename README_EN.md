# ForZip — Forensic Packing and Verification Tool

**ForZip** is an open-source forensic utility designed for the secure packing of digital evidence. Unlike conventional compression tools, ForZip implements a strict forensic workflow that ensures data integrity and traceability from the moment of capture through subsequent analysis.

## Key Features

*   **Single-Pass Hashing**: Computes cryptographic hashes of original files *during* compression (a single disk read), documenting the exact state of the evidence at its source without duplicating I/O.
*   **Advanced Security**: Implements military-grade **AES-256** encryption to protect the contents of evidence containers.
*   **Large Volume Support**: Natively uses **Zip64** technology, allowing the management of evidence containers exceeding 4GB, ideal for disk images and large databases.
*   **Forensic Report and Manifest**: Generates a machine-readable **`.manifest.json`** with every package (operator metadata, system details, operation parameters, and per-file hashes with source paths and timestamps) and a machine-readable **`.manifest.json`** that serves as the source of truth for automated verification.
*   **Evidence Verification**: Re-hashes the ZIP contents against its manifest and issues a **per-file verdict** (OK / altered / missing / extra), plus verification of the container's global hash.
*   **Digital Signature**: Optional CMS/PKCS#7 signing of the manifest with the operator's X.509 certificate, making tampering evident. Independently verifiable (e.g. with OpenSSL).
*   **Modern and Dynamic Interface**: Designed with a premium dark aesthetic, optimized for long work sessions and featuring full support for real-time language switching (Spanish/English).

## Software Modules

1.  **Pack (Compress)**: The system's core. Allows adding files and folders, selecting hash algorithms (MD5, SHA-1, SHA-256, SHA-512), defining compression levels, and applying encryption.
2.  **Extract**: Secure decompression of ZIP files, with support for encrypted containers and detailed event logging. From the context menu, *Extract here*, *Extract to subfolder* and *Extract…* open their own window (7-Zip style) without launching the full application.
3.  **Hash Batch**: Mass hash calculation tool for files on disk without needing to pack them, ideal for quick evidence inventories. From the Explorer context menu, *Calculate hash* instead opens a lightweight standalone window (7-Zip style) with name, path, size, timestamp and hashes, ready to copy to the clipboard or export as a `.txt` report, without launching the full application.
4.  **Verify**: Audit engine that re-hashes a ZIP container against its manifest, also validating the digital signature when present. From the context menu, *Verify* opens its own window with the verdict, the details and the on-demand report.
5.  **Password Generator**: Integrated utility to create high-entropy passwords suitable for protecting sensitive evidence. Length is configurable (minimum 8 characters) and visually ambiguous characters (`0 O o I l 1 |`) are always excluded, so the password can be transcribed or dictated without errors.

## Quick Start Guide

1.  **To pack evidence**:
    *   Drag files into the "Pack" zone.
    *   Select the desired hash algorithms.
    *   Define the output file path.
    *   Press "Pack". Hashing and compression happen in a single pass (0-100%).
    *   Upon completion, fill in the operator details to generate the manifest (optionally, sign it with your certificate). Write the package fingerprint down in your chain of custody record; *Save report…* writes the text report if you need it.

2.  **To verify integrity**:
    *   Go to the "Verify" tab.
    *   Load the `.manifest.json` or the package itself (re-hashes the ZIP evidence). `.report.txt` reports from earlier versions are still checked against their `.sha256`.
    *   The system issues a per-file verdict and, if the manifest is signed, validates the digital signature.

## Reserved profile and sealed envelope `.fzs` (since 2.0.0)

An encrypted ZIP protects the content but not the wrapper: the file name and the manifest are still in plain sight. Once the package is uploaded to third-party cloud storage, that is enough for an indexer to learn the case number, the number of files and the names of the evidence — and enough for a sync client to unpack whatever it finds on its own.

The **Reserved profile** encloses the finished package —ZIP, manifest and signature— in a **sealed `.fzs` envelope**: a container encrypted with AES-256-GCM whose key is derived from the password with PBKDF2-HMAC-SHA256 at 600,000 iterations. The envelope reveals no file names, no file count, no case number, no operator. Only its size and the date it was sealed.

Sealing produces three pieces with three different destinations:

*   **`FZS-xxxxxxxx.fzs`** — the envelope. This is the only thing that goes to the cloud.
*   **`FZS-xxxxxxxx.receipt.json`** — the public receipt, for the case file. It declares the hash of the *envelope*, not of the content, so it lets anyone verify that the envelope was not altered **without needing the password** and without granting access to anything.
*   **The key handover record** — for the in-person act in which the password is handed over against a signed receipt. It is never uploaded.

Verification has two levels and ForZip picks the right one by itself: without a password it issues the public verdict (envelope intact or altered, plus signer and date); with the password it adds the usual per-file verdict.

### The format is open

The security of the envelope rests on the password, never on the secrecy of the format. That is why `.fzs` is published rather than hidden:

*   **[`Documentation/Formato_FZS_v1.md`](Documentation/Formato_FZS_v1.md)** — the full specification: binary layout, KDF parameters, nonce and AAD construction, and the verification algorithm, at the level of detail needed to write your own extractor without reading the C# code. (Written in Spanish.)
*   **[`Tools/fzs_extract.py`](Tools/fzs_extract.py)** — a standalone extractor of about a hundred lines of Python 3, needing only the standard library plus `cryptography`. It decrypts the envelope and dumps its contents: nothing else. It was written from the specification and not from the code, and an integration test checks on every format change that the two agree byte for byte. A copy also travels inside every envelope.

That is what neutralizes the risk a proprietary format introduces: ten years from now, somebody with Python opens the evidence even if ForZip no longer runs.

### Custody of the binary that sealed it

The report declares which version of ForZip produced each package. The other half is the organization's procedure, not the program's: **archive the exact executable of every version used in production, together with its SHA-256, in the organization's internal registry** — not in the cloud. This is the laboratory practice of working with validated, frozen tool versions, and it is what allows a verification to be reproduced years later with the same tool that produced it. Without it, the published specification and the extractor only get you halfway.

### What this version does not solve

The operator is entitled to know the limits of the tool:

*   **The size of the envelope reveals the order of magnitude of the content.** There is no padding.
*   **A copy of the evidence still exists on a third party's infrastructure.** Sealing removes the visible information, not the institutional exposure nor the applicable jurisdiction. The decision to upload sensitive material to third-party cloud storage should rest on a written protocol of the organization, not on an individual operator's judgment.
*   **The password is the only factor.** If it is lost, the content is unrecoverable; if it leaks, the protection is gone entirely. In-person handover against a signed receipt is part of the security system, not administrative paperwork.
*   **The executables are still unsigned (no Authenticode).** Pending since 1.3.0.

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
*   **Runtime**: .NET 10 (included in the self-contained version)

---
© 2026 Patagonia Robot — Advanced Forensic Technology  
Developed by Claudio Andino  
License: Apache License 2.0
