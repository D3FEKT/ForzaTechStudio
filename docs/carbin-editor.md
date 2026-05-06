

# Carbin Editor

The **Carbin Editor** page lets you open, inspect, edit, and rebuild `.carbin` scene files - the primary packaging format used by ForzaTech to describe a car's complete visual representation. A carbin file defines which `.modelbin` geometry files make up each named section of the car, how those sections behave at different upgrade levels, and scene-level properties such as the skeleton path, LOD flags, and build metadata.

The format is used by the game engine at runtime as `CarScene` to load and instance every visible part of the car, from the body shell and interior to swappable bumpers and spoilers.

---

## Opening a File

- **Drag and drop** a `.carbin` file directly onto the page.
- Use the **Open** button in the toolbar (Ctrl+O).
- Use the **New** button (Ctrl+N) to create a blank carbin pre-filled with Forza Horizon 5 defaults.

---

## Toolbar

| Button | Shortcut | Description |
|---|---|---|
| New | Ctrl+N | Create a blank carbin with default FH5 settings |
| Open | Ctrl+O | Browse and open an existing `.carbin` file |
| Save | Ctrl+S | Overwrite the original file with current edits |
| Save As | Ctrl+Shift+S | Write to a new file path |
| Close | Ctrl+W | Unload the current file and return to the empty state |

The loaded file name and a status message are shown in the header. A progress banner appears while the file is being parsed or saved.

---

## Page Tabs

Once a file is loaded the editor is split into three tabs:

| Tab | Description |
|---|---|
| Configuration | Scene-level metadata, version, skeleton path, LOD flags |
| Standard Parts | Non-upgradable parts and their model references |
| Upgradable Parts | Parts with per-upgrade-level model variants and shared models |

---

## Configuration Tab

### File Metadata

Displayed at the top of the tab after loading:

| Field | Description |
|---|---|
| Scene Version | Version number read from the carbin binary header |
| Model Version | Version of the embedded model blob entries |
| Series | Whether the file was detected as Horizon or Motorsport, based on scene version |

### Scene Settings

| Field | Description |
|---|---|
| Output Game Version | Dropdown selecting which game version to write when saving. Changes scene and model version numbers accordingly |
| Ordinal | Numeric identifier for this scene, used internally by the game |
| Scene Name | Short identifier string derived from the skeleton path (e.g. `CUSTOMCAR`) |
| Media Name | Asset media folder name used to build game paths (e.g. `CUSTOMCAR`) |
| Skeleton Path | Full in-game path to the `_skeleton.modelbin` file (e.g. `game:\media\cars\CUSTOMCAR\scene\_skeleton.modelbin`). This path is used by the engine to load bone data for animation and part attachment |
| Build Strict Mode | When enabled, the game will reject the carbin if any referenced asset is missing rather than silently skipping it |

### Supported Output Game Versions

| Selection | Scene Version | Model Version | Series |
|---|---|---|---|
| Forza Motorsport 5 | 5 | 14 | Motorsport |
| Forza Motorsport 6 / 7 | 5 | 17 | Motorsport |
| Forza Motorsport 2023 | 10 - 11 | 21 | Motorsport |
| Forza Horizon 2 | 5 | 15 | Horizon |
| Forza Horizon 3 / 4 | 5 | 16 | Horizon |
| Forza Horizon 5 | 6 | 18 | Horizon |

### Level of Detail (LOD) Flags

A set of checkboxes controls which LOD levels are included when the scene is built. Each flag is a single bit in a 16-bit field written to the carbin:

| Flag | Description |
|---|---|
| LODS | Shadow LOD mesh group |
| LOD0 | Highest detail level (closest camera distance) |
| LOD1 | Second detail level |
| LOD2 | Third detail level |
| LOD3 | Fourth detail level |
| LOD4 | Fifth detail level |
| LOD5 | Lowest detail level (furthest camera distance) |

Typical configuration for Forza Horizon 5 has LODS through LOD4 enabled and LOD5 disabled.

---

## Standard Parts Tab

Standard parts (called non-upgradable parts in the format) are car sections that have a fixed geometry that does not change with in-game upgrades. The entire car body, doors, trunk, interior, windows and similar components are stored here.

### Parts List (left panel)

Lists all standard parts in the file. Each entry shows the part type name and the number of models attached to it.

Toolbar buttons:

| Button | Description |
|---|---|
| Add | Add a new empty standard part defaulting to `CCarParts_CarBody` type |
| Remove | Delete the currently selected part and all its models |
| Convert | Move the selected part to the Upgradable Parts tab, adding a default stock upgrade entry |

### Part Configuration (right panel)

When a part is selected:

| Field | Description |
|---|---|
| Part Type | Dropdown selecting the `CCarPartsEnum` value for this section (see part types below) |
| Version | Internal version number for this part entry, preserved from the source file for byte-accurate round-trips |
| Bounding Box | Expandable section showing Min/Max XYZ extents for the part, used by the engine for culling |

### Part Types Reference

The following `CCarPartsEnum` values are available:

| Value | Name | Description |
|---|---|---|
| 0 | CCarParts_Engine | Engine geometry |
| 1 | CCarParts_Drivetrain | Drivetrain components |
| 2 | CCarParts_CarBody | Primary car body shell |
| 3 | CCarParts_Motor | Motor parts (electric vehicles) |
| 4 | CCarParts_Brakes | Brake geometry |
| 5 | CCarParts_SpringDamper | Suspension spring / damper parts |
| 6 | CCarParts_AntiSwayFront | Front anti-roll bar |
| 7 | CCarParts_AntiSwayRear | Rear anti-roll bar |
| 8 | CCarParts_TireCompound | Tire geometry |
| 9 | CCarParts_RearWing | Rear wing / spoiler |
| 10 | CCarParts_RimSizeFront | Front wheel rims |
| 11 | CCarParts_RimSizeRear | Rear wheel rims |
| 12 - 33 | CCarParts_Camshaft through CCarParts_Differential | Internal engine and drivetrain upgrade geometry |
| 34 | CCarParts_FrontBumper | Front bumper |
| 35 | CCarParts_RearBumper | Rear bumper |
| 36 | CCarParts_Hood | Bonnet / hood |
| 37 | CCarParts_SideSkirts | Side skirts |
| 38 | CCarParts_TireWidthFront | Front tire width geometry |
| 39 | CCarParts_TireWidthRear | Rear tire width geometry |
| 40 | CCarParts_WeightReduction | Weight reduction parts |
| 41 | CCarParts_ChassisStiffness | Chassis stiffness parts |
| 42 | CCarParts_Ballast | Ballast |
| 43 | CCarParts_MotorParts | Motor upgrade parts |
| 44 | CCarParts_WheelStyle | Wheel style geometry |
| 45 | CCarParts_Aspiration | Aspiration upgrade parts |

### Models (right panel)

When a part is selected its model list shows every `.modelbin` reference attached to it.

Each entry displays:
- The `.modelbin` file name
- The full in-game asset path (e.g. `game:\media\cars\CUSTOMCAR\scene\body_a.modelbin`)
- The AO swatchbin path when one is assigned

Toolbar buttons next to the list:

| Button | Description |
|---|---|
| Add | Browse for one or more `.modelbin` files to add to this part |
| Remove | Remove the selected model entry from the part |
| Set AO | Browse for a `.swatchbin` ambient occlusion texture to associate with the selected model |
| Move | Move the selected model to a different part in the list |

Right-click a model entry to edit its model path or AO path directly as text.

Use the **Search** box above the list to filter by file name or game path.

The **Model Version** field next to the search box shows the version number of the selected model entry and can be edited for byte-accurate round-trips.

### Material Indices

Below the model list, the **Material Indices** card shows the material name-to-index mapping for the selected model entry. These map material slot names (e.g. `body_paint`) to integer indices used by the material system.

| Button | Description |
|---|---|
| Add | Add a new blank material index row |
| Remove | Remove the selected row |
| Load | Import material names from a `.modelbin` or `.carbin` file. When loading from a carbin, a selector dialog lets you pick which model to copy from |

Each row has an editable material name field and a numeric index value.

### Transform and Bone

Below the material indices, the **Transform and Bone** card shows the world-space position and attachment settings for the selected model:

| Field | Description |
|---|---|
| Bone Name | Name of the skeleton bone this model attaches to (default is root) |
| Bone ID | Numeric bone index |
| Snap to Parent | When enabled the model snaps to the parent bone transform |
| Pos X / Y / Z | World-space position offset for the model |
| Scale | Uniform scale factor |

### Render Properties

The **Render Properties** card controls how the model is drawn:

| Field | Description |
|---|---|
| Draw Groups | Checkboxes for Exterior, Cockpit, Shadow, Hood, Windshield Reflection, Driverless Cockpit, Windshield Reflection Driverless Cockpit, and Proxy LOD. Controls which rendering passes include this model |
| Receives Impact Mask | Model receives bullet or debris impact decals |
| Receives Splatter Mask | Model receives mud / water splatter |
| Receives Damage | Model participates in deformation damage |
| Receives Dirt | Model receives dirt accumulation |
| Receives Oil | Model receives oil splatter effects |
| Receives Rubber | Model receives tyre rubber marks |
| Receives Rain | Model receives rain wetness effects |
| Is Interior Windshield | Marks the model as an interior-facing windshield surface |

### Droppable Part Settings

When a model is configured as a droppable part (a panel that detaches on collision damage):

| Field | Description |
|---|---|
| Is Droppable | Enables detachment behaviour |
| Drop Value | Threshold value controlling when the part detaches |
| Drop Part ID | Hex identifier linking this model to a physics drop part definition |
| Break Amount | Force amount required to break this part free |

---

## Upgradable Parts Tab

Upgradable parts are sections of the car that have multiple geometry variants, one per upgrade level. Examples include front and rear bumpers, hoods, and side skirts. The game selects which variant to render based on the installed upgrade level.

### Parts List (left panel)

Lists all upgradable parts. Each entry shows the part type name.

Toolbar buttons:

| Button | Description |
|---|---|
| Add | Add a new empty upgradable part defaulting to `CCarParts_FrontBumper` |
| Remove | Delete the currently selected upgradable part |
| Convert | Move the selected part back to the Standard Parts tab (upgrade IDs are cleared) |

### Upgrades

When an upgradable part is selected, the **Upgrades** list shows each upgrade level entry.

Toolbar buttons:

| Button | Description |
|---|---|
| Add | Add a new upgrade entry |
| Remove | Remove the selected upgrade entry |

Each upgrade entry has:

| Field | Description |
|---|---|
| Version | Internal version number for this upgrade entry |
| Level | The upgrade tier this entry represents (0 = stock) |
| Is Stock | Marks this entry as the default stock configuration |
| ID | Unique identifier for this upgrade, referenced by shared model entries |
| Car Body ID | Links this upgrade to a specific car body database entry |
| Parent Is Stock | Indicates the parent upgrade is stock |
| Bounding Box | Min/Max XYZ extents for this upgrade level's geometry |

### Shared Models

Below the upgrades list, the **Shared Models** section lists models that are used across multiple upgrade levels. Each shared model entry has a list of **Upgrade IDs** specifying which upgrade levels it appears with.

Toolbar buttons:

| Button | Description |
|---|---|
| Add | Browse for a `.modelbin` to add as a shared model |
| Remove | Remove the selected shared model |
| Add Upgrade ID | Add an upgrade ID reference to the selected shared model |
| Remove Upgrade ID | Remove the selected upgrade ID from the shared model |

The model detail fields (material indices, transform, bone, render properties, droppable settings) work identically to those in the Standard Parts tab.

---

## Version and Series Detection

After loading, the Configuration tab shows the detected Scene Version and Model Version read from the binary. The editor uses these to determine the correct enum encoding for part types:

- Scene version 6 indicates Forza Horizon 5
- Scene version 10 or higher indicates Forza Motorsport 2023
- Scene version 5 is shared between older Motorsport and older Horizon titles; the model version is used to narrow it further
- Part type values are encoded differently in older versions (CCarParts_EnumV1 vs CCarParts_Enum). The editor handles this automatically during both read and write

---

## Saving Changes

Use **Save** (Ctrl+S) to overwrite the original file in place, or **Save As** (Ctrl+Shift+S) to write to a new path. The Output Game Version dropdown on the Configuration tab controls which version numbers are written to the file. Changing this dropdown before saving is how you retarget a carbin to a different game.

Back up the original `.carbin` before saving. An incorrect model path, wrong version number, or a missing skeleton path can prevent the game from loading the car entirely.

---

## Converting a .carbin

To convert a `.carbin` to a different game version with full path rewriting and blob header updates, use the [Conversion Tool](conversion-tool.md) page. The Carbin Editor can change the version numbers manually via the Output Game Version dropdown but does not rewrite game paths or model blob internals. The Conversion Tool handles the complete transformation in one step.

