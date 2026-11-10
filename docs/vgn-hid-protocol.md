# VGN HUB HID protocol notes: F1 SE mouse and V98Pro V4 keyboard

This document records the transport and battery protocols derived from the public
VGN HUB web driver and validated against physically connected devices. It is
intentionally limited to the read paths used by VGN Orb.

- Public app: <https://drive.vgnlab.com.cn/>
- Inspected bundles (2026-11-10): `/assets/index-CeliRb9v.js`,
  `/assets/vendor-sparklink-DdtyOIlG.js`,
  `/assets/vendor-bytech-Glbys9o2.js`
- Relevant mouse code paths: WebHID device list, `ca` mouse service,
  `sendCommand`, `readHidBuffer`, `getDeviceBattery`, `deviceBatteryLink`
- Relevant keyboard code paths: `Qs` BYTECH wrapper, `battery-change`,
  `getBatteryStatus`, `VgnKeyboardProtocol` equivalent

No vendor JavaScript is copied into this repository. This file is a behavioral
description and a regression record for the C# adapter.

## Transport

VGN HUB uses **WebHID** (`navigator.hid`), not WebUSB or WebSerial.

## VGN F1 SE mouse

For the VGN F1 SE family the useful top-level collection is:

| Field | Value |
| --- | --- |
| Vendor ID | `0x3554` (13652) |
| Wired F1 SE product IDs | `0xFB51` (64337) and `0xF511` (62737) |
| 2.4G receiver product ID | `0xFB50` (64336) |
| Usage page | `0xFF02` (65282) |
| Usage | `0x0002` (2) |
| Report ID | `0x08` |
| Report size | 17 bytes including report ID |
| Output report size | 17 bytes including report ID |

The wired `0xFB51` and receiver `0xFB50` paths were both physically validated;
the receiver path reported `100%` while active. `0xF511` appears in the official
web driver's device table with the same `vgn-f1-pm` handler and report ID but was
not physically available here.

On Windows the validated collections were:

```text
wired:     HID\VID_3554&PID_FB51&MI_01&COL05
receiver:  HID\VID_3554&PID_FB50&MI_01&COL05
```

The device identified itself as:

```text
Product:  VGN DragonFly F1 SE
Mfr:      Compx
Firmware: 313
```

## Frame format

A 17-byte HID report has this layout:

| Offset | Meaning |
| --- | --- |
| 0 | Report ID, always `0x08` |
| 1 | Command |
| 2 | Status in responses; `0x00` means success |
| 3-4 | Reserved (zero) |
| 5 | Data length / mode |
| 6-15 | Command data, zero padded |
| 16 | Checksum |

The checksum is chosen so that **all 17 bytes sum to `0x55` modulo 256**:

```text
checksum = (0x55 - sum(bytes[0..15])) & 0xFF
```

The same checksum rule is used by requests and responses.

## Commands used

| Command | Name | Purpose |
| --- | --- | --- |
| `0x03` | `DeviceOnLine` | Check whether the mouse is online |
| `0x04` | `BatteryLevel` | Read battery level, charging flag, voltage |
| `0x12` | `ReadVersionID` | Read firmware version |

The public web driver also contains encryption, driver-status, flash, pairing and
configuration commands. VGN Orb deliberately does **not** implement those.

## Request examples

Online query:

```text
08 03 00 00 00 00 00 00 00 00 00 00 00 00 00 00 4A
```

Battery query:

```text
08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49
```

Firmware query:

```text
08 12 00 00 00 00 00 00 00 00 00 00 00 00 00 00 3B
```

## Response examples

### Online

```text
08 03 00 00 00 01 01 9D 85 19 00 00 00 00 00 00 0D
```

- Byte 6 = `0x01` -> online.
- Bytes 7-9 = device address `9D 85 19`.

### Battery

Captured from the real mouse:

```text
08 04 00 00 00 02 64 01 10 56 00 00 00 00 00 00 7C
```

- Byte 6 = `0x64` -> **100%**
- Byte 7 = `0x01` -> **charging**
- Bytes 8-9 = `0x1056` -> **4182 mV** (big-endian)
- Byte 16 = `0x7C` -> checksum; all 17 bytes sum to `0x155`, i.e. `0x55` modulo 256

VGN Orb displays the firmware-reported percentage directly and also exposes the
reported voltage. VGN HUB additionally applies a voltage lookup and cached
hysteresis algorithm; that cosmetic smoothing is not reproduced here.

### Firmware

```text
08 12 00 00 00 02 03 13 00 00 00 00 00 00 00 00 23
```

VGN HUB formats this as `"3" + "13"` -> `313`.

## Read sequence

1. Enumerate the vendor HID collection `0xFF02 / 0x0002` with a 17-byte input
   and output report.
2. Open the collection and listen for input report ID `0x08`.
3. Send `DeviceOnLine`.
4. Read the online response and verify byte 6 is `1`.
5. Send `BatteryLevel`.
6. Read the response, validate the checksum, and parse bytes 6-9.
7. Optionally send `ReadVersionID` and parse bytes 6-7.

The C# implementation is in
[`VgnJmProtocol.cs`](../src/VGNOrb.Devices.Vgn/VgnJmProtocol.cs) and
[`VgnHidDevice.cs`](../src/VGNOrb.Devices.Vgn/VgnHidDevice.cs).

## VGN V98Pro V4 keyboard

The keyboard path in the public web driver is handled by the BYTECH raw HID SDK.
The VGN Orb implementation is an independent C# reimplementation of the verified
read commands.

### HID collection

| Field | Value |
| --- | --- |
| Vendor ID | `0x391D` (14621) |
| Verified wired PID | `0x200B` (8203) |
| Verified receiver PID | `0x2A03` (10755) |
| Other V98Pro V4 PIDs in web table | `0x201C`, `0x2A0A` |
| Usage page | `0xFF60` (65376) |
| Usage | `0x0061` (97) |
| Report ID | `0x09` |
| Report size | 64 bytes including report ID |

On Windows the validated collections were:

```text
wired:     HID\VID_391D&PID_200B&MI_03&COL01
receiver:  HID\VID_391D&PID_2A03&MI_03
```

The device identified itself as:

```text
Product:  VGN V98Pro V4 8K
Mfr:      BY
Firmware: 23.2.0
```

### Frame format

A 64-byte raw HID report contains report ID `0x09` followed by a 63-byte
BYTECH packet:

| Raw offset | Meaning |
| --- | --- |
| 0 | Report ID, `0x09` |
| 1 | Command |
| 2 | Command parameter |
| 3 | Reserved (zero) |
| 4 | Total packet count (1 for these queries) |
| 5 | Current packet index (0) |
| 6 | Data length |
| 7-62 | Command data, zero padded |
| 63 | Checksum |

The checksum is seeded with the report ID:

```text
checksum = (255 - ((0x09 + sum(packet[0..61])) % 256)) & 0xFF
```

It is stored at packet offset 62 / raw offset 63.

### Commands

| Command | Parameter | Purpose |
| --- | --- | --- |
| `0x87` (135) | `0` | Battery level and charging flags |
| `0x82` (130) | `2` | Firmware version |

Battery query (raw 64-byte report):

```text
09 87 00 00 01 00 00 00 ... 00 6E
```

Captured battery response:

```text
09 87 00 00 01 00 02 64 11 00 ... 00 F7
```

- Raw byte 7 = `0x64` -> **100%**
- Raw byte 8 = `0x11` -> charging = `(flags & 0xF0) >> 4 != 0`,
  full = `(flags & 0x0F) != 0`
- The keyboard battery response does not carry a voltage field.

Firmware query and response:

```text
TX: 09 82 02 00 01 00 02 00 00 00 ... 6F
RX: 09 82 02 00 01 00 02 20 17 00 ... 38
```

VGN HUB formats the response bytes as `23.2.0`.

### Read sequence

1. Enumerate the vendor HID collection `0xFF60 / 0x0061` with 64-byte input
   and output reports.
2. Open the collection and listen for input report ID `0x09`.
3. Send `0x87`, read the response and validate the seeded checksum.
4. Parse raw bytes 7-8 for level and charging flags.
5. Optionally send `0x82 / 0x02` and parse raw bytes 7-8 for the firmware.

The C# implementation is in
[`VgnKeyboardProtocol.cs`](../src/VGNOrb.Devices.Vgn/VgnKeyboardProtocol.cs) and
[`VgnKeyboardDevice.cs`](../src/VGNOrb.Devices.Vgn/VgnKeyboardDevice.cs).

## Deliberately unsupported

- No parameter or firmware writes on real hardware. The web driver's write
  commands have not been independently validated.
- No bootloader or pairing protocol.
- Keyboard support is limited to the V98Pro V4 family PIDs listed above; the
  wired `0x200B` and receiver `0x2A03` PIDs were physically validated.
- F1 SE support covers the verified wired `0xFB51` and receiver `0xFB50` PIDs.

The simulator remains available and is used automatically when no supported
hardware is found.
