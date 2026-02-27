# 🐛 meshIt — BLE Troubleshooting Guide

Common Bluetooth Low Energy issues on Windows and how to fix them.

---

## 1. "BLE adapter not found"

**Symptoms:** App shows "BLE not available" on startup.

**Fixes:**

- Open **Device Manager** → **Bluetooth** and verify an adapter is listed.
- If the adapter is missing, check that it's enabled in BIOS/UEFI.
- Try **Settings → Bluetooth & devices** and toggle Bluetooth off/on.
- Update the Bluetooth driver: right-click the adapter in Device Manager → **Update driver**.

---

## 2. Bluetooth is on but no peers are discovered

**Possible causes & fixes:**

| Cause                                    | Fix                                                                                                          |
| ---------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| BLE advertising not supported by adapter | Some older Bluetooth 4.0 adapters only support Classic Bluetooth. Check specs for BLE support.               |
| Windows Bluetooth service not running    | Open `services.msc` → find **Bluetooth Support Service** → ensure it's **Running** and set to **Automatic**. |
| App permissions                          | Go to **Settings → Privacy & security → Bluetooth** and make sure apps can access Bluetooth.                 |
| Firewall/antivirus blocking              | Temporarily disable firewall/AV to test. Add `meshIt.exe` to the allowed apps list.                          |
| Peers too far away                       | BLE range is typically 10–30 m indoors. Move devices closer.                                                 |

---

## 3. Peers discovered but connection fails

**Fixes:**

- **Restart Bluetooth**: Toggle Bluetooth off/on in Settings.
- **Restart the app** on both machines.
- **Unpair old pairings**: If the devices were previously paired via Windows Bluetooth settings, go to **Settings → Bluetooth & devices** and remove the pairing. meshIt does not use traditional pairing.
- **Check Windows version**: GATT Server APIs require Windows 10 **version 1809** (build 17763) or later. Run `winver` to check.

---

## 4. File transfer stuck or fails midway

**Fixes:**

- Ensure both devices remain within BLE range for the entire transfer.
- Avoid transferring files > 50 MB over BLE (it will be very slow due to ~247-byte MTU).
- Check **%APPDATA%\meshIt\logs\app.log** for specific error messages.
- Close other apps that may be using the Bluetooth adapter.

---

## 5. "Access Denied" or permission errors

**Fixes:**

- Run the app **as Administrator** (right-click → Run as administrator).
- Ensure the Bluetooth capability is available:
  1. Open **Settings → Privacy & security → Bluetooth**.
  2. Enable "Let desktop apps access your Bluetooth".
- On a managed (corporate) device, your IT admin may have restricted Bluetooth access via Group Policy.

---

## 6. App crashes on startup

**Fixes:**

- Ensure you have **.NET 8 Desktop Runtime** installed. Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0).
- Delete corrupted settings: remove the folder `%APPDATA%\meshIt\` and restart the app.
- Check crash logs at `%APPDATA%\meshIt\logs\app.log`.

---

## 7. Messages not persisting after restart

**Fixes:**

- Ensure `%APPDATA%\meshIt\messages.db` exists and is not corrupted.
- If corrupted, delete `messages.db` — it will be recreated on next launch (previous messages will be lost).

---

## 8. High CPU or battery drain

**Causes:**

- BLE scanning runs continuously in the background.
- **Fix:** Close the app when not in use. A future version will add a "pause scanning" option.

---

## 9. Two instances on the same PC don't see each other

> ⚠️ This is expected behavior on many BLE adapters.

Most Bluetooth adapters cannot advertise and scan to/from themselves. To properly test peer discovery and messaging, **use two separate Windows machines**.

---

## 📋 Diagnostic Steps

When reporting an issue, include:

1. **Windows version**: Run `winver` and note the full build number.
2. **Bluetooth adapter model**: Device Manager → Bluetooth → adapter name + driver version.
3. **Log file**: Attach `%APPDATA%\meshIt\logs\app.log`.
4. **Steps to reproduce**: What you did, what you expected, what happened.

---

## 🔗 Useful Links

- [Microsoft BLE GATT Documentation](https://learn.microsoft.com/en-us/windows/uwp/devices-sensors/bluetooth-low-energy-overview)
- [Windows BLE API Reference](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth)
- [.NET 8 Download](https://dotnet.microsoft.com/download/dotnet/8.0)
