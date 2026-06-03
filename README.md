<div align="center">
  <img src="ForzaTechStudio/Assets/store_logo.PNG" width="96" alt="ForzaTech Studio logo" />

  # ForzaTech Studio

  A WinUI 3 desktop application for browsing, editing, and creating Forza Motorsport & Horizon game assets.

  [![.NET 9](https://img.shields.io/badge/.NET-9-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
  [![Windows](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

  [Screenshots](#screenshots) • [Features](#features) • [File Formats](#file-formats) • [Getting Started](#getting-started) • [Building](#building-from-source)
</div>

---

ForzaTech Studio is a WinUI 3 desktop app for working with Forza Motorsport and Forza Horizon game assets. It provides a modern, unified interface for viewing, editing, and converting game files — covering 3D models, textures, car scenes, materials, shaders, physics data, and more.

> [!NOTE]
> Asset extraction is for personal modding and research purposes only. Modifying game files may break the code of conduct and terms and conditions, use at your own risk.

## Screenshots

<div align="center">
  <table>
    <tr>
      <td align="center"><img src="ForzaTechStudio/Assets/Screenshots/Screenshot1.png" width="480" alt="Home page"/><br/><sub>Home page — quick-launch cards and navigation sidebar</sub></td>
      <td align="center"><img src="ForzaTechStudio/Assets/Screenshots/Screenshot2.png" width="480" alt="3D Viewer"/><br/><sub>3D Viewer — real-time model preview with transform editing, showing FM6 skyline with animation</sub></td>
    </tr>
    <tr>
      <td align="center"><img src="ForzaTechStudio/Assets/Screenshots/Screenshot3.png" width="480" alt="Swatchbin Editor"/><br/><sub>Swatchbin Editor — texture metadata, format info and preview</sub></td>
      <td align="center"><img src="ForzaTechStudio/Assets/Screenshots/Screenshot4.png" width="480" alt="Carbin Editor"/><br/><sub>Carbin Editor — car scene parts and material assignments</sub></td>
    </tr>
    <tr>
      <td align="center" colspan="2"><img src="ForzaTechStudio/Assets/Screenshots/Screenshot5.png" width="480" alt="3d Viewer"/><br/><sub>3D Viewer — real-time model preview of loaded motorsport track</sub></td>
    </tr>
  </table>
</div>

## Features

### 3D Viewer

The **3D Viewer** page provides an interactive viewport for previewing ForzaTech model assets and allowing modification to it's transforms allowing repositioning, rescaling and rotation of parts.
It uses [HelixToolkit WinUI](https://github.com/helix-toolkit/helix-toolkit) with SharpDX for hardware-accelerated rendering.

Full page documentation at [docs/3d-viewer.md](docs/3d-viewer.md)

### Model Tools

| Tool | Description |
|---|---|
| **Modelbin Editor** | Edit meshes, LOD groups, materials, and bone hierarchies in `.modelbin` files. Full transform editing (position, scale, rotation) with undo/redo support. Also opens `locators.xml`, `physicsdefinition.bin`, and `lights.bin` for in-place transform editing. [Full documentation](docs/modelbin-editor.md) |
| **Modelbin Creator** | Step-by-step wizard to import OBJ/FBX assets and compile them into a Forza-compatible `.modelbin`, including material slot mapping and LOD configuration. Supports targeting Forza Horizon 6 (Modl 1.4 / Mesh 1.12) alongside FH5, FM2023, FH4 and earlier titles. |
| **Conversion Tool** | Batch convert `.carbin`, `.modelbin`, `lights.bin`, and `.swatchbin` files across game versions (FH2/3/4/5, FM5/6/7/8). Supports bulk Xbox-swizzled swatchbin conversion. [Full documentation](docs/conversion-tool.md) |
| **Physics Definition Creator** | Create and edit `.bin` physics collision models — bounding volumes, collision primitives, and material flags. [Full documentation](docs/physics-definition.md) |

### Material Tools

| Tool | Description |
|---|---|
| **Material Library** | Extract material blobs from `.modelbin` files into a local library. Browse, search, and refresh saved materials for reuse across projects. |
| **Materials & Shaders** | Open and inspect `.materialbin` and `.shaderbin` assets. Browse the full game material library (configured via Game Setup), follow texture references into swatchbin metadata, and view typed shader parameters (textures, floats, vectors, colors, samplers). |
| **Manufacturer Colors** | View OEM paint color entries from `burG`-header `.bin` files, including color name, RGB values, and finish type. [Full documentation](docs/manufacturer-colors.md) |
| **Swatchbin Editor** | Open, preview, export, import, and create `.swatchbin` texture files. Displays full metadata: DXGI format, dimensions, mip levels, platform (PC/Xbox), GUID, and flags. Supports BC1–BC7, R8G8B8A8, B8G8R8A8, and more. Full PC↔Xbox cross-conversion (`XG_TILE_MODE_2D_THIN` / `XG_TILE_MODE_1D_THIN`). [Full documentation](docs/swatchbin-viewer.md) |

### Carbin Editor

Full read/write editor for `.carbin` car scene bundles. Add or remove car parts (body, bumpers, spoilers, wings, etc.), configure upgrade slots, link ambient occlusion swatchbins, manage model-to-material assignments, and edit scene-graph properties. Includes cross-game conversion (FH4 → FH5).

Full page documentation at [docs/carbin-editor.md](docs/carbin-editor.md)

### XML Tools

| Tool | Description |
|---|---|
| **BXML Editor** | Parse and edit ForzaTech Binary XML (`.bxml`) files. Node-tree panel with attribute editing and full round-trip save. [Full documentation](docs/bxml-editor.md) |
| **Car Scene XML** | Edit all auxiliary scene descriptors stored alongside a car model: `ShakeBones`, `CarAttributes`, `GlobalCarAttributes`, `IKAnchorBones`, `Locators`, and `.avpins` animation pin data. [Full documentation](docs/car-scene-xml.md) |

### Additional Tools

| Tool | Description |
|---|---|
| **Zip Viewer** | Browse the contents of Forza-format ZIP archives (LZX method 21) without extraction. |
| **Lights Editor** | View and edit `.bin` light group databases used by car and environment models. [Full documentation](docs/lights-editor.md) |
| **FXB Editor** | Inspect FXStudio particle-bank (`.fxb`) files — emitter properties, effect chains, and timing data. [Full documentation](docs/fxb-viewer.md) |
| **String Tables** | Browse localization `.str` string tables, search by key or value. |
| **Create ZIP** | Build Forza-compatible mod archives with the correct internal directory structure and compression method. [Full documentation](docs/create-zip.md) |

## File Formats

| Format | Extension | Purpose |
|---|---|---|
| Modelbin | `.modelbin` | 3D models with meshes, LODs, and material references |
| Carbin | `.carbin` | Car scene bundles — parts, materials, slots, AO links |
| Swatchbin | `.swatchbin` `.pb` | DX-compressed textures (PC and Xbox formats) |
| Materialbin | `.materialbin` | Material parameter and shader binding data |
| Shaderbin | `.shaderbin` | Compiled shader libraries |
| Binary XML | `.bxml` | ForzaTech binary-encoded XML documents |
| Manufacturer Colors | `burG` `.bin` | OEM paint colors per manufacturer |
| ZIP archive | `.zip` | Forza mod archives (LZX method 21 compression) |
| FXStudio bank | `.fxb` | Particle and effect databases |
| String table | `.str` | Localization key-value tables |
| Lights | `.bin` | Car/environment light group databases |
| Light presets | `.bin` | Customization light preset configurations |
| Physics Definition | `.bin` | Collision model and physics metadata |
| Mini Zip | `.minizip` | Compact archive format used by ForzaTech for tracks and world maps. Contents are extracted and loaded automatically |

## Getting Started
Full page documentation at [docs/getting-started.md](docs/getting-started.md)

### Prerequisites

- Windows 10 or 11 (build 19041 or later)
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- A GPU with DirectX 11 support (required for the 3D viewer)



### Installation

1. Download the latest release ZIP from the [Releases](../../releases) page.
2. Extract to a folder of your choice.
3. Run `ForzaTech Studio.exe`.

On first launch the app creates a `Materials/` folder next to the executable for your local material library, and prompts you to configure your installed game directories via **Settings → Game Setup**.

> [!TIP]
> For FH5 and FM2023, most game files are TransformIT-encrypted. Decrypt them first using `ForzaTools.Decryptor` or another compatible tool before opening them in ForzaTech Studio. Decryption keys are not provided.

## Building from Source

**Requirements:** Visual Studio 2022 (17.9+) with the **.NET desktop** and **Windows App SDK** workloads installed.

```bash
git clone https://github.com/D3FEKT/ForzaTechStudio.git
cd ForzaTechStudio
```

Open `ForzaTechStudio.sln`, set **ForzaTechStudio** as the startup project, and build with the **x64** configuration.

Alternatively, from the repository root:

```bash
dotnet build ForzaTechStudio\ForzaTechStudio.csproj
```

## Known Limitations

- **Encrypted files** — FH5 and FM2023 TransformIT-encrypted archives require external decryption; keys are not included.
- **BC6H textures** — HDR BC6H-compressed textures are not decoded in the preview; export with an external tool.
- **Animation editing** — Bone animation editing is still a work in progress.
- **Shaderbin indexing** — The game asset database must be rebuilt after first-time Game Setup to enable shader library browsing.

## Credits

- **Nenkai** — Original ForzaTools library and file format research
- **Doliman100** — Reverse engineering Forza file formats and documentation
- **[HelixToolkit](https://github.com/helix-toolkit/helix-toolkit)** — 3D rendering
- **[AssimpNet](https://github.com/assimp/assimp)** — OBJ/FBX model import
- **[BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET)** — Texture compression
