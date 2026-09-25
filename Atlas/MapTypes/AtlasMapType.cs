using Atlas.Libs;

namespace Atlas.MapTypes;

public class AtlasMapType : CMapType, IContext
{
    private readonly AtlasEngine atlas = new();
    
    public void Main()
    {
        // These example names are for Lagoon. Water modes require valid block names.
        atlas.SetRemoveWaterBlockMapping(new Dictionary<string, string>
        {
            ["LagoonVoid"] = "Beach",
            ["Land"] = "LagoonGrassVoid",
            ["Beach"] = "LagoonBeachVoid"
        });
        atlas.SetRestoreWaterBlockMapping(
            new List<string> { "LagoonGrassVoid", "LagoonBeachVoid" }, "LagoonVoid");

        atlas.Initialize();
        atlas.SetSelectionMode(AtlasEngine.SelectionMode.RemoveWater);
        Log("Atlas example ready: click to select a tower footprint.");
    }

    public void Loop()
    {
        // Update once per editor frame, then consume event queues so they do not accumulate.
        atlas.Update();

        foreach (var change in atlas.SelectionChanged)
            Log($"Selection preview: {change.Coords.Count} cells");

        foreach (var change in atlas.SelectionConfirmed)
            Log($"Selection confirmed: {change.Coords.Count} cells");

        foreach (var removal in atlas.ItemRemovals)
            Log($"Item removed at {removal.Position}");
    }
}
