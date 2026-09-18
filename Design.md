# Atlas

Atlas is a ManiaScript library to natively enhance map editors.

## Overview

Project is split into 3 parts:

- **Atlas** - the ManiaScript library itself
- **Atlas.Server** - host for multiplayer map editor
- **Atlas.Macroblocker** - utility to generate macroblocks from items

This design centralized around the library itself, with the server and macroblocker serving as supporting components.

## Event system

ManiaScript natively does not support events, therefore the library exposes these events as lists that are cleared during the function call:

```cs
private IList<SMyEvent> myEvents = [];
public IList<SMyEvent> MyEvents
{
    get
    {
        var tempEvents = myEvents;
        myEvents = [];
        return tempEvents;
    }
}
```

## Selection system

Selection system of Atlas is fully custom based on `CustomSelectionCoords` and is colored by `CustomSelectionRGB`, so it can be unfortunately just one color at a time.

Selection can stay indefinitly or until confirming an action.

### Current selection

- Current selection coords should be remembered in a `Int3[]` global variable
- If this selection is visible should be determined by a `Boolean` global variable

This allows other selection methods to safely overlap the selection while not losing the information of the current selection. This selection is not expected to have specific limits.

Any other following selection should keep the current selection visualization intact.

### Drag selection

Drag selection can be anything that starts with a cursor mouse press at one coord and ends with a mouse release at another cursor coord.

This can be done in several modes:

- 3D selection
  - From XYZ cursor coord to another XYZ coord
  - Always forming a cube
- 3D selection on ground
  - From XYZ cursor coord to another XZ coord with the same Y and selecting every Y until the ground
  - Forming a cube thats variously cut around the ground
- 2D selection
  - From XYZ cursor coord to another XZ coord with the same Y
  - Always forming a cube but with a height of 1
- 2D selection on ground
  - From XZ cursor coord at ground level Y to XZ coord with possibly different Y
  - Forming a coverage of ground with a height of 1

During the drag, each selection change should be reported back via an event. On mouse release, the final selection should be reported separately. The start and end coords should be also reported.

### Remove water

Remove water feature uses **2D selection on ground** drag selection.

On selection confirm, it first places the flat terrain, then takes the terrain void block, places it across the whole terrain part, then the same for terrain border void block on the border part.

The library needs to accept a mapping of block names for the terrain blocks to void blocks. Library consumer should be able to set it. Example:

```
Atlas::SetRemoveWaterBlockMapping([
    "Grass" => "LagoonGrassVoid"
    "Beach" => "LagoonBeachVoid"
]);
```

Removed water coordinates should be stored in a metadata variable `Int3[] Atlas_RemovedWater`. These coordinates are highlighted (as selection) when the mode is enabled.

Selection color should be brown to symbolize drought.

### Restore water

Restore water is the inverse of **Remove water**. It uses the same **2D selection on
ground** drag selection, but only operates on coordinates having the border/grass void blocks placed.

On selection confirm, the library restores water at every selected recorded
coordinate (that have border/grass void blocks) by:

1. Removing the border/terrain void block
2. Removing the terrain
3. Placing water void

It then removes those coordinates from `Atlas_RemovedWater`.

While this mode is enabled, all coordinates in `Atlas_RemovedWater` are highlighted
as the current selection. The selection color should be brown to symbolize drought.

The library needs to accept the mapping of block name for the border/terrain void blocks to water void block.

```
Atlas::SetRestoreWaterBlockMapping(["LagoonGrassVoid", "LagoonBeachVoid"], "LagoonVoid");
```
