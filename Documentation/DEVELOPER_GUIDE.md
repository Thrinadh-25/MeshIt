# 👩‍💻 meshIt — Developer Guide

## Architecture

meshIt follows the **MVVM (Model-View-ViewModel)** pattern with WPF and .NET 8.

### Layers

```
Views (XAML)  →  ViewModels (CommunityToolkit.Mvvm)  →  Services  →  Models
                                                            ↓
                                                    BLE / SQLite / File I/O
```

### Key Technologies

| Component     | Technology                          |
| ------------- | ----------------------------------- |
| UI            | WPF (.NET 8)                        |
| MVVM          | CommunityToolkit.Mvvm               |
| Database      | SQLite via EF Core                  |
| BLE           | Windows.Devices.Bluetooth           |
| Crypto        | NSec.Cryptography                   |
| Compression   | K4os.Compression.LZ4                |
| Notifications | Microsoft.Toolkit.Uwp.Notifications |
| Audio         | NAudio                              |
| System Tray   | Hardcodet.NotifyIcon.Wpf            |
| QR Codes      | QRCoder                             |

---

## Project Structure

```
meshIt/
├── Crypto/              # Noise handshake, key derivation, signatures
├── Models/              # Data models (Peer, Message, Packet, etc.)
├── Services/            # Business logic (BLE, messaging, crypto, etc.)
├── ViewModels/          # MVVM ViewModels
├── Views/               # XAML user controls
├── Controls/            # Custom controls (RichTextParser, EmojiData)
├── Converters/          # WPF value converters
├── Helpers/             # Packet builder, chunk helper
├── Data/                # EF Core DbContext
├── Resources/
│   ├── Themes/          # Dark, Light, HighContrast dictionaries
│   └── Localization/    # en-US, es-ES, fr-FR strings
└── Documentation/       # Guides and manuals
```

---

## Building

```bash
dotnet restore
dotnet build
dotnet run
```

### Publish self-contained:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## Adding a New Theme

1. Create `Resources/Themes/MyTheme.xaml`
2. Copy the key names from `DarkTheme.xaml`
3. Set your color values
4. Add the theme name to `ThemeService.AppTheme` enum
5. Update the `ApplyTheme` switch expression

---

## Adding a New Language

1. Copy `Resources/Localization/en-US.xaml`
2. Rename to your locale (e.g., `de-DE.xaml`)
3. Translate all string values
4. Add the locale code to `LocalizationService.SupportedLanguages`

---

## Adding a New Service

1. Create `Services/MyService.cs` with your logic
2. Add it as a field in `MainViewModel`
3. Initialize it in the constructor
4. Wire up events if needed
5. Expose commands via `[RelayCommand]` attributes

---

## Packet Protocol

### v1 (Phase 1)

```
[1 byte version=0x01] [16 bytes sender GUID] [1 byte type] [payload]
```

### v2 (Phase 2+)

```
[1 byte version=0x02] [16 bytes sender GUID] [1 byte type]
[32 bytes originator pub key] [32 bytes destination pub key]
[1 byte hop count] [1 byte flags (compressed)]
[payload]
```

---

## Testing

Currently manual testing. Planned:

- xUnit for service-level unit tests
- Integration tests for Noise handshake
- UI automation with WinAppDriver
