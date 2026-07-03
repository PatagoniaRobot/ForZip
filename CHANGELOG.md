# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Simple mode (guided) in the Compress view**: a toggle switches between *Simple* (drop the evidence, review, one big "Package evidence" button — ForZip enforces the recommended forensic defaults: SHA-256 per file, report, manifest and sidecar; technical options hidden) and *Advanced* (the full previous screen). The chosen mode persists in `config.json` (`SimpleMode`). AES-256 password protection remains available in both modes.
- **Result screen after packaging**: on success the Compress view shows a plain-language summary of every generated artifact (`.zip`/volumes, `.report.txt`, `.manifest.json`, `.sha256`, `.p7s`) explaining what each file is and why it must stay with the package, plus the global SHA-256 fingerprint with *Copy fingerprint*, *Open folder* (Explorer with the file selected) and *New package* actions. If the operator dialog is cancelled, the screen warns that the evidence won't be verifiable later.
- **RFC 3161 trusted timestamping** of the manifest signature: `SignatureService` can request a timestamp token from a TSA (e.g. DigiCert) and embed it in the CMS signature as the standard `id-aa-timeStampToken` unsigned attribute, making the signing date verifiable by third parties (unlike `signingTime`, which is signer-declared). Verification validates the token against the signature (`Rfc3161TimestampToken.VerifySignatureForSignerInfo`) and reports timestamp, validity and TSA identity in CLI `verify`/`sign` and in the GUI Verify view; an invalid token marks the evidence as compromised. If the TSA is unreachable the signature is still written and a specific warning (`TimestampUnavailableException`) is surfaced. CLI: `--timestamp-url` on `zip`, `-t|--timestamp-url` on `sign`. GUI: timestamp checkbox (default on) + TSA URL in the operator dialog; default TSA configurable (`TimestampServerUrl` in `config.json`). Verified end-to-end against `http://timestamp.digicert.com`.
- **Case data in the GUI operator dialog**: case number, description and court (previously CLI-only via `--case`/`--court`) now flow into the report and manifest.
- **Context-menu self-repair on startup**: if the integration is registered but points to an executable that no longer exists (moved folder, USB drive letter change), ForZip silently re-registers with its current path when the window opens. It never overwrites a registration whose executable still exists (another live copy).
- **First-run integration offer**: on a PC where ForZip was never integrated, a one-time dialog (per machine/user, tracked in `HKCU`) offers to add the context menu — one click instead of discovering Settings → Integration.
- **Verify on `.p7s` files**: right-clicking a detached signature now offers *Verify with ForZip*; the GUI resolves `x.manifest.json.p7s` to its manifest (also when dropped onto the Verify view).

### Changed
- The Compress view is fully localized (browse buttons, encryption label, tooltips, progress labels and file-picker titles were hardcoded in Spanish).

### Fixed
- **Context-menu language now follows the app language**: the Explorer menu labels are stored in the registry at registration time and were never refreshed. Now they are rewritten when the app language changes, refreshed (idempotently) on startup, and headless `--register-shell` uses the language saved in `config.json` instead of the OS auto-detected one. Only applies when the registration belongs to the current executable.
- **"Juzgado" renamed to "Dependencia judicial"** in the case form (requests may come from a prosecutor's office, not only a court): full label + example watermark in the operator dialog, short label ("Dependencia"/"Authority") in the report to preserve column alignment, and updated CLI help for `--court`.
- **Activity log now follows the UI language**: every log entry emitted by the GUI (packaging, extraction, hashing, signing, navigation, shell integration) plus the log console title/tooltips and the Verify details pane were hardcoded in Spanish and now come from the es/en string resources.
- **Windows context-menu integration (portable)**: optional right-click menu in Explorer, 7-Zip/WinRAR style, registered under `HKCU` — no installer, no admin, fully reversible. Context-sensitive cascading "ForZip" submenu: *Compress / Hash* on files & folders, *Extract here / Extract to subfolder / Extract… / Verify / Hash* on `.zip` and `.001` volumes, *Compress* on folder background. Toggled from **Settings → Integration**; also scriptable headless via `--register-shell` / `--unregister-shell` / `--shell-status`. The GUI now accepts a verb + path on launch (`compress|extract|extract-here|extract-to|hash|verify`) and opens the matching view with the file(s) loaded. Pure layout/parsing logic lives in `ForZip.Core.Shell` (tested); registry I/O is Windows-guarded in the GUI. *(The GUI project now targets `net8.0-windows` to use the built-in registry APIs.)* A fail-safe **single-instance** coordinator (mutex + named pipe) forwards a multi-file selection to the running window so they accumulate in one place instead of opening several windows.
- **GUI verification parity for split archives**: the Verify view now lists the per-volume verdict and surfaces `ContentVerificationError`, distinguishing "compromised" (tamper detected) from "verification incomplete" (e.g. content couldn't be decrypted) — matching the CLI.
- **Split archives (multi-volume)**: optionally divide the output into fixed-size volumes (`.001`, `.002`, …), 7-Zip style, via `ZipOptions.SplitSize`. Hashing and splitting happen in the same single pass: each volume's SHA-256 is computed on the fly (`SplittingWriteStream`). Extraction and verification auto-detect and reassemble the volumes transparently through a seekable concatenated stream (`ConcatenatedReadStream`); the logical archive is never materialised as a single file on disk.
- **Forensic manifest v1.1**: records `isSplit` and a per-volume list (`fileName`, `size`, `sha256`); the report `.txt` gained a volumes section. The global `zipSha256` remains the hash of the *logical* archive, so content verification is identical for single-file and split archives.
- **Per-volume verification**: `VerifyArchiveAsync` issues an independent verdict for each volume (OK / altered / missing). This runs before content verification and is preserved even if the payload cannot be decompressed/decrypted (`ArchiveVerificationResult.ContentVerificationError`), so a damaged or tampered volume is still pinpointed when the password is missing.
- **CLI `--split <size>`** with suffix parsing (`700M`, `4096MB`, `1.5G`, `100K`); `unzip`/`verify` accept the `.001` volume directly. **GUI**: a "split into volumes" toggle with preset/custom sizes in the compress view; the extract view accepts `.001` files.

## [1.1.0] - 2026-06-01

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
