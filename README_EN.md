# ForZip — Forensic Packing and Verification Tool

**ForZip** is an open-source forensic utility designed for the secure packing of digital evidence. Unlike conventional compression tools, ForZip implements a strict forensic workflow that ensures data integrity and traceability from the moment of capture through subsequent analysis.

## Key Features

*   **Dual-Phase Processing**: Computes cryptographic hashes of original files *before* starting compression, documenting the exact state of the evidence at its source.
*   **Advanced Security**: Implements military-grade **AES-256** encryption to protect the contents of evidence containers.
*   **Large Volume Support**: Natively uses **Zip64** technology, allowing the management of evidence containers exceeding 4GB, ideal for disk images and large databases.
*   **Automatic Forensic Reports**: Generates detailed reports in `.report.txt` format, including operator metadata, system details, operation parameters, and individual hashes for every processed file.
*   **Integrity Verification**: Allows validating existing evidence containers by comparing current files against previously generated forensic reports.
*   **Modern and Dynamic Interface**: Designed with a premium dark aesthetic, optimized for long work sessions and featuring full support for real-time language switching (Spanish/English).

## Software Modules

1.  **Pack (Compress)**: The system's core. Allows adding files and folders, selecting hash algorithms (MD5, SHA-1, SHA-256, SHA-512), defining compression levels, and applying encryption.
2.  **Extract**: Secure decompression of ZIP files, with support for encrypted containers and detailed event logging.
3.  **Hash Batch**: Mass hash calculation tool for files on disk without needing to pack them, ideal for quick evidence inventories.
4.  **Verify**: Audit engine that loads ForZip reports and verifies that evidence on disk has not been altered.
5.  **Password Generator**: Integrated utility to create high-entropy passwords suitable for protecting sensitive evidence.

## Quick Start Guide

1.  **To pack evidence**:
    *   Drag files into the "Pack" zone.
    *   Select the desired hash algorithms.
    *   Define the output file path.
    *   Press "Pack". The system will first perform hashing (0-50% progress) and then compression (50-100%).
    *   Upon completion, fill in the operator details to generate the forensic report.

2.  **To verify integrity**:
    *   Go to the "Verify" tab.
    *   Load the `.report.txt` report file.
    *   The system will locate the referenced files and validate their hashes, issuing an integrity verdict.

## System Requirements

*   **OS**: Windows 10/11 (x64)
*   **Runtime**: .NET 8.0 (included in the self-contained version)

---
© 2026 Patagonia Robot — Advanced Forensic Technology  
Developed by Claudio Andino  
License: Apache License 2.0
