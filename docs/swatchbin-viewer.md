# Swatchbin Viewer


The **Swatchbin Viewer** page lets you open, inspect, and export `.swatchbin` and `.pb` texture files used throughout the Forza titles. It handles both **PC** format textures and **Xbox (Durango)** format textures, and can batch-convert Xbox tiles to a PC-compatible bundle.

---

## Opening Files

- **Drag and drop** one or more supported files directly onto the page.
- Click the **Open** button in the toolbar to browse.

### Supported Input Types

| Type | Description |
|---|---|
| `.swatchbin` | Single-texture Grub bundle containing one `TXCB` texture blob |
| `.pb` | Multi-texture Grub bundle (Packed Bundle). Same binary format as `.swatchbin` but may contain multiple `TXCB` blobs. Each blob is split out into a separate viewer entry automatically |
| `.zip` | Standard ZIP archive. Any `.swatchbin` entries are extracted individually. Any `.pb` entries are parsed and split per texture blob |
| `.minizip` | Playground MiniZip (PGZP) streaming archive. Entries with resource type `Texture` (type code 5) use the `.pb` extension. All texture bundles are extracted and each `TXCB` blob is added as a separate viewer entry |
| Forza asset bundle | Any raw Grub bundle file containing one or more `TextureContentBlob` (`TXCB`) blobs |

When an archive or bundle with multiple textures is opened the viewer extracts each texture to a temporary directory and populates the **texture selector dropdown** at the top of the page.

---

## Texture Selector

If more than one texture was loaded, a **ComboBox** at the top lets you switch between them. Each entry shows a generated display name:

- If the texture blob carries an `IdentifierMetadata` tag, the name is `tex_0x<id-hex>`.
- If a `TextureContentHeader` is present and contains a GUID, the name is `tex_<guid>`.
- Otherwise the name falls back to `tex00`, `tex01`

---

## Info Panel

The right-hand panel displays the parsed texture metadata:

| Field | Description |
|---|---|
| **Texture ID** | GUID from the `TextureContentHeader` (PC) or Durango header, or blank if absent |
| **Dimensions** | Width, Height, Depth |
| **Mip Levels** | Number of mip levels stored in the file |
| **Encoding** | Block-compression or uncompressed pixel format (see table below) |
| **DXGI Format** | Numeric DXGI format code and its name string |
| **Color Profile** | Linear, sRGB, or other colour space hint |
| **Flags** | Texture Cube, 3D Texture, Premultiplied Alpha |
| **Bundle Version** | `Major.Minor` of the enclosing Grub bundle |
| **Blob Version** | `Major.Minor` of the `TXCB` blob **version 1.x = PC, version 2.x = Xbox/Durango** |

### Encoding Formats

| Code | Name | Description |
|---|---|---|
| 0 | BC1 | DXT1, opaque or 1-bit alpha, 4 bpp |
| 1 | BC2 | DXT3, explicit 4-bit alpha, 8 bpp |
| 2 | BC3 | DXT5, interpolated alpha, 8 bpp |
| 3 | UnsignedBC4 | Single-channel unsigned, 4 bpp |
| 4 | SignedBC4 | Single-channel signed, 4 bpp |
| 5 | UnsignedBC5 | Two-channel unsigned (normal maps), 8 bpp |
| 6 | SignedBC5 | Two-channel signed, 8 bpp |
| 7 | UnsignedBC6H | HDR unsigned half-float, 8 bpp |
| 8 | SignedBC6H | HDR signed half-float, 8 bpp |
| 9 | BC7 | High-quality RGBA, 8 bpp |
| 10 | R32G32B32A32Float | 128 bpp uncompressed float |
| 11 | R16G16B16A16 | 64 bpp uncompressed half |
| 12 | R16G16B16A16Float | 64 bpp float |
| 13 | R8G8B8A8 | 32 bpp uncompressed |
| 14 | B5G6R5 | 16 bpp no alpha |
| 15 | B5G5R5A1 | 16 bpp 1-bit alpha |
| 16 | DCT | Discrete Cosine Transform (lossy) |
| 17 | IntegerDCT | Integer DCT variant |
| 18 | Procedural | Procedurally generated, no raw pixel data |

---

## Viewport

The decoded texture is rendered in the central image viewer:

| Action | Control |
|---|---|
| Pan | Left mouse button + drag |
| Zoom | Scroll wheel |
| Reset view | Double-click |

The zoom level is shown in the status bar. The viewer preserves the aspect ratio and uses nearest-neighbour scaling by default.

---

## Platform Detection: PC vs. Xbox (Durango)

The viewer automatically detects the texture platform from the `TXCB` blob version:

- **Blob version 1.x** - **PC format**. Pixel data is stored linearly and can be decoded or exported directly.
- **Blob version 2.x** - **Durango / Xbox format**. Pixel data is stored in a GPU-specific **tiled** layout. The viewer calls the native `xg.dll` (shipped with ForzaTech Studio) to detile the texture before display.

### Durango Tile Metadata

Xbox textures carry extra tiling fields in their `TextureContentHeader`:

| Field | Description |
|---|---|
| **Tile Mode** | XG tile mode constant (e.g. `XG_TILE_MODE_2D_THIN_1`) |
| **Tile Relative Mip Levels** | How many mip levels are physically tiled |
| **Tile Relative Dimensions** | Width, Height, Depth as stored in the tiled surface |

The detile pipeline uses `xg.dll` (`XGComputeTextureLayout`) to compute the exact per-mip memory layout, then copies each mip from the tiled buffer to a linear DDS layout.

---

## Export

### Export as DDS

Saves the raw pixel data with a standard **DDS** header built from the texture metadata. The exported file can be opened in tools such as DirectXTex `texconv`, Paint.NET (with the DDS plugin), or GIMP.

> **Note:** Xbox format textures are automatically detiled before export. The exported DDS is always in linear (PC) layout.

### Export as PNG

Decodes the block-compressed data to RGBA8 using the **BCnEncoder** library and saves as a 24- or 32-bit PNG. Supported for all BC1-BC7 formats. HDR and float formats are tone-mapped to 8-bit.

### Xbox to PC Conversion (Save as Swatchbin)

The **Convert to PC** button (shown only for Durango format files) runs the full conversion pipeline:

1. Detile the tiled pixel data via `xg.dll`.
2. Dealign row pitches to match linear DDS layout.
3. Build a new `DDS` byte array.
4. Re-encode the result back into a PC-format Grub bundle (`TXCB` blob version 1.x).
5. Save as a `.swatchbin` file.

The output file can be dropped directly into a car ZIP archive and referenced by the game engine.

---

## The .pb Texture Bundle Format

A `.pb` file (Packed Bundle) is a **Grub bundle** using exactly the same container format as `.swatchbin`, `.modelbin`, and all other ForzaTech gruB files. The only meaningful difference from a `.swatchbin` is that a `.pb` can contain **more than one `TXCB` texture blob**, making it a multi-texture bundle in a single file.

### Where .pb Files Are Found

`.pb` files appear in two main locations:

- **Inside `.minizip` streaming archives** - MiniZip entries with resource type code `5` (`Texture`) use the `.pb` extension. These archives are the chunk-based world streaming format used by Forza Horizon titles (see the [Create Zip](create-zip.md) page for MiniZip details). The viewer detects Grub bundle magic bytes on any entry whose type is not explicitly `.swatchbin` or `.pb`, so it can also surface texture bundles in entries without names.
- **Inside standard `.zip` car archives** - A single `.pb` file inside a car zip can carry multiple textures that would otherwise each require their own `.swatchbin`. This is common for track and environment texture sets.

### How the Viewer Handles .pb Files

Because a single `.pb` can hold several independent textures, the viewer always splits it on load:

1. The bundle is parsed and all `TXCB` blobs are enumerated.
2. Each blob is wrapped in its own minimal Grub bundle and written to a temporary `.swatchbin` file on disk.
3. Each temporary file is added to the texture selector as a separate entry, labelled with the source file name and the per-blob texture identifier (GUID or hash ID if present, otherwise a sequential index).

This means a `.pb` containing four textures appears as four distinct entries in the selector, each with its own info panel and export options. The temporary files are cleaned up automatically when the page is navigated away from or the application closes.

### Identifying the Texture Name

The name assigned to each extracted entry follows the same priority as a regular `.swatchbin`:

1. If the blob carries an `IdentifierMetadata` tag, the name is `tex_0x<id-hex>`.
2. If the `TextureContentHeader` contains a GUID, the name is `tex_<guid>`.
3. Otherwise the name falls back to `tex00`, `tex01`, etc.

The source file name is prepended in square brackets so you can tell which `.pb` or archive the texture came from, for example `[GeoChunk0.minizip] entry_0012.pb tex_0x1A3F9C00`.

---

## File Format Reference

A `.swatchbin` file is a **Grub bundle** (the same container format used by `.modelbin`, and other asset types). The bundle wraps a single `TextureContentBlob` (`TXCB` tag `0x54584342`), which carries:

- A `TextureContentHeader` metadata entry (`TXCH` tag) containing all dimension, format, and platform-specific fields.
- An optional `IdentifierMetadata` entry, a 32-bit hash ID used by the asset pipeline.
- The raw pixel data as the blob payload.

A `.pb` file uses the same structure but can contain multiple `TXCB` blobs in sequence within one bundle.

```
[Bundle Header]  magic "Grub" (0x47727562)  VersionMajor / VersionMinor
    TXCB Blob  (VersionMajor 1=PC, 2=Durango)
        TXCH Metadata  (dimensions, format, tile info, GUID)
        IDEN Metadata  (optional 32-bit hash)
        Pixel Data     (tiled on Xbox, linear on PC)
    [TXCB Blob]  (additional blobs present only in .pb files)
    ...
```
