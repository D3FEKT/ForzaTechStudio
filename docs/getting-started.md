# Getting Started with ForzaTech Studio

ForzaTech Studio is a WinUI 3 desktop application for viewing, editing, and converting Forza Motorsport and Forza Horizon game asset files.

---

## System Requirements

| Requirement | Details |
|---|---|
| OS | Windows 10 (19041+) or Windows 11 |
| Runtime | [Windows App SDK 1.8](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads) |
| .NET | .NET 9 Desktop Runtime |
| Architecture | x64 only |

---

## Installation

1. Download the latest release from the [Releases](https://github.com/D3FEKT/ForzaTechStudio/releases) page.
2. Extract the ZIP to a folder of your choice.
3. Run `ForzaTechStudio.exe`.

> **Note:** The Windows App SDK runtime must be installed on your machine. If the app fails to launch, download and install it from Microsoft's website (link above).

---

## First Launch - Game Setup

On first launch you will be taken to the **Home** page. Before using any editor you should configure your game directories so that the app can resolve asset paths correctly.

1. Click the **Game Setup** button in the bottom bar on the Home page, or navigate to **Settings, Game Setup** from the nav bar.
2. In the Setup page, point each game entry to the root installation folder of the corresponding Forza title (e.g. the folder that contains `media\` or `content\`).
3. Click **Save**.

**Recommended:** 
Click the Build Database button to build a database file of game assets for faster backend browsing. 

**Recommended:** 
Use the material library to build a library of materials from the game files.
This allows you to easily reuse and edit material blobs across any modelbin, especially the material shader parameters.

The app stores these paths in a local settings file (`SettingsConfig`) so they persist between sessions.

---

## Opening Files

Most editors support **drag-and-drop**. Simply drag a supported file directly onto the page.

Alternatively, each page has an **Open** button (or a file picker button in the toolbar) that opens a standard file dialog.

---

## Navigation

Use the **left navigation bar** (NavigationView) to switch between tools. See the [Navigation Guide](navigation.md) for a full breakdown of every page.


---

## Page Descriptions

| Page | Description |
|---|---|
| **Home** | Landing page with quick-launch tiles and links to documentation. |
| **3D Viewer** | Interactive 3D viewport for previewing model assets. Supports meshes, skeletons, locators, and animations. |
| **Carbin Editor** | Open and edit `.carbin` bundle archives that define a car's scene graph, parts hierarchy, and model references. |
| **Modelbin Editor** | Inspect and modify `.modelbin` files - materials, meshes, skeletons, vertex layouts, and shader parameters. |
| **Swatchbin Editor** | View, edit, and create `.swatchbin` texture atlas files. Supports Xbox and PC format variants. |
| **FXB Viewer** | Browse FXB files and inspect their property tree. |
| **Materials** | View and edit individual material definitions extracted from model bundles. |
| **Materials & Shaders** | Workspace for editing shader parameters and material libraries across multiple assets. |
| **Lights Editor** | Configure car scene lighting - edit light entries and presets. |
| **Physics Definition** | View and edit physics body definitions for car models. |
| **String Tables** | Browse and search game string tables (localisation data). |
| **BXML Editor** | Open and edit Binary XML (BXML) asset files. |
| **Conversion Tool** | Batch convert car scene assets between game versions. |
| **Create Zip** | Package files into a Zip archive for deployment or sharing. Supports extraction of minizip. |
| **Create Modelbin** | Build a new `.modelbin` file from imported geometry and materials. |
| **Game Setup** | Configure game installation directories used for asset path resolution. |

---

## Updating the App

Replace the application folder with the contents of a newer release. Settings and preferences are stored separately and will not be lost.
