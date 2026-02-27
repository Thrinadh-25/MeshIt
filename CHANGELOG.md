# 📋 meshIt — Changelog

## [3.0.0] — 2026-02-27

### 🎨 UI/UX Polish

- **Theme system** — Dark, Light, High Contrast themes with live switching
- **Rich text messages** — Support for **bold**, _italic_, `code` formatting
- **Emoji picker** — Categorized emoji data (Smileys, Gestures, Hearts, Objects, Symbols)
- **Drag-and-drop file sending** — Drop files anywhere on the window
- **Multi-language support** — English, Spanish, French localization

### 🔔 System Integration

- **Toast notifications** — Windows 10/11 toast notifications for messages and peers
- **Voice messages** — Record and play back 16kHz mono audio via NAudio

### 🔒 Security Enhancements

- **Screen lock** — PIN-based lock with idle timeout
- **Emergency wipe** — Triple-tap identity header to wipe all data
- **Encrypted backup** — AES-256 encrypted export/import with PBKDF2 key derivation

### 📊 Diagnostics

- **Network health dashboard** — Active peers, messages sent/received, mesh stats
- **Signal history** — RSSI readings with visual indicator
- **Crash reporter** — Automatic crash logging to %APPDATA%\meshIt\crashes\

### ⚡ Performance

- **Enhanced MainViewModel** — Unified Phase 1+2+3 service orchestration
- **Idle lock timer** — Background timer for screen lock timeout

### 📝 Documentation

- Added `USER_MANUAL.md`
- Added `DEVELOPER_GUIDE.md`
- Added `SECURITY.md`
- Updated `README.md` for v3

---

## [2.0.0] — 2026-02-27

### 🔐 Security (Phase 2)

- **Noise Protocol XX** — Mutual authentication with ChaCha20-Poly1305 transport encryption
- **Cryptographic identity** — X25519 + Ed25519 keypairs, SHA-256 fingerprints
- **QR code verification** — Generate and scan verification QR codes
- **Trust system** — Unknown / Verified / Favorite peer trust levels
- **DPAPI key protection** — Private keys encrypted at rest

### 🌐 Mesh Networking

- **Multi-hop routing** — Flood routing, max 7 hops, deduplication, loop prevention
- **Store-and-forward** — Queue messages for offline peers (7-day expiry)

### 📢 Channels

- **IRC-style channels** — `/join`, `/leave`, `/who` commands
- Group messaging across the mesh

### 🗜️ Compression

- **LZ4 compression** — Automatic for messages > 100 bytes

### 🔄 Backward Compatibility

- Phase 1 peers fall back to AES-256-CBC encryption
- Packet v2 format with routing headers

---

## [1.0.0] — 2026-02-26

### 🚀 Core Features (Phase 1)

- BLE auto-discovery of nearby peers
- Direct peer-to-peer messaging
- File sharing with chunked transfer and progress tracking
- Message persistence in SQLite
- Dark mode WPF UI
- AES-256-CBC message encryption with pre-shared key
