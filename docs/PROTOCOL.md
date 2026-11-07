# Aula F87 Pro (wired) configuration protocol

Captured from the vendor tool (`OemDrv.exe` 2.1, "AULA F87 PRO") with a `hid.dll` proxy
placed next to a copy of the tool, on firmware V2.0.0, identity `03 00 00 00 00 A9`
("F87 PRO Wired"). Every command below was observed on the wire and read back with AulaKb.

## Transport

- USB `258A:010C` (SinoWealth). Interface 1, top-level collection with usage page `0xFF00`,
  usage `0x01`, **520-byte feature report, id 6**.
- Frame (SetFeature, 520 bytes):

  | byte | meaning |
  |---|---|
  | 0 | report id `06` |
  | 1 | command |
  | 2 | sub command (layer for the matrix) |
  | 3 | `00` |
  | 4 | chunk count (`01` for everything below) |
  | 5 | chunk index |
  | 6-7 | payload length, little endian |
  | 8.. | payload |

- Read: send `command | 0x80` with the wanted length, then GetFeature (report id 6). The reply
  echoes the 8-byte header and carries the data from byte 8.
- The vendor tool retries a failed SetFeature 3 times, 70 ms apart, and spaces writes ~60-80 ms.

## Commands

| write | read | length | content |
|---|---|---|---|
| — | `82` sub 1 | 6 | identity, e.g. `03 00 00 00 00 A9` |
| `03` sub n | `83` sub n | 512 | key matrix, layer n (0 default, 1 FN1, 2 FN2, 3 Tap) |
| `04` | `84` | 128 | lighting/settings profile |
| `05` | `85` | variable | macro blob |
| `06` | `86` | 384 | self-define per-key colours |
| `08` | — | 378 | direct frame, not stored |
| `0A` | `8A` | 512 | effect colour palette |

The firmware version the vendor tool shows (V2.0.0) is the USB `bcdDevice` (`0x0200`), not a command.

## Profile (128 bytes, ends `5A A5`)

| offset | meaning |
|---|---|
| 3 | debounce stage 0..4 (UI shows 1..5, default 1) |
| 9 | 1 while Self-define is active |
| 10 | active effect |
| 22 | tap sensitivity, 0 = tap off (UI default 127) |
| 56 + 2·id | brightness 0..4 for effect `id` |
| 57 + 2·id | `speed << 4 \| colour slot`; slot 0..6 = palette colour, 7 = colourful |

Effects (`id`): 00 Off, 01 Fixed on, 02 Respire, 03 Rainbow, 04 Flash away, 05 Raindrops,
07 Ripples shining, 08 Stars twinkle, 0A Retro snake, 0B Neon stream, 0C Reaction,
0D Sine wave, 0F Rotating windmill, 10 Colorful waterfall, 11 Blossoming, 15 Self-define.
The tool hides 06 Rainbow wheel, 09 Shadow disappear, 0E Retinue scanning and
12 Rotating storm (`LedMask=0x22120` in KB.ini); whether this firmware renders them is untested.
Self-define's parameter bytes hold `07 47` and the tool offers no brightness/speed for it.

The vendor tool's Save is read-modify-write: `84` read, `04` write, then `0A` write.

## Palette (512 bytes)

7 RGB colours per effect at `21·id + 3·slot`, `5A A5` at 506. Picking a single colour in the
tool writes it to slot 0 and sets the effect's slot to 0.

## Per-key colours (384 bytes)

Planar: red `[0..125]`, green `[126..251]`, blue `[252..377]`, indexed by LED.
LED index = `column · 6 + row` (row 0 = Esc row … row 5 = Ctrl row); Esc 0, A 9, W 14, S 15,
D 21, ← 89, ↑ 94, ↓ 95, → 101. The direct frame (`08`) uses the same LED order but interleaved RGB.

## Key matrix (512 bytes per layer, ends `5A A5`)

Entry `n` (4 bytes at `4n`) belongs to LED/key index `n`.

| type | bytes | example |
|---|---|---|
| keyboard | `00 mods key1 key2` | Esc `00 00 00 29`, Ctrl+Shift+K `00 03 00 0E`, Alt+F4 `00 04 3D 00` |
| disabled | `00 00 00 00` | |
| mouse | `01 button 01 00` | button 1 left … 5 forward |
| consumer | `02 00 hi lo` | play/pause `02 00 00 CD`, calculator `02 00 01 92` |
| macro | `03 mode count index` | mode 1 = count times, 4 = while held, 2 = until any key |
| function | `07 00 00 code` | 01 Win lock, 02 keyboard lock, 04 factory reset (Fn+Esc) |
| lighting | `08 param a b` | Fn-layer light controls: param 00 effect, 02 colour, 03 brightness, 04 speed |

`mods` is the HID modifier bit mask (01 LCtrl, 02 LShift, 04 LAlt, 08 LWin, …).

### ISO keys

The vendor "wired" tables are ANSI: matrix entries 75 and 76 are empty, so on an ISO
keyboard the key left of Enter and the key right of left Shift type nothing. Found by
giving every empty entry a distinct probe key and pressing the physical keys:
**75 = ISO key left of Enter** (HID `0x32`, "," on Turkish Q), **76 = ISO key next to
left Shift** (HID `0x64`, "<" on Turkish Q). AulaKb's factory default layer fills both.

## Macros

Record: `[event count LE16][bytes after header LE16][name length][name UTF-16LE]` followed by
4 bytes per event: `[00 down / 80 up][delay hi][delay lo][HID usage]` (delay after the event, ms).
Several macros are stored back to back in one blob; the sub command is ignored by the read.
Writing two macros and reading them back works; that a key's macro `index` selects the n-th
record is inferred from the tool, not yet confirmed by pressing a key.

## Factory reset

There is no reset command. The tool's Reset writes the default matrix (4 layers), the
default profile (Neon stream) and the default palette. Per-key colours and macros are untouched.

## Not used, on purpose

Feature report id 5 (6 bytes, interface collection 5) is the SinoWealth ISP / bootloader entry
channel; the bootloader enumerates as `0603:1020`. Firmware updates go through it. AulaKb never
sends report 5, never reads the `88`/`89` code windows, and has no update path.
