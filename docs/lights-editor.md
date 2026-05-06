# Lights Editor

The **Lights Editor** page lets you view and edit the two lighting data files that together define a car's complete lighting setup in ForzaTech: `lights.bin` and `LightPresets.bin`. Both files can be loaded independently or together, and each has its own editing tab.

---

## Overview of the Two Files

| File | Purpose |
|---|---|
| `lights.bin` | Defines each individual light source on the car - its world-space position, rotation, which model it is attached to, and which preset it references |
| `LightPresets.bin` | A shared library of light preset definitions - each preset stores the full visual parameters for a light (colour, intensity, type, glow, cone angle, shadows, etc.) |

A light entry in `lights.bin` names a preset from `LightPresets.bin` via its **PresetName** field. This separation means many different cars can share the same preset library, and changing a preset affects every car that uses it.

---

## Opening Files

- **Drag and drop** `lights.bin`, `LightPresets.bin`, or a `.zip` archive containing both onto the page.
- Files can also be loaded via the **Open** button in the toolbar.
- When a `.zip` is dropped, the tool automatically extracts and loads both files if present.

The page shows the **Lights** tab and **Presets** tab independently. Each tab becomes active as soon as its respective file is loaded.

---

## Page Layout

| Area | Description |
|---|---|
| **Tab bar** | Switches between the Lights tab (lights.bin) and the Presets tab (LightPresets.bin) |
| **Lights tab** | Model attachment list on the left, light entries in the centre, light properties on the right |
| **Presets tab** | Preset list on the left, parameter table and editors on the right |
| **Status bar** | Shows the loaded file path, version info, and light/preset counts |

---

## Lights Tab (lights.bin)

### File Format

`lights.bin` is a little-endian binary file. Its layout depends on the version stored in the header:

| Version | Header | Notes |
|---|---|---|
| 0 | No magic or version field - file starts directly with the attachment count | Oldest format, found in early Horizon titles |
| 1 | `0xDEADBEEF` magic + version uint32 | Adds LOD override entries after the light list |
| 2 | `0xDEADBEEF` magic + version uint32 | Adds a `PresetName` string field per light entry |
| 3 | `0xDEADBEEF` magic + version uint32 | Adds a 16-byte GUID per light entry |

The binary structure written per light entry is:

```
4 bytes  - ID (uint32)
4 bytes  - Flags bitmask (uint32)
4 bytes  - AttachmentIndex (uint32)
16 bytes - Position XMFLOAT4 (x, y, z, w)
16 bytes - Rotation quaternion XMFLOAT4 (x, y, z, w)
16 bytes - DamagePosition XMFLOAT4
16 bytes - DamageRotation XMFLOAT4
[v2+] variable - PresetName (uint32 length + ASCII chars, no null terminator)
[v3+] 16 bytes - GUID (Data1:uint32, Data2:uint16, Data3:uint16, Data4:byte[8])
```

### Attachment List (Left Panel)

The left panel lists all **model attachments** in the file. An attachment is a reference to a `.modelbin` asset that one or more lights are parented to. Each attachment stores:

- **Full Path** - the in-game asset path to the modelbin.
- **Bone Index** - the skeleton bone index within that modelbin that the attachment is anchored to.

### Light Entries (Centre Panel)

| Column | Description |
|---|---|
| **Index** | Zero-based position of this light in the file |
| **ID (Hex)** | The light's unique 32-bit identifier (`0xXXXXXXXX`) |
| **Name** | Resolved friendly name from the hash mapper (if known) |
| **Flags** | Active flag names in abbreviated form |
| **Attachment** | The model this light is attached to |

### Light Properties (Right Panel)

#### Identity

- **ID / ID Hex** - editable as decimal or hex. The hash mapper resolves this to a human-readable name if found in the database.
- **Attachment Index** - which model attachment this light is parented to.
- **Preset Name** - the name of the `LightPresets.bin` preset this light references. Use **Jump to Preset** to navigate directly to that preset in the Presets tab.
- **V3 GUID** - 16-byte GUID present in version-3 files.

#### Transform - Normal State

- **Position (X, Y, Z)** - position of the light relative to its attachment bone.
- **Rotation (X, Y, Z, W)** - quaternion orientation of the light.

#### Transform - Damage State

- **Damage Position / Damage Rotation** - override transform used when the car has taken damage. Allows lights to shift position to match physically deformed geometry.

#### Flags

| Flag | Bit | Description |
|---|---|---|
| `Exterior` | 0x01 | Light is visible from outside the car |
| `Cockpit` | 0x02 | Light is visible in the cockpit/interior view |
| `Casts Shadows` | 0x04 | Light contributes to shadow maps |
| `Hood` | 0x08 | Light is associated with the bonnet/hood area |
| `Windshield Reflection` | 0x10 | Light produces a reflection visible through the windshield |
| `Driverless Cockpit` | 0x20 | Light is active in the driverless cockpit camera mode |
| `Windshield Reflection (Driverless)` | 0x40 | Windshield reflection in driverless cockpit mode |
| `Proxy LOD` | 0x80 | Light uses a proxy/LOD representation at distance |

The raw flags hex value is editable directly alongside the individual checkboxes.

### LOD Overrides

Version-1 and later files include a **LOD override** section after the light list. Each override entry contains a LOD index, an array ID, and a full Position/Rotation/DamagePosition/DamageRotation transform set. These allow light positions to be adjusted per LOD level to match the simplified geometry used at distance.

### ID Source Selection

Use the **ID Source** selector to choose which hash name dictionary is used for resolving light IDs:

- **FH3 / FH4** - Forza Horizon 3 and 4 light name database.
- **FH5** - Forza Horizon 5 / Forza Motorsport (2023) light name database.

---

## Presets Tab (LightPresets.bin)

### File Format

`LightPresets.bin` uses the **LDFB** format (`LDFB` FourCC = `0x4246444C` little-endian). The 16-byte header contains:

| Field | Size | Description |
|---|---|---|
| FourCC | 4 bytes | `LDFB` magic identifier |
| Size | 4 bytes | Total file size in bytes |
| Version | 4 bytes | Format version (1 = V1, 2 = V2) |
| Preset Count | 4 bytes | Number of presets in the file |

There are two version variants:

**V1 (FH3 / FH4):** Preset entries follow the header directly. Presets are referenced by position index only.

**V2 (FH5 / FM2023):** A table of `uint32` preset ID hashes (one per preset) is written between the header and the preset data. Each preset can be referenced by its unique ID hash.

Each **preset entry** starts with an 8-byte block (`PresetSize` uint32 + `ParameterCount` uint32), followed by `ParameterCount` parameter tuples in the wire format: `{ uint8 id, uint8 length, byte[length] value }`.

### Preset List

| Column | Description |
|---|---|
| **Index** | Zero-based position in the file |
| **ID Hex** | Hash ID (V2 only; V1 shows `V1`) |
| **Display Name** | Derived from the `LightProjectedTexture` parameter if present; otherwise `Preset [index]` or `Preset 0xXXXXXXXX` |

### Parameter Reference

#### Light Parameters

| ID | Name | Type | Description |
|---|---|---|---|
| 0x00 | LightEnabled | Bool | Whether the light source is active |
| 0x01 | LightType | UInt32 | Light shape: `Spot` (1), `PointAmbient` (2), `SpotSimple` (3) |
| 0x02 | LightColour | Vec3 | RGB emission colour (0.0 to 1.0 per channel) |
| 0x03 | LightRange | Float | Maximum illumination radius in world units |
| 0x04 | LightIntensity | Float | Brightness scalar multiplier |
| 0x05 | LightPenumbraAngle | Float | Outer soft edge angle for spot lights (degrees) |
| 0x06 | LightConeAngle | Float | Inner hard cone angle for spot lights (degrees) |
| 0x07 | LightAttenuationProfile | UInt32 | Distance falloff curve index |
| 0x08 | LightActivationThreshold | Float | Minimum intensity level before the light deactivates |
| 0x09 | LightActivationTransition | Float | Blend time for activation/deactivation changes |
| 0x0A | LightShadowType | UInt32 | Shadow mode: `ShadowOnly` (3), `DynamicNoShadow` (4) |
| 0x0B | LightProjectedTexture | String | Path to a projected texture (IES profile or gobo pattern) |
| 0x45 | LightCinematicsOnly | Bool | Light only active during cinematic sequences |
| 0x46 | LightNonCinematics | Bool | Light disabled during cinematic sequences |

#### Glow / Lens Flare Parameters

| ID | Name | Type | Description |
|---|---|---|---|
| 0x0C | GlowEnabled | Bool | Enables the glow/lens flare effect for this preset |
| 0x0D | GlowSize | Float | Base size of the glow billboard element |
| 0x10 | GlowBrightness | Float | Overall glow brightness multiplier |
| 0x11 | GlowColour | Vec3 | RGB tint of the glow element |
| 0x12 | GlowPrimaryBrightness | Float | Brightness of the primary glow layer |
| 0x13 - 0x14 | GlowPrimaryFalloff* | Float | Start and end distances for primary glow falloff |
| 0x15 | GlowSecondaryBrightness | Float | Brightness of the secondary glow layer |
| 0x16 - 0x17 | GlowSecondaryFalloff* | Float | Start and end distances for secondary glow falloff |
| 0x18 | GlowOcclusionFalloffPower | Float | Power curve for occlusion-based glow fading |
| 0x19 - 0x1A | GlowActivation* | Float | Activation threshold and transition time for the glow |
| 0x1B - 0x25 | GlowGlow* | various | Sub-element: main glow halo shape parameters |
| 0x26 - 0x2E | GlowFlare* | various | Sub-element: lens flare shape and brightness parameters |
| 0x2F - 0x38 | GlowStreak* | various | Sub-element: streak/ray shape parameters |
| 0x39 - 0x3C | GlowOcclusion* | various | Occlusion cone and radius parameters |
| 0x3D - 0x3F | GlowDistanceFade* | various | Distance-based fade start, end, and amount |
| 0x47 | GlowCubeMapOnly | Bool | Glow only visible in cube map captures |
| 0x48 | GlowFlareRotationSpeed | Float | Rotation speed of the animated flare element |

### Editing Parameters

Click any parameter row to edit it. The input control adapts to the parameter type:

- **Bool** - checkbox toggle.
- **Float** - numeric spinner (4 decimal places).
- **UInt32** - numeric field; `LightType` and `LightShadowType` show a named dropdown with their known enum values.
- **Vec3 (colour)** - three float fields plus a colour picker swatch.
- **String** - text input for the asset path.

Parameters with `length = 0` display `<default>` - the game uses its own built-in default for that parameter.

---

## Saving Changes

### lights.bin
Use **Save Lights** on the Lights tab to write back to the original file, or **Save Lights As** to choose a new path.

### LightPresets.bin
Use **Save Presets** on the Presets tab to write back to the original file, or **Save Presets As** to choose a new path.

### Saving to ZIP
If both files were loaded from a `.zip`, use **Save to ZIP** to write both updated files back into the archive at once, preserving all other archive contents.

> Always back up original files before saving. Incorrect flag values, broken transform data, or malformed preset parameters can prevent the game's lighting system from loading the car.

---

## Hash Mapper

The **LightHashMapper** service resolves raw 32-bit light IDs to human-readable names using a bundled JSON dictionary. Unknown IDs display as their raw hex value. Custom hash mappings can be added by extending the dictionary file in the app's data folder.

---

## Tips

- Use the **Filter** box above the light list to search by name, hash, or flag value.
- **Jump to Preset** on any light entry switches directly to the Presets tab and selects that preset - useful for quickly checking what colour and intensity a light has without manually browsing the preset list.
- When both files are loaded from the same car's bundle, each light's preset name resolves to its display name from the loaded `LightPresets.bin`.

