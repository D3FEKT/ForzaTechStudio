# Physics Definition

The **Physics Definition** page lets you view, edit, and generate `physicsdefinition.bin` files - binary files that define the collision geometry and physics body data used by the ForzaTech physics engine for a car or object.

---

## What is physicsdefinition.bin?

`physicsdefinition.bin` is a binary file stored alongside a car's asset bundle. It describes the **collision shapes** the physics engine uses when simulating the car's rigid body. Without a valid physics definition, a car cannot be loaded into the physics world or driven.

The file contains one or more **physics definition entries**, each with:

- A **type** - the kind of physics object (vehicle, prop, debris, etc.).
- A **collision shape** - a point cloud, bounding box, or convex hull that approximates the car's silhouette.
- A **version** number that determines the binary layout the game expects.

---

## Opening a File

- **Drag and drop** a `physicsdefinition.bin` file onto the page.
- Use the **Open** button in the toolbar.

The app accepts any `.bin` file and attempts to parse it as a physics definition. Standard game files are named `physicsdefinition.bin`.

---

## Page Layout

| Area | Description |
|---|---|
| **Top bar** | Loaded file name and status |
| **Settings panel** | Generation mode, definition type, point count, and target version selectors |
| **Source models panel** | List of `.modelbin` files to use as geometry source for generation |
| **Definition list** | Parsed definition entries from the loaded file |
| **Entry detail** | Read-only or editable values for the selected definition |

---

## Definition Types

The **Type** selector controls what kind of physics object is being defined:

| Type | Description |
|---|---|
| `Vehicle` | Standard drivable car - used for all player and AI vehicles |
| `Prop` | Static or dynamic scene object (barriers, cones, etc.) |
| `Debris` | Breakaway fragment - used for crash deformation pieces |

---

## Generation Modes

When generating a new physics definition from imported geometry, three shape generation modes are available:

### Point Cloud
Samples the surface of the source model and outputs a set of 3D points. The physics engine fits a collision shape around these points at runtime.

- Best for irregular or organic silhouettes.
- The **Point Count** field controls how many sample points are generated (default: 200). Higher counts are more accurate but increase memory usage.

### Box
Computes an axis-aligned bounding box (AABB) that fully encloses the source geometry.

- Fastest to compute and cheapest at runtime.
- Least accurate - only suitable for simple rectangular objects or as a rough first approximation.

### Convex Hull
Computes the smallest convex shape that contains all the geometry vertices. This is the most geometrically accurate of the three modes and is the standard used for car bodies.

- More accurate than Box but requires more processing time to compute.
- The game uses a `SimdConvexHull` optimised representation internally.

---

## Adding Source Models

Click **Add Models** to open a file picker and select one or more `.modelbin` files. The page uses these models as the geometric source when generating a new physics definition.

- Multiple models can be added (e.g. body shell + chassis combined).
- Models are listed with their file paths and can be removed individually with the **×** button next to each entry.

---

## Target Version

The **Target Version** field sets the format version written into the output file. The default value matches the version used by current game targets. Only change this when specifically targeting an older game version.

---

## Generating a New Definition

1. Add one or more source `.modelbin` files.
2. Select a **Type** and **Generation Mode**.
3. Optionally adjust **Point Count** (Point Cloud mode only).
4. Click **Generate**.

The definition list will populate with the result. Review the generated shape before saving.

---

## Saving Changes

Use **Save** (`Ctrl + S`) to write the current definition back to the original file, or **Save As** to choose a new path.

Alternatively, click **New** to start a blank definition from scratch without loading an existing file first.
