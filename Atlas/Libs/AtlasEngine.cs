namespace Atlas.Libs;

/// <summary>Editor-side map tools. Call Update once per editor frame.</summary>
public class AtlasEngine : CMapEditorPlugin, ILib
{
    private readonly MathLib mathLib = new();

    public enum SelectionMode
    {
        None,
        Box3D,
        BoxToGround,
        Plane2D,
        Ground2D,
        Line1D,
        Tower,
        RemoveWater,
        RestoreWater
    }

    public struct SelectionChange
    {
        public Int3 Start;
        public Int3 End;
        public List<Int3> Coords;
    }

    public struct ItemBlock
    {
        public string MacroblockName;
        public Int3 MacroblockCoord;
        public int MacroblockDir;
        public Vec3 ItemPosition;
        public bool Ground;
        public string Family;
        public int PieceIndex;
        public int Width;
        public int Depth;
    }

    public struct ItemRemoval
    {
        public Vec3 Position;
        public List<ItemBlock> RemovedBlocks;
    }

    public struct Placement
    {
        public Int3 Coord;
        public string MacroblockName;
        public CardinalDirections Direction;
        public bool Ground;
        public string Family;
        public int PieceIndex;
        public int Width;
        public int Depth;
    }

    public struct FreeformCandidate
    {
        public int Piece;
        public CardinalDirections Direction;
        public int Priority;
    }

    private readonly List<Int3> currentSelection = [];
    private bool selectionVisible;
    private bool dragging;
    private bool previousMouseDown;
    private Int3 dragStart;
    private SelectionMode mode;
    private int towerWidth;
    private int towerDepth;
    private bool lastRollbackFailed;
    private readonly List<Int3> lastSelectionChange = [];
    private bool hasLastSelectionChange;
    private readonly List<SelectionChange> selectionChanged = [];
    private readonly List<SelectionChange> selectionConfirmed = [];
    private readonly List<ItemRemoval> itemRemovals = [];
    private readonly List<Vec3> previousItems = [];
    private readonly Dictionary<string, string> removeWaterMapping = [];
    private readonly List<string> restoreWaterVoidNames = [];
    private string waterVoidName = "";
    private readonly Dictionary<string, List<List<List<string>>>> itemBlockGroups = [];
    private readonly Dictionary<string, Dictionary<int, int>> cubePieceMapping = [];

    public SelectionMode Mode => mode;
    public bool LastRollbackFailed => lastRollbackFailed;
    public bool SelectionVisible => selectionVisible;
    public IList<Int3> CurrentSelection => currentSelection;
    public IList<SelectionChange> SelectionChanged
    {
        get
        {
            var result = new List<SelectionChange>();
            foreach (var entry in selectionChanged) result.Add(entry);
            selectionChanged.Clear();
            return result;
        }
    }

    public IList<SelectionChange> SelectionConfirmed
    {
        get
        {
            var result = new List<SelectionChange>();
            foreach (var entry in selectionConfirmed) result.Add(entry);
            selectionConfirmed.Clear();
            return result;
        }
    }

    public IList<ItemRemoval> ItemRemovals
    {
        get
        {
            var result = new List<ItemRemoval>();
            foreach (var entry in itemRemovals) result.Add(entry);
            itemRemovals.Clear();
            return result;
        }
    }

    private static bool SameCoord(Int3 a, Int3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    private static bool SamePosition(Vec3 a, Vec3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    private void ResetSelectionChangeTracking()
    {
        lastSelectionChange.Clear();
        hasLastSelectionChange = false;
    }

    private bool SelectionChangedSinceLastEvent(IList<Int3> coords)
    {
        if (hasLastSelectionChange && lastSelectionChange.Count == coords.Count)
        {
            var matches = true;
            for (var i = 0; i < coords.Count; i++)
                if (!SameCoord(lastSelectionChange[i], coords[i])) { matches = false; break; }
            if (matches) return false;
        }

        lastSelectionChange.Clear();
        foreach (var coord in coords) lastSelectionChange.Add(coord);
        hasLastSelectionChange = true;
        return true;
    }

    private static bool SameItemBlock(ItemBlock a, ItemBlock b) =>
        a.MacroblockName == b.MacroblockName &&
        SameCoord(a.MacroblockCoord, b.MacroblockCoord) &&
        a.MacroblockDir == b.MacroblockDir &&
        SamePosition(a.ItemPosition, b.ItemPosition) &&
        a.Ground == b.Ground && a.Family == b.Family &&
        a.PieceIndex == b.PieceIndex && a.Width == b.Width && a.Depth == b.Depth;

    private static int IndexOfItemBlock(IList<ItemBlock> blocks, ItemBlock target)
    {
        for (var index = 0; index < blocks.Count; index++)
            if (SameItemBlock(blocks[index], target)) return index;
        return -1;
    }

    private static List<ItemBlock> WithoutItemBlockAt(IList<ItemBlock> blocks, int index)
    {
        var remaining = new List<ItemBlock>();
        for (var i = 0; i < blocks.Count; i++)
            if (i != index) remaining.Add(blocks[i]);
        return remaining;
    }

    public static int DirectionToIndex(CardinalDirections direction)
    {
        if (direction == CardinalDirections.East) return 1;
        if (direction == CardinalDirections.South) return 2;
        if (direction == CardinalDirections.West) return 3;
        return 0;
    }

    public static CardinalDirections DirectionFromIndex(int index)
    {
        if (index == 1) return CardinalDirections.East;
        if (index == 2) return CardinalDirections.South;
        if (index == 3) return CardinalDirections.West;
        return CardinalDirections.North;
    }

    public void SetItemBlockList(IList<ItemBlock> blocks)
    {
        var copy = new List<ItemBlock>();
        foreach (var block in blocks) copy.Add(block);
        Metadata<List<ItemBlock>>.For(Map, out var stored, name: "Atlas_ItemBlocks");
        stored.Value!.Clear();
        stored.Value.AddRange(copy);
    }

    public IList<ItemBlock> GetItemBlockList()
    {
        Metadata<List<ItemBlock>>.For(Map, out var stored, name: "Atlas_ItemBlocks");
        return stored.Value!;
    }

    public IList<Int3> GetRemovedWater()
    {
        Metadata<List<Int3>>.For(Map, out var stored, name: "Atlas_RemovedWater");
        return stored.Value!;
    }

    public void SetRemovedWater(IList<Int3> coords)
    {
        var copy = new List<Int3>();
        foreach (var coord in coords) copy.Add(coord);
        Metadata<List<Int3>>.For(Map, out var stored, name: "Atlas_RemovedWater");
        stored.Value!.Clear();
        foreach (var coord in copy) stored.Value.Add(coord);
    }

    public void SetItemBlockGroup(string family, List<List<List<string>>> variants) => itemBlockGroups[family] = variants;

    public void SetLayeredItemBlockGroup(string family, List<List<List<string>>> bottom,
        List<List<List<string>>> middle, List<List<List<string>>> top)
    {
        SetItemBlockGroup(family + "#bottom", bottom);
        SetItemBlockGroup(family + "#middle", middle);
        SetItemBlockGroup(family + "#top", top);
    }

    /// <summary>Maps cardinal connection masks to family piece indices (N=1, E=2, S=4, W=8).</summary>
    public void SetCubePieceMapping(string family, Dictionary<int, int> mapping)
    {
        var copy = new Dictionary<int, int>();
        foreach (var entry in mapping) copy[entry.Key] = entry.Value;
        cubePieceMapping[family] = copy;
    }

    public void SetRemoveWaterBlockMapping(Dictionary<string, string> mapping)
    {
        removeWaterMapping.Clear();
        foreach (var pair in mapping) removeWaterMapping[pair.Key] = pair.Value;
    }

    public void SetRestoreWaterBlockMapping(IList<string> voidNames, string waterVoid)
    {
        restoreWaterVoidNames.Clear();
        foreach (var name in voidNames) restoreWaterVoidNames.Add(name);
        waterVoidName = waterVoid;
    }

    public void SetTowerSelectionSize(int width, int depth)
    {
        var nextWidth = Math.Max(1, width);
        var nextDepth = Math.Max(1, depth);
        if (towerWidth != nextWidth || towerDepth != nextDepth) ResetSelectionChangeTracking();
        towerWidth = nextWidth;
        towerDepth = nextDepth;
    }
    public int GetRealGroundHeight(int x, int z) => GetGroundHeight(x, z);

    public int GetFakeGroundHeight(int x, int z)
    {
        var height = GetGroundHeight(x, z);
        foreach (var block in GetItemBlockList())
            if (block.Ground && x >= block.MacroblockCoord.X &&
                x < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                z >= block.MacroblockCoord.Z &&
                z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                block.MacroblockCoord.Y > height) height = block.MacroblockCoord.Y;
        return height;
    }

    public void SetSelectionMode(SelectionMode nextMode)
    {
        if (mode != nextMode) ResetSelectionChangeTracking();
        mode = nextMode;
        dragging = false;
        if (nextMode == SelectionMode.RemoveWater)
        {
            SetCurrentSelection(GetWaterSelectionCoords(GetRemovedWater()), true);
            CustomSelectionRGB = new Vec3(0.55f, 0.30f, 0.10f);
        }
        else if (nextMode == SelectionMode.RestoreWater)
        {
            SetCurrentSelection(GetRemovedWater(), true);
            CustomSelectionRGB = new Vec3(0.55f, 0.30f, 0.10f);
        }
        else ClearSelection();
    }

    private List<Int3> GetWaterSelectionCoords(IList<Int3> coords)
    {
        var result = new List<Int3>();
        foreach (var coord in coords) result.Add(new Int3(coord.X, CollectionGroundY, coord.Z));
        return result;
    }

    public void SetCurrentSelection(IList<Int3> coords, bool visible)
    {
        var copy = new List<Int3>();
        foreach (var coord in coords) copy.Add(coord);
        currentSelection.Clear();
        foreach (var coord in copy) currentSelection.Add(coord);
        selectionVisible = visible;
        DrawSelection();
    }

    public void ClearSelection()
    {
        currentSelection.Clear();
        selectionVisible = false;
        CustomSelectionCoords.Clear();
        HideCustomSelection();
    }

    private void DrawSelection()
    {
        CustomSelectionCoords.Clear();
        if (!selectionVisible) { HideCustomSelection(); return; }
        foreach (var coord in currentSelection) CustomSelectionCoords.Add(coord);
        ShowCustomSelection();
    }

    public void Initialize()
    {
        previousItems.Clear();
        foreach (var item in Items) previousItems.Add(item.Position);
        DrawSelection();
    }

    public void Update()
    {
        SyncManuallyRemovedItems();
        var pressed = Input.MouseLeftButton;
        if (mode == SelectionMode.None) { previousMouseDown = pressed; return; }
        var cursorCoord = Cursor.Coord;
        if (mode == SelectionMode.Ground2D || mode == SelectionMode.RemoveWater || mode == SelectionMode.RestoreWater)
        {
            cursorCoord = GetMouseCoordOnGround();
            if (mode == SelectionMode.RestoreWater)
                cursorCoord = new Int3(cursorCoord.X, CollectionGroundY, cursorCoord.Z);
        }

        // Keep the editor cursor on the same grid position used to build the preview.
        // Ground-based modes resolve their placement from the mouse ray rather than
        // the cursor's previous height, so without this the cursor and selection drift.
        var displayCursorCoord = cursorCoord;
        if (mode == SelectionMode.RemoveWater)
            displayCursorCoord = new Int3(cursorCoord.X, CollectionGroundY, cursorCoord.Z);
        Cursor.Coord = displayCursorCoord;

        if (mode != SelectionMode.None)
        {
            PlaceMode = EPlaceMode.CustomSelection;
        }

        if (mode == SelectionMode.Tower)
        {
            var towerEnd = new Int3(cursorCoord.X + Math.Max(1, towerWidth) - 1, cursorCoord.Y,
                cursorCoord.Z + Math.Max(1, towerDepth) - 1);
            var towerCoords = BuildSelection(cursorCoord, towerEnd, SelectionMode.Tower);
            DrawPreview(towerCoords);
            if (SelectionChangedSinceLastEvent(towerCoords))
                selectionChanged.Add(new SelectionChange { Start = cursorCoord, End = towerEnd, Coords = towerCoords });
            if (pressed && !previousMouseDown)
            {
                selectionConfirmed.Add(new SelectionChange { Start = cursorCoord, End = towerEnd, Coords = towerCoords });
                SetCurrentSelection(towerCoords, true);
            }
            previousMouseDown = pressed;
            return;
        }
        if (pressed && !previousMouseDown)
        {
            dragStart = cursorCoord;
            dragging = true;
            ResetSelectionChangeTracking();
        }
        if (dragging)
        {
            var coords = BuildSelection(dragStart, cursorCoord, mode);
            var change = new SelectionChange { Start = dragStart, End = cursorCoord, Coords = coords };
            if (pressed)
            {
                if (SelectionChangedSinceLastEvent(coords)) selectionChanged.Add(change);
                DrawPreview(coords);
            }
            else
            {
                selectionConfirmed.Add(change);
                if (mode == SelectionMode.RemoveWater) RemoveWater(dragStart, cursorCoord);
                else if (mode == SelectionMode.RestoreWater) RestoreWater(coords);
                else SetCurrentSelection(coords, true);
                dragging = false;
            }
        }
        else if (selectionVisible) DrawSelection();
        previousMouseDown = pressed;
    }

    private void DrawPreview(IList<Int3> coords)
    {
        CustomSelectionCoords.Clear();
        if (selectionVisible)
            foreach (var coord in currentSelection) CustomSelectionCoords.Add(coord);
        foreach (var coord in coords)
            if (!CustomSelectionCoords.Contains(coord)) CustomSelectionCoords.Add(coord);
        ShowCustomSelection();
    }

    public List<Int3> BuildSelection(Int3 start, Int3 end, SelectionMode selectionMode)
    {
        var result = new List<Int3>();
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minZ = Math.Min(start.Z, end.Z);
        var maxZ = Math.Max(start.Z, end.Z);
        if (selectionMode == SelectionMode.Line1D)
        {
            var dx = Math.Abs(end.X - start.X);
            var dy = Math.Abs(end.Y - start.Y);
            var dz = Math.Abs(end.Z - start.Z);
            var count = Math.Max(dx, Math.Max(dy, dz));
            for (var i = 0; i <= count; i++)
            {
                if (dx >= dy && dx >= dz)
                {
                    var step = i;
                    if (end.X < start.X) step = -i;
                    result.Add(new Int3(start.X + step, start.Y, start.Z));
                }
                else if (dy >= dz)
                {
                    var step = i;
                    if (end.Y < start.Y) step = -i;
                    result.Add(new Int3(start.X, start.Y + step, start.Z));
                }
                else
                {
                    var step = i;
                    if (end.Z < start.Z) step = -i;
                    result.Add(new Int3(start.X, start.Y, start.Z + step));
                }
            }
            return result;
        }
        var groundForBox = 0;
        if (selectionMode == SelectionMode.BoxToGround) groundForBox = GetFakeGroundHeight(minX, minZ);
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        {
            if (selectionMode == SelectionMode.Ground2D || selectionMode == SelectionMode.RemoveWater ||
                selectionMode == SelectionMode.RestoreWater)
            {
                var y = GetRealGroundHeight(x, z);
                if (selectionMode == SelectionMode.Ground2D) y = GetFakeGroundHeight(x, z);
                else if (selectionMode == SelectionMode.RemoveWater || selectionMode == SelectionMode.RestoreWater)
                    y = CollectionGroundY;
                result.Add(new Int3(x, y, z));
            }
            else if (selectionMode == SelectionMode.Plane2D) result.Add(new Int3(x, start.Y, z));
            else
            {
                var top = start.Y;
                var bottom = GetFakeGroundHeight(x, z);
                if (selectionMode == SelectionMode.Box3D)
                {
                    top = Math.Max(start.Y, end.Y);
                    bottom = Math.Min(start.Y, end.Y);
                }
                else if (selectionMode == SelectionMode.BoxToGround)
                {
                    if (GetFakeGroundHeight(x, z) != groundForBox) return new List<Int3>();
                    bottom = groundForBox;
                }
                if (selectionMode == SelectionMode.Tower) top = Math.Max(start.Y, end.Y);
                for (var y = bottom; y <= top; y++) result.Add(new Int3(x, y, z));
            }
        }
        return result;
    }

    public bool RemoveWater(Int3 start, Int3 end)
    {
        lastRollbackFailed = false;
        Log($"RemoveWater requested: from ({start.X}, {start.Y}, {start.Z}) to ({end.X}, {end.Y}, {end.Z}).");
        if (TerrainBlockModels.Count == 0)
        {
            Log("RemoveWater failed: no terrain block models are available.");
            return false;
        }

        var terrain = TerrainBlockModels[0];
        var minX = Math.Min(start.X, end.X);
        var maxX = Math.Max(start.X, end.X);
        var minZ = Math.Min(start.Z, end.Z);
        var maxZ = Math.Max(start.Z, end.Z);
        var originalGroundCoords = new List<Int3>();
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
            originalGroundCoords.Add(new Int3(x, GetRealGroundHeight(x, z), z));

        if (!PlaceTerrainBlocks(terrain, start, end))
        {
            Log($"RemoveWater failed: could not place {terrain.Name} from {start} to {end}.");
            return false;
        }

        var succeeded = true;
        Metadata<List<Int3>>.For(Map, out var storedWater, name: "Atlas_RemovedWater");
        foreach (var originalCoord in originalGroundCoords)
        {
            var coord = new Int3(originalCoord.X, GetRealGroundHeight(originalCoord.X, originalCoord.Z), originalCoord.Z);

            var old = GetBlock(coord);
            if (old == null)
            {
                Log($"RemoveWater failed: no block at ({coord.X}, {coord.Y}, {coord.Z}).");
                succeeded = false;
                continue;
            }
            if (!removeWaterMapping.ContainsKey(old.BlockModel.Name))
            {
                Log($"RemoveWater failed: block '{old.BlockModel.Name}' at ({coord.X}, {coord.Y}, {coord.Z}) has no void mapping.");
                succeeded = false;
                continue;
            }
            var voidName = removeWaterMapping[old.BlockModel.Name];
            var voidBlock = GetBlockModelFromName(voidName);
            if (voidBlock == null)
            {
                Log($"RemoveWater failed: mapped void block '{voidName}' was not found.");
                succeeded = false;
                continue;
            }

            if (!PlaceBlock(voidBlock, coord, CardinalDirections.North))
            {
                Log($"RemoveWater failed: could not place void block '{voidName}' at ({coord.X}, {coord.Y}, {coord.Z}).");
                succeeded = false;
                continue;
            }
            if (!storedWater.Value!.Contains(originalCoord)) storedWater.Value.Add(originalCoord);
        }
        SetCurrentSelection(GetWaterSelectionCoords(storedWater.Value!), true);
        CustomSelectionRGB = new Vec3(0.55f, 0.30f, 0.10f);
        return succeeded;
    }

    public bool RestoreWater(IList<Int3> coords)
    {
        lastRollbackFailed = false;
        if (coords.Count == 0) return false;
        var water = GetBlockModelFromName(waterVoidName);
        if (water == null)
        {
            Log($"RestoreWater failed: water block '{waterVoidName}' was not found.");
            return false;
        }
        Metadata<List<Int3>>.For(Map, out var storedWater, name: "Atlas_RemovedWater");
        var succeeded = true;
        var availableVoidCoords = new List<Int3>();
        var selectionCoords = new List<Int3>();
        foreach (var coord in coords)
            if (!selectionCoords.Contains(coord)) selectionCoords.Add(coord);

        // Snapshot the configured void blocks before phase 1 changes Blocks.
        foreach (var block in Blocks)
        {
            if (block == null) continue;
            if (restoreWaterVoidNames.Contains(block.BlockModel.Name) && !availableVoidCoords.Contains(block.Coord))
                availableVoidCoords.Add(block.Coord);
        }

        // 1. Try removing configured terrain and border void blocks at every selected coordinate.
        foreach (var groundCoord in selectionCoords)
        {
            var voidCoord = groundCoord;
            if (!availableVoidCoords.Contains(voidCoord))
                voidCoord = new Int3(groundCoord.X, CollectionGroundY + 1, groundCoord.Z);
            if (!availableVoidCoords.Contains(voidCoord)) continue;
            if (!RemoveBlock(voidCoord))
            {
                Log($"RestoreWater failed: could not remove configured void block at {voidCoord}.");
                succeeded = false;
            }
        }

        // 2. Remove terrain one coordinate at a time.
        var terrainReadyCoords = new List<Int3>();
        foreach (var groundCoord in selectionCoords)
        {
            if (!RemoveTerrainBlocks(groundCoord, groundCoord))
            {
                Log($"RestoreWater failed: could not remove terrain at {groundCoord}.");
                succeeded = false;
                continue;
            }
            terrainReadyCoords.Add(groundCoord);
        }

        // 3. Place water within the selection, removing matching metadata on success.
        foreach (var groundCoord in terrainReadyCoords)
        {
            if (!PlaceBlock(water, groundCoord, CardinalDirections.North))
            {
                Log($"RestoreWater failed: could not place water at {groundCoord}.");
                succeeded = false;
                continue;
            }
            var removedMetadata = new List<Int3>();
            foreach (var storedCoord in storedWater.Value!)
                if (storedCoord.X == groundCoord.X && storedCoord.Z == groundCoord.Z) removedMetadata.Add(storedCoord);
            foreach (var storedCoord in removedMetadata) storedWater.Value.Remove(storedCoord);
        }

        // 4. Try placing the configured border void outside the selection. Failure is expected.
        if (removeWaterMapping.ContainsKey("Beach"))
        {
            var beachVoidName = removeWaterMapping["Beach"];
            var beachVoid = GetBlockModelFromName(beachVoidName);
            if (beachVoid != null)
            {
                var outsideCoords = new List<Int3>();
                foreach (var coord in selectionCoords)
                {
                    var neighbors = new List<Int3>
                    {
                        new Int3(coord.X - 1, coord.Y, coord.Z),
                        new Int3(coord.X + 1, coord.Y, coord.Z),
                        new Int3(coord.X, coord.Y, coord.Z - 1),
                        new Int3(coord.X, coord.Y, coord.Z + 1),
                        new Int3(coord.X - 1, coord.Y, coord.Z - 1),
                        new Int3(coord.X - 1, coord.Y, coord.Z + 1),
                        new Int3(coord.X + 1, coord.Y, coord.Z - 1),
                        new Int3(coord.X + 1, coord.Y, coord.Z + 1)
                    };
                    foreach (var neighbor in neighbors)
                    {
                        var insideSelection = false;
                        foreach (var selected in selectionCoords)
                            if (selected.X == neighbor.X && selected.Z == neighbor.Z) insideSelection = true;
                        if (!insideSelection && !outsideCoords.Contains(neighbor)) outsideCoords.Add(neighbor);
                    }
                }
                foreach (var coord in outsideCoords)
                    PlaceBlock(beachVoid, coord, CardinalDirections.North);
            }
        }
        SetCurrentSelection(storedWater.Value!, true);
        CustomSelectionRGB = new Vec3(0.55f, 0.30f, 0.10f);
        return succeeded;
    }

    public bool PlaceItemBlock(string macroblockName, Int3 coord, CardinalDirections direction, bool ground,
        string family, int pieceIndex) =>
        PlaceItemBlockWithFootprint(macroblockName, coord, direction, ground, family, pieceIndex, 1, 1);

    public bool PlaceItemBlockWithFootprint(string macroblockName, Int3 coord, CardinalDirections direction,
        bool ground, string family, int pieceIndex, int width, int depth)
    {
        if (width < 1 || depth < 1) return false;
        var model = GetMacroblockModelFromFilePath(macroblockName);
        if (model == null || !CanPlaceMacroblock_NoDestruction(model, coord, direction)) return false;
        var previousCount = Items.Count;
        if (!PlaceMacroblock_NoDestruction(model, coord, direction)) return false;
        var position = Items.Count > previousCount ? Items[previousCount].Position : GetVec3FromCoord(coord);
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        storedBlocks.Value!.Add(new ItemBlock
        {
            MacroblockName = macroblockName, MacroblockCoord = coord, MacroblockDir = DirectionToIndex(direction),
            ItemPosition = position, Ground = ground, Family = family, PieceIndex = pieceIndex,
            Width = width, Depth = depth
        });
        SnapshotItems();
        return true;
    }

    public int RemoveItemBlocksAtCoord(Int3 coord) => RemoveItemBlocks(coord, "", -1);

    public int RemoveItemBlocksByName(Int3 coord, string macroblockName) =>
        RemoveItemBlocks(coord, macroblockName, -1);

    public int RemoveItemBlocks(Int3 coord, string macroblockName, int direction)
    {
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var remaining = new List<ItemBlock>();
        var count = 0;
        foreach (var block in storedBlocks.Value!)
        {
            if (InsideItemBlock(coord, block) &&
                (macroblockName == "" || block.MacroblockName == macroblockName) &&
                (direction < 0 || block.MacroblockDir == direction))
            {
                var model = GetMacroblockModelFromFilePath(block.MacroblockName);
                if (model != null && RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
                {
                    count++;
                    continue;
                }
            }
            remaining.Add(block);
        }
        if (count > 0) SetItemBlockList(remaining);
        SnapshotItems();
        return count;
    }

    private void SnapshotItems()
    {
        previousItems.Clear();
        foreach (var item in Items) previousItems.Add(item.Position);
    }

    public void SyncManuallyRemovedItems()
    {
        if (Items.Count > previousItems.Count)
        {
            for (var i = previousItems.Count; i < Items.Count; i++)
                previousItems.Add(Items[i].Position);
            return;
        }
        if (Items.Count == previousItems.Count) return;

        var counts = new Dictionary<Vec3, int>();
        foreach (var item in Items)
        {
            if (!counts.ContainsKey(item.Position)) counts[item.Position] = 0;
            counts[item.Position]++;
        }
        var removedPositions = new List<Vec3>();
        for (var i = previousItems.Count - 1; i >= 0; i--)
        {
            var position = previousItems[i];
            if (!counts.ContainsKey(position) || counts[position] == 0)
            {
                removedPositions.Add(position);
                previousItems.RemoveAt(i);
            }
            else counts[position]--;
        }
        foreach (var position in removedPositions)
        {
            itemRemovals.Add(new ItemRemoval
            {
                Position = position,
                RemovedBlocks = RemoveItemBlocksAtPosition(position)
            });
        }
        SnapshotItems();
    }

    private List<ItemBlock> RemoveItemBlocksAtPosition(Vec3 position)
    {
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var remaining = new List<ItemBlock>();
        var removed = new List<ItemBlock>();
        foreach (var block in storedBlocks.Value!)
        {
            if (SamePosition(block.ItemPosition, position))
            {
                var model = GetMacroblockModelFromFilePath(block.MacroblockName);
                if (model != null && RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
                {
                    removed.Add(block);
                    continue;
                }
            }
            remaining.Add(block);
        }
        if (removed.Count > 0) SetItemBlockList(remaining);
        return removed;
    }

    private static bool HasSide(int mask, int side) => mask % (side * 2) >= side;

    private static int AddSide(int mask, int side)
    {
        if (!HasSide(mask, side)) mask += side;
        return mask;
    }

    private static int RotateMask(int mask, int turns)
    {
        for (var turn = 0; turn < turns; turn++)
        {
            var rotated = 0;
            if (HasSide(mask, 1)) rotated += 2;
            if (HasSide(mask, 2)) rotated += 4;
            if (HasSide(mask, 4)) rotated += 8;
            if (HasSide(mask, 8)) rotated += 1;
            mask = rotated;
        }
        return mask;
    }

    public static int RoadPieceForMask(int mask)
    {
        if (mask == 0) return 0;
        if (mask == 15) return 5;
        var count = 0;
        if (HasSide(mask, 1)) count++;
        if (HasSide(mask, 2)) count++;
        if (HasSide(mask, 4)) count++;
        if (HasSide(mask, 8)) count++;
        if (count == 1) return 1;
        if (count == 3) return 4;
        if (mask == 5 || mask == 10) return 3;
        return 2;
    }

    public static CardinalDirections RoadDirectionForMask(int mask)
    {
        var piece = RoadPieceForMask(mask);
        var baseMask = 0;
        if (piece == 1) baseMask = 1;
        if (piece == 2) baseMask = 3;
        if (piece == 3) baseMask = 5;
        if (piece == 4) baseMask = 11;
        if (piece == 5) baseMask = 15;
        for (var turn = 0; turn < 4; turn++)
            if (RotateMask(baseMask, turn) == mask) return DirectionFromIndex(turn);
        return CardinalDirections.North;
    }

    private static int MaskForRoadPiece(int piece, CardinalDirections direction)
    {
        var mask = 0;
        if (piece == 1) mask = 1;
        if (piece == 2) mask = 3;
        if (piece == 3) mask = 5;
        if (piece == 4) mask = 11;
        if (piece == 5) mask = 15;
        return RotateMask(mask, DirectionToIndex(direction));
    }

    private string VariantFor(string family, int piece, bool ground, Int3 coord)
    {
        if (!itemBlockGroups.ContainsKey(family)) return "";
        var layers = itemBlockGroups[family];
        var layer = 0;
        if (ground) layer = 1;
        if (layer >= layers.Count || piece < 0 || piece >= layers[layer].Count) return "";
        var variants = layers[layer][piece];
        if (variants.Count == 0) return "";
        var seed = (coord.X * 31 + coord.Y * 17 + coord.Z * 13) % variants.Count;
        if (seed < 0) seed += variants.Count;
        return variants[seed];
    }

    private bool WithinMap(Int3 coord) => coord.X >= 0 && coord.Z >= 0 && coord.Y >= 0 &&
        coord.X < Map.Size.X && coord.Z < Map.Size.Z && coord.Y < Map.Size.Y;

    public bool ExecutePlacementPlan(IList<Placement> plan)
    {
        lastRollbackFailed = false;
        if (plan.Count == 0) return false;
        var previous = new List<ItemBlock>();
        var additions = new List<Placement>();
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        foreach (var entry in plan)
        {
            if (!WithinMap(entry.Coord) || GetMacroblockModelFromFilePath(entry.MacroblockName) == null) return false;
            foreach (var other in additions)
                if (FootprintsOverlap(entry, other)) return false;
            additions.Add(entry);
            var width = Math.Max(1, entry.Width);
            var depth = Math.Max(1, entry.Depth);
            for (var x = entry.Coord.X; x < entry.Coord.X + width; x++)
            for (var z = entry.Coord.Z; z < entry.Coord.Z + depth; z++)
                if (!WithinMap(new Int3(x, entry.Coord.Y, z))) return false;
            foreach (var block in storedBlocks.Value!)
            {
                if (!ItemBlockFootprintsOverlap(block, entry)) continue;
                if (block.Family != entry.Family) return false;
                if (SameCoord(block.MacroblockCoord, entry.Coord) &&
                    block.MacroblockName == entry.MacroblockName && block.MacroblockDir == DirectionToIndex(entry.Direction))
                    continue;
                if (IndexOfItemBlock(previous, block) < 0) previous.Add(block);
            }
        }
        var removed = new List<ItemBlock>();
        foreach (var block in previous)
        {
            var index = IndexOfItemBlock(storedBlocks.Value!, block);
            if (index < 0) { RestoreRemoved(removed); return false; }
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
            {
                RestoreRemoved(removed);
                return false;
            }
            SetItemBlockList(WithoutItemBlockAt(storedBlocks.Value!, index));
            removed.Add(block);
        }
        var placed = new List<Placement>();
        foreach (var entry in additions)
        {
            var alreadyThere = false;
            foreach (var block in GetItemBlockList())
                if (SameCoord(block.MacroblockCoord, entry.Coord) && block.MacroblockName == entry.MacroblockName &&
                    block.MacroblockDir == DirectionToIndex(entry.Direction)) alreadyThere = true;
            if (alreadyThere) continue;
            if (!PlaceItemBlockWithFootprint(entry.MacroblockName, entry.Coord, entry.Direction, entry.Ground,
                    entry.Family, entry.PieceIndex, entry.Width, entry.Depth))
            {
                foreach (var newBlock in placed)
                    if (RemoveItemBlocks(newBlock.Coord, newBlock.MacroblockName,
                            DirectionToIndex(newBlock.Direction)) == 0) lastRollbackFailed = true;
                RestoreRemoved(removed);
                return false;
            }
            placed.Add(entry);
        }
        SnapshotItems();
        return true;
    }

    private static bool InsideFootprint(Int3 coord, Placement placement) =>
        coord.Y == placement.Coord.Y && coord.X >= placement.Coord.X &&
        coord.X < placement.Coord.X + Math.Max(1, placement.Width) &&
        coord.Z >= placement.Coord.Z && coord.Z < placement.Coord.Z + Math.Max(1, placement.Depth);

    private static bool InsideItemBlock(Int3 coord, ItemBlock block) =>
        coord.Y == block.MacroblockCoord.Y && coord.X >= block.MacroblockCoord.X &&
        coord.X < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
        coord.Z >= block.MacroblockCoord.Z && coord.Z < block.MacroblockCoord.Z + Math.Max(1, block.Depth);

    private static bool ItemBlockFootprintsOverlap(ItemBlock block, Placement placement) =>
        block.MacroblockCoord.Y == placement.Coord.Y &&
        block.MacroblockCoord.X < placement.Coord.X + Math.Max(1, placement.Width) &&
        placement.Coord.X < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
        block.MacroblockCoord.Z < placement.Coord.Z + Math.Max(1, placement.Depth) &&
        placement.Coord.Z < block.MacroblockCoord.Z + Math.Max(1, block.Depth);

    private static bool FootprintsOverlap(Placement a, Placement b) =>
        a.Coord.Y == b.Coord.Y && a.Coord.X < b.Coord.X + Math.Max(1, b.Width) &&
        b.Coord.X < a.Coord.X + Math.Max(1, a.Width) &&
        a.Coord.Z < b.Coord.Z + Math.Max(1, b.Depth) &&
        b.Coord.Z < a.Coord.Z + Math.Max(1, a.Depth);

    private void RestoreRemoved(IList<ItemBlock> removed)
    {
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        foreach (var block in removed)
        {
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model != null && PlaceMacroblock_NoDestruction(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
                storedBlocks.Value!.Add(block);
            else lastRollbackFailed = true;
        }
        SnapshotItems();
    }

    /// <summary>Apply server-resolved item changes and replace the removed-water metadata snapshot.</summary>
    public bool ApplyResolvedChanges(IList<ItemBlock> toRemove, IList<Placement> toPlace,
        IList<Int3> removedWater)
    {
        lastRollbackFailed = false;
        var removed = new List<ItemBlock>();
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        foreach (var block in toRemove)
        {
            var index = IndexOfItemBlock(storedBlocks.Value!, block);
            if (index < 0) { RestoreRemoved(removed); return false; }
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
            {
                RestoreRemoved(removed);
                return false;
            }
            SetItemBlockList(WithoutItemBlockAt(storedBlocks.Value!, index));
            removed.Add(block);
        }
        if (toPlace.Count > 0 && !ExecutePlacementPlan(toPlace))
        {
            RestoreRemoved(removed);
            return false;
        }
        SetRemovedWater(removedWater);
        SnapshotItems();
        return true;
    }

    private static int SideBetween(Int3 first, Int3 second)
    {
        if (first.Y != second.Y) return 0;
        if (first.X == second.X && first.Z == second.Z + 1) return 1;
        if (first.X + 1 == second.X && first.Z == second.Z) return 2;
        if (first.X == second.X && first.Z + 1 == second.Z) return 4;
        if (first.X == second.X + 1 && first.Z == second.Z) return 8;
        return 0;
    }

    private static int OppositeSide(int side)
    {
        if (side == 1) return 4;
        if (side == 2) return 8;
        if (side == 4) return 1;
        if (side == 8) return 2;
        return 0;
    }

    private static int IndexOfCoord(IList<Int3> coords, Int3 coord)
    {
        for (var i = 0; i < coords.Count; i++) if (SameCoord(coords[i], coord)) return i;
        return -1;
    }

    public List<Placement> ResolveRoadPath(IList<Int3> path, string family)
    {
        var result = new List<Placement>();
        if (path.Count == 0) return result;
        var coords = new List<Int3>();
        var masks = new List<int>();
        foreach (var coord in path)
        {
            if (!WithinMap(coord) || coord.Y != GetFakeGroundHeight(coord.X, coord.Z)) return result;
            if (IndexOfCoord(coords, coord) < 0) { coords.Add(coord); masks.Add(0); }
        }
        for (var i = 1; i < path.Count; i++)
        {
            var side = SideBetween(path[i - 1], path[i]);
            if (side == 0) return result;
            var previousIndex = IndexOfCoord(coords, path[i - 1]);
            var currentIndex = IndexOfCoord(coords, path[i]);
            masks[previousIndex] = AddSide(masks[previousIndex], side);
            masks[currentIndex] = AddSide(masks[currentIndex], OppositeSide(side));
        }
        var selectedCount = coords.Count;
        for (var i = 0; i < selectedCount; i++)
        {
            foreach (var old in GetItemBlockList())
            {
                if (old.Family != family || old.MacroblockCoord.Y != coords[i].Y) continue;
                var side = SideBetween(coords[i], old.MacroblockCoord);
                if (side == 0) continue;
                var neighborIndex = IndexOfCoord(coords, old.MacroblockCoord);
                if (neighborIndex < 0)
                {
                    neighborIndex = coords.Count;
                    coords.Add(old.MacroblockCoord);
                    masks.Add(0);
                }
                masks[i] = AddSide(masks[i], side);
                masks[neighborIndex] = AddSide(masks[neighborIndex], OppositeSide(side));
            }
        }
        for (var i = 0; i < coords.Count; i++)
        {
            foreach (var old in GetItemBlockList())
            {
                if (!SameCoord(old.MacroblockCoord, coords[i])) continue;
                if (old.Family != family) return new List<Placement>();
                var oldMask = MaskForRoadPiece(old.PieceIndex, DirectionFromIndex(old.MacroblockDir));
                if (HasSide(oldMask, 1)) masks[i] = AddSide(masks[i], 1);
                if (HasSide(oldMask, 2)) masks[i] = AddSide(masks[i], 2);
                if (HasSide(oldMask, 4)) masks[i] = AddSide(masks[i], 4);
                if (HasSide(oldMask, 8)) masks[i] = AddSide(masks[i], 8);
            }
            var ground = coords[i].Y == GetFakeGroundHeight(coords[i].X, coords[i].Z);
            var piece = RoadPieceForMask(masks[i]);
            var variant = VariantFor(family, piece, ground, coords[i]);
            if (variant == "") return new List<Placement>();
            result.Add(new Placement { Coord = coords[i], MacroblockName = variant,
                Direction = RoadDirectionForMask(masks[i]), Ground = ground, Family = family, PieceIndex = piece });
        }
        return result;
    }

    public bool PlaceRoadPath(IList<Int3> path, string family) => ExecutePlacementPlan(ResolveRoadPath(path, family));

    public static FreeformCandidate ResolveFreeformOverlap(int oldPiece, CardinalDirections oldDirection,
        int newPiece, CardinalDirections newDirection)
    {
        var result = new FreeformCandidate { Piece = newPiece, Direction = newDirection, Priority = 0 };
        var difference = (DirectionToIndex(newDirection) - DirectionToIndex(oldDirection) + 4) % 4;
        var perpendicular = difference == 1 || difference == 3;
        var opposite = difference == 2;
        if (oldPiece == 11 && newPiece == 11 && perpendicular)
        { result.Piece = 4; result.Priority = 3; }
        else if (((oldPiece == 10 && newPiece == 9) || (oldPiece == 9 && newPiece == 10)) && opposite)
        { result.Piece = 3; result.Priority = 3; }
        else if (oldPiece == 9 && newPiece == 9 && opposite)
        { result.Piece = 2; result.Priority = 3; }
        else if ((oldPiece == 5 && (newPiece == 11 || newPiece == 12)) ||
                 ((oldPiece == 11 || oldPiece == 12) && newPiece == 5))
        { result.Piece = 1; result.Priority = 3; }
        else if (oldPiece == 9 && newPiece == 9 && perpendicular)
        { result.Piece = 0; result.Priority = 2; }
        else if (oldPiece == 12 && newPiece == 12 && !perpendicular)
        { result.Piece = 11; result.Priority = 2; }
        else if (oldPiece == 12 && newPiece == 12 && perpendicular)
        { result.Piece = 10; result.Priority = 2; }
        else if (oldPiece == 12 && newPiece == 11 && perpendicular)
        { result.Piece = 8; result.Priority = 2; }
        else if (oldPiece == 9 && newPiece == 12 && perpendicular)
        {
            result.Piece = 6;
            if (difference == 3) result.Piece = 7;
            result.Priority = 2;
        }
        return result;
    }

    public List<Placement> ResolveFreeform1x1(IList<Int3> selection, string family)
    {
        var result = new List<Placement>();
        if (selection.Count == 0) return result;
        var minX = selection[0].X;
        var maxX = minX;
        var minZ = selection[0].Z;
        var maxZ = minZ;
        var y = selection[0].Y;
        foreach (var coord in selection)
        {
            if (!WithinMap(coord) || coord.Y != y || coord.Y != GetFakeGroundHeight(coord.X, coord.Z)) return new List<Placement>();
            minX = Math.Min(minX, coord.X);
            maxX = Math.Max(maxX, coord.X);
            minZ = Math.Min(minZ, coord.Z);
            maxZ = Math.Max(maxZ, coord.Z);
        }
        if (selection.Count != (maxX - minX + 1) * (maxZ - minZ + 1)) return result;
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        {
            var coord = new Int3(x, y, z);
            if (IndexOfCoord(selection, coord) < 0) return new List<Placement>();
            var piece = 13;
            var direction = CardinalDirections.North;
            if (maxX > minX && maxZ == minZ)
            {
                piece = 11;
                direction = CardinalDirections.East;
                if (x == minX || x == maxX)
                {
                    piece = 12;
                    var inward = 2;
                    if (x == maxX) inward = 8;
                    direction = RoadDirectionForMask(15 - OppositeSide(inward));
                }
            }
            else if (maxZ > minZ && maxX == minX)
            {
                piece = 11;
                direction = CardinalDirections.North;
                if (z == minZ || z == maxZ)
                {
                    piece = 12;
                    var inward = 4;
                    if (z == maxZ) inward = 1;
                    direction = RoadDirectionForMask(15 - OppositeSide(inward));
                }
            }
            else if (maxX > minX && maxZ > minZ)
            {
                if ((x == minX || x == maxX) && (z == minZ || z == maxZ))
                {
                    piece = 9;
                    var mask = 0;
                    if (x == minX) mask += 2; else mask += 8;
                    if (z == minZ) mask += 4; else mask += 1;
                    direction = RoadDirectionForMask(mask);
                }
                else if (x == minX || x == maxX || z == minZ || z == maxZ)
                {
                    piece = 5;
                    if (x == minX) direction = CardinalDirections.East;
                    else if (x == maxX) direction = CardinalDirections.West;
                    else if (z == minZ) direction = CardinalDirections.South;
                }
                else piece = 4;
            }
            var seenStates = new List<int>();
            seenStates.Add(piece * 4 + DirectionToIndex(direction));
            for (var pass = 0; pass < 8; pass++)
            {
                var best = new FreeformCandidate { Piece = piece, Direction = direction, Priority = 0 };
                foreach (var old in GetItemBlockList())
                {
                    if (!SameCoord(old.MacroblockCoord, coord)) continue;
                    if (old.Family != family) return new List<Placement>();
                    var candidate = ResolveFreeformOverlap(old.PieceIndex, DirectionFromIndex(old.MacroblockDir), piece, direction);
                    if (candidate.Priority > best.Priority) best = candidate;
                    else if (candidate.Priority > 0 && candidate.Priority == best.Priority &&
                             (candidate.Piece != best.Piece || candidate.Direction != best.Direction))
                        return new List<Placement>();
                }
                if (best.Priority == 0 || (best.Piece == piece && best.Direction == direction)) break;
                var state = best.Piece * 4 + DirectionToIndex(best.Direction);
                if (seenStates.Contains(state) || pass == 7) return new List<Placement>();
                seenStates.Add(state);
                piece = best.Piece;
                direction = best.Direction;
            }
            var variant = VariantFor(family, piece, true, coord);
            if (variant == "") return new List<Placement>();
            result.Add(new Placement { Coord = coord, MacroblockName = variant, Direction = direction,
                Ground = true, Family = family, PieceIndex = piece, Width = 1, Depth = 1 });
        }
        return result;
    }

    public bool PlaceFreeform1x1(IList<Int3> selection, string family) =>
        ExecutePlacementPlan(ResolveFreeform1x1(selection, family));

    public List<Placement> ResolveCube2x2(IList<Int3> selection, string family)
    {
        var result = new List<Placement>();
        if (selection.Count == 0) return result;
        var minX = selection[0].X;
        var maxX = minX;
        var minY = selection[0].Y;
        var maxY = minY;
        var minZ = selection[0].Z;
        var maxZ = minZ;
        foreach (var coord in selection)
        {
            minX = Math.Min(minX, coord.X);
            maxX = Math.Max(maxX, coord.X);
            minY = Math.Min(minY, coord.Y);
            maxY = Math.Max(maxY, coord.Y);
            minZ = Math.Min(minZ, coord.Z);
            maxZ = Math.Max(maxZ, coord.Z);
        }
        var sizeX = maxX - minX + 1;
        var sizeZ = maxZ - minZ + 1;
        if (sizeX < 2 || sizeZ < 2 || sizeX != sizeZ || sizeX % 2 != 0 ||
            selection.Count != sizeX * sizeZ * (maxY - minY + 1)) return result;
        foreach (var coord in selection) if (!WithinMap(coord)) return new List<Placement>();
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        {
            var ground = GetFakeGroundHeight(x, z);
            if (ground != minY) return new List<Placement>();
            for (var y = minY; y <= maxY; y++)
                if (IndexOfCoord(selection, new Int3(x, y, z)) < 0) return new List<Placement>();
        }
        for (var x = minX; x <= maxX; x += 2)
        for (var z = minZ; z <= maxZ; z += 2)
        {
            var mask = 0;
            if (z > minZ) mask += 1;
            if (x < maxX - 1) mask += 2;
            if (z < maxZ - 1) mask += 4;
            if (x > minX) mask += 8;
            if (!cubePieceMapping.ContainsKey(family) || !cubePieceMapping[family].ContainsKey(mask))
                return new List<Placement>();
            var piece = cubePieceMapping[family][mask];
            for (var y = minY; y <= maxY; y++)
            {
                var layer = family + "#middle";
                if (y == minY) layer = family + "#bottom";
                if (y == maxY) layer = family + "#top";
                var anchor = new Int3(x, y, z);
                var variant = VariantFor(layer, piece, y == minY, anchor);
                if (variant == "") return new List<Placement>();
                result.Add(new Placement { Coord = anchor, MacroblockName = variant,
                    Direction = CardinalDirections.North, Ground = y == minY, Family = family,
                    PieceIndex = piece, Width = 2, Depth = 2 });
            }
        }
        return result;
    }

    public bool PlaceCube2x2(IList<Int3> selection, string family) =>
        ExecutePlacementPlan(ResolveCube2x2(selection, family));

    public List<Placement> ResolveTower(IList<Int3> selection, string family, int width, int depth)
    {
        var result = new List<Placement>();
        if (selection.Count == 0 || width < 1 || depth < 1) return result;
        var minX = selection[0].X;
        var maxX = minX;
        var minY = selection[0].Y;
        var maxY = minY;
        var minZ = selection[0].Z;
        var maxZ = minZ;
        foreach (var coord in selection)
        {
            minX = Math.Min(minX, coord.X);
            maxX = Math.Max(maxX, coord.X);
            minY = Math.Min(minY, coord.Y);
            maxY = Math.Max(maxY, coord.Y);
            minZ = Math.Min(minZ, coord.Z);
            maxZ = Math.Max(maxZ, coord.Z);
        }
        if ((maxX - minX + 1) % width != 0 || (maxZ - minZ + 1) % depth != 0 ||
            selection.Count != (maxX - minX + 1) * (maxZ - minZ + 1) * (maxY - minY + 1)) return result;
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        for (var y = minY; y <= maxY; y++)
            if (IndexOfCoord(selection, new Int3(x, y, z)) < 0 ||
                GetFakeGroundHeight(x, z) != minY) return new List<Placement>();
        for (var x = minX; x <= maxX; x += width)
        for (var z = minZ; z <= maxZ; z += depth)
        for (var y = minY; y <= maxY; y++)
        {
            var anchor = new Int3(x, y, z);
            var layer = family + "#middle";
            if (y == minY) layer = family + "#bottom";
            if (y == maxY) layer = family + "#top";
            var variant = VariantFor(layer, 0, y == minY, anchor);
            if (variant == "") return new List<Placement>();
            result.Add(new Placement { Coord = anchor, MacroblockName = variant, Direction = CardinalDirections.North,
                Ground = y == minY, Family = family, PieceIndex = 0, Width = width, Depth = depth });
        }
        return result;
    }

    public bool PlaceTower(IList<Int3> selection, string family, int width, int depth) =>
        ExecutePlacementPlan(ResolveTower(selection, family, width, depth));

    public List<Placement> ResolveFreeform2x2(IList<Int3> selection, string family, int height)
    {
        var result = new List<Placement>();
        if (selection.Count < 4 || height < 1) return result;
        var minX = selection[0].X;
        var minZ = selection[0].Z;
        foreach (var coord in selection)
        {
            minX = Math.Min(minX, coord.X);
            minZ = Math.Min(minZ, coord.Z);
        }
        var anchors = new List<Int3>();
        foreach (var coord in selection)
        {
            var x = minX + (coord.X - minX) / 2 * 2;
            var z = minZ + (coord.Z - minZ) / 2 * 2;
            var anchor = new Int3(x, coord.Y, z);
            if (IndexOfCoord(anchors, anchor) < 0) anchors.Add(anchor);
        }
        var connected = new List<Int3>();
        connected.Add(anchors[0]);
        var i = 0;
        while (i < connected.Count)
        {
            var anchor = connected[i];
            var north = new Int3(anchor.X, anchor.Y, anchor.Z - 2);
            var east = new Int3(anchor.X + 2, anchor.Y, anchor.Z);
            var south = new Int3(anchor.X, anchor.Y, anchor.Z + 2);
            var west = new Int3(anchor.X - 2, anchor.Y, anchor.Z);
            if (IndexOfCoord(anchors, north) >= 0 && IndexOfCoord(connected, north) < 0) connected.Add(north);
            if (IndexOfCoord(anchors, east) >= 0 && IndexOfCoord(connected, east) < 0) connected.Add(east);
            if (IndexOfCoord(anchors, south) >= 0 && IndexOfCoord(connected, south) < 0) connected.Add(south);
            if (IndexOfCoord(anchors, west) >= 0 && IndexOfCoord(connected, west) < 0) connected.Add(west);
            i++;
        }
        if (connected.Count != anchors.Count) return new List<Placement>();
        foreach (var anchor in anchors)
        {
            for (var x = anchor.X; x < anchor.X + 2; x++)
            for (var z = anchor.Z; z < anchor.Z + 2; z++)
                if (!WithinMap(new Int3(x, anchor.Y, z)) ||
                    IndexOfCoord(selection, new Int3(x, anchor.Y, z)) < 0 ||
                    GetFakeGroundHeight(x, z) != anchor.Y) return new List<Placement>();
            var mask = 0;
            if (IndexOfCoord(anchors, new Int3(anchor.X, anchor.Y, anchor.Z - 2)) >= 0) mask += 1;
            if (IndexOfCoord(anchors, new Int3(anchor.X + 2, anchor.Y, anchor.Z)) >= 0) mask += 2;
            if (IndexOfCoord(anchors, new Int3(anchor.X, anchor.Y, anchor.Z + 2)) >= 0) mask += 4;
            if (IndexOfCoord(anchors, new Int3(anchor.X - 2, anchor.Y, anchor.Z)) >= 0) mask += 8;
            var piece = 3;
            if (mask == 0 || mask == 15) piece = 0;
            else if (mask == 3 || mask == 6 || mask == 9 || mask == 12) piece = 2;
            else if (mask == 7 || mask == 11 || mask == 13 || mask == 14) piece = 1;
            var direction = RoadDirectionForMask(mask);
            for (var layerIndex = 0; layerIndex < height; layerIndex++)
            {
                var y = anchor.Y + layerIndex;
                var layer = family + "#middle";
                if (layerIndex == 0) layer = family + "#bottom";
                if (layerIndex == height - 1) layer = family + "#top";
                var placedAt = new Int3(anchor.X, y, anchor.Z);
                var variant = VariantFor(layer, piece, layerIndex == 0, placedAt);
                if (variant == "") return new List<Placement>();
                result.Add(new Placement { Coord = placedAt, MacroblockName = variant,
                    Direction = direction, Ground = layerIndex == 0, Family = family,
                    PieceIndex = piece, Width = 2, Depth = 2 });
            }
        }
        return result;
    }

    public bool PlaceFreeform2x2(IList<Int3> selection, string family, int height) =>
        ExecutePlacementPlan(ResolveFreeform2x2(selection, family, height));
}
