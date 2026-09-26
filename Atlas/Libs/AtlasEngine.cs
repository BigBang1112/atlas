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

    public enum FreeformPlacementMode
    {
        SelectionOnly,
        ConnectExisting
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

    public struct ItemBlockSubvariant
    {
        public string MacroblockName;
    }

    public struct ItemBlockVariant
    {
        public List<ItemBlockSubvariant> Subvariants;
        /// <summary>Clockwise quarter turns added to the resolved piece direction.</summary>
        public int DirectionOffset;
    }

    private struct SelectedItemBlockVariant
    {
        public string MacroblockName;
        public int DirectionOffset;
    }

    private struct NoItemReplacement
    {
        public Int3 Coord;
        public CardinalDirections Direction;
    }

    private struct NoItemRemoval
    {
        public bool Success;
        public List<NoItemReplacement> Removed;
    }

    private enum AtlasEditKind { Item, RemoveWater, RestoreWater }

    private struct AtlasEdit
    {
        public AtlasEditKind Kind;
        public List<ItemBlock> Before;
        public List<ItemBlock> After;
        public List<ItemBlock> AddedBlocks;
        public List<ItemBlock> RemovedBlocks;
        public List<NoItemReplacement> RemovedNoItemBlocks;
        public string NoItemBlockName;
        public Int3 Start;
        public Int3 End;
        public List<Int3> WaterCoords;
        public List<Int3> BeforeWater;
        public List<Int3> AfterWater;
    }

    public struct FreeformCandidate
    {
        public int Piece;
        public CardinalDirections Direction;
    }

    private readonly List<Int3> currentSelection = [];
    private bool selectionVisible;
    private bool dragging;
    private bool previousMouseDown;
    private bool selectionInputEnabled;
    private Int3 dragStart;
    private SelectionMode mode;
    private FreeformPlacementMode freeformPlacementMode;
    private int towerWidth;
    private int towerDepth;
    private bool lastRollbackFailed;
    private readonly List<Int3> lastSelectionChange = [];
    private bool hasLastSelectionChange;
    private bool emitSelectionChanged;
    private readonly List<SelectionChange> selectionChanged = [];
    private readonly List<SelectionChange> selectionConfirmed = [];
    private readonly List<ItemRemoval> itemRemovals = [];
    private readonly List<Vec3> previousItems = [];
    private readonly List<AtlasEdit> undoEdits = [];
    private readonly List<AtlasEdit> redoEdits = [];
    private bool replayingHistory;
    private int deferredItemSnapshotUpdates;
    private readonly Dictionary<string, string> removeWaterMapping = [];
    private readonly List<string> restoreWaterVoidNames = [];
    private string waterVoidName = "";
    private string noItemBlockName = "";
    private readonly Dictionary<string, List<List<ItemBlockVariant>>> itemBlockGroups = [];
    private readonly Dictionary<string, Dictionary<int, int>> cubePieceMapping = [];
    private readonly Dictionary<Int3, int> groundItemHeights = [];
    private bool groundItemHeightsValid;

    public SelectionMode Mode => mode;
    public FreeformPlacementMode FreeformMode => freeformPlacementMode;
    public bool LastRollbackFailed => lastRollbackFailed;
    public bool CanUndoAtlasEdit => undoEdits.Count > 0;
    public bool CanRedoAtlasEdit => redoEdits.Count > 0;
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

    private static bool SamePlacement(ItemBlock block, Placement placement) =>
        SameCoord(block.MacroblockCoord, placement.Coord) &&
        block.MacroblockName == placement.MacroblockName &&
        block.MacroblockDir == DirectionToIndex(placement.Direction) &&
        block.Ground == placement.Ground && block.Family == placement.Family &&
        block.PieceIndex == placement.PieceIndex &&
        block.Width == placement.Width && block.Depth == placement.Depth;

    private static int IndexOfItemBlock(IList<ItemBlock> blocks, ItemBlock target)
    {
        for (var index = 0; index < blocks.Count; index++)
            if (SameItemBlock(blocks[index], target)) return index;
        return -1;
    }

    private static List<ItemBlock> CopyItemBlocks(IList<ItemBlock> blocks)
    {
        var copy = new List<ItemBlock>();
        foreach (var block in blocks) copy.Add(block);
        return copy;
    }

    private static List<Int3> CopyCoords(IList<Int3> coords)
    {
        var copy = new List<Int3>();
        foreach (var coord in coords) copy.Add(coord);
        return copy;
    }

    private static bool SameCoordLists(IList<Int3> first, IList<Int3> second)
    {
        if (first.Count != second.Count) return false;
        var sameOrder = true;
        for (var i = 0; i < first.Count; i++)
            if (!SameCoord(first[i], second[i])) { sameOrder = false; break; }
        if (sameOrder) return true;
        var counts = new Dictionary<Int3, int>();
        foreach (var coord in second)
        {
            if (!counts.ContainsKey(coord)) counts[coord] = 0;
            counts[coord]++;
        }
        foreach (var coord in first)
        {
            if (!counts.ContainsKey(coord) || counts[coord] == 0) return false;
            counts[coord]--;
        }
        return true;
    }

    private static bool SameItemBlockLists(IList<ItemBlock> first, IList<ItemBlock> second)
    {
        if (first.Count != second.Count) return false;
        var sameOrder = true;
        for (var i = 0; i < first.Count; i++)
            if (!SameItemBlock(first[i], second[i])) { sameOrder = false; break; }
        return sameOrder || ItemBlockDifference(first, second).Count == 0;
    }

    private static List<ItemBlock> ItemBlockDifference(IList<ItemBlock> first, IList<ItemBlock> second)
    {
        var unmatched = new Dictionary<Int3, List<ItemBlock>>();
        foreach (var block in second)
        {
            var coord = block.MacroblockCoord;
            if (!unmatched.ContainsKey(coord)) unmatched[coord] = new List<ItemBlock>();
            var bucket = unmatched[coord];
            bucket.Add(block);
            unmatched[coord] = bucket;
        }
        var result = new List<ItemBlock>();
        foreach (var block in first)
        {
            var coord = block.MacroblockCoord;
            if (!unmatched.ContainsKey(coord)) { result.Add(block); continue; }
            var bucket = unmatched[coord];
            var index = IndexOfItemBlock(bucket, block);
            if (index < 0) result.Add(block);
            else
            {
                bucket.RemoveAt(index);
                unmatched[coord] = bucket;
            }
        }
        return result;
    }

    private void RecordItemEdit(IList<ItemBlock> before, IList<ItemBlock> after,
        IList<NoItemReplacement> removedNoItemBlocks)
    {
        if (replayingHistory) return;
        var removed = ItemBlockDifference(before, after);
        var added = ItemBlockDifference(after, before);
        if (removed.Count == 0 && added.Count == 0) return;
        var placeholders = new List<NoItemReplacement>();
        foreach (var entry in removedNoItemBlocks) placeholders.Add(entry);
        PushUndoEdit(new AtlasEdit { Kind = AtlasEditKind.Item,
            Before = CopyItemBlocks(before), After = CopyItemBlocks(after),
            AddedBlocks = added, RemovedBlocks = removed,
            RemovedNoItemBlocks = placeholders, NoItemBlockName = noItemBlockName });
    }

    private void RecordWaterEdit(AtlasEditKind kind, Int3 start, Int3 end, IList<Int3> coords,
        IList<Int3> before)
    {
        if (replayingHistory) return;
        PushUndoEdit(new AtlasEdit { Kind = kind, Start = start, End = end,
            WaterCoords = CopyCoords(coords), BeforeWater = CopyCoords(before),
            AfterWater = CopyCoords(GetRemovedWater()) });
    }

    private void PushUndoEdit(AtlasEdit edit)
    {
        undoEdits.Add(edit);
        if (undoEdits.Count > 64) undoEdits.RemoveAt(0);
        redoEdits.Clear();
    }

    public void ClearAtlasEditHistory()
    {
        undoEdits.Clear();
        redoEdits.Clear();
    }

    private bool ApplyItemSnapshot(IList<ItemBlock> target, IList<ItemBlock> toRemove,
        IList<ItemBlock> toPlaceBlocks)
    {
        var toPlace = new List<Placement>();
        foreach (var block in toPlaceBlocks)
        {
            toPlace.Add(new Placement { Coord = block.MacroblockCoord,
                MacroblockName = block.MacroblockName, Direction = DirectionFromIndex(block.MacroblockDir),
                Ground = block.Ground, Family = block.Family, PieceIndex = block.PieceIndex,
                Width = block.Width, Depth = block.Depth });
        }
        if (!ApplyResolvedChangesCore(toRemove, toPlace, GetRemovedWater(), false)) return false;
        SetItemBlockList(target);
        SnapshotItems();
        return true;
    }

    private bool ReplayWaterEdit(AtlasEdit edit, bool undo)
    {
        if (edit.Kind == AtlasEditKind.RemoveWater)
            return undo ? RestoreWater(edit.WaterCoords) : RemoveWater(edit.Start, edit.End);
        if (!undo) return RestoreWater(edit.WaterCoords);
        foreach (var coord in edit.WaterCoords)
            if (!RemoveWater(coord, coord)) return false;
        return true;
    }

    private void RefreshWaterSelection()
    {
        if (mode == SelectionMode.RemoveWater)
            SetCurrentSelection(GetWaterSelectionCoords(GetRemovedWater()), true);
        else if (mode == SelectionMode.RestoreWater)
            SetCurrentSelection(GetRemovedWater(), true);
    }

    public bool UndoAtlasEdit()
    {
        if (!CanUndoAtlasEdit) return false;
        var edit = undoEdits[undoEdits.Count - 1];
        if ((edit.Kind == AtlasEditKind.Item && !SameItemBlockLists(GetItemBlockList(), edit.After)) ||
            (edit.Kind != AtlasEditKind.Item && !SameCoordLists(GetRemovedWater(), edit.AfterWater)))
        { ClearAtlasEditHistory(); return false; }
        var configuredNoItemBlockName = noItemBlockName;
        if (edit.Kind == AtlasEditKind.Item) noItemBlockName = edit.NoItemBlockName;
        replayingHistory = true;
        var succeeded = false;
        if (edit.Kind == AtlasEditKind.Item)
        {
            succeeded = ApplyItemSnapshot(edit.Before, edit.AddedBlocks, edit.RemovedBlocks);
            if (succeeded)
            {
                lastRollbackFailed = false;
                Log($"Atlas undo: restoring {edit.RemovedNoItemBlocks.Count} no-item block(s).");
                RestoreNoItemBlocks(edit.RemovedNoItemBlocks);
                succeeded = !lastRollbackFailed;
                if (!succeeded) ApplyItemSnapshot(edit.After, edit.RemovedBlocks, edit.AddedBlocks);
                else SnapshotItems();
            }
        }
        else
        {
            succeeded = ReplayWaterEdit(edit, true);
            if (succeeded) SetRemovedWater(edit.BeforeWater);
            else if (ReplayWaterEdit(edit, false)) SetRemovedWater(edit.AfterWater);
            else lastRollbackFailed = true;
        }
        replayingHistory = false;
        if (edit.Kind == AtlasEditKind.Item) noItemBlockName = configuredNoItemBlockName;
        if (edit.Kind != AtlasEditKind.Item) RefreshWaterSelection();
        if (!succeeded)
        {
            if (lastRollbackFailed) ClearAtlasEditHistory();
            return false;
        }
        undoEdits.RemoveAt(undoEdits.Count - 1);
        redoEdits.Add(edit);
        return true;
    }

    public bool RedoAtlasEdit()
    {
        if (!CanRedoAtlasEdit) return false;
        var edit = redoEdits[redoEdits.Count - 1];
        if ((edit.Kind == AtlasEditKind.Item && !SameItemBlockLists(GetItemBlockList(), edit.Before)) ||
            (edit.Kind != AtlasEditKind.Item && !SameCoordLists(GetRemovedWater(), edit.BeforeWater)))
        { ClearAtlasEditHistory(); return false; }
        var configuredNoItemBlockName = noItemBlockName;
        if (edit.Kind == AtlasEditKind.Item) noItemBlockName = edit.NoItemBlockName;
        replayingHistory = true;
        var succeeded = edit.Kind == AtlasEditKind.Item
            ? ApplyItemSnapshot(edit.After, edit.RemovedBlocks, edit.AddedBlocks) : ReplayWaterEdit(edit, false);
        if (edit.Kind != AtlasEditKind.Item)
        {
            if (succeeded) SetRemovedWater(edit.AfterWater);
            else if (ReplayWaterEdit(edit, true)) SetRemovedWater(edit.BeforeWater);
            else lastRollbackFailed = true;
        }
        replayingHistory = false;
        if (edit.Kind == AtlasEditKind.Item) noItemBlockName = configuredNoItemBlockName;
        if (edit.Kind != AtlasEditKind.Item) RefreshWaterSelection();
        if (!succeeded)
        {
            if (lastRollbackFailed) ClearAtlasEditHistory();
            return false;
        }
        redoEdits.RemoveAt(redoEdits.Count - 1);
        undoEdits.Add(edit);
        return true;
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
        groundItemHeightsValid = false;
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
        if (!replayingHistory) ClearAtlasEditHistory();
        var copy = new List<Int3>();
        foreach (var coord in coords) copy.Add(coord);
        Metadata<List<Int3>>.For(Map, out var stored, name: "Atlas_RemovedWater");
        stored.Value!.Clear();
        foreach (var coord in copy) stored.Value.Add(coord);
    }

    public void SetItemBlockGroup(string family, List<List<ItemBlockVariant>> variants) => itemBlockGroups[family] = variants;

    public void SetFreeformPlacementMode(FreeformPlacementMode mode) => freeformPlacementMode = mode;

    public void SetLayeredItemBlockGroup(string family, List<List<ItemBlockVariant>> bottom,
        List<List<ItemBlockVariant>> middle, List<List<ItemBlockVariant>> top)
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
        ClearAtlasEditHistory();
        removeWaterMapping.Clear();
        foreach (var pair in mapping) removeWaterMapping[pair.Key] = pair.Value;
    }

    public void SetRestoreWaterBlockMapping(IList<string> voidNames, string waterVoid)
    {
        ClearAtlasEditHistory();
        restoreWaterVoidNames.Clear();
        foreach (var name in voidNames) restoreWaterVoidNames.Add(name);
        waterVoidName = waterVoid;
    }

    /// <summary>Block name or macroblock file path to replace under ground item blocks.</summary>
    public void SetNoItemBlockName(string blockName) => noItemBlockName = blockName;

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
        if (!groundItemHeightsValid)
        {
            groundItemHeights.Clear();
            foreach (var block in GetItemBlockList())
            {
                if (!block.Ground) continue;
                for (var cellX = block.MacroblockCoord.X; cellX < block.MacroblockCoord.X + Math.Max(1, block.Width); cellX++)
                for (var cellZ = block.MacroblockCoord.Z; cellZ < block.MacroblockCoord.Z + Math.Max(1, block.Depth); cellZ++)
                {
                    var cell = new Int3(cellX, 0, cellZ);
                    if (!groundItemHeights.ContainsKey(cell) || groundItemHeights[cell] < block.MacroblockCoord.Y)
                        groundItemHeights[cell] = block.MacroblockCoord.Y;
                }
            }
            groundItemHeightsValid = true;
        }
        var height = GetGroundHeight(x, z);
        var key = new Int3(x, 0, z);
        if (groundItemHeights.ContainsKey(key) && groundItemHeights[key] > height) height = groundItemHeights[key];
        return height;
    }

    public void SetSelectionMode(SelectionMode nextMode)
    {
        if (mode != nextMode) ResetSelectionChangeTracking();
        mode = nextMode;
        Cursor.HideDirectionalArrow = nextMode != SelectionMode.None;
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

    public void SetSelectionChangeEventsEnabled(bool enabled)
    {
        emitSelectionChanged = enabled;
        if (!enabled) selectionChanged.Clear();
    }

    public void SetSelectionInputEnabled(bool enabled)
    {
        if (selectionInputEnabled == enabled) return;
        selectionInputEnabled = enabled;
        if (enabled) return;

        // A drag that reaches the UI must not be confirmed when the mouse is released.
        dragging = false;
        previousMouseDown = Input.MouseLeftButton;
        ResetSelectionChangeTracking();
        DrawSelection();
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
        ClearAtlasEditHistory();
        deferredItemSnapshotUpdates = 0;
        selectionInputEnabled = true;
        groundItemHeightsValid = false;
        emitSelectionChanged = true;
        previousItems.Clear();
        foreach (var item in Items)
            if (item != null) previousItems.Add(item.Position);
        DrawSelection();
    }

    public void Update()
    {
        SyncManuallyRemovedItems();
        if (!selectionInputEnabled)
        {
            previousMouseDown = Input.MouseLeftButton;
            return;
        }
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
            if (pressed)
            {
                if (SelectionChangedSinceLastEvent(towerCoords))
                {
                    if (emitSelectionChanged)
                        selectionChanged.Add(new SelectionChange { Start = cursorCoord, End = towerEnd, Coords = towerCoords });
                    DrawPreview(towerCoords);
                }
                if (!previousMouseDown)
                    selectionConfirmed.Add(new SelectionChange { Start = cursorCoord, End = towerEnd, Coords = towerCoords });
            }
            else if (previousMouseDown) ClearSelection();
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
                if (SelectionChangedSinceLastEvent(coords))
                {
                    if (emitSelectionChanged) selectionChanged.Add(change);
                    DrawPreview(coords);
                }
            }
            else
            {
                selectionConfirmed.Add(change);
                if (mode == SelectionMode.RemoveWater) RemoveWater(dragStart, cursorCoord);
                else if (mode == SelectionMode.RestoreWater) RestoreWater(coords);
                else ClearSelection();
                dragging = false;
            }
        }
        else if (selectionVisible) DrawSelection();
        previousMouseDown = pressed;
    }

    private void DrawPreview(IList<Int3> coords)
    {
        CustomSelectionCoords.Clear();
        var shown = new Dictionary<Int3, bool>();
        if (selectionVisible)
            foreach (var coord in currentSelection)
            { CustomSelectionCoords.Add(coord); shown[coord] = true; }
        foreach (var coord in coords)
            if (!shown.ContainsKey(coord)) CustomSelectionCoords.Add(coord);
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
                var y = selectionMode == SelectionMode.Ground2D ? GetFakeGroundHeight(x, z) : CollectionGroundY;
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
                    if (bottom != groundForBox) return new List<Int3>();
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
        var beforeWater = CopyCoords(GetRemovedWater());
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
        if (succeeded) RecordWaterEdit(AtlasEditKind.RemoveWater, start, end, originalGroundCoords, beforeWater);
        else if (!replayingHistory) ClearAtlasEditHistory();
        return succeeded;
    }

    public bool RestoreWater(IList<Int3> coords)
    {
        lastRollbackFailed = false;
        if (coords.Count == 0) return false;
        var beforeWater = CopyCoords(GetRemovedWater());
        var water = GetBlockModelFromName(waterVoidName);
        if (water == null)
        {
            Log($"RestoreWater failed: water block '{waterVoidName}' was not found.");
            return false;
        }
        Metadata<List<Int3>>.For(Map, out var storedWater, name: "Atlas_RemovedWater");
        var succeeded = true;
        var availableVoidCoords = new Dictionary<Int3, bool>();
        var selectionCoords = new List<Int3>();
        var selectedCoords = new Dictionary<Int3, bool>();
        var selectedColumns = new Dictionary<Int3, bool>();
        foreach (var coord in coords)
            if (!selectedCoords.ContainsKey(coord))
            {
                selectionCoords.Add(coord);
                selectedCoords[coord] = true;
                selectedColumns[new Int3(coord.X, 0, coord.Z)] = true;
            }

        // Snapshot the configured void blocks before phase 1 changes Blocks.
        foreach (var block in Blocks)
        {
            if (block == null) continue;
            if (restoreWaterVoidNames.Contains(block.BlockModel.Name))
                availableVoidCoords[block.Coord] = true;
        }

        // 1. Try removing configured terrain and border void blocks at every selected coordinate.
        foreach (var groundCoord in selectionCoords)
        {
            var voidCoord = groundCoord;
            if (!availableVoidCoords.ContainsKey(voidCoord))
                voidCoord = new Int3(groundCoord.X, CollectionGroundY + 1, groundCoord.Z);
            if (!availableVoidCoords.ContainsKey(voidCoord)) continue;
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
        var restoredColumns = new Dictionary<Int3, bool>();
        foreach (var groundCoord in terrainReadyCoords)
        {
            if (!PlaceBlock(water, groundCoord, CardinalDirections.North))
            {
                Log($"RestoreWater failed: could not place water at {groundCoord}.");
                succeeded = false;
                continue;
            }
            restoredColumns[new Int3(groundCoord.X, 0, groundCoord.Z)] = true;
        }
        if (restoredColumns.Count > 0)
        {
            var remainingWater = new List<Int3>();
            foreach (var storedCoord in storedWater.Value!)
                if (!restoredColumns.ContainsKey(new Int3(storedCoord.X, 0, storedCoord.Z)))
                    remainingWater.Add(storedCoord);
            storedWater.Value!.Clear();
            foreach (var storedCoord in remainingWater) storedWater.Value.Add(storedCoord);
        }

        // 4. Try placing the configured border void outside the selection. Failure is expected.
        if (removeWaterMapping.ContainsKey("Beach"))
        {
            var beachVoidName = removeWaterMapping["Beach"];
            var beachVoid = GetBlockModelFromName(beachVoidName);
            if (beachVoid != null)
            {
                var outsideCoords = new List<Int3>();
                var outsideSeen = new Dictionary<Int3, bool>();
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
                        new Int3(coord.X + 1, coord.Y, coord.Z + 1),
                        new Int3(coord.X - 2, coord.Y, coord.Z - 2),
                        new Int3(coord.X - 2, coord.Y, coord.Z + 2),
                        new Int3(coord.X + 2, coord.Y, coord.Z - 2),
                        new Int3(coord.X + 2, coord.Y, coord.Z + 2)
                    };
                    foreach (var neighbor in neighbors)
                    {
                        if (selectedColumns.ContainsKey(new Int3(neighbor.X, 0, neighbor.Z)) ||
                            outsideSeen.ContainsKey(neighbor)) continue;
                        outsideCoords.Add(neighbor);
                        outsideSeen[neighbor] = true;
                    }
                }
                foreach (var coord in outsideCoords)
                    PlaceBlock(beachVoid, coord, CardinalDirections.North);
            }
        }
        SetCurrentSelection(storedWater.Value!, true);
        CustomSelectionRGB = new Vec3(0.55f, 0.30f, 0.10f);
        if (succeeded) RecordWaterEdit(AtlasEditKind.RestoreWater, selectionCoords[0], selectionCoords[0],
            selectionCoords, beforeWater);
        else if (!replayingHistory) ClearAtlasEditHistory();
        return succeeded;
    }

    public bool PlaceItemBlock(string macroblockName, Int3 coord, CardinalDirections direction, bool ground,
        string family, int pieceIndex) =>
        PlaceItemBlockWithFootprint(macroblockName, coord, direction, ground, family, pieceIndex, 1, 1);

    public bool PlaceItemBlockWithFootprint(string macroblockName, Int3 coord, CardinalDirections direction,
        bool ground, string family, int pieceIndex, int width, int depth)
    {
        lastRollbackFailed = false;
        if (width < 1 || depth < 1) return false;
        var before = CopyItemBlocks(GetItemBlockList());
        var model = GetMacroblockModelFromFilePath(macroblockName);
        if (model == null) return false;
        var removalResult = RemoveNoItemBlocks(coord, width, depth, ground);
        if (!removalResult.Success)
        {
            RestoreNoItemBlocks(removalResult.Removed);
            return false;
        }
        if (!CanPlaceMacroblock_NoDestruction(model, coord, direction))
        {
            RestoreNoItemBlocks(removalResult.Removed);
            return false;
        }
        var previousCount = Items.Count;
        if (!PlaceMacroblock_NoDestruction(model, coord, direction))
        {
            RestoreNoItemBlocks(removalResult.Removed);
            return false;
        }
        var position = Items.Count > previousCount ? Items[previousCount].Position : GetVec3FromCoord(coord);
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        storedBlocks.Value!.Add(new ItemBlock
        {
            MacroblockName = macroblockName, MacroblockCoord = coord, MacroblockDir = DirectionToIndex(direction),
            ItemPosition = position, Ground = ground, Family = family, PieceIndex = pieceIndex,
            Width = width, Depth = depth
        });
        groundItemHeightsValid = false;
        SnapshotItems();
        RecordItemEdit(before, GetItemBlockList(), removalResult.Removed);
        return true;
    }

    public int RemoveItemBlocksAtCoord(Int3 coord) => RemoveItemBlocks(coord, "", -1);

    public int RemoveItemBlocksByName(Int3 coord, string macroblockName) =>
        RemoveItemBlocks(coord, macroblockName, -1);

    public int RemoveItemBlocks(Int3 coord, string macroblockName, int direction)
    {
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var before = CopyItemBlocks(storedBlocks.Value!);
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
        if (count > 0) RecordItemEdit(before, remaining, new List<NoItemReplacement>());
        return count;
    }

    private void SnapshotItems()
    {
        if (replayingHistory)
        {
            // The editor can expose newly placed anchors before Position is valid.
            // Read them only after the replay has settled across update frames.
            deferredItemSnapshotUpdates = 2;
            return;
        }
        previousItems.Clear();
        foreach (var item in Items)
            if (item != null) previousItems.Add(item.Position);
    }

    public void SyncManuallyRemovedItems()
    {
        if (deferredItemSnapshotUpdates > 0)
        {
            deferredItemSnapshotUpdates--;
            if (deferredItemSnapshotUpdates == 0) SnapshotItems();
            return;
        }
        if (Items.Count > previousItems.Count)
        {
            SnapshotItems();
            return;
        }
        if (Items.Count == previousItems.Count) return;

        var counts = new Dictionary<Vec3, int>();
        foreach (var item in Items)
        {
            if (item == null) continue;
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
            var removedBlocks = RemoveItemBlocksAtPosition(position);
            // Editor item arrays can lag Atlas placements. Undo and redo validate
            // their metadata snapshots before replaying, so a count change alone
            // must not discard earlier edits.
            itemRemovals.Add(new ItemRemoval
            {
                Position = position,
                RemovedBlocks = removedBlocks
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

    private static int UnionMask(int first, int second)
    {
        if (HasSide(second, 1)) first = AddSide(first, 1);
        if (HasSide(second, 2)) first = AddSide(first, 2);
        if (HasSide(second, 4)) first = AddSide(first, 4);
        if (HasSide(second, 8)) first = AddSide(first, 8);
        return first;
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

    private SelectedItemBlockVariant VariantFor(string family, int piece, bool ground, Int3 coord)
    {
        if (!itemBlockGroups.ContainsKey(family)) return new SelectedItemBlockVariant { MacroblockName = "" };
        var layers = itemBlockGroups[family];
        var layer = 0;
        if (ground) layer = 1;
        if (layer >= layers.Count || piece < 0 || piece >= layers[layer].Count)
            return new SelectedItemBlockVariant { MacroblockName = "" };
        var variant = layers[layer][piece];
        if (variant.Subvariants.Count == 0)
            return new SelectedItemBlockVariant { MacroblockName = "" };
        var seed = (coord.X * 31 + coord.Y * 17 + coord.Z * 13) % variant.Subvariants.Count;
        if (seed < 0) seed += variant.Subvariants.Count;
        return new SelectedItemBlockVariant { MacroblockName = variant.Subvariants[seed].MacroblockName,
            DirectionOffset = variant.DirectionOffset };
    }

    private int DirectionOffsetFor(ItemBlock block)
    {
        if (!itemBlockGroups.ContainsKey(block.Family) || block.PieceIndex < 0) return 0;
        var layers = itemBlockGroups[block.Family];
        var layer = 0;
        if (block.Ground) layer = 1;
        if (layer >= layers.Count || block.PieceIndex >= layers[layer].Count) return 0;
        var variant = layers[layer][block.PieceIndex];
        foreach (var subvariant in variant.Subvariants)
            if (subvariant.MacroblockName == block.MacroblockName) return variant.DirectionOffset;
        return 0;
    }

    private static CardinalDirections OffsetDirection(CardinalDirections direction, int offset)
    {
        var index = (DirectionToIndex(direction) + offset) % 4;
        if (index < 0) index += 4;
        return DirectionFromIndex(index);
    }

    private CardinalDirections LogicalDirectionFor(ItemBlock block) =>
        OffsetDirection(DirectionFromIndex(block.MacroblockDir), -DirectionOffsetFor(block));

    private static int FreeformModelDirectionOffset(int piece)
    {
        // The freeform pieces use different model-facing axes for these four shapes.
        // Keep topology and overlap directions logical; convert only at the model boundary.
        if (piece == 0 || piece == 1) return 2;
        if (piece == 11 || piece == 12) return -1;
        return 0;
    }

    private CardinalDirections LogicalFreeformDirectionFor(ItemBlock block) =>
        OffsetDirection(LogicalDirectionFor(block), -FreeformModelDirectionOffset(block.PieceIndex));

    private bool WithinMap(Int3 coord) => coord.X >= 0 && coord.Z >= 0 && coord.Y >= 0 &&
        coord.X < Map.Size.X && coord.Z < Map.Size.Z && coord.Y < Map.Size.Y;

    private NoItemRemoval RemoveNoItemBlocks(Int3 coord, int width, int depth, bool ground)
    {
        var removed = new List<NoItemReplacement>();
        if (!ground || noItemBlockName == "")
            return new NoItemRemoval { Success = true, Removed = removed };
        var macroblockModel = GetMacroblockModelFromFilePath(noItemBlockName);
        if (macroblockModel == null && GetBlockModelFromName(noItemBlockName) == null)
        {
            Log($"No item block '{noItemBlockName}' was not found.");
            return new NoItemRemoval { Success = false, Removed = removed };
        }
        for (var x = coord.X; x < coord.X + width; x++)
        for (var z = coord.Z; z < coord.Z + depth; z++)
        {
            var cell = new Int3(x, CollectionGroundY, z);
            if (macroblockModel != null)
            {
                // Use the API result: the editor may publish its item and block
                // arrays after this call, so their counts are not authoritative yet.
                var heights = new List<int> { CollectionGroundY };
                if (coord.Y != CollectionGroundY) heights.Add(coord.Y);
                var removedAtCell = false;
                foreach (var y in heights)
                {
                    if (removedAtCell) break;
                    var candidate = new Int3(x, y, z);
                    for (var direction = 0; direction < 4; direction++)
                    {
                        if (!RemoveMacroblock(macroblockModel, candidate, DirectionFromIndex(direction)))
                            continue;
                        removed.Add(new NoItemReplacement
                        {
                            Coord = candidate, Direction = DirectionFromIndex(direction)
                        });
                        removedAtCell = true;
                        break;
                    }
                }
                continue;
            }
            var block = GetBlock(cell);
            if (block == null || block.BlockModel.Name != noItemBlockName) continue;
            if (!RemoveBlock(cell)) return new NoItemRemoval { Success = false, Removed = removed };
            var blockDirection = CardinalDirections.North;
            if (block.Direction == CBlock.CardinalDirections.East) blockDirection = CardinalDirections.East;
            else if (block.Direction == CBlock.CardinalDirections.South) blockDirection = CardinalDirections.South;
            else if (block.Direction == CBlock.CardinalDirections.West) blockDirection = CardinalDirections.West;
            removed.Add(new NoItemReplacement { Coord = cell, Direction = blockDirection });
        }
        return new NoItemRemoval { Success = true, Removed = removed };
    }

    private void RestoreNoItemBlocks(IList<NoItemReplacement> removed)
    {
        if (removed.Count == 0) return;
        var macroblockModel = GetMacroblockModelFromFilePath(noItemBlockName);
        if (macroblockModel != null)
        {
            foreach (var entry in removed)
                if (!PlaceMacroblock_NoDestruction(macroblockModel, entry.Coord, entry.Direction))
                {
                    Log($"Could not restore no-item macroblock '{noItemBlockName}' at {entry.Coord}.");
                    lastRollbackFailed = true;
                }
            return;
        }
        var blockModel = GetBlockModelFromName(noItemBlockName);
        foreach (var entry in removed)
            if (blockModel == null || !PlaceBlock(blockModel, entry.Coord, entry.Direction))
            {
                Log($"Could not restore no-item block '{noItemBlockName}' at {entry.Coord}.");
                lastRollbackFailed = true;
            }
    }

    public bool ExecutePlacementPlan(IList<Placement> plan)
    {
        lastRollbackFailed = false;
        if (plan.Count == 0) return false;
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var existing = storedBlocks.Value!;
        var existingByCell = new Dictionary<Int3, List<int>>();
        for (var index = 0; index < existing.Count; index++)
        {
            var block = existing[index];
            for (var x = block.MacroblockCoord.X; x < block.MacroblockCoord.X + Math.Max(1, block.Width); x++)
            for (var z = block.MacroblockCoord.Z; z < block.MacroblockCoord.Z + Math.Max(1, block.Depth); z++)
            {
                var cell = new Int3(x, block.MacroblockCoord.Y, z);
                if (!existingByCell.ContainsKey(cell)) existingByCell[cell] = new List<int>();
                existingByCell[cell].Add(index);
            }
        }
        var plannedCells = new Dictionary<Int3, bool>();
        var removedIndices = new Dictionary<int, bool>();
        foreach (var entry in plan)
        {
            if (!WithinMap(entry.Coord) || GetMacroblockModelFromFilePath(entry.MacroblockName) == null) return false;
            var width = Math.Max(1, entry.Width);
            var depth = Math.Max(1, entry.Depth);
            for (var x = entry.Coord.X; x < entry.Coord.X + width; x++)
            for (var z = entry.Coord.Z; z < entry.Coord.Z + depth; z++)
            {
                var cell = new Int3(x, entry.Coord.Y, z);
                if (!WithinMap(cell) || plannedCells.ContainsKey(cell)) return false;
                plannedCells[cell] = true;
                if (!existingByCell.ContainsKey(cell)) continue;
                foreach (var index in existingByCell[cell])
                {
                    var block = existing[index];
                    if (block.Family != entry.Family) return false;
                    if (SamePlacement(block, entry))
                        continue;
                    removedIndices[index] = true;
                }
            }
        }
        var removed = new List<ItemBlock>();
        for (var index = 0; index < existing.Count; index++)
        {
            if (!removedIndices.ContainsKey(index)) continue;
            var block = existing[index];
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
            {
                RollbackPlacementPlan(removed, new List<ItemBlock>(), new List<NoItemReplacement>());
                return false;
            }
            removed.Add(block);
        }
        var placed = new List<ItemBlock>();
        var removedNoItemBlocks = new List<NoItemReplacement>();
        foreach (var entry in plan)
        {
            var alreadyThere = false;
            if (existingByCell.ContainsKey(entry.Coord))
                foreach (var index in existingByCell[entry.Coord])
                {
                    var block = existing[index];
                    if (!removedIndices.ContainsKey(index) && SamePlacement(block, entry))
                        alreadyThere = true;
                }
            if (alreadyThere) continue;
            var model = GetMacroblockModelFromFilePath(entry.MacroblockName);
            var removalResult = RemoveNoItemBlocks(entry.Coord, Math.Max(1, entry.Width),
                Math.Max(1, entry.Depth), entry.Ground);
            foreach (var cell in removalResult.Removed) removedNoItemBlocks.Add(cell);
            if (!removalResult.Success)
            {
                RollbackPlacementPlan(removed, placed, removedNoItemBlocks);
                return false;
            }
            if (model == null || !CanPlaceMacroblock_NoDestruction(model, entry.Coord, entry.Direction))
            {
                RollbackPlacementPlan(removed, placed, removedNoItemBlocks);
                return false;
            }
            var previousCount = Items.Count;
            if (!PlaceMacroblock_NoDestruction(model, entry.Coord, entry.Direction))
            {
                RollbackPlacementPlan(removed, placed, removedNoItemBlocks);
                return false;
            }
            var position = !replayingHistory && Items.Count > previousCount
                ? Items[previousCount].Position : GetVec3FromCoord(entry.Coord);
            placed.Add(new ItemBlock
            {
                MacroblockName = entry.MacroblockName, MacroblockCoord = entry.Coord,
                MacroblockDir = DirectionToIndex(entry.Direction), ItemPosition = position,
                Ground = entry.Ground, Family = entry.Family, PieceIndex = entry.PieceIndex,
                Width = entry.Width, Depth = entry.Depth
            });
        }
        if (removed.Count > 0 || placed.Count > 0)
        {
            var before = CopyItemBlocks(existing);
            var updated = new List<ItemBlock>();
            for (var index = 0; index < existing.Count; index++)
                if (!removedIndices.ContainsKey(index)) updated.Add(existing[index]);
            foreach (var block in placed) updated.Add(block);
            SetItemBlockList(updated);
            SnapshotItems();
            RecordItemEdit(before, updated, removedNoItemBlocks);
        }
        return true;
    }

    private void RollbackPlacementPlan(IList<ItemBlock> removed, IList<ItemBlock> placed,
        IList<NoItemReplacement> removedNoItemBlocks)
    {
        // Metadata still describes the original layout until the whole plan succeeds.
        var failedToRemove = new List<ItemBlock>();
        for (var index = placed.Count - 1; index >= 0; index--)
        {
            var block = placed[index];
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
            { failedToRemove.Add(block); lastRollbackFailed = true; }
        }
        var failedToRestore = new List<ItemBlock>();
        foreach (var block in removed)
        {
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !PlaceMacroblock_NoDestruction(model, block.MacroblockCoord,
                    DirectionFromIndex(block.MacroblockDir)))
            { failedToRestore.Add(block); lastRollbackFailed = true; }
        }
        RestoreNoItemBlocks(removedNoItemBlocks);
        if (lastRollbackFailed)
        {
            var updated = new List<ItemBlock>();
            foreach (var block in GetItemBlockList())
                if (IndexOfItemBlock(failedToRestore, block) < 0) updated.Add(block);
            foreach (var block in failedToRemove) updated.Add(block);
            SetItemBlockList(updated);
        }
        SnapshotItems();
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
        var wasReplaying = replayingHistory;
        if (!wasReplaying) ClearAtlasEditHistory();
        replayingHistory = true;
        var result = ApplyResolvedChangesCore(toRemove, toPlace, removedWater, true);
        replayingHistory = wasReplaying;
        return result;
    }

    private bool ApplyResolvedChangesCore(IList<ItemBlock> toRemove, IList<Placement> toPlace,
        IList<Int3> removedWater, bool applyWater)
    {
        lastRollbackFailed = false;
        var removed = new List<ItemBlock>();
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var remaining = CopyItemBlocks(storedBlocks.Value!);
        foreach (var block in toRemove)
        {
            var index = IndexOfItemBlock(remaining, block);
            if (index < 0)
            {
                if (removed.Count > 0) SetItemBlockList(remaining);
                RestoreRemoved(removed);
                return false;
            }
            var model = GetMacroblockModelFromFilePath(block.MacroblockName);
            if (model == null || !RemoveMacroblock(model, block.MacroblockCoord, DirectionFromIndex(block.MacroblockDir)))
            {
                if (removed.Count > 0) SetItemBlockList(remaining);
                RestoreRemoved(removed);
                return false;
            }
            remaining.RemoveAt(index);
            removed.Add(block);
        }
        if (removed.Count > 0) SetItemBlockList(remaining);
        if (toPlace.Count > 0 && !ExecutePlacementPlan(toPlace))
        {
            RestoreRemoved(removed);
            return false;
        }
        if (applyWater) SetRemovedWater(removedWater);
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
                var oldMask = MaskForRoadPiece(old.PieceIndex, LogicalDirectionFor(old));
                if (HasSide(oldMask, 1)) masks[i] = AddSide(masks[i], 1);
                if (HasSide(oldMask, 2)) masks[i] = AddSide(masks[i], 2);
                if (HasSide(oldMask, 4)) masks[i] = AddSide(masks[i], 4);
                if (HasSide(oldMask, 8)) masks[i] = AddSide(masks[i], 8);
            }
            var ground = coords[i].Y == GetFakeGroundHeight(coords[i].X, coords[i].Z);
            var piece = RoadPieceForMask(masks[i]);
            var variant = VariantFor(family, piece, ground, coords[i]);
            if (variant.MacroblockName == "") return new List<Placement>();
            result.Add(new Placement { Coord = coords[i], MacroblockName = variant.MacroblockName,
                Direction = OffsetDirection(RoadDirectionForMask(masks[i]), variant.DirectionOffset),
                Ground = ground, Family = family, PieceIndex = piece });
        }
        return result;
    }

    public bool PlaceRoadPath(IList<Int3> path, string family) => ExecutePlacementPlan(ResolveRoadPath(path, family));

    public static FreeformCandidate ResolveFreeformOverlap(int oldPiece, CardinalDirections oldDirection,
        int newPiece, CardinalDirections newDirection)
    {
        var sides = UnionMask(FreeformSideMask(oldPiece, oldDirection),
            FreeformSideMask(newPiece, newDirection));
        var diagonals = UnionMask(FreeformDiagonalMask(oldPiece, oldDirection),
            FreeformDiagonalMask(newPiece, newDirection));
        return ResolveFreeformMasks(sides, diagonals, oldPiece == 14 || newPiece == 14);
    }

    // Both masks use clockwise bits 1, 2, 4, 8: N/E/S/W for sides,
    // NW/NE/SE/SW for diagonals.
    private static int FreeformSideMask(int piece, CardinalDirections direction)
    {
        var mask = 0;
        if (piece >= 0 && (piece <= 4 || piece == 14)) mask = 15;
        else if (piece >= 5 && piece <= 8) mask = 11;
        else if (piece == 9 || piece == 10) mask = 3;
        else if (piece == 11) mask = 5;
        else if (piece == 12) mask = 1;
        return RotateMask(mask, DirectionToIndex(direction));
    }

    // Diagonals supported by the shape itself, even when their cells are not tracked.
    private static int FreeformDiagonalMask(int piece, CardinalDirections direction)
    {
        var mask = 0;
        if (piece == 0) mask = 14;
        else if (piece == 1) mask = 12;
        else if (piece == 2) mask = 10;
        else if (piece == 3) mask = 8;
        else if (piece == 5) mask = 3;
        else if (piece == 6) mask = 2;
        else if (piece == 7) mask = 1;
        else if (piece == 9) mask = 2;
        else if (piece == 14) mask = 15;
        return RotateMask(mask, DirectionToIndex(direction));
    }

    private static int FreeformSideCount(int mask)
    {
        var count = 0;
        if (HasSide(mask, 1)) count++;
        if (HasSide(mask, 2)) count++;
        if (HasSide(mask, 4)) count++;
        if (HasSide(mask, 8)) count++;
        return count;
    }

    private static CardinalDirections DirectionForDiagonalMask(int mask, int northMask)
    {
        for (var turn = 0; turn < 4; turn++)
            if (RotateMask(northMask, turn) == mask) return DirectionFromIndex(turn);
        return CardinalDirections.North;
    }

    private static int FreeformOccupiedSides(Dictionary<Int3, bool> occupied, Int3 coord)
    {
        var mask = 0;
        if (occupied.ContainsKey(new Int3(coord.X, coord.Y, coord.Z - 1))) mask += 1;
        if (occupied.ContainsKey(new Int3(coord.X + 1, coord.Y, coord.Z))) mask += 2;
        if (occupied.ContainsKey(new Int3(coord.X, coord.Y, coord.Z + 1))) mask += 4;
        if (occupied.ContainsKey(new Int3(coord.X - 1, coord.Y, coord.Z))) mask += 8;
        return mask;
    }

    private static int FreeformOccupiedDiagonals(Dictionary<Int3, bool> occupied, Int3 coord)
    {
        var mask = 0;
        if (occupied.ContainsKey(new Int3(coord.X - 1, coord.Y, coord.Z - 1))) mask += 1;
        if (occupied.ContainsKey(new Int3(coord.X + 1, coord.Y, coord.Z - 1))) mask += 2;
        if (occupied.ContainsKey(new Int3(coord.X + 1, coord.Y, coord.Z + 1))) mask += 4;
        if (occupied.ContainsKey(new Int3(coord.X - 1, coord.Y, coord.Z + 1))) mask += 8;
        return mask;
    }

    private static FreeformCandidate ResolveFreeformMasks(int mask, int diagonals, bool hasFiller)
    {
        var missingDiagonals = 15 - diagonals;
        var sides = FreeformSideCount(mask);
        var direction = RoadDirectionForMask(mask);
        var piece = 13; // Isolated cross.
        if (sides == 1) piece = 12; // Line end.
        else if (sides == 2)
        {
            if (mask == 5 || mask == 10) piece = 11;
            else
            {
                var normalized = RotateMask(missingDiagonals, (4 - DirectionToIndex(direction)) % 4);
                piece = HasSide(normalized, 2) ? 10 : 9; // Open or filled inner corner.
            }
        }
        else if (sides == 3)
        {
            var normalized = RotateMask(missingDiagonals, (4 - DirectionToIndex(direction)) % 4);
            var missingLeft = HasSide(normalized, 1);
            var missingRight = HasSide(normalized, 2);
            piece = 5;
            if (missingLeft && missingRight) piece = 8;
            else if (missingLeft) piece = 6;
            else if (missingRight) piece = 7;
        }
        else if (sides == 4)
        {
            // Each missing diagonal leaves one exposed corner of an otherwise surrounded cell.
            if (missingDiagonals == 0) piece = hasFiller ? 14 : 4;
            else if (missingDiagonals == 15) piece = 4;
            else
            {
                var missingCount = 0;
                if (HasSide(missingDiagonals, 1)) missingCount++;
                if (HasSide(missingDiagonals, 2)) missingCount++;
                if (HasSide(missingDiagonals, 4)) missingCount++;
                if (HasSide(missingDiagonals, 8)) missingCount++;
                if (missingCount == 1)
                {
                    piece = 0;
                    direction = DirectionForDiagonalMask(missingDiagonals, 1);
                }
                else if (missingCount == 2 && (missingDiagonals == 5 || missingDiagonals == 10))
                {
                    piece = 2;
                    direction = DirectionForDiagonalMask(missingDiagonals, 5);
                }
                else if (missingCount == 2)
                {
                    piece = 1;
                    direction = DirectionForDiagonalMask(missingDiagonals, 3);
                }
                else
                {
                    piece = 3;
                    direction = DirectionForDiagonalMask(missingDiagonals, 7);
                }
            }
        }
        return new FreeformCandidate { Piece = piece, Direction = direction };
    }

    internal static FreeformCandidate ResolveFreeformCell(Dictionary<Int3, bool> occupied,
        Dictionary<Int3, bool> selected, Int3 coord, int oldPiece,
        CardinalDirections oldDirection, bool hasFiller)
    {
        var sides = FreeformOccupiedSides(occupied, coord);
        var diagonals = FreeformOccupiedDiagonals(occupied, coord);
        if (oldPiece >= 0)
        {
            sides = UnionMask(sides, FreeformSideMask(oldPiece, oldDirection));
            diagonals = UnionMask(diagonals, FreeformDiagonalMask(oldPiece, oldDirection));
        }
        if (oldPiece >= 0 && selected.ContainsKey(coord) &&
            FreeformSideCount(FreeformOccupiedSides(selected, coord)) >= 3 &&
            FreeformSideCount(sides) == 3)
        {
            // Only an area edge supplies both corner supports. A selected Corner
            // supplies one and can resolve to Deadend4 or Deadend8 instead.
            var edgeDirection = RoadDirectionForMask(sides);
            diagonals = UnionMask(diagonals, RotateMask(3, DirectionToIndex(edgeDirection)));
        }
        return ResolveFreeformMasks(sides, diagonals, hasFiller);
    }

    public List<Placement> ResolveFreeform1x1(IList<Int3> selection, string family)
    {
        var result = new List<Placement>();
        if (selection.Count == 0) return result;
        var minX = selection[0].X;
        var maxX = minX;
        var minZ = selection[0].Z;
        var maxZ = minZ;
        var selectedLookup = new Dictionary<Int3, bool>();
        var selectedByCell = new Dictionary<Int3, Int3>();
        foreach (var coord in selection)
        {
            if (!WithinMap(coord) || coord.Y != GetFakeGroundHeight(coord.X, coord.Z)) return new List<Placement>();
            var cell = new Int3(coord.X, 0, coord.Z);
            if (selectedByCell.ContainsKey(cell)) return result;
            selectedByCell[cell] = coord;
            selectedLookup[coord] = true;
            minX = Math.Min(minX, coord.X);
            maxX = Math.Max(maxX, coord.X);
            minZ = Math.Min(minZ, coord.Z);
            maxZ = Math.Max(maxZ, coord.Z);
        }
        if (selection.Count != (maxX - minX + 1) * (maxZ - minZ + 1)) return result;
        var selected = new List<Int3>();
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
        {
            var cell = new Int3(x, 0, z);
            if (!selectedByCell.ContainsKey(cell)) return new List<Placement>();
            selected.Add(selectedByCell[cell]);
        }

        // Rebuild cells beside the selection as well as cells it covers. Their visible
        // edges change when a neighboring rectangle joins them.
        var existing = GetItemBlockList();
        var occupied = new Dictionary<Int3, bool>();
        var affected = new List<Int3>();
        var affectedLookup = new Dictionary<Int3, bool>();
        var oldByCoord = new Dictionary<Int3, ItemBlock>();
        var duplicateCoords = new Dictionary<Int3, bool>();
        foreach (var coord in selected) { affected.Add(coord); affectedLookup[coord] = true; }
        foreach (var old in existing)
        {
            var oldCoord = old.MacroblockCoord;
            if (oldCoord.X >= minX - 1 && oldCoord.X <= maxX + 1 &&
                oldCoord.Z >= minZ - 1 && oldCoord.Z <= maxZ + 1)
            {
                if (oldByCoord.ContainsKey(oldCoord)) duplicateCoords[oldCoord] = true;
                else oldByCoord[oldCoord] = old;
            }
            if (oldCoord.X <= maxX && oldCoord.X + Math.Max(1, old.Width) > minX &&
                oldCoord.Z <= maxZ && oldCoord.Z + Math.Max(1, old.Depth) > minZ)
                foreach (var coord in selected)
                    if (InsideItemBlock(coord, old) && (!old.Ground || old.Width != 1 || old.Depth != 1))
                        return new List<Placement>();
            if (freeformPlacementMode == FreeformPlacementMode.SelectionOnly) continue;
            if (old.Family != family || oldCoord.X < minX - 2 || oldCoord.X > maxX + 2 ||
                oldCoord.Z < minZ - 2 || oldCoord.Z > maxZ + 2) continue;
            var nearby = false;
            var touchesSelection = false;
            for (var dx = -2; dx <= 2; dx++)
            for (var dz = -2; dz <= 2; dz++)
            {
                if (!selectedLookup.ContainsKey(new Int3(oldCoord.X + dx, oldCoord.Y, oldCoord.Z + dz))) continue;
                nearby = true;
                if (Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1) touchesSelection = true;
            }
            if (nearby && (!old.Ground || old.Width != 1 || old.Depth != 1)) return new List<Placement>();
            if (old.Ground && old.Width == 1 && old.Depth == 1)
            {
                occupied[oldCoord] = true;
                if (touchesSelection && !affectedLookup.ContainsKey(oldCoord))
                { affected.Add(oldCoord); affectedLookup[oldCoord] = true; }
            }
        }
        foreach (var coord in selected) occupied[coord] = true;

        var hasFiller = VariantFor(family, 14, true, selected[0]).MacroblockName != "";
        foreach (var coord in affected)
        {
            var hasOld = oldByCoord.ContainsKey(coord);
            var oldBlock = hasOld ? oldByCoord[coord] : new ItemBlock { MacroblockName = "" };
            if (hasOld && (oldBlock.Family != family || duplicateCoords.ContainsKey(coord)))
                return new List<Placement>();
            var oldLogicalDirection = CardinalDirections.North;
            if (hasOld) oldLogicalDirection = LogicalFreeformDirectionFor(oldBlock);
            var oldPiece = -1;
            if (hasOld) oldPiece = oldBlock.PieceIndex;
            var desired = ResolveFreeformCell(occupied, selectedLookup, coord,
                oldPiece, oldLogicalDirection, hasFiller);
            if (hasOld && oldBlock.PieceIndex == desired.Piece &&
                oldLogicalDirection == desired.Direction)
            {
                result.Add(new Placement { Coord = coord, MacroblockName = oldBlock.MacroblockName,
                    Direction = DirectionFromIndex(oldBlock.MacroblockDir), Ground = true,
                    Family = family, PieceIndex = oldBlock.PieceIndex, Width = 1, Depth = 1 });
                continue;
            }
            var variant = VariantFor(family, desired.Piece, true, coord);
            if (variant.MacroblockName == "") return new List<Placement>();
            result.Add(new Placement { Coord = coord, MacroblockName = variant.MacroblockName,
                Direction = OffsetDirection(desired.Direction,
                    variant.DirectionOffset + FreeformModelDirectionOffset(desired.Piece)),
                Ground = true, Family = family, PieceIndex = desired.Piece, Width = 1, Depth = 1 });
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
                if (variant.MacroblockName == "") return new List<Placement>();
                result.Add(new Placement { Coord = anchor, MacroblockName = variant.MacroblockName,
                    Direction = OffsetDirection(CardinalDirections.North, variant.DirectionOffset),
                    Ground = y == minY, Family = family,
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
            if (variant.MacroblockName == "") return new List<Placement>();
            result.Add(new Placement { Coord = anchor, MacroblockName = variant.MacroblockName,
                Direction = OffsetDirection(CardinalDirections.North, variant.DirectionOffset),
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
                if (variant.MacroblockName == "") return new List<Placement>();
                result.Add(new Placement { Coord = placedAt, MacroblockName = variant.MacroblockName,
                    Direction = OffsetDirection(direction, variant.DirectionOffset),
                    Ground = layerIndex == 0, Family = family,
                    PieceIndex = piece, Width = 2, Depth = 2 });
            }
        }
        return result;
    }

    public bool PlaceFreeform2x2(IList<Int3> selection, string family, int height) =>
        ExecutePlacementPlan(ResolveFreeform2x2(selection, family, height));
}
