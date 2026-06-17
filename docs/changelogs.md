# Version 0.9.2.0 - Change Log


## General
- Fixed an issue with broken material paths on modelbin's
- Removed assimp library requirement

## 3d viewer
- Speed improvements, continuing to optimise
- UI reworks
- Fixed an issue causing FH6 modelbins to appear with broken normals after rotation
- Fixed an issue with some models going the opposite direction in the viewport compared to the actual transform value
- Fixed an issue with exporting to FBX being flipped
- Improved exporting bones to FBX
- Added support for all UV channels in UV editor window and exporting to FBX
- Added Blender style hotkeys to 3d viewer for free control of position (G), scale (S), rotation (R). Also supports axis lock with X, Y, Z hotkeys.
- Added Buttons for in 3d viewer position, scale, rotation + a gizmo for each
- Added RGB colour picker for carpaint materials under manufacturer colours
- Added open folder support (drag n drop, UI button)
- Initial backend added for FH6 animations (WIP)

## Modelbin Creator
- Multi UV channel support on FBX import
- UI fixes
- Improved material blob creation inside modelbins based on provided game material zips. 

## Carbin editor
- Fixed a bug when searching for a model and deleting it removes the incorrect chosen model
- Added zip opening support
- Added more matching strings for material hashes

## Swatchbin editor
- Fixed an issue with replacing textures didn't change to the specified settings
- Added support to more texture formats for creation and replacement (.bmp, .tif, .tiff, .webp, .heic, and .heif)





