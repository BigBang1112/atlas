using Atlas.Libs;

namespace Atlas.Tests;

/// <summary>Rebuild with OutputType=Exe and OutputPath=bin/AtlasTestRunner/, then run its Atlas.dll.</summary>
public static class AtlasTests
{
    public static void Main()
    {
        var directions = new[]
        {
            CMapEditorPlugin.CardinalDirections.North,
            CMapEditorPlugin.CardinalDirections.East,
            CMapEditorPlugin.CardinalDirections.South,
            CMapEditorPlugin.CardinalDirections.West
        };
        for (var index = 0; index < directions.Length; index++)
        {
            Check(AtlasEngine.DirectionToIndex(directions[index]) == index,
                "cardinal direction uses its stable metadata index");
            Check(AtlasEngine.DirectionFromIndex(index) == directions[index],
                "metadata direction restores the matching editor direction");
        }

        Check(AtlasEngine.RoadPieceForMask(0) == 0, "isolated road uses Base");
        Check(AtlasEngine.RoadPieceForMask(1) == 1, "one connection uses Deadend");
        Check(AtlasEngine.RoadPieceForMask(3) == 2, "turn uses Corner");
        Check(AtlasEngine.RoadPieceForMask(5) == 3, "opposite connections use Straight");
        Check(AtlasEngine.RoadPieceForMask(7) == 4, "three connections use TShaped");
        Check(AtlasEngine.RoadPieceForMask(15) == 5, "four connections use Cross");
        Check(AtlasEngine.RoadDirectionForMask(2) == CMapEditorPlugin.CardinalDirections.East,
            "eastbound deadend is rotated east");
        Check(AtlasEngine.RoadDirectionForMask(10) == CMapEditorPlugin.CardinalDirections.East,
            "east-west straight has stable orientation");
        for (var mask = 0; mask < 16; mask++)
        {
            var piece = AtlasEngine.RoadPieceForMask(mask);
            Check(piece >= 0 && piece <= 5, "every four-way mask resolves to a road piece");
        }
        Check(AtlasEngine.ResolveFreeformOverlap(11, CMapEditorPlugin.CardinalDirections.North, 11,
            CMapEditorPlugin.CardinalDirections.East).Piece == 4,
            "perpendicular straights resolve to Base15");
        Check(AtlasEngine.ResolveFreeformOverlap(9, CMapEditorPlugin.CardinalDirections.North, 9,
            CMapEditorPlugin.CardinalDirections.South).Piece == 2,
            "opposite corners resolve to Base5");
        Check(AtlasEngine.ResolveFreeformOverlap(12, CMapEditorPlugin.CardinalDirections.North, 12,
            CMapEditorPlugin.CardinalDirections.East).Piece == 10,
            "perpendicular endpoints resolve to Corner8");
        Check(AtlasEngine.ResolveFreeformOverlap(12, CMapEditorPlugin.CardinalDirections.North, 12,
            CMapEditorPlugin.CardinalDirections.South).Piece == 11,
            "opposite endpoints form a straight line");
        Check(AtlasEngine.ResolveFreeformOverlap(12, CMapEditorPlugin.CardinalDirections.North, 11,
            CMapEditorPlugin.CardinalDirections.East).Piece == 8,
            "a perpendicular line across an endpoint forms Deadend12");
        var corner = new Int3(8, 1, 8);
        var cornerSelection = new Dictionary<Int3, bool>
        {
            [corner] = true,
            [new Int3(9, 1, 8)] = true,
            [new Int3(8, 1, 9)] = true,
            [new Int3(9, 1, 9)] = true
        };
        var cornerOverEnd = AtlasEngine.ResolveFreeformCell(cornerSelection, cornerSelection,
            corner, 12, CMapEditorPlugin.CardinalDirections.North, false);
        Check(cornerOverEnd.Piece == 6 && cornerOverEnd.Direction == CMapEditorPlugin.CardinalDirections.East,
            "Corner over TShaped forms Deadend4");
        var edge = new Int3(9, 1, 8);
        var edgeSelection = new Dictionary<Int3, bool>
        {
            [edge] = true,
            [new Int3(8, 1, 8)] = true,
            [new Int3(10, 1, 8)] = true,
            [new Int3(9, 1, 9)] = true,
            [new Int3(8, 2, 9)] = true,
            [new Int3(10, 2, 9)] = true
        };
        Check(AtlasEngine.ResolveFreeformCell(edgeSelection, edgeSelection, edge, 12,
            CMapEditorPlugin.CardinalDirections.East, false).Piece == 5,
            "an area edge joined to TShaped stays a solid Deadend");
        var east = new Int3(edge.X + 1, edge.Y, edge.Z);
        var connectedEndpoint = new Dictionary<Int3, bool>
        {
            [edge] = true,
            [east] = true
        };
        var selectedNeighbor = new Dictionary<Int3, bool> { [east] = true };
        var recalculatedEndpoint = AtlasEngine.ResolveFreeformCell(connectedEndpoint,
            selectedNeighbor, edge, 12, CMapEditorPlugin.CardinalDirections.North, false);
        Check(recalculatedEndpoint.Piece == 12 &&
            recalculatedEndpoint.Direction == CMapEditorPlugin.CardinalDirections.East,
            "an unselected endpoint rotates toward a newly connected neighbor");
        Check(AtlasEngine.ResolveFreeformCell(connectedEndpoint, selectedNeighbor, edge, 4,
            CMapEditorPlugin.CardinalDirections.North, false).Piece == 4,
            "an existing base is not downgraded when a neighbor is added");
        var base7 = AtlasEngine.ResolveFreeformOverlap(10, CMapEditorPlugin.CardinalDirections.South, 9,
            CMapEditorPlugin.CardinalDirections.North);
        var reversedBase7 = AtlasEngine.ResolveFreeformOverlap(9, CMapEditorPlugin.CardinalDirections.North, 10,
            CMapEditorPlugin.CardinalDirections.South);
        Check(base7.Piece == 3 && base7.Direction == CMapEditorPlugin.CardinalDirections.South &&
            reversedBase7.Piece == base7.Piece && reversedBase7.Direction == base7.Direction,
            "Base7 orientation does not depend on overlap order");
        for (var piece = 0; piece <= 14; piece++)
            Check(AtlasEngine.ResolveFreeformOverlap(piece, CMapEditorPlugin.CardinalDirections.North,
                piece, CMapEditorPlugin.CardinalDirections.North).Piece == piece,
                "overlapping an identical shape leaves its piece unchanged");

        var atlas = new AtlasEngine();
        Check(atlas.FreeformMode == AtlasEngine.FreeformPlacementMode.SelectionOnly,
            "freeform placement only changes selected cells by default");
        atlas.SetFreeformPlacementMode(AtlasEngine.FreeformPlacementMode.ConnectExisting);
        Check(atlas.FreeformMode == AtlasEngine.FreeformPlacementMode.ConnectExisting,
            "freeform placement can connect neighboring tracked cells");
        var line = atlas.BuildSelection(new Int3(4, 2, 8), new Int3(1, 9, 8), AtlasEngine.SelectionMode.Line1D);
        Check(line.Count == 8, "line includes both endpoints");
        Check(line[0].X == 4 && line[0].Y == 2 && line[0].Z == 8, "line begins at cursor");
        Check(line[7].X == 4 && line[7].Y == 9 && line[7].Z == 8, "line follows dominant axis");
        var reverse = atlas.BuildSelection(new Int3(3, 1, 9), new Int3(3, 1, 6), AtlasEngine.SelectionMode.Line1D);
        Check(reverse.Count == 4 && reverse[0].Z == 9 && reverse[3].Z == 6,
            "reverse drag keeps ordered coordinates");

        var eventBatch = """{"events":[{"sequence":3,"resolvedChanges":{"placedItemBlocks":[{"name":"a\"}b"}]}},{"sequence":4,"resolvedChanges":{"removedWater":[[1,2,3]]}}],"revision":4}""";
        var events = AtlasMultiplayer.SplitEvents(eventBatch);
        Check(events.Count == 2, "nested JSON stays within each event");
        Check(events[0].Contains("\"sequence\":3") && events[1].Contains("\"sequence\":4"),
            "events preserve server order");
        Check(AtlasMultiplayer.SplitEvents("{\"events\":[],\"revision\":4}").Count == 0,
            "empty long poll has no events");
        Console.WriteLine("Atlas tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
