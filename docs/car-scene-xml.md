# Car Scene XML

The **Car Scene XML** page is a multi-format editor for the various XML data files that accompany a ForzaTech car scene. A single page handles several distinct file formats - the editor auto-detects which type has been loaded and shows the appropriate editing interface.

---

## Supported File Formats

| File / Extension | Root Element | Description |
|---|---|---|
| `ShakeBones.xml` | `<ShakeBoneSettings>` | Defines procedural bone shake noise for engine vibration and suspension movement |
| `*.xml` (locators) | `<CarLocators>` | Named attachment/reference points in the car scene |
| `CarAttributes.xml` | `<CarAttributes>` | Per-car attribute values (dimensions, mass distribution, etc.) |
| `GlobalCarAttributes.xml` | `<GlobalCarAttributes>` | Global library of attribute definitions shared across cars |
| `IKAnchorBones.xml` | `<IKAnchorBones>` | Inverse kinematics anchor bone definitions for driver animations |
| `*.avpins` | `<PointsOfInterest>` | Points of interest attached to the car (camera targets, particle spawn points, etc.) |

---

## Opening a File

- **Drag and drop** any supported XML file or `.avpins` file onto the page.
- Use the **Open** button in the toolbar.

The editor inspects the root XML element name to determine the file type automatically, then switches to the appropriate editing mode.

---

## ShakeBones Mode

**File:** `game:\Media\Cars\_library\ShakeBones.xml`

Shake bones define procedural noise that is applied to specific skeleton bones to simulate engine vibration, chassis flex, and suspension travel. Each entry specifies noise parameters along the XYZ axes (positional) and Yaw/Pitch/Roll (rotational).

The editor shows each bone entry with its:

- **Bone Name** - which skeleton bone receives the noise.
- **NoiseXYZ** fields - positional noise amplitude per axis (up to 5 noise layers per axis, `ShakeBoneNoiseXYZ_0` through `_4`).
- **NoiseYPR** fields - rotational noise amplitude per axis (up to 5 layers, `ShakeBoneNoiseYPR_0` through `_4`).

---

## Locators Mode

**Files:** car-specific locator XMLs

Locators are named world-space attachment points within the car scene. They are referenced by game systems to know where to spawn exhaust particles, attach tow hooks, position cameras, and more.

Common locator names include:

| Locator | Purpose |
|---|---|
| `carLocator_Exhaust_000` | Primary exhaust particle origin |
| `carLocator_exhaustL` / `carLocator_exhaustR` | Left/right exhaust pipes |
| `carLocator_steeringWheel` | Steering wheel bone origin |
| `carLocator_seatCamLF` / `carLocator_seatCamRF` | In-car camera positions |
| `carLocator_bumperF` | Front bumper reference point |
| `carLocator_hood` | Bonnet/hood reference point |
| `carLocator_engineSmoke` | Engine smoke spawn position |

Each locator entry has a **name**, a **position** (XYZ), and an optional **rotation**.

---

## Car Attributes Mode

**Files:** per-car `CarAttributes.xml`

Car attributes are scalar values that describe physical and gameplay properties of a specific car. These include values for dimensions, weight distribution, suspension geometry, and more. The editor shows all attributes as a flat key/value list. Values can be edited inline.

---

## Global Car Attributes Mode

**Files:** `GlobalCarAttributes.xml` (shared library)

Defines the full schema of all possible car attribute names and their default values. This is a library file - edits here affect the fallback values for any car that does not override a particular attribute in its own `CarAttributes.xml`.

---

## IK Anchor Bones Mode

**File:** `game:\Media\Cars\_library\IKAnchorBones.xml`

Inverse Kinematics (IK) anchor bones define the rest-pose reference points that the driver animation IK solver uses. Each entry maps a named IK target (hand grip, foot pedal, etc.) to a specific skeleton bone name and offset.

---

## AvPins Mode

**Files:** `*.avpins`

AvPins (Points of Interest) are named 3D positions attached to the car that game systems subscribe to. They are used for effects, camera targets, audio emitter positions, and scripted events. Each pin has a **name** and a **world-space transform**.

---

## Saving Changes

Use **Save** (`Ctrl + S`) to write changes back to the original file. The file is written as plain text XML regardless of whether it was originally a compiled or text format.

Use **Save As** to write to a new path.

---

## Raw XML Tab

All modes include a **Raw XML** tab showing the full document as editable text. Use **Refresh** to update the text from the current in-memory state, edit freely, then click **Apply** to parse your changes back into the structured view.
