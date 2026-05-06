# Manufacturer Colors

The **Manufacturer Colors** page lets you view and edit `ManufacturerColors.bin`, a binary file that defines the palette of factory paint colors available for a car. Each entry maps a color preview swatch to one or more material slots on the car model.

---

## What is ManufacturerColors.bin?

Every car in ForzaTech has a `ManufacturerColors.bin` file that lists the manufacturer's official color options. When a player selects a paint color in the car customization menu, the game looks up the corresponding entry in this file to know which material indices to tint and what preview color to show in the UI.

Each color entry holds:

- A **path** to the associated `.swatchbin` or material reference.
- A **material index mask** a bitmask that controls which material slots on the car receive the color tint.
- A **preview color** an RGB float3 value used to render the color chip in the color picker UI.

---

## Opening a File

- **Drag and drop** a `ManufacturerColors.bin` file onto the page.
- Use the **Open** button in the toolbar.

The app expects a file named `ManufacturerColors.bin` or `manufacturercolors.bin` (case-insensitive). Files are typically found inside the car's asset bundle.

---

## Page Layout

| Area | Description |
|---|---|
| **Left, Color Tree** | Hierarchical list of all color entries in the file |
| **Right, Entry Properties** | Editable fields for the selected color entry |

---

## Color Tree

The tree groups color entries hierarchically. Each leaf node represents a single manufacturer color option. Folder nodes group entries by category (where the file contains groupings).

Select any leaf entry to view and edit its properties in the right panel.

---

## Color Entry Properties

| Property | Description |
|---|---|
| **Path** | The asset path associated with this color (e.g. a `.swatchbin` paint texture or material name) |
| **Material Index Mask** | A bitmask that determines which material slots on the model are affected. Each set bit corresponds to one material slot index |
| **Preview Color** | The RGB swatch color shown in the game's paint picker UI. Edit using the color picker or by entering float values (0.0 - 1.0 per channel) |

### Material Index Mask
The mask is stored as a `uint32`. Setting bit `n` enables the color override for material slot index `n`. For example:

- `0x00000001` only slot 0 (typically the primary body paint)
- `0x00000003` slots 0 and 1
- `0xFFFFFFFF` all slots

---

## Adding and Removing Entries

- **Add** a new entry using the **+** button in the toolbar.
- **Delete** the selected entry using the **Delete** key or the **Remove** button.

New entries are appended to the end of the list with default values. Rename the path and set the mask and preview color before saving.

---

## Saving Changes

Click **Save** (`Ctrl + S`) to write changes to the original file, or **Save As** to write to a new path.

> Incorrect material index masks can cause wrong paint slots to be tinted or no color to appear at all. Always test changes in-game before distributing a mod.
