# 📖 meshIt — User Manual

## Getting Started

### First Launch

1. Run **meshIt.exe** or `dotnet run`
2. Enter your nickname — this identifies you on the mesh
3. The app generates a unique cryptographic identity (X25519 + Ed25519 keypairs)
4. BLE scanning starts automatically

### Finding Peers

- Nearby devices running meshIt appear in the **Peers** sidebar
- Green dot = online, gray = offline
- Signal strength is shown as a bar

### Sending Messages

1. Click a peer in the sidebar
2. Type your message in the text box
3. Press **Enter** or click **Send**
4. Messages support **markdown**: `**bold**`, `*italic*`, `` `code` ``

### Sending Files

- Click the **📎** button and select a file
- Or **drag and drop** files onto the window
- Progress is shown in the right panel

### Voice Messages

- Click **🎤** in the status bar to start recording
- Click again to stop and send
- Audio is recorded as 16kHz mono WAV

---

## Channels

IRC-style group messaging:

- `/join #channel` — join or create a channel
- `/leave #channel` — leave a channel
- `/who #channel` — list members
- Type normally while in a channel to broadcast to all members

---

## Security Features

### Identity Verification

- Click your identity header (top-left) to view your fingerprint and QR code
- Compare fingerprints out-of-band or scan QR codes to verify peers
- Verified peers show a ✅ badge

### Screen Lock

- Go to **⚙️ Settings** → **Screen Lock**
- Set a 4–8 digit PIN
- The app locks after the configured idle timeout

### Emergency Wipe

- **Triple-tap the identity header** within 2 seconds → triggers data wipe
- Confirmation dialog before proceeding
- Deletes all messages, settings, and keys

---

## Themes & Languages

### Switching Themes

Settings → Theme → Choose **Dark**, **Light**, or **High Contrast**

### Switching Languages

Settings → Language → Choose **English**, **Spanish**, or **French**

---

## Network Diagnostics

Click **📊** to view:

- Active peer count
- Messages sent/received
- Mesh hops relayed
- Signal strength history
- Noise sessions active

---

## Backup & Restore

### Export

Click **💾** in the toolbar → saves an encrypted `.meshit-backup` file

### Import

Settings → Backup → Import from file

---

## Troubleshooting

| Issue                   | Solution                                                 |
| ----------------------- | -------------------------------------------------------- |
| No peers found          | Ensure Bluetooth is enabled and BLE 4.0+ adapter present |
| Messages not sending    | Check that the peer is online (green dot)                |
| App won't start         | Delete `%APPDATA%\meshIt` and restart                    |
| DLL locked during build | Close all running instances, delete `obj/bin` folders    |
