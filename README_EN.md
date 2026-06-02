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
3.  **Hash Batch**: Mass hash calculation tool for files on disk without needing to pack them, ideal for quick evidence inventories.
4.  **Verify**: Audit engine that checks a report's integrity (against its `.sha256`) or re-hashes a ZIP container against its manifest, also validating the digital signature when present.
5.  **Password Generator**: Integrated utility to create high-entropy passwords suitable for protecting sensitive evidence.

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

## System Requirements

*   **OS**: Windows 10/11 (x64)
*   **Runtime**: .NET 8.0 (included in the self-contained version)

---
© 2026 Patagonia Robot — Advanced Forensic Technology  
Developed by Claudio Andino  
License: Apache License 2.0
