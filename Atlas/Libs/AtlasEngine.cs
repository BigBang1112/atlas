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
        RemoveItemGroup,
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
        /// <summary>Old cell removed atomically when a freeform piece changes height.</summary>
        public bool HasReplacementCoord;
        public Int3 ReplacementCoord;
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

    private struct AtlasEdit
    {
        public List<ItemBlock> AddedBlocks;
        public List<ItemBlock> RemovedBlocks;
        public List<NoItemReplacement> RemovedNoItemBlocks;
        public List<NoItemReplacement> AddedNoItemBlocks;
        public string NoItemBlockName;
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
    private readonly Dictionary<string, int> itemBlockSelectionHeights = [];
    private readonly Dictionary<string, string> itemBlockSupports = [];
    private readonly Dictionary<string, int> itemBlockSupportPieces = [];
    private readonly Dictionary<string, int> groundCursorOffsets = [];
    private readonly Dictionary<string, int> groundPreviewHeights = [];
    private readonly Dictionary<string, int> freeformFillerHeights = [];
    private string baseGroundGroup = "";
    private string groundSelectionGroup = "";
    private readonly Dictionary<string, Dictionary<int, int>> cubePieceMapping = [];
    private readonly Dictionary<Int3, int> groundItemHeights = [];
    private readonly Dictionary<Int3, int> removedWaterGroundHeights = [];
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

    private static Dictionary<Int3, List<ItemBlock>> GroupItemBlocksByCoord(IList<ItemBlock> blocks)
    {
        var grouped = new Dictionary<Int3, List<ItemBlock>>();
        foreach (var block in blocks)
        {
            var coord = block.MacroblockCoord;
            if (!grouped.ContainsKey(coord)) grouped[coord] = new List<ItemBlock>();
            var bucket = grouped[coord];
            bucket.Add(block);
            grouped[coord] = bucket;
        }
        return grouped;
    }

    private static bool MatchesItemBlockChanges(IList<ItemBlock> blocks,
        IList<ItemBlock> expectedPresent, IList<ItemBlock> expectedAbsent)
    {
        var unmatched = GroupItemBlocksByCoord(blocks);
        foreach (var block in expectedAbsent)
        {
            var coord = block.MacroblockCoord;
            if (unmatched.ContainsKey(coord) && IndexOfItemBlock(unmatched[coord], block) >= 0)
                return false;
        }
        foreach (var block in expectedPresent)
        {
            var coord = block.MacroblockCoord;
            if (!unmatched.ContainsKey(coord)) return false;
            var bucket = unmatched[coord];
            var index = IndexOfItemBlock(bucket, block);
            if (index < 0) return false;
            bucket.RemoveAt(index);
            unmatched[coord] = bucket;
        }
        return true;
    }

    private static List<ItemBlock> ItemBlockDifference(IList<ItemBlock> first, IList<ItemBlock> second)
    {
        var unmatched = GroupItemBlocksByCoord(second);
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

    private void PushItemEdit(IList<ItemBlock> removed, IList<ItemBlock> added,
        IList<NoItemReplacement> removedNoItemBlocks)
    {
        PushItemEditWithBase(removed, added, removedNoItemBlocks, new List<NoItemReplacement>());
    }

    private void PushItemEditWithBase(IList<ItemBlock> removed, IList<ItemBlock> added,
        IList<NoItemReplacement> removedNoItemBlocks, IList<NoItemReplacement> addedNoItemBlocks)
    {
        if (replayingHistory || (removed.Count == 0 && added.Count == 0)) return;
        var placeholders = new List<NoItemReplacement>();
        foreach (var entry in removedNoItemBlocks) placeholders.Add(entry);
        var addedPlaceholders = new List<NoItemReplacement>();
        foreach (var entry in addedNoItemBlocks) addedPlaceholders.Add(entry);
        PushUndoEdit(new AtlasEdit {
            AddedBlocks = CopyItemBlocks(added), RemovedBlocks = CopyItemBlocks(removed),
            RemovedNoItemBlocks = placeholders, AddedNoItemBlocks = addedPlaceholders,
            NoItemBlockName = noItemBlockName });
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

    private bool ApplyItemChanges(IList<ItemBlock> toRemove, IList<ItemBlock> toPlaceBlocks)
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
        SnapshotItems();
        return true;
    }

    public bool UndoAtlasEdit()
    {
        if (!CanUndoAtlasEdit) return false;
        var edit = undoEdits[undoEdits.Count - 1];
        if (!MatchesItemBlockChanges(GetItemBlockList(), edit.AddedBlocks, edit.RemovedBlocks))
        { ClearAtlasEditHistory(); return false; }
        var configuredNoItemBlockName = noItemBlockName;
        noItemBlockName = edit.NoItemBlockName;
        replayingHistory = true;
        var succeeded = ApplyItemChanges(edit.AddedBlocks, edit.RemovedBlocks);
        if (succeeded)
        {
            lastRollbackFailed = false;
            Log($"Atlas undo: restoring {edit.RemovedNoItemBlocks.Count} no-item block(s).");
            RestoreNoItemBlocks(edit.RemovedNoItemBlocks);
            succeeded = !lastRollbackFailed;
            if (!succeeded)
            {
                var rolledBack = ApplyItemChanges(edit.RemovedBlocks, edit.AddedBlocks);
                if (rolledBack && edit.AddedNoItemBlocks.Count > 0)
                {
                    lastRollbackFailed = false;
                    RestoreNoItemBlocks(edit.AddedNoItemBlocks);
                    rolledBack = !lastRollbackFailed;
                }
                if (!rolledBack) lastRollbackFailed = true;
            }
            else SnapshotItems();
        }
        replayingHistory = false;
        noItemBlockName = configuredNoItemBlockName;
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
        if (!MatchesItemBlockChanges(GetItemBlockList(), edit.RemovedBlocks, edit.AddedBlocks))
        { ClearAtlasEditHistory(); return false; }
        var configuredNoItemBlockName = noItemBlockName;
        noItemBlockName = edit.NoItemBlockName;
        replayingHistory = true;
        var succeeded = ApplyItemChanges(edit.RemovedBlocks, edit.AddedBlocks);
        if (succeeded && edit.AddedNoItemBlocks.Count > 0)
        {
            lastRollbackFailed = false;
            RestoreNoItemBlocks(edit.AddedNoItemBlocks);
            succeeded = !lastRollbackFailed;
            if (!succeeded && !ApplyItemChanges(edit.AddedBlocks, edit.RemovedBlocks))
                lastRollbackFailed = true;
        }
        replayingHistory = false;
        noItemBlockName = configuredNoItemBlockName;
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
        groundItemHeightsValid = false;
    }

    public void SetItemBlockGroup(string family, List<List<ItemBlockVariant>> variants) => itemBlockGroups[family] = variants;

    /// <summary>Name the untracked group represented by the map's base ground.</summary>
    public void SetBaseGroundGroup(string family) => baseGroundGroup = family;

    /// <summary>Set the placement height above the supporting group and the required support.</summary>
    public void SetItemBlockGroundPlacement(string family, int selectionHeight, string allowedOnGroup,
        int allowedSupportPiece)
    {
        itemBlockSelectionHeights[family] = selectionHeight;
        itemBlockSupports[family] = allowedOnGroup;
        itemBlockSupportPieces[family] = allowedSupportPiece;
    }

    /// <summary>Configure the editor cursor and preview independently of placement height.</summary>
    public void SetGroundSelectionDisplay(string family, int cursorOffset, int previewHeight)
    {
        groundCursorOffsets[family] = Math.Max(0, cursorOffset);
        groundPreviewHeights[family] = Math.Max(1, previewHeight);
    }

    /// <summary>Raise the freeform interior filler above the piece it replaces.</summary>
    public void SetFreeformFillerHeight(string family, int height)
    {
        freeformFillerHeights[family] = Math.Max(0, height);
    }

    private int FreeformFillerHeight(string family, int piece)
    {
        if (piece == 14 && freeformFillerHeights.ContainsKey(family))
            return freeformFillerHeights[family];
        return 0;
    }

    private bool IsRaisedFreeformFiller(ItemBlock block, string family) =>
        block.Family == family && block.PieceIndex == 14 && FreeformFillerHeight(family, 14) > 0;

    private bool IsAllowedSupport(ItemBlock block, string family) =>
        itemBlockSupports.ContainsKey(family) && block.Ground &&
        block.Family == itemBlockSupports[family] &&
        (itemBlockSupportPieces[family] < 0 || block.PieceIndex == itemBlockSupportPieces[family]);

    private bool CanReplaceSupport(ItemBlock block, string family, Int3 coord) =>
        IsAllowedSupport(block, family) && itemBlockSelectionHeights[family] == 0 &&
        block.Width == 1 && block.Depth == 1 && SameCoord(block.MacroblockCoord, coord);

    public void SetGroundSelectionGroup(string family)
    {
        if (groundSelectionGroup == family) return;
        groundSelectionGroup = family;
        ResetSelectionChangeTracking();
        ClearSelection();
    }

    private int GetGroupGroundHeight(int x, int z, string family)
    {
        if (!itemBlockSupports.ContainsKey(family)) return GetFakeGroundHeight(x, z);
        var support = itemBlockSupports[family];
        var height = -1;
        if (support == baseGroundGroup)
        {
            // The base group is untracked; removed-water metadata retains
            // its original ground height when the terrain has changed.
            GetFakeGroundHeight(x, z);
            var cell = new Int3(x, 0, z);
            height = removedWaterGroundHeights.ContainsKey(cell)
                ? removedWaterGroundHeights[cell] : GetGroundHeight(x, z);
        }
        else
        {
            // Once a support piece is replaced, the new family remains selectable.
            var ownHeight = -1;
            foreach (var block in GetItemBlockList())
                if (block.Ground && block.Family == family &&
                    x >= block.MacroblockCoord.X && x < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                    z >= block.MacroblockCoord.Z && z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                    block.MacroblockCoord.Y > ownHeight)
                    ownHeight = block.MacroblockCoord.Y;
            if (ownHeight >= 0) return ownHeight;
            foreach (var block in GetItemBlockList())
                if (IsAllowedSupport(block, family) &&
                    x >= block.MacroblockCoord.X && x < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                    z >= block.MacroblockCoord.Z && z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                    block.MacroblockCoord.Y > height)
                    height = block.MacroblockCoord.Y;
        }
        if (height < 0)
        {
            return -1;
        }

        return height + itemBlockSelectionHeights[family];
    }

    private bool HasAllowedGroundSupport(Int3 coord, string family)
    {
        if (!itemBlockSupports.ContainsKey(family)) return true;
        if (coord.Y != GetGroupGroundHeight(coord.X, coord.Z, family)) return false;
        if (itemBlockSupports[family] != baseGroundGroup)
        {
            foreach (var block in GetItemBlockList())
                if (block.Ground && block.Family == family &&
                    coord.X >= block.MacroblockCoord.X && coord.X < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                    coord.Z >= block.MacroblockCoord.Z && coord.Z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                    coord.Y == block.MacroblockCoord.Y) return true;
            foreach (var block in GetItemBlockList())
                if (IsAllowedSupport(block, family) &&
                    coord.X >= block.MacroblockCoord.X && coord.X < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                    coord.Z >= block.MacroblockCoord.Z && coord.Z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                    coord.Y == block.MacroblockCoord.Y + itemBlockSelectionHeights[family]) return true;
            return false;
        }
        // A base-ground group cannot be started on another tracked ground family.
        foreach (var block in GetItemBlockList())
            if (block.Ground && block.Family != family &&
                coord.X >= block.MacroblockCoord.X && coord.X < block.MacroblockCoord.X + Math.Max(1, block.Width) &&
                coord.Z >= block.MacroblockCoord.Z && coord.Z < block.MacroblockCoord.Z + Math.Max(1, block.Depth) &&
                block.MacroblockCoord.Y >= coord.Y) return false;
        return true;
    }

    private Int3 GetMouseCoordForGroundGroup(string family)
    {
        var offset = groundCursorOffsets[family];
        var previousY = Math.Max(0, Math.Min(Map.Size.Y - 1, Cursor.Coord.Y));
        var candidateHeights = new Dictionary<int, bool> { [previousY] = true };
        var groundMouse = GetMouseCoordOnGround();
        var baseY = GetGroupGroundHeight(groundMouse.X, groundMouse.Z, family);
        if (baseY >= 0 && baseY + offset < Map.Size.Y)
            candidateHeights[baseY + offset] = true;
        foreach (var block in GetItemBlockList())
        {
            var placementY = -1;
            if (block.Ground && block.Family == family) placementY = block.MacroblockCoord.Y;
            else if (IsAllowedSupport(block, family))
                placementY = block.MacroblockCoord.Y + itemBlockSelectionHeights[family];
            if (placementY >= 0 && placementY + offset < Map.Size.Y)
                candidateHeights[placementY + offset] = true;
        }

        var bestY = -1;
        var bestCoord = new Int3(groundMouse.X, 0, groundMouse.Z);
        foreach (var displayY in candidateHeights.Keys)
        {
            var hit = GetMouseCoordAtHeight(displayY);
            if (!WithinMap(hit)) continue;
            var placementY = GetGroupGroundHeight(hit.X, hit.Z, family);
            if (placementY < 0 || placementY + offset != displayY || displayY <= bestY) continue;
            bestY = displayY;
            bestCoord = new Int3(hit.X, placementY, hit.Z);
        }
        if (bestY >= 0) return bestCoord;
        var fallback = GetMouseCoordAtHeight(previousY);
        return new Int3(fallback.X, Math.Max(0, previousY - offset), fallback.Z);
    }

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

    private void UpdateGroundItemHeights(IList<ItemBlock> removed, IList<ItemBlock> added)
    {
        if (!groundItemHeightsValid) return;
        foreach (var block in removed)
            if (block.Ground)
            {
                groundItemHeightsValid = false;
                return;
            }
        foreach (var block in added)
        {
            if (!block.Ground) continue;
            for (var cellX = block.MacroblockCoord.X;
                cellX < block.MacroblockCoord.X + Math.Max(1, block.Width); cellX++)
            for (var cellZ = block.MacroblockCoord.Z;
                cellZ < block.MacroblockCoord.Z + Math.Max(1, block.Depth); cellZ++)
            {
                var cell = new Int3(cellX, 0, cellZ);
                if (!groundItemHeights.ContainsKey(cell) ||
                    groundItemHeights[cell] < block.MacroblockCoord.Y)
                    groundItemHeights[cell] = block.MacroblockCoord.Y;
            }
        }
    }

    public int GetFakeGroundHeight(int x, int z)
    {
        if (!groundItemHeightsValid)
        {
            groundItemHeights.Clear();
            removedWaterGroundHeights.Clear();
            foreach (var coord in GetRemovedWater())
            {
                var cell = new Int3(coord.X, 0, coord.Z);
                if (!removedWaterGroundHeights.ContainsKey(cell) ||
                    coord.Y < removedWaterGroundHeights[cell])
                    removedWaterGroundHeights[cell] = coord.Y;
            }
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
        var key = new Int3(x, 0, z);
        // Removed-water terrain changes the real ground height. Ground items still
        // use the height saved before that terrain was placed.
        var height = removedWaterGroundHeights.ContainsKey(key)
            ? removedWaterGroundHeights[key] : GetGroundHeight(x, z);
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
        else
        {
            ClearSelection();
            CustomSelectionRGB = nextMode == SelectionMode.RemoveItemGroup
                ? new Vec3(0.85f, 0.20f, 0.15f) : new Vec3(1.0f, 1.0f, 1.0f);
        }
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
        AddSelectionVisualCoords(currentSelection);
        ShowCustomSelection();
    }

    private void AddSelectionVisualCoords(IList<Int3> coords)
    {
        var shown = new Dictionary<Int3, bool>();
        var offset = 0;
        var height = 1;
        if (mode == SelectionMode.Ground2D && groundCursorOffsets.ContainsKey(groundSelectionGroup))
        {
            offset = groundCursorOffsets[groundSelectionGroup];
            height = groundPreviewHeights[groundSelectionGroup];
        }
        foreach (var coord in coords)
            for (var layer = 0; layer < height; layer++)
            {
                var visual = new Int3(coord.X, coord.Y + offset + layer, coord.Z);
                if (visual.Y >= Map.Size.Y || shown.ContainsKey(visual)) continue;
                CustomSelectionCoords.Add(visual);
                shown[visual] = true;
            }
    }

    public void Initialize()
    {
        ClearAtlasEditHistory();
        deferredItemSnapshotUpdates = 0;
        selectionInputEnabled = true;
        groundItemHeightsValid = false;
        emitSelectionChanged = true;
        SnapshotItems();
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
        if (mode == SelectionMode.Ground2D || mode == SelectionMode.RemoveItemGroup ||
            mode == SelectionMode.RemoveWater || mode == SelectionMode.RestoreWater)
        {
            if ((mode == SelectionMode.Ground2D || mode == SelectionMode.RemoveItemGroup) &&
                groundSelectionGroup != "" &&
                groundCursorOffsets.ContainsKey(groundSelectionGroup) &&
                groundCursorOffsets[groundSelectionGroup] > 0)
                cursorCoord = GetMouseCoordForGroundGroup(groundSelectionGroup);
            else
            {
                cursorCoord = GetMouseCoordOnGround();
                if ((mode == SelectionMode.Ground2D || mode == SelectionMode.RemoveItemGroup) &&
                    groundSelectionGroup != "")
                {
                    var groupHeight = GetGroupGroundHeight(cursorCoord.X, cursorCoord.Z,
                        groundSelectionGroup);
                    if (groupHeight >= 0)
                        cursorCoord = new Int3(cursorCoord.X, groupHeight, cursorCoord.Z);
                }
            }
            if (mode == SelectionMode.RestoreWater)
                cursorCoord = new Int3(cursorCoord.X, CollectionGroundY, cursorCoord.Z);
        }

        // Placement keeps the group's support Y; the visible cursor uses its display Y.
        var displayCursorCoord = cursorCoord;
        if (mode == SelectionMode.RemoveWater)
            displayCursorCoord = new Int3(cursorCoord.X, CollectionGroundY, cursorCoord.Z);
        else if ((mode == SelectionMode.Ground2D || mode == SelectionMode.RemoveItemGroup) &&
                 groundCursorOffsets.ContainsKey(groundSelectionGroup))
            displayCursorCoord = new Int3(cursorCoord.X,
                Math.Min(Map.Size.Y - 1, cursorCoord.Y + groundCursorOffsets[groundSelectionGroup]), cursorCoord.Z);
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
        var combined = new List<Int3>();
        if (selectionVisible)
            foreach (var coord in currentSelection) combined.Add(coord);
        foreach (var coord in coords) combined.Add(coord);
        AddSelectionVisualCoords(combined);
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
            if (selectionMode == SelectionMode.Ground2D || selectionMode == SelectionMode.RemoveItemGroup ||
                selectionMode == SelectionMode.RemoveWater ||
                selectionMode == SelectionMode.RestoreWater)
            {
                var y = selectionMode == SelectionMode.Ground2D || selectionMode == SelectionMode.RemoveItemGroup
                    ? groundSelectionGroup == "" ? GetFakeGroundHeight(x, z)
                        : GetGroupGroundHeight(x, z, groundSelectionGroup)
                    : CollectionGroundY;
                if (y < 0) return new List<Int3>();
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
        groundItemHeightsValid = false;
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
        groundItemHeightsValid = false;
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
        if (!PlaceMacroblock_NoDestruction(model, coord, direction))
        {
            RestoreNoItemBlocks(removalResult.Removed);
            return false;
        }
        var position = GetVec3FromCoord(coord);
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var added = new ItemBlock
        {
            MacroblockName = macroblockName, MacroblockCoord = coord, MacroblockDir = DirectionToIndex(direction),
            ItemPosition = position, Ground = ground, Family = family, PieceIndex = pieceIndex,
            Width = width, Depth = depth
        };
        storedBlocks.Value!.Add(added);
        UpdateGroundItemHeights(new List<ItemBlock>(), new List<ItemBlock> { added });
        SnapshotItems();
        PushItemEdit(new List<ItemBlock>(), new List<ItemBlock> { added }, removalResult.Removed);
        return true;
    }

    public int RemoveItemBlocksAtCoord(Int3 coord) => RemoveItemBlocks(coord, "", -1);

    public int RemoveItemBlocksByName(Int3 coord, string macroblockName) =>
        RemoveItemBlocks(coord, macroblockName, -1);

    public int RemoveItemBlocks(Int3 coord, string macroblockName, int direction)
    {
        Metadata<List<ItemBlock>>.For(Map, out var storedBlocks, name: "Atlas_ItemBlocks");
        var remaining = new List<ItemBlock>();
        var removed = new List<ItemBlock>();
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
                    removed.Add(block);
                    continue;
                }
            }
            remaining.Add(block);
        }
        if (count > 0) SetItemBlockList(remaining);
        SnapshotItems();
        if (count > 0) PushItemEdit(removed, new List<ItemBlock>(), new List<NoItemReplacement>());
        return count;
    }

    private void SnapshotItems()
    {
        // Editor item anchors can exist before their Position becomes valid.
        // A placement or removal must settle before their positions are read.
        deferredItemSnapshotUpdates = 2;
    }

    private void CaptureItemsSnapshot()
    {
        previousItems.Clear();
        foreach (var item in Items)
            if (item != null) previousItems.Add(item.Position);
    }

    public void SyncManuallyRemovedItems()
    {
        if (deferredItemSnapshotUpdates > 0)
        {
            deferredItemSnapshotUpdates--;
            if (deferredItemSnapshotUpdates == 0) CaptureItemsSnapshot();
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
        CaptureItemsSnapshot();
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

    private static int ExceptMask(int mask, int excluded)
    {
        if (HasSide(mask, 1) && HasSide(excluded, 1)) mask -= 1;
        if (HasSide(mask, 2) && HasSide(excluded, 2)) mask -= 2;
        if (HasSide(mask, 4) && HasSide(excluded, 4)) mask -= 4;
        if (HasSide(mask, 8) && HasSide(excluded, 8)) mask -= 8;
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
        var plannedCells = new Dictionary<Int3, bool>();
        var replacementCells = new Dictionary<Int3, bool>();
        var minX = plan[0].Coord.X;
        var maxX = minX;
        var minY = plan[0].Coord.Y;
        var maxY = minY;
        var minZ = plan[0].Coord.Z;
        var maxZ = minZ;
        foreach (var entry in plan)
        {
            if (!WithinMap(entry.Coord) || GetMacroblockModelFromFilePath(entry.MacroblockName) == null) return false;
            var width = Math.Max(1, entry.Width);
            var depth = Math.Max(1, entry.Depth);
            minX = Math.Min(minX, entry.Coord.X);
            maxX = Math.Max(maxX, entry.Coord.X + width - 1);
            minY = Math.Min(minY, entry.Coord.Y);
            maxY = Math.Max(maxY, entry.Coord.Y);
            minZ = Math.Min(minZ, entry.Coord.Z);
            maxZ = Math.Max(maxZ, entry.Coord.Z + depth - 1);
            if (entry.HasReplacementCoord)
            {
                if (width != 1 || depth != 1 || !WithinMap(entry.ReplacementCoord) ||
                    entry.ReplacementCoord.X != entry.Coord.X ||
                    entry.ReplacementCoord.Z != entry.Coord.Z ||
                    entry.ReplacementCoord.Y == entry.Coord.Y ||
                    replacementCells.ContainsKey(entry.ReplacementCoord)) return false;
                replacementCells[entry.ReplacementCoord] = true;
                minY = Math.Min(minY, entry.ReplacementCoord.Y);
                maxY = Math.Max(maxY, entry.ReplacementCoord.Y);
            }
            for (var x = entry.Coord.X; x < entry.Coord.X + width; x++)
            for (var z = entry.Coord.Z; z < entry.Coord.Z + depth; z++)
            {
                var cell = new Int3(x, entry.Coord.Y, z);
                if (!WithinMap(cell) || plannedCells.ContainsKey(cell)) return false;
                plannedCells[cell] = true;
            }
        }
        foreach (var cell in replacementCells.Keys)
        {
            if (plannedCells.ContainsKey(cell)) return false;
            plannedCells[cell] = true;
        }
        var existingByCell = new Dictionary<Int3, List<int>>();
        for (var index = 0; index < existing.Count; index++)
        {
            var block = existing[index];
            if (block.MacroblockCoord.Y < minY || block.MacroblockCoord.Y > maxY ||
                block.MacroblockCoord.X > maxX ||
                block.MacroblockCoord.X + Math.Max(1, block.Width) <= minX ||
                block.MacroblockCoord.Z > maxZ ||
                block.MacroblockCoord.Z + Math.Max(1, block.Depth) <= minZ) continue;
            for (var x = block.MacroblockCoord.X; x < block.MacroblockCoord.X + Math.Max(1, block.Width); x++)
            for (var z = block.MacroblockCoord.Z; z < block.MacroblockCoord.Z + Math.Max(1, block.Depth); z++)
            {
                var cell = new Int3(x, block.MacroblockCoord.Y, z);
                if (!plannedCells.ContainsKey(cell)) continue;
                if (!existingByCell.ContainsKey(cell)) existingByCell[cell] = new List<int>();
                existingByCell[cell].Add(index);
            }
        }
        var removedIndices = new Dictionary<int, bool>();
        foreach (var entry in plan)
        {
            if (entry.HasReplacementCoord)
            {
                if (!existingByCell.ContainsKey(entry.ReplacementCoord) ||
                    existingByCell[entry.ReplacementCoord].Count != 1) return false;
                var sourceIndex = existingByCell[entry.ReplacementCoord][0];
                var source = existing[sourceIndex];
                if (!source.Ground || source.Width != 1 || source.Depth != 1 ||
                    (source.Family != entry.Family &&
                     !CanReplaceSupport(source, entry.Family, entry.ReplacementCoord))) return false;
                removedIndices[sourceIndex] = true;
            }
            var width = Math.Max(1, entry.Width);
            var depth = Math.Max(1, entry.Depth);
            for (var x = entry.Coord.X; x < entry.Coord.X + width; x++)
            for (var z = entry.Coord.Z; z < entry.Coord.Z + depth; z++)
            {
                var cell = new Int3(x, entry.Coord.Y, z);
                if (!existingByCell.ContainsKey(cell)) continue;
                if (entry.HasReplacementCoord) return false;
                foreach (var index in existingByCell[cell])
                {
                    var block = existing[index];
                    if (block.Family != entry.Family &&
                        !CanReplaceSupport(block, entry.Family, cell)) return false;
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
            if (!PlaceMacroblock_NoDestruction(model, entry.Coord, entry.Direction))
            {
                RollbackPlacementPlan(removed, placed, removedNoItemBlocks);
                return false;
            }
            var position = GetVec3FromCoord(entry.Coord);
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
            for (var index = storedBlocks.Value!.Count - 1; index >= 0; index--)
                if (removedIndices.ContainsKey(index)) storedBlocks.Value.RemoveAt(index);
            foreach (var block in placed) storedBlocks.Value!.Add(block);
            UpdateGroundItemHeights(removed, placed);
            SnapshotItems();
            PushItemEdit(removed, placed, removedNoItemBlocks);
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

    private static bool ItemBlockTouchesCells(ItemBlock block, Dictionary<Int3, bool> cells,
        int minX, int maxX, int minZ, int maxZ)
    {
        var coord = block.MacroblockCoord;
        if (coord.X > maxX || coord.X + Math.Max(1, block.Width) <= minX ||
            coord.Z > maxZ || coord.Z + Math.Max(1, block.Depth) <= minZ) return false;
        var endX = Math.Min(maxX, coord.X + Math.Max(1, block.Width) - 1);
        var endZ = Math.Min(maxZ, coord.Z + Math.Max(1, block.Depth) - 1);
        for (var x = Math.Max(minX, coord.X); x <= endX; x++)
        for (var z = Math.Max(minZ, coord.Z); z <= endZ; z++)
            if (cells.ContainsKey(new Int3(x, coord.Y, z))) return true;
        return false;
    }

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
        var existing = storedBlocks.Value!;
        var unmatched = GroupItemBlocksByCoord(toRemove);
        var removedIndices = new Dictionary<int, bool>();
        var matchedCount = 0;
        for (var index = 0; index < existing.Count; index++)
        {
            var block = existing[index];
            var coord = block.MacroblockCoord;
            if (!unmatched.ContainsKey(coord)) continue;
            var bucket = unmatched[coord];
            var targetIndex = IndexOfItemBlock(bucket, block);
            if (targetIndex < 0) continue;
            removedIndices[index] = true;
            bucket.RemoveAt(targetIndex);
            unmatched[coord] = bucket;
            matchedCount++;
        }
        if (matchedCount != toRemove.Count) return false;

        var remaining = new List<ItemBlock>();
        for (var index = 0; index < existing.Count; index++)
            if (!removedIndices.ContainsKey(index)) remaining.Add(existing[index]);
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
    private static bool IsFreeformBase(int piece) => piece >= 0 && (piece <= 4 || piece == 14);

    private static int FreeformSideMask(int piece, CardinalDirections direction)
    {
        var mask = 0;
        if (IsFreeformBase(piece)) mask = 15;
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

    private static int FreeformConnectedSides(Dictionary<Int3, int> sideMasks,
        Int3 coord, int ownMask)
    {
        var mask = 0;
        var north = new Int3(coord.X, coord.Y, coord.Z - 1);
        var east = new Int3(coord.X + 1, coord.Y, coord.Z);
        var south = new Int3(coord.X, coord.Y, coord.Z + 1);
        var west = new Int3(coord.X - 1, coord.Y, coord.Z);
        if (HasSide(ownMask, 1) && sideMasks.ContainsKey(north) && HasSide(sideMasks[north], 4)) mask += 1;
        if (HasSide(ownMask, 2) && sideMasks.ContainsKey(east) && HasSide(sideMasks[east], 8)) mask += 2;
        if (HasSide(ownMask, 4) && sideMasks.ContainsKey(south) && HasSide(sideMasks[south], 1)) mask += 4;
        if (HasSide(ownMask, 8) && sideMasks.ContainsKey(west) && HasSide(sideMasks[west], 2)) mask += 8;
        return mask;
    }

    private static int FreeformConnectedDiagonals(Dictionary<Int3, int> diagonalMasks,
        Dictionary<Int3, bool> removed, Int3 coord, int oldMask)
    {
        var mask = 0;
        var northWest = new Int3(coord.X - 1, coord.Y, coord.Z - 1);
        var northEast = new Int3(coord.X + 1, coord.Y, coord.Z - 1);
        var southEast = new Int3(coord.X + 1, coord.Y, coord.Z + 1);
        var southWest = new Int3(coord.X - 1, coord.Y, coord.Z + 1);
        if (!removed.ContainsKey(northWest))
        {
            if (diagonalMasks.ContainsKey(northWest))
            {
                if (HasSide(diagonalMasks[northWest], 4)) mask += 1;
            }
            else if (HasSide(oldMask, 1)) mask += 1;
        }
        if (!removed.ContainsKey(northEast))
        {
            if (diagonalMasks.ContainsKey(northEast))
            {
                if (HasSide(diagonalMasks[northEast], 8)) mask += 2;
            }
            else if (HasSide(oldMask, 2)) mask += 2;
        }
        if (!removed.ContainsKey(southEast))
        {
            if (diagonalMasks.ContainsKey(southEast))
            {
                if (HasSide(diagonalMasks[southEast], 1)) mask += 4;
            }
            else if (HasSide(oldMask, 4)) mask += 4;
        }
        if (!removed.ContainsKey(southWest))
        {
            if (diagonalMasks.ContainsKey(southWest))
            {
                if (HasSide(diagonalMasks[southWest], 2)) mask += 8;
            }
            else if (HasSide(oldMask, 8)) mask += 8;
        }
        return mask;
    }

    private static int FreeformSupportedDiagonals(int sides)
    {
        var mask = 0;
        if (HasSide(sides, 1) && HasSide(sides, 8)) mask += 1;
        if (HasSide(sides, 1) && HasSide(sides, 2)) mask += 2;
        if (HasSide(sides, 2) && HasSide(sides, 4)) mask += 4;
        if (HasSide(sides, 4) && HasSide(sides, 8)) mask += 8;
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
        if (oldPiece >= 0 && (selected.ContainsKey(coord) || IsFreeformBase(oldPiece)))
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

    internal static FreeformCandidate ResolveFreeformRemovalCell(Dictionary<Int3, int> sideMasks,
        Dictionary<Int3, int> diagonalMasks, Dictionary<Int3, bool> removed, Int3 coord, int oldPiece,
        CardinalDirections oldDirection, bool hasFiller)
    {
        // Side connections must match both surviving pieces. A filled corner can
        // remain encoded in the old piece without a separate diagonal item. If a
        // diagonal item exists, its facing corner determines whether they join.
        var sides = FreeformConnectedSides(sideMasks, coord,
            FreeformSideMask(oldPiece, oldDirection));
        var diagonals = FreeformConnectedDiagonals(diagonalMasks, removed, coord,
            FreeformDiagonalMask(oldPiece, oldDirection));
        diagonals = ExceptMask(diagonals, 15 - FreeformSupportedDiagonals(sides));
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
            if (!WithinMap(coord) || !HasAllowedGroundSupport(coord, family)) return new List<Placement>();
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
            if ((!old.Ground || old.Width != 1 || old.Depth != 1) &&
                oldCoord.X <= maxX && oldCoord.X + Math.Max(1, old.Width) > minX &&
                oldCoord.Z <= maxZ && oldCoord.Z + Math.Max(1, old.Depth) > minZ)
            {
                // Check only the old block's overlapping footprint, rather than
                // scanning every selected cell for each existing item.
                var overlapMaxX = Math.Min(maxX, oldCoord.X + Math.Max(1, old.Width) - 1);
                var overlapMaxZ = Math.Min(maxZ, oldCoord.Z + Math.Max(1, old.Depth) - 1);
                for (var x = Math.Max(minX, oldCoord.X); x <= overlapMaxX; x++)
                for (var z = Math.Max(minZ, oldCoord.Z); z <= overlapMaxZ; z++)
                {
                    var cell = new Int3(x, 0, z);
                    if (selectedByCell.ContainsKey(cell) && selectedByCell[cell].Y == oldCoord.Y)
                        return new List<Placement>();
                }
            }
            if (freeformPlacementMode == FreeformPlacementMode.SelectionOnly) continue;
            if (old.Family != family || oldCoord.X < minX - 2 || oldCoord.X > maxX + 2 ||
                oldCoord.Z < minZ - 2 || oldCoord.Z > maxZ + 2) continue;
            if (IsRaisedFreeformFiller(old, family)) continue;
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
            if (hasOld && duplicateCoords.ContainsKey(coord))
                return new List<Placement>();
            if (hasOld && oldBlock.Family != family)
            {
                if (!CanReplaceSupport(oldBlock, family, coord)) return new List<Placement>();
                hasOld = false;
            }
            if (hasOld && IsRaisedFreeformFiller(oldBlock, family))
                hasOld = false;
            var oldLogicalDirection = CardinalDirections.North;
            if (hasOld) oldLogicalDirection = LogicalFreeformDirectionFor(oldBlock);
            var oldPiece = -1;
            if (hasOld) oldPiece = oldBlock.PieceIndex;
            var desired = ResolveFreeformCell(occupied, selectedLookup, coord,
                oldPiece, oldLogicalDirection, hasFiller);
            var fillerHeight = FreeformFillerHeight(family, desired.Piece);
            if (hasOld && fillerHeight == 0 && oldBlock.PieceIndex == desired.Piece &&
                oldLogicalDirection == desired.Direction)
            {
                result.Add(new Placement { Coord = coord, MacroblockName = oldBlock.MacroblockName,
                    Direction = DirectionFromIndex(oldBlock.MacroblockDir), Ground = true,
                    Family = family, PieceIndex = oldBlock.PieceIndex, Width = 1, Depth = 1 });
                continue;
            }
            var variant = VariantFor(family, desired.Piece, true, coord);
            if (variant.MacroblockName == "") return new List<Placement>();
            var placedAt = new Int3(coord.X, coord.Y + fillerHeight, coord.Z);
            if (!WithinMap(placedAt)) return new List<Placement>();
            result.Add(new Placement { Coord = placedAt,
                HasReplacementCoord = fillerHeight > 0,
                ReplacementCoord = coord, MacroblockName = variant.MacroblockName,
                Direction = OffsetDirection(desired.Direction,
                    variant.DirectionOffset + FreeformModelDirectionOffset(desired.Piece)),
                Ground = true, Family = family, PieceIndex = desired.Piece, Width = 1, Depth = 1 });
        }
        return result;
    }

    public bool PlaceFreeform1x1(IList<Int3> selection, string family) =>
        ExecutePlacementPlan(ResolveFreeform1x1(selection, family));

    private int GetRemovalSupportHeight(int x, int z, string support)
    {
        var column = new Int3(x, 0, z);
        var height = -1;
        if (itemBlockSupports.ContainsKey(support) && itemBlockSupports[support] == baseGroundGroup)
        {
            GetFakeGroundHeight(x, z);
            height = removedWaterGroundHeights.ContainsKey(column)
                ? removedWaterGroundHeights[column] : GetGroundHeight(x, z);
            if (height >= 0) height += itemBlockSelectionHeights[support];
        }
        else height = GetGroupGroundHeight(x, z, support);
        return height;
    }

    /// <summary>Remove selected ground pieces from a freeform 1x1 group and resolve its exposed edges.</summary>
    public bool RemoveFreeform1x1(IList<Int3> selection, string family)
    {
        if (selection.Count == 0) return false;
        var selected = new Dictionary<Int3, bool>();
        foreach (var coord in selection)
        {
            if (!WithinMap(coord)) return false;
            selected[coord] = true;
        }
        var existing = GetItemBlockList();
        var toRemove = new List<ItemBlock>();
        var removedCoords = new Dictionary<Int3, bool>();
        var removedShapeCoords = new Dictionary<Int3, bool>();
        var hasDependents = false;
        foreach (var supportRule in itemBlockSupports)
            if (supportRule.Value == family) hasDependents = true;
        var supportHeights = new Dictionary<Int3, int>();
        if (itemBlockSupports.ContainsKey(family))
        {
            var supportFamily = itemBlockSupports[family];
            foreach (var supportBlock in existing)
            {
                if (!supportBlock.Ground || supportBlock.Family != supportFamily) continue;
                for (var x = supportBlock.MacroblockCoord.X;
                     x < supportBlock.MacroblockCoord.X + Math.Max(1, supportBlock.Width); x++)
                for (var z = supportBlock.MacroblockCoord.Z;
                     z < supportBlock.MacroblockCoord.Z + Math.Max(1, supportBlock.Depth); z++)
                {
                    var column = new Int3(x, 0, z);
                    if (!supportHeights.ContainsKey(column) ||
                        supportHeights[column] < supportBlock.MacroblockCoord.Y)
                        supportHeights[column] = supportBlock.MacroblockCoord.Y;
                }
            }
        }
        foreach (var block in existing)
        {
            if (!selected.ContainsKey(block.MacroblockCoord) || block.Family != family) continue;
            if (!block.Ground || block.Width != 1 || block.Depth != 1) return false;
            // A supported group must be removed before its supporting piece.
            if (hasDependents)
                foreach (var dependent in existing)
                    if (itemBlockSupports.ContainsKey(dependent.Family) &&
                        itemBlockSupports[dependent.Family] == family && dependent.Ground &&
                        block.MacroblockCoord.X >= dependent.MacroblockCoord.X &&
                        block.MacroblockCoord.X < dependent.MacroblockCoord.X + Math.Max(1, dependent.Width) &&
                        block.MacroblockCoord.Z >= dependent.MacroblockCoord.Z &&
                        block.MacroblockCoord.Z < dependent.MacroblockCoord.Z + Math.Max(1, dependent.Depth) &&
                        dependent.MacroblockCoord.Y == block.MacroblockCoord.Y +
                            itemBlockSelectionHeights[dependent.Family]) return false;
            toRemove.Add(block);
            removedCoords[block.MacroblockCoord] = true;
            if (IsRaisedFreeformFiller(block, family))
                removedShapeCoords[new Int3(block.MacroblockCoord.X,
                    block.MacroblockCoord.Y - FreeformFillerHeight(family, 14),
                    block.MacroblockCoord.Z)] = true;
            else
                removedShapeCoords[block.MacroblockCoord] = true;
        }
        if (toRemove.Count == 0) return false;

        var minX = toRemove[0].MacroblockCoord.X;
        var maxX = minX;
        var minZ = toRemove[0].MacroblockCoord.Z;
        var maxZ = minZ;
        foreach (var block in toRemove)
        {
            minX = Math.Min(minX, block.MacroblockCoord.X);
            maxX = Math.Max(maxX, block.MacroblockCoord.X);
            minZ = Math.Min(minZ, block.MacroblockCoord.Z);
            maxZ = Math.Max(maxZ, block.MacroblockCoord.Z);
        }
        var sideMasks = new Dictionary<Int3, int>();
        var diagonalMasks = new Dictionary<Int3, int>();
        var neighbors = new List<ItemBlock>();
        var raisedFillerHeight = FreeformFillerHeight(family, 14);
        if (raisedFillerHeight > 0 && itemBlockSupports.ContainsKey(family))
            foreach (var block in existing)
            {
                if (block.Family != family || !block.Ground || block.Width != 1 || block.Depth != 1 ||
                    block.MacroblockCoord.X < minX - 2 || block.MacroblockCoord.X > maxX + 2 ||
                    block.MacroblockCoord.Z < minZ - 2 || block.MacroblockCoord.Z > maxZ + 2) continue;
                var column = new Int3(block.MacroblockCoord.X, 0, block.MacroblockCoord.Z);
                var baseY = supportHeights.ContainsKey(column) ? supportHeights[column] :
                    GetRemovalSupportHeight(block.MacroblockCoord.X, block.MacroblockCoord.Z,
                        itemBlockSupports[family]);
                var virtualY = block.MacroblockCoord.Y - raisedFillerHeight;
                var firstVirtual = true;
                while (virtualY >= baseY && virtualY >= 0)
                {
                    // Removing a raised filler exposes its immediate lower cell.
                    // Other upper pieces restore a filler, so lower layers remain filled.
                    if (!firstVirtual || !removedCoords.ContainsKey(block.MacroblockCoord) ||
                        !IsRaisedFreeformFiller(block, family))
                    {
                        var virtualCoord = new Int3(block.MacroblockCoord.X, virtualY,
                            block.MacroblockCoord.Z);
                        sideMasks[virtualCoord] = 15;
                        diagonalMasks[virtualCoord] = 15;
                    }
                    firstVirtual = false;
                    virtualY -= raisedFillerHeight;
                }
            }
        foreach (var block in existing)
        {
            if (block.Family != family || !block.Ground || block.Width != 1 || block.Depth != 1 ||
                removedCoords.ContainsKey(block.MacroblockCoord)) continue;
            var isRaisedFiller = IsRaisedFreeformFiller(block, family);
            var coord = isRaisedFiller
                ? new Int3(block.MacroblockCoord.X,
                    block.MacroblockCoord.Y - raisedFillerHeight, block.MacroblockCoord.Z)
                : block.MacroblockCoord;
            if (coord.X < minX - 2 || coord.X > maxX + 2 ||
                coord.Z < minZ - 2 || coord.Z > maxZ + 2) continue;
            if (!isRaisedFiller)
            {
                var logicalDirection = LogicalFreeformDirectionFor(block);
                sideMasks[coord] = FreeformSideMask(block.PieceIndex, logicalDirection);
                diagonalMasks[coord] = FreeformDiagonalMask(block.PieceIndex, logicalDirection);
            }
            var touchesRemoval = false;
            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
                if (removedShapeCoords.ContainsKey(new Int3(coord.X + dx, coord.Y, coord.Z + dz)))
                    touchesRemoval = true;
            if (touchesRemoval) neighbors.Add(block);
        }
        var hasFiller = VariantFor(family, 14, true, toRemove[0].MacroblockCoord).MacroblockName != "";
        var plan = new List<Placement>();
        foreach (var block in neighbors)
        {
            var coord = IsRaisedFreeformFiller(block, family)
                ? new Int3(block.MacroblockCoord.X,
                    block.MacroblockCoord.Y - raisedFillerHeight, block.MacroblockCoord.Z)
                : block.MacroblockCoord;
            var desired = ResolveFreeformRemovalCell(sideMasks, diagonalMasks, removedShapeCoords, coord,
                block.PieceIndex, LogicalFreeformDirectionFor(block), hasFiller);
            if (block.PieceIndex == desired.Piece &&
                LogicalFreeformDirectionFor(block) == desired.Direction) continue;
            var variant = VariantFor(family, desired.Piece, true, coord);
            if (variant.MacroblockName == "") return false;
            var fillerHeight = FreeformFillerHeight(family, desired.Piece);
            var placedAt = new Int3(coord.X, coord.Y + fillerHeight, coord.Z);
            if (!WithinMap(placedAt)) return false;
            var changesHeight = !SameCoord(placedAt, block.MacroblockCoord);
            plan.Add(new Placement { Coord = placedAt,
                HasReplacementCoord = changesHeight, ReplacementCoord = block.MacroblockCoord,
                MacroblockName = variant.MacroblockName,
                Direction = OffsetDirection(desired.Direction,
                    variant.DirectionOffset + FreeformModelDirectionOffset(desired.Piece)),
                Ground = true, Family = family, PieceIndex = desired.Piece, Width = 1, Depth = 1 });
        }
        if (itemBlockSupports.ContainsKey(family) &&
            itemBlockSupports[family] != baseGroundGroup &&
            itemBlockSupportPieces[family] >= 0 && itemBlockSelectionHeights[family] == 0)
        {
            // Restore dock filler at the base, esplanade filler after an upper
            // piece, or a lower filler after removing a raised interior cell.
            var support = itemBlockSupports[family];
            var piece = itemBlockSupportPieces[family];
            foreach (var block in toRemove)
            {
                var replacementFamily = support;
                var replacementPiece = piece;
                var replacementCoord = block.MacroblockCoord;
                var column = new Int3(block.MacroblockCoord.X, 0, block.MacroblockCoord.Z);
                var baseHeight = supportHeights.ContainsKey(column) ? supportHeights[column] :
                    GetRemovalSupportHeight(block.MacroblockCoord.X,
                        block.MacroblockCoord.Z, support);
                if (block.MacroblockCoord.Y > baseHeight)
                {
                    if (block.PieceIndex != 14)
                    {
                        replacementFamily = family;
                        replacementPiece = 14;
                    }
                    else
                    {
                        replacementCoord = new Int3(replacementCoord.X,
                            replacementCoord.Y - FreeformFillerHeight(family, 14),
                            replacementCoord.Z);
                        if (replacementCoord.Y > baseHeight)
                        {
                            replacementFamily = family;
                            replacementPiece = 14;
                        }
                    }
                }
                var variant = VariantFor(replacementFamily, replacementPiece, true, replacementCoord);
                if (variant.MacroblockName == "") return false;
                plan.Add(new Placement { Coord = replacementCoord,
                    MacroblockName = variant.MacroblockName,
                    Direction = OffsetDirection(CardinalDirections.North, variant.DirectionOffset),
                    Ground = true, Family = replacementFamily, PieceIndex = replacementPiece,
                    Width = 1, Depth = 1 });
            }
        }
        var baseMacroblock = noItemBlockName == "" ? null : GetMacroblockModelFromFilePath(noItemBlockName);
        var baseBlock = noItemBlockName == "" || baseMacroblock != null
            ? null : GetBlockModelFromName(noItemBlockName);
        if (noItemBlockName != "" && baseMacroblock == null && baseBlock == null)
        {
            Log($"No item block '{noItemBlockName}' was not found.");
            return false;
        }

        var affectedCells = new Dictionary<Int3, bool>();
        foreach (var block in toRemove) affectedCells[block.MacroblockCoord] = true;
        foreach (var entry in plan)
        {
            affectedCells[entry.Coord] = true;
            if (entry.HasReplacementCoord) affectedCells[entry.ReplacementCoord] = true;
            minX = Math.Min(minX, entry.Coord.X);
            maxX = Math.Max(maxX, entry.Coord.X);
            minZ = Math.Min(minZ, entry.Coord.Z);
            maxZ = Math.Max(maxZ, entry.Coord.Z);
        }
        var beforeAffected = new List<ItemBlock>();
        foreach (var block in existing)
            if (ItemBlockTouchesCells(block, affectedCells, minX, maxX, minZ, maxZ))
                beforeAffected.Add(block);

        var wasReplaying = replayingHistory;
        replayingHistory = true;
        var applied = ApplyResolvedChangesCore(toRemove, plan, GetRemovedWater(), false);
        replayingHistory = wasReplaying;
        if (!applied)
        {
            if (lastRollbackFailed) ClearAtlasEditHistory();
            return false;
        }

        var afterAffected = new List<ItemBlock>();
        var addedBases = new List<NoItemReplacement>();
        var coveredColumns = new Dictionary<Int3, bool>();
        var baseColumns = new Dictionary<Int3, bool>();
        foreach (var block in toRemove)
            baseColumns[new Int3(block.MacroblockCoord.X, 0, block.MacroblockCoord.Z)] = true;
        foreach (var block in GetItemBlockList())
        {
            if (ItemBlockTouchesCells(block, affectedCells, minX, maxX, minZ, maxZ))
                afterAffected.Add(block);
            if (!block.Ground || block.MacroblockCoord.X > maxX ||
                block.MacroblockCoord.X + Math.Max(1, block.Width) <= minX ||
                block.MacroblockCoord.Z > maxZ ||
                block.MacroblockCoord.Z + Math.Max(1, block.Depth) <= minZ) continue;
            var endX = Math.Min(maxX, block.MacroblockCoord.X + Math.Max(1, block.Width) - 1);
            var endZ = Math.Min(maxZ, block.MacroblockCoord.Z + Math.Max(1, block.Depth) - 1);
            for (var x = Math.Max(minX, block.MacroblockCoord.X); x <= endX; x++)
            for (var z = Math.Max(minZ, block.MacroblockCoord.Z); z <= endZ; z++)
            {
                var column = new Int3(x, 0, z);
                if (baseColumns.ContainsKey(column)) coveredColumns[column] = true;
            }
        }
        var baseSucceeded = true;
        foreach (var block in toRemove)
        {
            var coord = block.MacroblockCoord;
            var column = new Int3(coord.X, 0, coord.Z);
            if (!baseColumns.ContainsKey(column)) continue;
            baseColumns.Remove(column);
            if (coveredColumns.ContainsKey(column) || noItemBlockName == "") continue;
            var baseCoord = new Int3(coord.X, CollectionGroundY, coord.Z);
            var placed = baseMacroblock != null
                ? PlaceMacroblock_NoDestruction(baseMacroblock, baseCoord, CardinalDirections.North)
                : PlaceBlock(baseBlock, baseCoord, CardinalDirections.North);
            if (!placed && coord.Y != CollectionGroundY)
            {
                baseCoord = coord;
                placed = baseMacroblock != null
                    ? PlaceMacroblock_NoDestruction(baseMacroblock, baseCoord, CardinalDirections.North)
                    : PlaceBlock(baseBlock, baseCoord, CardinalDirections.North);
            }
            if (placed) addedBases.Add(new NoItemReplacement { Coord = baseCoord,
                Direction = CardinalDirections.North });
            else
            {
                Log($"Could not restore no-item block '{noItemBlockName}' at {coord}.");
                baseSucceeded = false;
            }
        }
        PushItemEditWithBase(ItemBlockDifference(beforeAffected, afterAffected),
            ItemBlockDifference(afterAffected, beforeAffected),
            new List<NoItemReplacement>(), addedBases);
        return baseSucceeded;
    }

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
