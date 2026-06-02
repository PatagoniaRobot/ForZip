# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Evidence verification**: re-hashes ZIP contents against a forensic manifest and reports a per-file verdict (OK / altered / missing / extra) plus a global ZIP SHA-256 check (`VerificationService`).
- **Forensic manifest** (`.manifest.json`): machine-readable source of truth generated alongside the `.report.txt` report.
- **Digital signatures**: optional detached CMS/PKCS#7 signing of the manifest with the operator's X.509 certificate (`.p7s`); verification reports signer and signing time (`SignatureService`).
- **CLI `sign` command** to sign an existing manifest; `zip` gained `--sign-cert`/`--sign-cert-password`, plus `--operator`, `--case`, `--court`, `--lang`, `--no-sidecar`.
- **GUI signing**: the operator confirmation dialog can pick a certificate and sign the manifest; the Verify view accepts manifests and shows per-file verdict and signature status.
- Report now includes source paths and UTC modification timestamps per file.
- Parallel batch hashing (`ComputeHashesBatchAsync`) used by the CLI `hash` command.

### Changed
- **Single-pass hashing**: files are hashed during packing (one read) instead of a separate pre-hash phase, halving I/O.
- ZIP entry timestamps are now stored in UTC for reproducibility.
- Empty directories are preserved in the archive and recreated on extraction.
- Decompression is fully asynchronous and recreates directory entries.
- `LogService` is thread-safe, rotates log files, and marshals to the UI thread.

### Fixed
- **CLI `verify` was broken** (always failed): report integrity now verifies against the `.sha256` sidecar, and the CLI generates the sidecar, the global ZIP hash, and the manifest (previously GUI-only).
- Removed hardcoded operator/language in CLI report generation; warns when `--report` is used without hashes.
- Removed dead code in `ReportService` and the disabled internal self-hash.

### Security
- Added `System.Security.Cryptography.Pkcs` (8.0.1) for CMS signing.

## [1.0.0] - 2026-05-12

### Added
- **GUI application** (Avalonia UI) with dark/light theme support
- **CLI application** with commands: `zip`, `unzip`, `hash`, `verify`, `genpass`
- ZIP compression with AES-256 encryption and configurable password
- ZIP extraction with encrypted archive support
- SHA-256 hash calculation and verification for forensic integrity
- Cryptographically secure password generator
- Bilingual interface (English / Spanish) with runtime switching
- Forensic chain-of-custody oriented workflow
- Cross-platform support (Windows, Linux, macOS) via .NET 8
- 24 xUnit tests covering core services
