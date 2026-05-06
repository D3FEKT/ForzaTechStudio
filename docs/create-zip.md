# Create Zip / Archive Viewer

The **Create Zip** page serves two purposes:

1. **Archive Viewer** : open an existing `.zip` or `.minizip` and inspect its contents in a tree view.
2. **Archive Builder** *(Build Mode)* : drag files and folders in, then export them as a standard ZIP or a Forza-style ZIP.

---

## Modes

| Mode | How to enter |
|---|---|
| **Archive Viewer** | Drag & drop a `.zip` or `.minizip` onto the page, or use **Open** |
| **Build Mode** | Drag files/folders onto the page when no archive is open, or click **New** |

Switch back to Build Mode at any time with the **New** button.

---

## Archive Viewer

### Opening an Archive

- **Drag and drop** a `.zip` or `.minizip` file onto the page.
- Use the **Open** button to browse.

The tree view on the left shows the directory hierarchy reconstructed from the archive's entry names. Each entry displays:

| Column | Description |
|---|---|
| **Name** | File/folder name |
| **Size** | Uncompressed size |
| **Packed Size** | Compressed size on disk |
| **Modified** | DOS date/time stamp (ZIP only) |
| **Method** | Compression method (Store, Deflate, Method 21) |

### Extracting Entries

Right-click any file entry for options:

- **Extract** - extract the selected entry to a folder you choose.
- **Extract All** - extract every entry in the archive.

---

## Build Mode

In Build Mode you assemble a new archive from scratch:

1. **Drag** files or entire folders onto the page. They appear in the tree view.
2. Set the **Archive Name** in the text box at the top.
3. Choose the output **Format** from the dropdown:
    **Standard Zip (Deflate)** portable ZIP with Deflate compression (method 8). Compatible with all standard ZIP tools.
    **Forza Zip (Store)**  ZIP structure with entries stored uncompressed (method 0). The Forza engine requires Store-encoded archives for direct streaming; use this when packaging car assets for the game.
4. Click **Save** to write the archive.

> **Tip:** The Forza engine reads car asset ZIPs at runtime. Always use **Forza Zip (Store)** when creating archives intended for the game.

---

## Standard ZIP Format

Both archive types use the standard [PKWare ZIP specification](https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT):

```
For each entry:
  [Local File Header]   PK\x03\x04 + metadata + file data
  ...
[Central Directory]     PK\x01\x02 × N entries
[End of Central Directory]  PK\x05\x06
```

### Compression Methods in ZIP

| Method Code | Name | Notes |
|---|---|---|
| 0 | Store | No compression - raw bytes |
| 8 | Deflate | RFC 1951 DEFLATE (LZ77 + Huffman), 32 KB window |
| 21 (0x15) | LZX / XMemCompress | Xbox-specific LZ compression (see below) |

---

## Method 21 - XMemCompress LZX

Forza titles on Xbox 360 and Xbox One ship game data in standard ZIP files where most entries are compressed with **method 21**, a non-standard extension to the ZIP format. Method 21 uses **Microsoft's XMemCompress LZX** algorithm - the same LZ-based codec found in `xcompress64.dll` (shipped with ForzaTech Studio).

### What is LZX?

LZX is a variant of LZ77 with a **Huffman** entropy coder, developed by Jonathan Forbes and Tomi Poutanen and later licensed by Microsoft for use in cabinet files, WIM images, and Xbox media. Key properties:

- **Window size**: configurable from 32 KB up to 2 MB. Forza archives typically use the default `0x20000` (128 KB) window.
- **Partition size** (`CompressionPartitionSize`): the engine processes the compressed stream in blocks. ForzaTech Studio uses the codec's default partition.
- **Codec context**: the `XMemCodecParametersLZX` struct controls the window and partition parameters when initializing `XMemCreateDecompressionContext`.

### How ForzaTech Studio Reads Method 21

When an entry's compression method field in the Local File Header equals `21`, `CustomZipFile` routes it through the native `xcompress64.dll`:

```
XMemCreateDecompressionContext(XMemCodecType.LZX, ...)
  ? XMemResetDecompressionContext(context)
    ? XMemDecompressStream(context, dest, destSize, src, srcSize)
      ? XMemDestroyDecompressionContext(context)
```

The decompressed bytes are then CRC32-checked against the value stored in the ZIP central directory before being written to disk.

> **Write support:** ForzaTech Studio can read method 21 archives but does not currently write them. The **Build Mode** always produces method 0 (Store) or method 8 (Deflate).

---

## Playground MiniZip (PGZP) Format

`.minizip` files are a completely separate, proprietary streaming archive format used by Playground Games. They share the `.minizip` extension but are **not** standard ZIP files.

### Identification

A MiniZip file always starts with the 4-byte magic number `PGZP` (`0x50475A50` little-endian).

### File Header

```
Offset  Size  Field
0x00     4    Magic - 0x50475A50 ("PGZP")
0x04     4    Version - currently 100 or 101
0x08     4    FolderIndicesOffset - offset of the folder-index block
0x0C     4    NumDirEntries - total file count
0x10     4    NumFolders - number of folder index entries
0x14     4    FilesPerChunk - entries per sub-chunk
0x18     4    NumSubChunks - number of sub-chunks
0x1C     4    (reserved / unknown)
```

### Chunk Layout

Entries are distributed across **sub-chunks**. Each sub-chunk groups `FilesPerChunk` entries whose data is contiguous on disk:

```
[FolderIndices block]   (padded to 8-byte alignment)

For each sub-chunk:
  DataStartOffset  u64   - absolute file offset where this chunk's data begins
  For each entry in this chunk:
    RelativeDataOffset  u32   - offset from DataStartOffset to this entry's data
    UncompressedSize    u32
    Flags               u16   - compression method + padding (see below)
    ParentDirIndex      u16   - index into the folder-index table

[LastEntry record]      - same layout as a regular entry; used to calculate
                          the compressed size of the final entry
```

### Flags Field

The 16-bit flags word packs the compression **method** and a **padding** byte count:

| Version | Method bits | Padding bits |
|---|---|---|
| 100 | bits 13..0 (`& 0x3FFF`) | bits 15..14 |
| 101 | bits 11..0 (`& 0x0FFF`) | bits 15..12 |

The padding byte count records how many zero bytes follow the compressed payload to maintain alignment before the next entry.

### Compression Methods in MiniZip

| Method | Name | Notes |
|---|---|---|
| 0 | Store | Raw uncompressed bytes |
| 8 | Deflate | RFC 1951; decompressed with `InflaterInputStream` (SharpZipLib) for version 100, or `SharpCompress.DeflateStream` for version 101 |
| 21 | XMemCompress LZX | Same codec as in the standard ZIP format - see above |
| 22 | Deflate + TFIT | Deflate with an additional TFIT (hash/crypto) wrapper; **not supported for extraction or replacement** |

### Companion Files

MiniZip archives named `GeoChunkN.minizip` are always accompanied by two companion files in the same directory:

| File | Content |
|---|---|
| `ChunkMapN.dat` | Per-entry resource type table. Each 4-byte record: 2 bytes unknown, 1 byte unknown, 1 byte `MiniZipResourceType` |
| `ChunkContentsMiniZipN.txt` | One entry path per line, optionally prefixed with `<PREZIPPED>`. Provides the human-readable names shown in the tree view |

Without these companions the viewer still loads, but all entries are labelled by index with their type shown as `Unknown`.

### Resource Types

| Type Code | Extension | Description |
|---|---|---|
| 0 | `.pgeo` | Procedural geometry |
| 1 | `.phys` | Physics template |
| 3 | `.owb` | AI open-world block |
| 4 | `.modelbin` | ForzaTech model binary |
| 5 | `.pb` | Texture bundle |
| 7 | `.gr2` | Granny animation |
| 9 | `.soundscape` | Audio soundscape |
| 10 | `.bank` | Audio sound bank |
| 11 | `.lightblock` | Light block data |
| 12 | `.pvsz` | PVS zone |
| 15 | `.hkx` | Havok NavMesh |
| 16 | `.zip` | Voxel GI pack |
| 17 | `.mtxmoddxt` | MegaTexture chunk |
| 18 | `.dxt` | MegaTexture source |
| 22 | `.gipack` | Indirect light |
| 24 | `.hqdxt` | MegaTexture source HQ |
| 25–27 | various | Entity streaming cell, volumetric fog |

### Computed Compressed Size

The MiniZip header does not store a compressed size field explicitly. ForzaTech Studio computes it at load time as:

```
CompressedSize[i] = DataOffset[i+1] - DataOffset[i] - Padding[i]
```

For the last entry, `DataOffset[last+1]` is read from the `LastEntry` record stored after the final sub-chunk.

---

## Replacing Entries

ForzaTech Studio can **rebuild** a MiniZip with one entry replaced:

1. Right-click an entry, **Replace with file...**
2. Browse to the replacement file.
3. The archive is rebuilt preserving the original compression method (Store or Deflate). Method 22 replacement is not supported.

The rebuilt archive is written to a temporary file first, then atomically renamed over the original.

---

## Notes and Limitations

- **Method 21 write** - not supported. The tool can decode LZX entries but cannot create new ones.
- **Method 22** - read-only; extraction is unsupported. This method is found only in certain map streaming archives.
- **Large archives** - loading a MiniZip with thousands of entries may take several seconds. The tree view is hidden during the rebuild phase to suppress per-node layout overhead.
- **Encoding** - ZIP entry names are decoded as UTF-8 (if flag bit 11 is set) or Code Page 437, with a fallback to Latin-1.
