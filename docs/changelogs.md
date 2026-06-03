# Version 0.9.1.9 - Change Log


## General
- More bug fixes related to fh6

## 3d viewer
- UI improvements
- Added UV map display window with live editing
- Initial carbin support for transform values (would need DB access to get correct scaling)
- Fixed an issue with saving back to opened zip, now rebuilds it fully
- Started support of swatchbin support on models and basic material parameters displaying (RGB, glass)
- Manufacturer colours RGB support onto modelbins
- Multi file save support
- Improved loading speed
- Added export options and general export improvements
- Changed to pyramids instead of cones on points in the viewport for a cleaner look (Looking at you varsinity)

## Modelbin Creator
- Initial support, bugs still expected
- OBJ and FBX supported for import
- Load game specific materials from material.zip's or use internal created material library for assignment of mats
- Supports all LOD's and shadows
- Single modelbin building with multiple meshes and multi modelbin output

## Carbin editor
- Fixed small bug with saving back to file changing parts not touched
- Support for FH6 material index hash ID's
- General UI improvements
- Fixed bug with more than 1 tab opened not displaying correctly

## Manufacturer colours
- Added support for FH6 version of the format

## BXML editor
- General UI improvements
- FH6 manifest support
- Fixed an issue with converting from xml to bxml