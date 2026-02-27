# 📡 meshIt — BLE Mesh Messaging v3

A Windows desktop app for **peer-to-peer messaging and file sharing** over Bluetooth Low Energy. No internet. No manual pairing. End-to-end encrypted with Noise Protocol.

---

## ✨ Features

### Phase 1 — Core

🔵 Auto-discovery • Direct P2P messaging • File sharing with progress • SQLite persistence • Dark UI

### Phase 2 — Security & Mesh

🔐 Noise XX protocol • 🌐 Multi-hop routing (7 hops) • 🆔 Cryptographic identity • 📱 QR verification • ⭐ Trust system • 📦 Store-and-forward • 📢 IRC channels • 🗜️ LZ4 compression

### Phase 3 — Production Polish

🎨 Themes (Dark/Light/HighContrast) • 🔔 Toast notifications • 🎤 Voice messages • 🔒 Screen lock • 🗑️ Emergency wipe • 📊 Network diagnostics • 💾 Encrypted backup • 🌍 Multi-language (EN/ES/FR) • 📁 Drag-and-drop files • **bold**/`code` formatting

---

## 📋 Requirements

| Requirement | Version         |
| ----------- | --------------- |
| Windows     | 10 / 11 (1809+) |
| .NET SDK    | 8.0+            |
| Bluetooth   | BLE 4.0+        |

---

## 🚀 Quick Start

```bash
dotnet restore
dotnet build
dotnet run
```

Or use Visual Studio 2022 → **F5**

---

## 🏗️ Architecture

```
meshIt/
├── Crypto/              # Noise, HKDF, Ed25519
├── Models/              # Peer, Message, Packet v2, NoiseSession, Channel
├── Services/            # 15+ services (BLE, messaging, crypto, themes, etc.)
├── ViewModels/          # MVVM with CommunityToolkit.Mvvm
├── Views/               # XAML views (Chat, Peers, Files, Settings, Diagnostics, Lock)
├── Controls/            # RichTextParser, EmojiData
├── Resources/Themes/    # Dark, Light, HighContrast
├── Resources/Localization/ # en-US, es-ES, fr-FR
├── Documentation/       # User Manual, Developer Guide
├── Scripts/             # build.bat, publish.bat
├── SECURITY.md
├── CHANGELOG.md
└── README.md
```

---

## 📖 Documentation

| Document                                            | Description                             |
| --------------------------------------------------- | --------------------------------------- |
| [SECURITY.md](SECURITY.md)                          | Cryptography, threat model, key storage |
| [CHANGELOG.md](CHANGELOG.md)                        | Version history (v1 → v3)               |
| [User Manual](Documentation/USER_MANUAL.md)         | How to use meshIt                       |
| [Developer Guide](Documentation/DEVELOPER_GUIDE.md) | Architecture & contribution             |
| [Troubleshooting](TROUBLESHOOTING.md)               | Common issues                           |

---

## 🔨 Publish

```bash
# Self-contained single file (x64):
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Or run `Scripts\publish.bat`
