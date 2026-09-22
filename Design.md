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
- Fake ground - decided by item blocks whose `Ground` field is `True`. At a given XZ coordinate, the highest qualifying item block above real ground defines fake-ground height. If none qualifies, fake ground is real ground.

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

### Tower selection

When enabled, it automatically selects a defined XZ region and fills the Y selection down to the ground whereever mouse is moved. Clicking approves the region to work with.

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
  CardinalDirections MacroblockDir;
  Vec3 ItemPosition;
  Boolean Ground;
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

The list is defined as `Text[][][]`, where the primary list stores up to 2 elements, first air variant list and second ground variant list (picked based on if selection is on ground or in air), second layer stores variant indices that are placed consistently, and each variant can have multiple subvariants which are purely randomized.

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

Sometimes it is useful to use bitwise operations to determine neighbors. ManiaScript doesn't support such operations, so play with traditional integer operations to replicate bits.

#### 1x1 freeform

- 2D selection on ground
- Official equivalent example: StadiumDirtBorder
- List of expected blocks:
  - BayDocks
  - BayEsplanade
  - BayUrbanStores
  - BayUrbanTrench
  - BayUrbanPark

Expected pieces ([Index] [Id]):
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

#### Placement resolution system (AI generated text)

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

#### 2x2 cube (any height)

- 3D selection on ground
- Official equivalent example: StadiumInflatable
- List of expected blocks:
  - BayBuilding1
  - BayBuilding2

Expected pieces ([Index] [Id]):
- [0] Cross
- [1] TShapedSA
- [2] TShapedE
- [3] TShapedSB
- [4] TShapedW
- [5] CornerNW
- [6] CornerNE
- [7] CornerSE
- [8] CornerSW

Expected rules:
1. Minimal placement region allowed is 2x2
2. Such item block group is typically divided into 3 layers: bottom, middle, top
3. Placement must always form a cube as a final result, not any other complex shape
3. An isolated valid anchor uses `Cross`. A connected anchor uses the piece whose open
   sides match its cardinal neighbours: `Cross` for four sides, a `TShaped*` variant
   for three sides, and the matching `Corner*` variant for a perpendicular pair.
4. The family configuration defines the cardinal orientation of `TShapedSA`,
   `TShapedE`, `TShapedSB`, and `TShapedW`; the resolver must use that explicit
   mapping rather than infer an orientation from the name.
5. A 2x2 or larger filled area is resolved from its outside boundary. Its outer corners
   use the matching `Corner*` pieces, while internal anchors use the most connected
   compatible piece.
6. A selected anchor is invalid when any cell of its footprint is outside the map,
   occupied by an incompatible tracked item block, or has an incompatible fake-ground
   height or terrain connection.

##### Placement resolution system

Resolve this mode on an anchor grid, then expand anchors to physical map cells only
for validation and placement:

1. Snap every selected coordinate to the cube grid and reject duplicate or overlapping
   anchors. Snapshot the tracked item blocks in every affected 2x2 footprint.
2. Build cardinal connectivity between anchors, derive their exterior boundary, and
   look up the family-specific connection-mask-to-piece mapping.
3. Produce one desired state per anchor, including its piece, rotation, and four-cell
   footprint. Resolve all anchors from the snapshot at once; no anchor may inspect a
   partially updated neighbour.
4. Preflight the union of all footprints. Existing tracked macroblocks that differ
   from the desired state are replacement candidates; untracked occupants or conflicts
   between desired footprints invalidate the entire plan.
5. On confirmation, replace the tracked candidates and place the final macroblocks as
   one transaction. Roll back to the snapshot if any removal or placement fails.

#### 2x2 freeform (any height)

- 2D selection on ground
- List of expected blocks:
  - BayUrbanMall

Expected pieces:
- [0] Cross
- [1] TShaped
- [2] Corner
- [3] Base1

Expected rules:
1. Minimal placement region allowed is 2x2
2. Such item block group is typically divided into 3 layers: bottom, middle, top
3. Placement can be connected variously with the same type of block, but always have to be at least 2x2

#### Road

- 1D selection
- List of expected blocks:
  - BayRoad
  - BayRoadSupport
  - BayBridgeRoad
  - BayBridgeRoadSupport
  - BayFlatsRoad
  - BayFlatsRoad2
  - BayTrenchRoad
  - BayTunnelRoad

Expected pieces:
- [0] Base
- [1] Deadend
- [2] Corner
- [3] Straight
- [4] TShaped
- [5] Cross

Expected rules:
1. A road selection is an ordered, orthogonal path of fake-ground coordinates. Each
   consecutive pair must be cardinally adjacent; diagonal jumps and gaps are invalid.
2. A single valid coordinate uses `Base`. In a simple path, the two end coordinates use
   `Deadend` facing the path and every intermediate coordinate uses `Straight`.
3. A change of direction at an intermediate coordinate uses `Corner`, rotated to join
   its predecessor and successor.
4. When selected paths meet, use `TShaped` for three connected cardinal sides and
   `Cross` for four. Re-visiting a coordinate is allowed only when it produces one of
   these valid junctions.
5. The path may join a compatible tracked road piece. The joined coordinate is resolved
   from the combined existing and requested connection mask; it is not blindly replaced
   by the piece required by the newly drawn segment alone.
6. A road may not join an incompatible family, a different fake-ground height, or an
   untracked item block. Those coordinates make the whole confirmation fail.

##### Placement resolution system

The road resolver converts the ordered path into a connection map before it creates any
macroblocks:

1. Record the ordered path supplied by the 1D selection, normalize it to fake-ground
   coordinates, and collect the four-direction connection mask for every visited
   coordinate.
2. Merge that mask with compatible tracked road pieces already at the same coordinates.
   Resolve masks with one table: 0 sides = `Base`, 1 = `Deadend`, opposite 2 =
   `Straight`, perpendicular 2 = `Corner`, 3 = `TShaped`, and 4 = `Cross`.
3. Resolve directions from the mask, not from drag order. This makes drawing the same
   road from either end produce the same result.
4. Diff the desired states against the tracked-road snapshot, preflight all changed
   coordinates and terrain connections, then remove and place only the changed
   macroblocks in one transaction. Restore the snapshot if the transaction fails.

#### Tower

Uses tower selection to place structures based on cursor's height. Such blocks can be 1x1 on XZ size or more (their sizes are defined by the item block group itself, there should be a parameter to define such resolution).

Such item block group is typically divided into 3 layers: bottom, middle, top.

List of expected blocks:
- BayUrbanBuildingMedical
- BayUrbanBuildingFinance
- BayUrbanBuildingBusiness

## Multiplayer editing

Multiplayer editor cannot support *free* item placement at all, as it's impossible to place items precisely with ManiaScript (no it just isn't xd). So the multiplayer capability is entirely left on blocks, macroblocks, and terrain.

ManiaScript supports only HTTP, so catching real-time events isn't as obvious, but is still possible with **HTTP long polling**.

- Client periodically sends requests, the server doesn't respond (or responds late enough) if there are no events.
- If no event is happening, client times out or the server sends a response of no data.
- If an event happens, server can use the open HTTP connection to fill in the data and the client is immediately acknowledged.

ManiaScript's default HTTP timeout is 30 seconds, so it is preferable to stay under this limit (somewhere around 20 seconds). Client requests the same endpoint again after completion of the previous endpoint while such session is running.

### Client/server protocol

The server is the authoritative sequencer for a map-editing session. The editor client
does not send raw map mutations over HTTP; it sends an intent that Atlas can validate
against the same map revision seen by the client. The server assigns every accepted
operation a monotonically increasing sequence number and distributes the resulting
operation to every connected editor, including its author.

Each client receives a session-specific `ClientId` when it joins a map-editing session.
It persists a generated `OperationId` while retrying the same request. This lets the
server recognize a retry without applying the edit twice.

#### Join session

Client sends:

```text
POST /sessions/{sessionId}/join
{
   "displayName": "Builder"
}
```

Server returns the client identity, current revision, and a snapshot needed to render
the authoritative state:

```text
200 OK
{
   "clientId": "client-7f3a",
   "revision": 184,
   "itemBlocks": [ ... ],
   "removedWater": [ ... ]
}
```

The snapshot includes metadata controlled by Atlas. The client reads the actual map
through the editor API and must not treat the snapshot as a replacement for it.

#### Submit an edit

Client sends one fully described intent after local preview and confirmation:

```text
POST /sessions/{sessionId}/operations
{
   "operationId": "8d66c2f4-9ea1-4a2d-a4f5-3c932f6e1aa1",
   "clientId": "client-7f3a",
   "baseRevision": 184,
   "kind": "PlaceRoad",
   "payload": {
      "family": "BayRoad",
      "path": [ [12, 4, 30], [13, 4, 30], [14, 4, 30] ]
   }
}
```

`payload` is specific to the operation kind. It contains immutable user intent, such
as selection coordinates, the selected family, or the requested water operation; it
does not contain client-computed macroblock removals or placements. The server
re-resolves the intent against its current map snapshot and either commits one complete
transaction or rejects it.

Server returns exactly one of:

```text
202 Accepted
{
   "operationId": "8d66c2f4-9ea1-4a2d-a4f5-3c932f6e1aa1",
   "sequence": 185,
   "revision": 185,
   "status": "committed"
}
```

```text
409 Conflict
{
   "operationId": "8d66c2f4-9ea1-4a2d-a4f5-3c932f6e1aa1",
   "revision": 186,
   "reason": "stale-revision"
}
```

```text
422 Unprocessable Content
{
   "operationId": "8d66c2f4-9ea1-4a2d-a4f5-3c932f6e1aa1",
   "revision": 184,
   "reason": "invalid-placement",
   "conflicts": [ [14, 4, 30] ]
}
```

`409` means the client must process events through the returned revision and may submit
a newly resolved intent. `422` means the intent is invalid at the supplied revision;
the map and Atlas metadata have not changed. Repeating a request with an already
committed `OperationId` returns its original acceptance response.

#### Receive edits with long polling

Client maintains one outstanding request while connected:

```text
GET /sessions/{sessionId}/events?clientId=client-7f3a&after=184&waitSeconds=20
```

The `after` value is the greatest event sequence fully applied by the client. The
server responds immediately when later events exist, when 20 seconds elapse, or when
the session is closed:

```text
200 OK
{
   "events": [
      {
         "sequence": 185,
         "operationId": "8d66c2f4-9ea1-4a2d-a4f5-3c932f6e1aa1",
         "authorClientId": "client-7f3a",
         "kind": "PlaceRoad",
         "resolvedChanges": {
            "removedItemBlocks": [ ... ],
            "placedItemBlocks": [ ... ],
            "removedWater": [ ... ]
         }
      }
   ],
   "revision": 185
}
```

The client applies events strictly in sequence order, then opens the next poll with the
last applied sequence. It receives its own committed event too, allowing every editor
to use one application path. A timeout response is `200 OK` with an empty `events`
list and the unchanged revision. If the requested sequence is older than the server's
retained event history, the server returns `410 Gone` with a fresh Atlas metadata
snapshot; the client replaces its local metadata and resumes polling from that
revision.
