# 🔒 meshIt — Security Documentation

## Cryptographic Overview

meshIt v2 uses a layered cryptographic approach for secure peer-to-peer BLE communication.

---

## Key Hierarchy

| Layer              | Algorithm                | Purpose                                         |
| ------------------ | ------------------------ | ----------------------------------------------- |
| **Identity**       | X25519 (Curve25519)      | Static keypair for Noise handshakes             |
| **Signing**        | Ed25519                  | Packet authentication                           |
| **Transport**      | ChaCha20-Poly1305 (AEAD) | Per-session message encryption                  |
| **Key Derivation** | HKDF-SHA256              | Derive transport keys from DH shared secrets    |
| **Fingerprint**    | SHA-256                  | Identity verification (hash of public key)      |
| **Compression**    | LZ4                      | Payload compression (applied before encryption) |

---

## Noise XX Handshake

meshIt uses a simplified **Noise XX pattern** for mutual authentication:

```
Initiator                        Responder
    |                                |
    | ---- e (ephemeral pub) ------> |   Message 1
    |                                |
    | <---- e, ee, s, es ----------- |   Message 2
    |       (eph pub + encrypted     |
    |        static pub)             |
    |                                |
    | ---- s, se -----------------> |   Message 3
    |      (encrypted static pub)    |
    |                                |
    [=== Transport keys derived ===]
```

**Why XX?** Mutual authentication without pre-shared keys. Neither party needs to know the other's identity before the handshake.

After the 3-message handshake, both sides derive symmetric send/receive keys using HKDF-SHA256.

---

## Key Storage

- Private keys are encrypted with **Windows DPAPI** (Data Protection API) on disk
- Stored at `%APPDATA%\meshIt\identity.json`
- DPAPI binds encryption to the current Windows user account
- Keys cannot be used if the file is copied to another user account

---

## Transport Encryption

After a Noise handshake:

1. Each message is encrypted with **ChaCha20-Poly1305** (AEAD)
2. A monotonically increasing **nonce counter** prevents replay attacks
3. The 16-byte Poly1305 **authentication tag** ensures integrity
4. Nonces are 96-bit: 32 zero bits + 64-bit counter

---

## Backward Compatibility

Phase 2 peers detect Phase 1 peers by packet version:

- **Version 0x01**: Fall back to AES-256-CBC with pre-shared key
- **Version 0x02**: Use Noise session encryption

---

## Trust Model

| Level        | Meaning                                                               |
| ------------ | --------------------------------------------------------------------- |
| **Unknown**  | No identity verification performed                                    |
| **Verified** | Fingerprint confirmed out-of-band (QR code scan or manual comparison) |
| **Favorite** | Verified + starred by the user                                        |

---

## Fingerprints

- **Full**: SHA-256 hash of the X25519 public key (64 hex chars)
- **Short**: First 8 hex chars (displayed in UI)
- **QR Code**: Encodes `meshit://verify?fp={fingerprint}&nick={nickname}`

---

## Mesh Routing Security

- Messages are **end-to-end encrypted** — relay nodes cannot read content
- Each message carries the originator and destination public keys
- **Hop count** (max 7) prevents infinite loops
- **Seen-by list** prevents routing loops
- **Message deduplication** via GUID prevents amplification attacks

---

## Threat Model

### Protected Against

- ✅ Passive eavesdropping (ChaCha20-Poly1305 encryption)
- ✅ Message tampering (Poly1305 MAC / CRC32 integrity)
- ✅ Replay attacks (nonce counters)
- ✅ Impersonation (mutual Noise XX authentication)
- ✅ Key theft from disk (DPAPI encryption)

### Not Protected Against

- ⚠️ Traffic analysis (BLE advertisements are public)
- ⚠️ Denial of service (flooding the mesh with packets)
- ⚠️ Compromised endpoint (if malware runs on the device)
- ⚠️ Side-channel attacks on BLE hardware

---

## Libraries Used

| Library                          | Version  | Purpose                                         |
| -------------------------------- | -------- | ----------------------------------------------- |
| **NSec.Cryptography**            | 24.4.0   | X25519, Ed25519, ChaCha20-Poly1305              |
| **System.Security.Cryptography** | built-in | SHA-256, HMAC-SHA256, DPAPI, AES-256 (fallback) |
| **K4os.Compression.LZ4**         | 1.3.8    | LZ4 fast compression                            |
| **QRCoder**                      | 1.6.0    | QR code generation                              |
