# Conversion Tool

The **Conversion Tool** page batch-converts Forza car scene assets between different game versions. It handles format differences in modelbin files, textures, and material references so that content made for one title can be ported to another.

---

## Opening Files

- **Drag and drop** one or more supported files onto the page.
- Use the **Add Files** button to open a file picker (multi-select supported).
- **Drag and drop a folder** to add all compatible files within it recursively.

### Supported Input Formats

| Extension | Description |
|---|---|
| `.modelbin` | ForzaTech model binary |
| `.carbin` | Carscene bundle archive |
| `.swatchbin` | Forza textures binary |

---

## Conversion Settings

### Source Game / Target Game

Use the **From** and **To** dropdowns to select the conversion direction, for example:

- Forza Motorsport 7 → Forza Horizon 5
- Forza Horizon 4 → Forza Motorsport (2023)

The tool adjusts shader mappings, blob layouts, and hash tables based on the chosen game pair.

### Material Resolution

When converting models the tool may need to resolve material references that differ between game versions. If a material cannot be automatically resolved a **Material Picker** dialog will appear letting you:

| Option | Effect |
|---|---|
| **Use Selected** | Map the unknown material to the file you pick from the list |
| **Skip** | Leave this material unmapped and continue |
| **Skip All** | Suppress all further material prompts for this batch |

---

## Output Mode

| Mode | Description |
|---|---|
| **Output to folder** | Write converted files into a specified output directory, mirroring the input folder structure |
| **Output to ZIP** | Package all converted files into a single `.zip` archive |

Select the mode using the radio buttons below the file list, then click the folder/path button to choose the destination.

---

## Running the Conversion

1. Add input files.
2. Select source and target games.
3. Choose an output mode and destination.
4. Click **Convert**.

A progress indicator and status message are shown during conversion. Errors are logged to the results list with per-file detail.

---

## Results

After conversion completes, the **Results** panel lists each file with a status icon:

| Icon | Meaning |
|---|---|
| ✅ | Converted successfully |
| ⚠️ | Converted with warnings (e.g. unmapped materials) |
| ❌ | Failed - see the error detail for this entry |

Click any result row to expand the full error or warning message.

---

## Tips

- Run a single test file first before converting a large batch to catch material resolution issues early.
- If many materials are unknown, pre-populate the material cache by opening the target game directory in **Game Setup** so the tool can scan available assets.
- ZIP output is convenient for sharing or deploying a full conversion in one file.

---

## Advanced: VLay Blob Patching

The **Advanced VLay Blob Patching** option is an experimental feature that takes a more thorough approach to correcting the vertex layout (`VLay`) blobs inside converted `.modelbin` files.

### Background

Each `.modelbin` contains one or more **VLay (Vertex Layout) blobs** that describe the exact format of the vertex data streams - which attributes are present (position, normal, tangent, colour, UV channels), their data types, and the per-vertex stride in bytes. The required layout varies by game version and by the specific part of the car the modelbin is used for:

| Game | Typical Full-Layout Stride | Notable Differences |
|---|---|---|
| FH2 / FM5 | 40 bytes | NORMAL and TANGENT as `Float16x4` |
| FM6 / FM7 | 28 bytes | NORMAL as `Snorm16x2`, TANGENT as `R10G10B10A2` |
| FH3 / FH4 | 36 bytes | Adds `COLOR0` channel and `TEXCOORD4` |
| FH5 | 40 bytes | Adds second and third TANGENT (`TANGENT1`, `TANGENT2`) |
| FM2023 | 36 bytes | Like FH5 but `COLOR0` omitted on most parts |

A standard conversion simply rewrites the version fields. Advanced VLay goes further: it cross-references the modelbin's filename against a database of known per-file VLay patterns (derived from analysis across all game versions) to determine the **exact element count, attribute types, and stride** that should be present for that specific part in the target game.

### When to Use It

Enable **Advanced VLay Blob Patching** when:

- A conversion with the standard mode produces rendering errors such as corrupted normals, stretched UVs, or black surfaces in-game.
- You are converting to or from FM2023, which has an inconsistent `COLOR0` presence compared to FH5.
- The modelbin is for a glass, caliper, or other part that uses a reduced layout rather than the full exterior layout.

### How It Works

When enabled, the conversion service:

1. Looks up the modelbin filename in the VLay pattern database to determine its **pattern category** (Full, DetailSecondary, Minimal, InteriorReduced, or PositionOnly).
2. Applies the exact set of vertex attributes and data types appropriate for that category in the target game version, rather than using a generic fallback layout.
3. Skips patching for parts categorised as `PositionOnly` (these have zero VLay elements and must not be modified).

### Caveats

- This feature is **experimental**. The pattern database covers the vast majority of standard part names but may misclassify custom or non-standard filenames.
- For parts not found in the database, the tool falls back to standard conversion logic.
- Always verify results in-game when using this option, particularly for unusual part names.

