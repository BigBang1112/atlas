using System.Collections.Generic;
using ManiaScriptSharp;
using Atlas.Libs;
using static ManiaScriptSharp.ManiaScript;

namespace Atlas.EditorPlugins;

/// <summary>
/// A runnable map-editor context showing how to host the Atlas library.
/// Change the block names below to match the map environment before using water modes.
/// </summary>
public class AtlasExamplePlugin : CMapEditorPlugin, IContext
{
    private readonly AtlasEngine atlas = new();

    public void Main()
    {
        // These example names are for Lagoon. Water modes require valid block names.
        atlas.SetRemoveWaterBlockMapping(new Dictionary<string, string>
        {
            ["Grass"] = "LagoonGrassVoid",
            ["Beach"] = "LagoonBeachVoid"
        });
        atlas.SetRestoreWaterBlockMapping(
            new List<string> { "LagoonGrassVoid", "LagoonBeachVoid" }, "LagoonVoid");

        atlas.Initialize();
        atlas.SetSelectionMode(AtlasEngine.SelectionMode.Ground2D);
        Log("Atlas example ready: drag on the ground to select cells.");
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

    // Call these from your own editor UI, or choose one in Main(), to switch tools.
    public void SelectGround() => atlas.SetSelectionMode(AtlasEngine.SelectionMode.Ground2D);
    public void RemoveWater() => atlas.SetSelectionMode(AtlasEngine.SelectionMode.RemoveWater);
    public void RestoreWater() => atlas.SetSelectionMode(AtlasEngine.SelectionMode.RestoreWater);
}
