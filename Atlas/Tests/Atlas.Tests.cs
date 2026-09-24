using System;
using ManiaScriptSharp;
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

        var atlas = new AtlasEngine();
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
