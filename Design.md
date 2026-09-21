# Atlas

Atlas is a ManiaScript library to natively enhance map editors.

## Overview

Project is split into 4 parts:

- **Atlas** - the ManiaScript library itself
- **Atlas.Server** - host for multiplayer map editor
- **Atlas.Macroblocker** - utility to generate macroblocks from items
- **Atlas.Patcher** - fill missing metadata script traits from Gbx

This design centralized around the library itself, with the server and macroblocker serving as supporting components.

## Base

The library expects the `CMapEditorPlugin` context and its higher contexts (`CMapType`, `CTmMapType`, `CSmMapType`). It should not be allowed elsewhere.

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

## Ground system

As item's ground is technically not the official ground, there are two variants that the library differentiates:

- Real ground - decided only by map editor's `GetGroundHeight`
- Fake ground - decided by an `Int3[]` global variable of ground positions if it's above official ground height, then by map editor's `GetGroundHeight`

## Selection system

Selection system of Atlas is fully custom based on `CustomSelectionCoords` and is colored by `CustomSelectionRGB`, so it can be unfortunately just one color at a time.

Selection can stay indefinitely or until confirming an action.

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
  - Forming a cube that matches the ground height that cannot change within the selection
  - Follows ground system
- 2D selection
  - From XYZ cursor coord to another XZ coord with the same Y
  - Always forming a cube but with a height of 1
- 2D selection on ground
  - From XZ cursor coord at ground level Y to XZ coord with possibly different Y
  - Forming a coverage of ground with a height of 1
  - Follows ground system
- 1D selection
  - From XYZ cursor coord to another XYZ coord along a single axis
  - Always forming a line

During the drag, each selection change should be reported back via an event. On mouse release, the final selection should be reported separately. The start and end coords should be also reported.

### Remove water

Remove water feature uses **2D selection on ground (real ground)** drag selection.

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
ground (real ground)** drag selection, but only operates on coordinates having the border/grass void blocks placed.

On selection confirm, the library restores water at every selected recorded
coordinate (that have border/grass void blocks) by:

1. Removing the border/terrain void block
2. Removing the terrain
3. Placing water void

It then removes those coordinates from `Atlas_RemovedWater`.

While this mode is enabled, all coordinates in `Atlas_RemovedWater` are highlighted
as the current selection. The selection color should be brown to symbolize drought.

The library needs to accept the mapping of block name for the border/terrain void blocks to water void block. Example:

```
Atlas::SetRestoreWaterBlockMapping(["LagoonGrassVoid", "LagoonBeachVoid"], "LagoonVoid");
```

## Item blocks

Items have a very limited API:

- All items are available in `Items` list (luckily)
- Item is stored as `CItemAnchor`, which has only `Position` and `Id`, and `Id` is always `NullId`, cannot be placed or even removed via function
- Waypoint items are additionally stored in `AnchorData` list as `CAnchorData`, those cannot be placed but can be removed with `RemoveItem` function

However, items can be contained inside a macroblock and manipulated with as the macroblock. Macroblocks should be pre-created/pre-generated before applying these methods.

Such placed items are called **item blocks** in the library and are stored in a metadata variable of the map.

Each item block data structure should look like this (preferably using just associative arrays instead of structs for later expansion):

```
#Struct SAtlasItemBlock {
  Text MacroblockName;
  Int3 MacroblockCoord;
  Vec3 ItemPosition;
  ...
}
```

### Macroblock creation

- Each terrain item needs one macroblock
- The item must be at the default position and rotation
- The units of the macroblock should preferably cover the whole item accurately

Macroblocks should follow the same folder structure as items, with the `Blocks\<env>\` prefix:

```
Blocks\Lagoon\Z_Bay\Z_BayDock\Z_BayDock\X_BayDocksBase1\BayDockBase1D.Macroblock.Gbx
```

`AbsolutePositionInMap` of the item placed in the macroblock should match the original item's pivot position (or just negated, to verify). The rest of the values should be zeroed.

Macroblocks can contain extra script metadata, add anything that'd be useful for placement, like the block's units. Icons can be simply copied over.

This should be implemented in **Atlas.Macroblocker**.

### Placing item blocks

Place item blocks using `PlaceMacroblock_NoDestruction` and store them into the `Atlas_ItemBlocks` metadata variable.

When placing the macroblock, the value of the `ItemPosition` should be detected by checking for changes in the `Items` list, specifically when a new entry appears. The rest can be set easily.

In case in the future the macroblock contains more than one item, the macroblock script metadata should contain the relative position of each item and all of them should be looked up and matched with some algorithm.

### Removing item blocks

Only way to remove non-waypoint items is to use `RemoveMacroblock` with the macroblock that has the item.

An instructed item block to remove can be removed in various ways:

- Only coord is provided
  - Coord is checked against `Atlas_ItemBlocks` and the macroblock name + direction is extracted from it
  - If there are multiple item blocks on the coord, all of those macroblocks are attempted to be removed
- Coord and macroblock name is provided
  - Coord and macroblock name is checked against `Atlas_ItemBlocks` and the direction is extracted from it
  - If there are multiple same-named item blocks on the coord, all of those macroblocks are attempted to be removed
- Coord, macroblock name, and expected direction is provided
  - Coord, macroblock name, and direction is checked against `Atlas_ItemBlocks`
  - If there are duplicate item blocks, all of those macroblocks are attempted to be removed

Users usually want to remove items freely, so this method is usable only when automatically fixing item block placement upon item removal.

### Syncing manually removed items

User is free to remove any item, but if the item is tracked by metadata of item blocks, or is placed at the exact same position as any other item, the state can desync.

To solve this, the library loops over the `Items` every tick and checks for any change from the last instance of the list. That should give out the item position that was removed.

**This caught item position should be then used to check the `Atlas_ItemBlocks` to remove all item blocks (via `RemoveMacroblock`) on that same position.** This should also throw an event that a removal of those item blocks and other untracked items happened, for example to adjust the terraforming automatically.

This is a smaller inconvenience that is needed to ensure the editor doesn't desync the state and won't competely break the terraforming.

## Advanced item block placement

Composing multiple variants of item blocks with a single placement tool requires a grouping mechanism. Such mechanism will be called **item block groups**. They are lists of macroblocks that are ordered in ways that library can successfully compose.

The list is defined as `Text[][]`, where the primary list stores variants that are placed consistently, and each variant can have multiple subvariants which are purely randomized.

Selection system can be used to place such item block groups in various ways (defined below).

### Initialization

Before any item block placement, the map needs to know what item blocks are already placed to be able to remove them correctly, and that's not so obvious.

The library needs to be instructed with the initial item blocks metadata list, otherwise it is basically impossible to figure out. This can be different per map base or environment, so consumer should configure it themself. At the start of the library, use:

```
Atlas::SetItemBlockList([...]);
```

### Placement

Build the complete placement plan before placing any item block so that there is no risk of partial placements.

A few different modes should exist to handle different placement scenarios.

#### 1x1 freeform

- example: BayDocks, BayEsplanade, StadiumDirtBorder
- 2D selection on ground

Expected pieces:
- [0] Base1
- [1] Base3
- [2] Base5
- [3] Base7
- [4] Base15
- [5] Deadend
- [6] Deadend4
- [7] Deadend8
- [8] Deadend12
- [9] Corner
- [10] Corner8
- [11] Straight
- [12] TShaped
- [13] Cross

Expected rules:
1. If only 1 coord is selected and nothing occupies it already, place Cross
2. If a line is selected with no connections, place TShaped pieces at the start and end mirrored, fill the line with Straight
3. If a line joins TShaped in the same direction or the mirrored one, it should change to Straight
4. If a line *start/end* coord occupies TShaped in a +1/-1 direction, Corner8 should be placed
5. If a line is placed *across* a TShaped that is a +1/-1 direction, Deadend12 should be placed
6. If a 2x2 is selected and nothing occupies it already, place Corner on all 4 coords with each of the directions
7. If a line *start/end* coord occupies Corner, Deadend4 or Deadend8 should be placed according to each of the directions
8. If at least 3x2 or 2x3 is formed, Deadend should be placed between Corner
9. If a straight line occupies another straight line that is a +1/-1 direction, Base15 should be placed
10. If Corner8 is formed opposite to Corner on the same coord, Base7 should be placed
11. If Corner is formed opposite to Corner on the same coord, Base5 should be placed
12. If TShaped or Straight is "expected" to be placed on Deadend, Base3 should be placed
13. If Corner gets expanded in one direction, it should turn into Base1

#### Placement resolution system (AI)

Treat a placement as a state-resolution problem, not as a sequence of immediate
macroblock placements. This prevents the outcome from depending on which selected
coordinate happens to be processed first.

1. On every selection update, create a read-only snapshot of the selected fake-ground
   coordinates and of the tracked item blocks already at those coordinates. A tracked
   piece is represented by its family, piece index, and cardinal direction. An
   untracked item block is a placement conflict, because Atlas cannot safely replace it.
2. Convert the selected coordinates into a topology: connected components, cardinal
   edges, line starts/ends, perpendicular crossings, 2x2 areas, and area boundaries.
   Keep the topology separate from the current placed pieces; the selection describes
   the requested addition, while the snapshot describes what it joins or overlays.
3. Produce a default desired state for every affected coordinate: a single cell becomes
   `Cross`, a standalone line becomes `TShaped`/`Straight`/mirrored `TShaped`, and an
   empty 2x2 becomes four outward-facing `Corner` pieces. Give every desired state an
   explicit cardinal direction.
4. Resolve rules 3-13 against the complete snapshot and desired-state map. Rules add
   candidate results rather than changing a coordinate in place. More specific overlay
   outcomes (`Base15`, `Base7`, `Base5`, and `Base3`) win over boundary outcomes;
   boundary outcomes win over the default line or area result. If equally specific
   candidates disagree, mark the coordinate as a conflict instead of choosing one
   arbitrarily.
5. Apply the candidates simultaneously to produce the next desired-state map. Repeat
   this resolution pass until the map is unchanged. If a state repeats or a conflict is
   found, the selection is invalid and cannot be confirmed. This handles chained rules,
   such as a line first changing a `TShaped` to `Straight` and then creating an overlay
   piece, without relying on iteration order.
6. Compare the final desired-state map with the snapshot and build a transaction:
   unchanged pieces are retained, tracked pieces with a different state are removed,
   and missing pieces are placed. Preflight all operations, including map bounds,
   macroblock footprints, fake-ground height, and every exposed terrain connection.
7. Only confirm the transaction when every operation is valid. Remove the required
   tracked macroblocks, place the replacements with `PlaceMacroblock_NoDestruction`,
   and update `Atlas_ItemBlocks` as one logical operation. If any operation fails,
   restore every removed piece from the saved snapshot and leave the map unchanged.

During a drag, the system may resolve the current selection to provide a preview, but
must not change the map. It runs the transaction only after the selection-confirm
event. The final selected coordinates remain the source of truth for the placement
plan, so future extensions can use the same resolver for roads and 2x2 families with
their own topology-to-piece rule tables.

#### 2x2 cube

- example: BayBuilding1
- 3D selection on ground

Expected pieces:
- [0] Cross
- [1] TShapedSA
- [2] TShapedE
- [3] TShapedSB
- [4] TShapedW
- [5] CornerNW
- [6] CornerNE
- [7] CornerSE
- [8] CornerSW

#### 2x2 freeform

- example: ?
- 2D selection on ground

Expected pieces:
- ?

#### Road

- example: BayRoad, BayFlatsRoad
- 1D selection

Expected pieces:
- [0] Base
- [1] Deadend
- [2] Corner
- [3] Straight
- [4] TShaped
- [5] Cross

## Multiplayer editing

Multiplayer editor cannot support *free* item placement at all, as it's impossible to place items precisely with ManiaScript (no it just isn't xd). So the multiplayer capability is entirely left on blocks, macroblocks, and terrain.

ManiaScript supports only HTTP, so catching real-time events isn't as obvious, but is still possible with **HTTP long polling**.

- Client periodically sends requests, the server doesn't respond (or responds late enough) if there are no events.
- If no event is happening, client times out or the server sends a response of no data.
- If an event happens, server can use the open HTTP connection to fill in the data and the client is immediately acknowledged.

TODO
