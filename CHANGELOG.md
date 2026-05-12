# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
