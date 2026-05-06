# Modelbin Editor

The **Modelbin Editor** page lets you inspect and modify `.modelbin` files, the primary binary format used by ForzaTech to store 3D model data including meshes, materials, skeletons, and shader parameters.

---

## Opening a File

- **Drag and drop** a `.modelbin` file onto the page.
- Use the **Open** button in the toolbar.

---

## Layout Overview

The page is split into three main areas:

| Area | Description |
|---|---|
| **Left Blob Tree** | Hierarchical list of every data blob contained in the file |
| **Centre Detail Panel** | Editable properties for the selected blob |
| **Right Info Bar** | Summary statistics (blob counts, file size, format version) |

---

## Blob Tree

Every `.modelbin` is composed of typed **blobs**. The tree groups them by type:

| Blob Type | Description |
|---|---|
| `MaterialBlob` | Material definition - name hash, shader, texture slots |
| `MeshBlob` | Geometry data - vertices, indices, vertex layout descriptor |
| `SkeletonBlob` | Bone hierarchy and bind-pose transforms |
| `LocatorBlob` | Named attachment points with world-space transforms |
| `ShaderParameterBlob` | Per-material shader constant values |
| `VertexLayoutBlob` | Vertex format descriptor (position, normal, UV channels, etc.) |

Click any blob to select it and view its properties in the centre panel.

---

## Material Properties

When a `MaterialBlob` is selected you can view and edit:

- **Name Hash** resolved display name (shown alongside the raw hash value).
- **Shader Name** the shader technique this material uses.
- **Texture Slots** list of texture references, each with a semantic name and path.
- **Shader Parameters** float/vector constants passed to the shader.

---

## Mesh Properties

When a `MeshBlob` is selected:

- **Vertex count** and **Index count** are shown as read-only statistics.
- **Material Name**  the material this mesh references.
- **Vertex Layout**  the attribute format used for each vertex stream.
- **Bounding Box**  min/max extents of the geometry.

---

## Saving Changes

Click **Save** (or press `Ctrl + S`) to write changes back to the original file, or **Save As** to write to a new path.

> Always keep a backup of original files before editing. Corrupted blobs can prevent the game from loading the asset.

---

## Name Hash Resolution

The editor uses a built-in **name hash database** to display friendly names alongside raw CRC/hash values. If a hash is unknown it will display as `0xXXXXXXXX (unknown)`.

---


