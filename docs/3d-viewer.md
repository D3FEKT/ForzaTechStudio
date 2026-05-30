# 3D Viewer

The **3D Viewer** page provides an interactive viewport for previewing ForzaTech model assets and allowing modification to it's transforms allowing repositioning, rescaling and rotation of parts.
It uses [HelixToolkit WinUI](https://github.com/helix-toolkit/helix-toolkit) with SharpDX for hardware-accelerated rendering.

---

## Opening Files

- **Drag and drop** one or more supported files directly onto the viewport.
- Use the **Open** button in the toolbar to browse and select files (Ctrl+O).
- Multiple files can be loaded at the same time. Each file appears as its own node in the scene tree.

### Supported File Types

| Extension | Description |
|---|---|
| `.modelbin` | ForzaTech model binary containing meshes, materials and skeleton data |
| `.gr2` | Granny binary file containing skeleton and/or animation data. When loaded, sibling `.gsf` files in the same folder are auto-discovered and loaded alongside it |
| `.bin` | Binary data file. `Lights.bin` files are detected automatically by magic number and display light transform data. `physicsdefinition.bin` loads physics collision geometry |
| `.carbin` | Car scene file containing modelbin references, bone attachment data and per-instance 4x4 transforms |
| `.xml` | Locators XML file containing named attachment points and their 4x4 world transform matrices |
| `.avpins` | Points of Interest file defining named camera anchor points with position, axis and radius data, used for Auto Vista |
| `.zip` | Archive containing any of the above file types. Contents are extracted and loaded automatically |
| `.minizip` | Compact archive format used by ForzaTech for tracks and world maps. Contents are extracted and loaded automatically |

---

## Viewport Controls

| Action | Control |
|---|---|
| Orbit / Rotate | Left mouse button + drag |
| Pan | Middle mouse button + drag |
| Zoom | Scroll wheel |
| Focus selected | F key or the Focus Selected button in the Camera menu |
| Select mesh | Left-click a mesh in the viewport |

---

## Toolbar

### File Operations

| Button | Shortcut | Description |
|---|---|---|
| Open | Ctrl+O | Browse and open one or more supported files |
| Save | Ctrl+S | Save changes to the currently selected file |
| Save As | Ctrl+Shift+S | Save the current file to a new location. When multiple file nodes are selected, prompts for a destination folder and writes all selected files there |
| Close Selected | Ctrl+W | Remove the selected node from the scene |
| Close All | Ctrl+Shift+W | Remove all loaded files from the scene |

### Edit

| Button | Shortcut | Description |
|---|---|---|
| Undo | Ctrl+Z | Undo the last transform edit |
| Redo | Ctrl+Y or Ctrl+Shift+Z | Redo the last undone edit |

### Export OBJ

Use the **Export OBJ** button to export geometry as a Wavefront OBJ file:

- **Export Whole Scene** - exports all loaded meshes together.
- **Export Selected Model** - exports only the meshes in the currently selected modelbin.

### Selection Mode

The **Selection Mode** toggle switches between selecting at the Mesh level and the Modelbin level when clicking objects in the viewport.

### Filter

The **Filter** button controls viewport visibility:

| Option | Shortcut | Description |
|---|---|---|
| Hide Selected | Ctrl+H | Hide the currently selected node |
| Isolate Selected | Ctrl+I | Hide everything except the selected node |
| Unhide Hidden | Ctrl+Shift+H | Make all hidden nodes visible again |
| Materials | - | Open the materials filter to toggle visibility per material name |
| Objects | - | Open the objects filter to toggle visibility per individual node |

### View Options

The **View** menu contains toggles for LOD levels and overlay types:

| Option | Description |
|---|---|
| LOD 0 - LOD 5 | Show or hide each level-of-detail mesh group |
| Shadows | Show or hide shadow meshes |
| Lights | Show or hide light objects loaded from a lights bin |
| Locators | Show or hide locator markers from XML files |
| Physics | Show or hide physics collision geometry |
| Skeletons | Show or hide skeleton bone visualisation |
| Damage Model | Show the morph damage mesh instead of the base mesh |
| Wireframe | Overlay polygon edges on all visible meshes |
| Normals | Draw surface normal vectors on all visible meshes |
| Bounding Boxes | Draw axis-aligned bounding boxes around each mesh |

### Camera

The **Camera** menu provides snap and focus controls including a Focus Selected button and camera preset positions.

---

## Scene Tree (Left Panel)

The left panel shows the full scene hierarchy for all loaded files. Each file is a top-level node and can be expanded to show its children:

- **Modelbin nodes** - top-level entry for each `.modelbin` file.
- **Mesh nodes** - individual geometry objects within a modelbin.
- **Bone nodes** - skeleton bones shown when a modelbin contains a skeleton.
- **Locator nodes** - named transform points loaded from an XML file.
- **Granny nodes** - skeleton and animation data loaded from a `.gr2` file.
- **Lights bin nodes** - light parts loaded from a `.bin` lights file.
- **Physics nodes** - collision geometry loaded from `physicsdefinition.bin`.
- **AvPins nodes** - Points of Interest loaded from an `.avpins` file.
- **Carbin nodes** - car scene entries loaded from a `.carbin` file, including part groups and modelbin instance references.

Each node has a checkbox to toggle its visibility in the viewport. Click a node to select it and show its properties in the panel below. Use Ctrl+left-click to select multiple file nodes in the tree; Save writes all selected files in one operation, grouping ZIP-backed entries so each archive is rebuilt once.

---

## Properties Panels (Lower Left)

When a node is selected the relevant property panel expands below the scene tree.

### Model Properties

Shown when a modelbin or mesh node is selected.

- **Model** - dropdown to select which loaded modelbin to inspect or edit.
- **Target Scope** - dropdown to select which meshes are affected by transform edits:
  - All meshes in the modelbin.
  - A specific material group.
  - A single individual mesh.
- **Position** - X, Y, Z translation values with increment/decrement buttons.
- **Scale** - X, Y, Z scale values with increment/decrement buttons.
- **Rotation (Degrees)** - X, Y, Z Euler rotation in degrees with increment/decrement buttons.
- **Reset Transformations** - resets all transform fields to their original values.
- **Bone** sub-section - shows the bone the selected mesh is attached to. Use the Change button to pick a different bone. The 4x4 local bone matrix is editable directly. The Use Bone Transforms toggle applies the bone matrix to the mesh in the viewport.
- **Morph** sub-section - shown when the mesh has morph/damage data. Select the morph buffer to preview in the Morph Buffer dropdown.

All transform changes are applied live to the scene and support full undo/redo.

### Light Transformations

Shown when a lights bin node is selected. Displays the editable Pos, Rot, Dmg Pos and Dmg Rot vectors (X/Y/Z/W) for each light part. Use the **Save Lights.bin** button to write changes back to the file.

### Locator Transformations

Shown when a locator XML node is selected. Displays a full 4x4 world transform matrix for each named locator with editable rows. Use the **Save Locators XML** button to write changes back to the file.

### Points of Interest

Shown when an avpins node is selected. Displays the full set of fields for each point including Position, Axis (Yaw/Pitch), Apex, Active Apex, Mid Apex and Near/Mid/Far radius values. Use the **Save .avpins** button to write changes back to the file.

### Carbin Properties

Shown when a carbin model entry is selected. Displays the referenced modelbin path, bone name, bone ID and 4x4 transform matrix. Matching loaded modelbins are instanced in the viewport for each carbin entry, so repeated references to the same modelbin render as separate transformed instances.

---

## Animation Playback

Shown when a Granny file with animation data is loaded. Located in the lower left panel.

- **Animation Clip** - dropdown to select which animation to play.
- **Track Filter** - select a specific bone track to isolate, or leave on All Tracks to play the full animation.
- **Link Skel MB** - manually link a `_skeleton.modelbin` to drive the animated skeleton if it was not auto-detected.
- **Play / Pause** - start or pause playback.
- **Stop** - stop playback and reset to the start frame.
- **Scrub bar** - drag to jump to any point in the animation.

---

## Undo / Redo

All transform edits (position, scale, rotation, bone matrix) support full undo/redo via Ctrl+Z and Ctrl+Y or Ctrl+Shift+Z.
